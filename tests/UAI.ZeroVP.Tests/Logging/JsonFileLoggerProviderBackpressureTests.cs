using Microsoft.Extensions.Logging;
using UAI.ZeroVP.API.Logging;

namespace UAI.ZeroVP.Tests.Logging;

/// <summary>
/// 0VP-T4-F14: when the file-log queue is full, dropped entries must be counted and reported
/// (event <c>LogEntriesDropped</c>), never lost silently.
/// </summary>
public sealed class JsonFileLoggerProviderBackpressureTests
{
    [Fact]
    public void FullQueue_DroppedEntriesAreCountedAndReportedInTheFile()
    {
        const int attempts = 100;

        // The writer thread asks the clock for the file name of every entry. Holding it there
        // simulates a disk that does not respond, so the one-slot queue fills up deterministically.
        var clock = new WriterBlockingTimeProvider(DateTimeOffset.UtcNow);
        using var harness = new LogHarness(options => options.QueueCapacity = 1, clock);
        var logger = harness.CreateLogger("Test.Burst");

        for (var i = 0; i < attempts; i++)
        {
            logger.LogInformation("burst {Index}", i);
        }

        var dropped = harness.Provider.DroppedCount;
        clock.Release();
        var entries = harness.Flush();

        // At most two entries fit: one taken by the stalled writer and one in the queue.
        Assert.InRange(dropped, attempts - 2, attempts - 1);

        var warning = Assert.Single(entries, entry => entry.Id() == LogEvents.LogEntriesDropped.Id);
        Assert.Equal("Warning", warning.Str("level"));
        Assert.Equal("LogEntriesDropped", warning.Str("event"));
        Assert.Equal(dropped, warning.Props().GetProperty("DroppedCount").GetInt64());

        // Every attempt is either in the file or counted in the warning.
        Assert.Equal(attempts, entries.Count - 1 + dropped);
    }

    [Fact]
    public void QueueWithRoom_ReportsNoDrops()
    {
        using var harness = new LogHarness();
        var logger = harness.CreateLogger("Test.Calm");

        for (var i = 0; i < 100; i++)
        {
            logger.LogInformation("calm {Index}", i);
        }

        var entries = harness.Flush();

        Assert.Equal(0, harness.Provider.DroppedCount);
        Assert.Equal(100, entries.Count);
        Assert.DoesNotContain(entries, entry => entry.Id() == LogEvents.LogEntriesDropped.Id);
    }

    /// <summary>
    /// A fixed clock that blocks every caller except the thread that created it (the test)
    /// until <see cref="Release"/> is called - in practice it stalls the provider's writer thread.
    /// </summary>
    private sealed class WriterBlockingTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        private readonly int _testThreadId = Environment.CurrentManagedThreadId;
        private readonly ManualResetEventSlim _released = new(initialState: false);

        public WriterBlockingTimeProvider(DateTimeOffset now) => _now = now;

        public void Release() => _released.Set();

        public override DateTimeOffset GetUtcNow()
        {
            if (Environment.CurrentManagedThreadId != _testThreadId)
            {
                _released.Wait(TimeSpan.FromSeconds(30));
            }

            return _now;
        }
    }
}
