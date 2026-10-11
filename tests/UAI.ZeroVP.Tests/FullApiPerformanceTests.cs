using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using UAI.ZeroVP.DTO;
using Xunit;
using Xunit.Abstractions;

namespace UAI.ZeroVP.Tests
{
    public class FullApiPerformanceTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly HttpClient _client;
        private readonly ITestOutputHelper _output;
        private const int MaxResponseTimeMs = 500;

        public FullApiPerformanceTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
        {
            _client = factory.CreateClient();
            _output = output;
        }

        // --- 1. TESTY PRO JEDNOHO UŽIVATELE (s přehledným výstupem) ---

        [Theory]
        [InlineData("GET", "/api/info")]
        [InlineData("GET", "/api/todos")]
        public async Task SingleUser_Endpoint_ShouldRespondWithinLimit(string method, string requestUri)
        {
            await _client.GetAsync(requestUri); // Warm-up

            var stopwatch = Stopwatch.StartNew();
            var response = await _client.GetAsync(requestUri);
            stopwatch.Stop();

            LogResult(method, requestUri, stopwatch.ElapsedMilliseconds, 1);
            
            response.EnsureSuccessStatusCode();
            Assert.True(stopwatch.ElapsedMilliseconds <= MaxResponseTimeMs, 
                $"Pomalá odpověď: {stopwatch.ElapsedMilliseconds} ms.");
        }

        // --- 2. ZÁTĚŽOVÝ TEST PRO VÍCE UŽIVATELŮ (Multi-user) ---

        [Theory]
        [InlineData("/api/todos", 50)]  // 50 souběžných uživatelů
        [InlineData("/api/info", 100)]  // 100 souběžných uživatelů
        public async Task MultiUser_GetEndpoints_ShouldRespondWithinLimit(string requestUri, int concurrentUsers)
        {
            await _client.GetAsync(requestUri); // Warm-up

            var tasks = new List<Task<long>>();
            var overallStopwatch = Stopwatch.StartNew();

            // Simulace souběžných požadavků
            for (int i = 0; i < concurrentUsers; i++)
            {
                tasks.Add(Task.Run(async () => 
                {
                    var sw = Stopwatch.StartNew();
                    var res = await _client.GetAsync(requestUri);
                    res.EnsureSuccessStatusCode();
                    sw.Stop();
                    return sw.ElapsedMilliseconds;
                }));
            }

            // Počkáme, až všichni "uživatelé" dokončí svůj request
            var results = await Task.WhenAll(tasks);
            overallStopwatch.Stop();

            var maxTime = results.Max();
            var avgTime = results.Average();
            var minTime = results.Min();

            // Zobrazení strukturovaných výsledků zátěže
            _output.WriteLine("");
            _output.WriteLine($"--- ZÁTĚŽOVÝ TEST: {concurrentUsers} UŽIVATELŮ ---");
            _output.WriteLine($"Endpoint:     {requestUri}");
            _output.WriteLine($"Celkový čas:  {overallStopwatch.ElapsedMilliseconds} ms (zpracování všech dotazů)");
            _output.WriteLine($"Nejrychlejší: {minTime} ms");
            _output.WriteLine($"Průměr:       {avgTime:F1} ms");
            
            LogResult("GET (Max)", requestUri, maxTime, concurrentUsers);

            Assert.True(maxTime <= MaxResponseTimeMs, 
                $"API při zátěži selhalo. Nejdelší request trval: {maxTime} ms");
        }

        // --- POMOCNÁ METODA PRO VYKRESLENÍ VÝSTUPU ---

        private void LogResult(string method, string uri, long elapsedMs, int users)
        {
            string statusIcon = elapsedMs <= MaxResponseTimeMs ? "🟢 PASS" : "🔴 FAIL";
            string userText = users == 1 ? "1 uživatel " : $"{users} uživatelů";
            
            _output.WriteLine($"[{statusIcon}] {method,-10} | {uri,-20} | {elapsedMs,4} ms | {userText}");
        }
    }
}