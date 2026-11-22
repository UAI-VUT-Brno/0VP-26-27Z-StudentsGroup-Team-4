using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using UAI.ZeroVP.DTO;
using UAI.ZeroVP.Tests.Logging; // Přidáno pro LogTestHelpers
using Xunit;
using Xunit.Abstractions;

namespace UAI.ZeroVP.Tests
{
    [Trait("Category", "Performance")]
    public class FullApiPerformanceTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
    {
        private readonly TempDir _logs = new();
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;
        private readonly ITestOutputHelper _output;
        private const int MaxResponseTimeMs = 500;

        // Výchozí hodnota 2500 odpovídá původnímu zadání. Lze ji přepsat proměnnou
        // prostředí PERFORMANCE_CONCURRENT_USERS, např. kdyby to na konkrétním
        // runneru nestíhalo v limitu 500 ms i po paralelizaci přípravy dat.
        private static readonly int ConcurrentUsers =
            int.TryParse(Environment.GetEnvironmentVariable("PERFORMANCE_CONCURRENT_USERS"), out var concurrentUsersFromEnv)
                ? concurrentUsersFromEnv
                : 2500;

        // Maximální počet souběžných příprav dat (PrepareIdAsync), aby se
        // nezahltilo úložiště (zejména souborový repozitář) najednou.
        private const int MaxConcurrentPreparations = 50;

        public FullApiPerformanceTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
        {
            // Zápis do souboru přesměrujeme do vlastní dočasné složky, aby testy nepsaly logy
            // do repozitáře; po testu se smaže. (RedirectLogsTo mění jen cílovou složku
            // souborového loggeru – logování jako takové je stejné jako v Program.cs.)
            _factory = factory.WithWebHostBuilder(builder => builder.RedirectLogsTo(_logs.FullPath));
            _client = _factory.CreateClient();

            _output = output;
        }

        public void Dispose()
        {
            // Nejdřív factory (vyprázdní a zavře soubor s logem), potom složku.
            _client.Dispose();
            _factory.Dispose();
            _logs.Dispose();
            GC.SuppressFinalize(this);
        }

        // Všechny endpointy z issue. {id} se při testu nahradí id skutečně vytvořeného úkolu.
        public static IEnumerable<object[]> AllEndpoints => new List<object[]>
        {
            new object[] { "GET",    "/api/info" },
            new object[] { "GET",    "/api/todos" },
            new object[] { "GET",    "/api/todos/overdue" },
            new object[] { "GET",    "/api/todos/{id}" },
            new object[] { "POST",   "/api/todos" },
            new object[] { "POST",   "/api/todos/repeat" },
            new object[] { "PUT",    "/api/todos/{id}" },
            new object[] { "POST",   "/api/todos/{id}/complete" },
            new object[] { "DELETE", "/api/todos/{id}" },
        };

        // --- 1. TESTY PRO JEDNOHO UŽIVATELE ---

        [Theory]
        [MemberData(nameof(AllEndpoints))]
        public async Task SingleUser_Endpoint_ShouldRespondWithinLimit(string method, string uriTemplate)
        {
            var baseline = await GetAllIdsAsync();

            try
            {
                // Warm-up na vlastním úkolu, aby se nespotřeboval ten měřený (např. u DELETE).
                using (var warmup = BuildRequest(method, uriTemplate, await PrepareIdAsync(uriTemplate)))
                {
                    (await _client.SendAsync(warmup)).Dispose();
                }

                var id = await PrepareIdAsync(uriTemplate);
                using var request = BuildRequest(method, uriTemplate, id);

                var stopwatch = Stopwatch.StartNew();
                using var response = await _client.SendAsync(request);
                stopwatch.Stop();

                _output.WriteLine("");
                _output.WriteLine("--- TEST: 1 UŽIVATEL ---");
                _output.WriteLine($"Endpoint:     {method} {uriTemplate}");
                _output.WriteLine($"Čas odpovědi: {stopwatch.ElapsedMilliseconds} ms");

                LogResult(method, uriTemplate, stopwatch.ElapsedMilliseconds, 1);

                response.EnsureSuccessStatusCode();
                Assert.True(stopwatch.ElapsedMilliseconds <= MaxResponseTimeMs,
                    $"Pomalá odpověď: {stopwatch.ElapsedMilliseconds} ms.");
            }
            finally
            {
                await CleanupAsync(baseline);
            }
        }

        // --- 2. ZÁTĚŽOVÝ TEST PRO VÍCE UŽIVATELŮ ---

        [Theory]
        [MemberData(nameof(AllEndpoints))]
        public async Task MultiUser_Endpoint_ShouldRespondWithinLimit(string method, string uriTemplate)
        {
            var baseline = await GetAllIdsAsync();

            try
            {
                // Warm-up
                using (var warmup = BuildRequest(method, uriTemplate, await PrepareIdAsync(uriTemplate)))
                {
                    (await _client.SendAsync(warmup)).Dispose();
                }

                // Příprava dat MIMO měření: každý uživatel má svůj vlastní úkol
                // (jinak by např. druhé DELETE na stejné id vrátilo 404).
                // Paralelizováno přes SemaphoreSlim místo sekvenčního volání -
                // s vyšším ConcurrentUsers by sekvenční příprava trvala déle
                // než samotné měřené volání a zbytečně by prodlužovala test.
                var ids = await PrepareIdsAsync(uriTemplate, ConcurrentUsers);

                var tasks = new List<Task<long>>();
                var overallStopwatch = Stopwatch.StartNew();

                for (int i = 0; i < ConcurrentUsers; i++)
                {
                    var id = ids[i];
                    tasks.Add(Task.Run(async () =>
                    {
                        using var request = BuildRequest(method, uriTemplate, id);
                        var sw = Stopwatch.StartNew();
                        using var res = await _client.SendAsync(request);
                        sw.Stop();
                        res.EnsureSuccessStatusCode();
                        return sw.ElapsedMilliseconds;
                    }));
                }

                var results = await Task.WhenAll(tasks);
                overallStopwatch.Stop();

                var maxTime = results.Max();
                var avgTime = results.Average();
                var minTime = results.Min();

                _output.WriteLine("");
                _output.WriteLine($"--- ZÁTĚŽOVÝ TEST: {ConcurrentUsers} UŽIVATELŮ ---");
                _output.WriteLine($"Endpoint:     {method} {uriTemplate}");
                _output.WriteLine($"Celkový čas:  {overallStopwatch.ElapsedMilliseconds} ms (zpracování všech dotazů)");
                _output.WriteLine($"Nejrychlejší: {minTime} ms");
                _output.WriteLine($"Průměr:       {avgTime:F1} ms");

                LogResult($"{method} (Max)", uriTemplate, maxTime, ConcurrentUsers);

                Assert.True(maxTime <= MaxResponseTimeMs,
                    $"API při zátěži selhalo. Nejdelší request trval: {maxTime} ms");
            }
            finally
            {
                await CleanupAsync(baseline);
            }
        }

        // --- POMOCNÉ METODY ---

        // Vytvoří úkol a vrátí jeho id, pokud ho endpoint potřebuje. Jinak vrátí null.
        private async Task<string?> PrepareIdAsync(string uriTemplate)
        {
            if (!uriTemplate.Contains("{id}"))
                return null;

            using var response = await _client.PostAsJsonAsync("/api/todos", CreateBody());
            response.EnsureSuccessStatusCode();

            return ReadId(await response.Content.ReadAsStringAsync());
        }

        // Paralelně připraví 'count' úkolů (nebo vrátí 'count' nullů, pokud endpoint
        // žádné id nepotřebuje) a omezí počet souběžných požadavků semaforem, aby
        // se úložiště nezahltilo najednou.
        private async Task<List<string?>> PrepareIdsAsync(string uriTemplate, int count)
        {
            using var semaphore = new SemaphoreSlim(MaxConcurrentPreparations);
            var tasks = new Task<string?>[count];

            for (int i = 0; i < count; i++)
            {
                tasks[i] = PrepareOneAsync();
            }

            var ids = await Task.WhenAll(tasks);
            return ids.ToList();

            async Task<string?> PrepareOneAsync()
            {
                await semaphore.WaitAsync();
                try
                {
                    return await PrepareIdAsync(uriTemplate);
                }
                finally
                {
                    semaphore.Release();
                }
            }
        }

        private static HttpRequestMessage BuildRequest(string method, string uriTemplate, string? id)
        {
            var uri = id is null ? uriTemplate : uriTemplate.Replace("{id}", id);
            var request = new HttpRequestMessage(new HttpMethod(method), uri);

            // Tělo potřebuje POST /api/todos (vytvoření), POST /api/todos/repeat
            // (hromadné vytvoření) a PUT (úprava).
            if (method == "POST" && uriTemplate == "/api/todos")
                request.Content = JsonContent.Create(CreateBody());
            else if (method == "POST" && uriTemplate == "/api/todos/repeat")
                request.Content = JsonContent.Create(CreateWeeklyBody());
            else if (method == "PUT")
                request.Content = JsonContent.Create(UpdateBody());

            return request;
        }

        private static CreateTodoItemRequest CreateBody() => new()
        {
            Title = "Perf test",
            Description = "Úkol vytvořený výkonnostním testem.",
            Priority = TodoPriority.Normal
        };

        private static CreateWeeklyTodoItemsRequest CreateWeeklyBody() => new()
        {
            Title = "Perf test (opakování)",
            Description = "Úkoly vytvořené výkonnostním testem přes /api/todos/repeat.",
            Priority = TodoPriority.Normal,
            FirstDueAtUtc = DateTimeOffset.UtcNow.AddDays(1),
            RepeatCount = 1
        };

        private static UpdateTodoItemRequest UpdateBody() => new()
        {
            Title = "Perf test (upraveno)",
            Description = "Úkol upravený výkonnostním testem.",
            Priority = TodoPriority.High,
            IsCompleted = false
        };

        // Odpověď API posílá enum jako string, proto se čte přes JsonDocument, ne přes TodoItemDto.
        private static string ReadId(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("id").GetString()!;
        }

        private async Task<HashSet<string>> GetAllIdsAsync()
        {
            using var response = await _client.GetAsync("/api/todos");
            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.EnumerateArray()
                .Select(e => e.GetProperty("id").GetString()!)
                .ToHashSet();
        }

        // Smaže úkoly, které test vytvořil. Bez toho by se mezi testy (sdílený hostitel)
        // seznam úkolů zvětšoval a další testy by měřily jiná data.
        private async Task CleanupAsync(HashSet<string> baseline)
        {
            var current = await GetAllIdsAsync();
            foreach (var id in current.Where(id => !baseline.Contains(id)))
            {
                (await _client.DeleteAsync($"/api/todos/{id}")).Dispose();
            }
        }

        private void LogResult(string method, string uri, long elapsedMs, int users)
        {
            string statusIcon = elapsedMs <= MaxResponseTimeMs ? "🟢 PASS" : "🔴 FAIL";
            string userText = users == 1 ? "1 uživatel " : $"{users} uživatelů";

            _output.WriteLine($"[{statusIcon}] {method,-12} | {uri,-28} | {elapsedMs,4} ms | {userText}");
        }
    }
}
