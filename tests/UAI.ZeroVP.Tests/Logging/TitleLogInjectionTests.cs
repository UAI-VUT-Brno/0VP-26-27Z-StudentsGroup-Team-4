using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UAI.ZeroVP.API.Logging;
using UAI.ZeroVP.DTO;

namespace UAI.ZeroVP.Tests.Logging;

/// <summary>
/// 0VP-T4-F14: a user-supplied <c>Title</c> must not be able to forge extra lines in
/// plain-text logs (CWE-117). The console logger prints the formatted message as is,
/// so the formatted message itself must not contain line breaks taken from the title.
/// </summary>
public sealed class TitleLogInjectionTests : IDisposable
{
    private const string ForgedTitle = "Koupit mléko\r\n[CRITICAL] admin logged in\nfake";

    private readonly TempDir _logs = new();
    private readonly CapturingLoggerProvider _captured = new();
    private readonly WebApplicationFactory<Program> _factory;

    public TitleLogInjectionTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["FileLogging:Directory"] = _logs.FullPath
                }));
            builder.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(_captured));
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        _logs.Dispose();
    }

    [Fact]
    public async Task Create_WithLineBreaksInTitle_DoesNotForgeLogLines()
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/todos", new CreateTodoItemRequest { Title = ForgedTitle });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var message = Assert.Single(_captured.MessagesFor(LogEvents.TodoCreated));
        AssertSingleLineWithEscapedBreaks(message);

        // The stored task keeps the title exactly as the client sent it.
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(ForgedTitle, created.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task CreateWeekly_WithLineBreaksInTitle_DoesNotForgeLogLines()
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/todos/repeat", new CreateWeeklyTodoItemsRequest
        {
            Title = ForgedTitle,
            FirstDueAtUtc = DateTimeOffset.UtcNow.AddDays(1),
            RepeatCount = 1
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var message = Assert.Single(_captured.MessagesFor(LogEvents.WeeklyTodosCreated));
        AssertSingleLineWithEscapedBreaks(message);
    }

    private static void AssertSingleLineWithEscapedBreaks(string message)
    {
        Assert.DoesNotContain('\r', message);
        Assert.DoesNotContain('\n', message);
        Assert.Contains(@"Koupit mléko\r\n[CRITICAL] admin logged in\nfake", message);
    }

    /// <summary>Keeps every formatted message, the way a plain-text logger would print it.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<(EventId EventId, string Message)> _entries = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_entries);

        public IReadOnlyList<string> MessagesFor(EventId eventId) =>
            _entries.Where(entry => entry.EventId.Id == eventId.Id).Select(entry => entry.Message).ToList();

        public void Dispose()
        {
        }

        private sealed class CapturingLogger : ILogger
        {
            private readonly ConcurrentQueue<(EventId, string)> _entries;

            public CapturingLogger(ConcurrentQueue<(EventId, string)> entries) => _entries = entries;

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                _entries.Enqueue((eventId, formatter(state, exception)));
        }
    }
}
