using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using UAI.ZeroVP.DTO;
using Xunit;
using Xunit.Abstractions;

namespace UAI.ZeroVP.Tests
{
    [Trait("Category", "Performance")]
    public class FullApiPerformanceTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly HttpClient _client;
        private readonly ITestOutputHelper _output;
        private const int MaxResponseTimeMs = 500;
        private const int ConcurrentUsers = 2500;

        public FullApiPerformanceTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
        {
            // Aplikace běží se stejným logováním jako v provozu (včetně console loggeru),
            // aby měření odpovídalo realitě.
            _client = factory.CreateClient();
            _output = output;
        }

        // Všechny endpointy z issue. {id} se při testu nahradí id skutečně vytvořeného úkolu.
        public static IEnumerable<object[]> AllEndpoints => new List<object[]>
        {
            new object[] { "GET",    "/api/info" },
            new object[] { "GET",    "/api/todos" },
            new object[] { "GET",    "/api/todos/{id}" },
            new object[] { "POST",   "/api/todos" },
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
                var ids = new List<string?>();
                for (int i = 0; i < ConcurrentUsers; i++)
                    ids.Add(await PrepareIdAsync(uriTemplate));

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

        private static HttpRequestMessage BuildRequest(string method, string uriTemplate, string? id)
        {
            var uri = id is null ? uriTemplate : uriTemplate.Replace("{id}", id);
            var request = new HttpRequestMessage(new HttpMethod(method), uri);

            // Tělo potřebuje jen POST /api/todos (vytvoření) a PUT (úprava).
            if (method == "POST" && uriTemplate == "/api/todos")
                request.Content = JsonContent.Create(CreateBody());
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