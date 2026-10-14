using System.Diagnostics;
using Fluxio.Transfer.Storage;
using Fluxio.Transfer.Transfers;

namespace Fluxio.Transfer.Tests;

public sealed class StorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fluxio-tests", Guid.NewGuid().ToString("N"));

    public StorageTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void History_records_queries_deletes_and_clears()
    {
        var store = new HistoryStore(Path.Combine(_root, "history.db"));
        var changes = 0;
        store.Changed += (_, _) => changes++;
        var now = DateTimeOffset.UtcNow;

        store.Record(new HistoryEntry(0, now.AddMinutes(-1), TransferDirection.Incoming, "a.jpg", 10, HistoryOutcome.Completed, "Android · Chrome", true));
        store.Record(new HistoryEntry(0, now, TransferDirection.Outgoing, "b.pdf", 20, HistoryOutcome.Cancelled, "iPhone · Safari", false));

        var entries = store.Query();
        Assert.Equal(["b.pdf", "a.jpg"], entries.Select(e => e.FileName));
        Assert.Equal(TransferDirection.Outgoing, entries[0].Direction);
        Assert.Equal(HistoryOutcome.Cancelled, entries[0].Outcome);
        Assert.True(entries[1].Verified);

        store.Delete([entries[0].Id]);
        Assert.Equal("a.jpg", Assert.Single(store.Query()).FileName);

        store.Clear();
        Assert.Empty(store.Query());
        Assert.Equal(4, changes);
    }

    [Fact]
    public void Cleanup_removes_only_leftover_part_files()
    {
        var temp = IncomingStore.EnsureTempFolder(_root);
        File.WriteAllText(Path.Combine(temp, "abc.part"), "x");
        File.WriteAllText(Path.Combine(_root, "keep.txt"), "x");

        var removed = IncomingStore.CleanupTempFolder(_root);

        Assert.Equal(1, removed);
        Assert.False(Directory.Exists(temp));
        Assert.True(File.Exists(Path.Combine(_root, "keep.txt")));
    }

    [Fact]
    public void Finalize_never_replaces_a_folder_even_with_overwrite()
    {
        Directory.CreateDirectory(Path.Combine(_root, "photos"));
        var part = Path.Combine(_root, "x.part");
        File.WriteAllText(part, "data");

        var saved = IncomingStore.Finalize(part, _root, "photos", ConflictPolicy.Overwrite);

        Assert.Equal(Path.Combine(_root, "photos (1)"), saved);
        Assert.True(Directory.Exists(Path.Combine(_root, "photos")));
    }

    [Fact]
    public void Speed_is_measured_over_the_window_and_drops_to_zero_when_idle()
    {
        long now = 0;
        var meter = new SpeedMeter(TimeSpan.FromSeconds(3), () => now);

        for (var i = 0; i < 20; i++)
        {
            meter.Add(100_000);
            now += Stopwatch.Frequency / 10; // 100 ms
        }

        // 2 MB in 2 s
        Assert.InRange(meter.BytesPerSecond, 900_000, 1_100_000);

        now += Stopwatch.Frequency * 5;
        Assert.Equal(0, meter.BytesPerSecond);

        Assert.Equal(TimeSpan.FromSeconds(10), SpeedMeter.EstimateRemaining(10_000, 1000));
        Assert.Null(SpeedMeter.EstimateRemaining(10_000, 0));
    }
}
