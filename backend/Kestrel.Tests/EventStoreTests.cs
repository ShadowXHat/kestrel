using Kestrel.Core.Models;
using Kestrel.Core.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Kestrel.Tests.Ingest;

public sealed class EventStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly EventStore _eventStore;
    private readonly IngestJobStore _jobStore;

    public EventStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "kestrel-tests", "store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        var database = new EventDatabase(Path.Combine(_directory, "events.db"));
        database.InitializeAsync().GetAwaiter().GetResult();
        _eventStore = new EventStore(database);
        _jobStore = new IngestJobStore(database);
    }

    public void Dispose()
    {
        // Release pooled connections so the WAL files are unlocked on delete.
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Insert_batch_and_counts_round_trip()
    {
        var now = DateTimeOffset.UtcNow;
        await _eventStore.InsertBatchAsync(
            "job-1",
            [EventFixtures.MakeEvent(1, time: now), EventFixtures.MakeEvent(2, time: now)],
            CancellationToken.None);
        await _eventStore.InsertBatchAsync("job-2", [EventFixtures.MakeEvent(3, time: now)], CancellationToken.None);

        Assert.Equal(3, await _eventStore.CountAsync());
        Assert.Equal(2, await _eventStore.CountByJobAsync("job-1"));
        Assert.Equal(1, await _eventStore.CountByJobAsync("job-2"));
        Assert.Equal(0, await _eventStore.CountByJobAsync("job-unknown"));
    }

    [Fact]
    public async Task Job_store_round_trips_lifecycle_counters_and_errors()
    {
        var job = new IngestJob
        {
            FileName = "sample.evtx",
            FilePath = "/staged/sample.evtx",
            FileSizeBytes = 4096,
            CreatedBy = "user-1",
        };
        await _jobStore.CreateAsync(job);

        var fetched = await _jobStore.GetAsync(job.Id);
        Assert.NotNull(fetched);
        Assert.Equal(IngestJobStatus.Queued, fetched!.Status);
        Assert.Equal("sample.evtx", fetched.FileName);
        Assert.Equal(4096, fetched.FileSizeBytes);
        Assert.Equal("user-1", fetched.CreatedBy);

        await _jobStore.SetRunningAsync(job.Id, DateTimeOffset.UtcNow);
        job.Status = IngestJobStatus.CompletedWithErrors;
        job.StartedAtUtc = DateTimeOffset.UtcNow;
        job.FinishedAtUtc = DateTimeOffset.UtcNow;
        job.TotalRecords = 5;
        job.ProcessedRecords = 3;
        job.FailedRecords = 2;
        job.FirstErrors =
        [
            new IngestRecordError(7, "no <EventID> element in the event XML"),
            new IngestRecordError(null, "no usable timestamp on the event"),
        ];
        await _jobStore.SetTerminalAsync(job);

        var terminal = await _jobStore.GetAsync(job.Id);
        Assert.NotNull(terminal);
        Assert.Equal(IngestJobStatus.CompletedWithErrors, terminal!.Status);
        Assert.Equal(5, terminal.TotalRecords);
        Assert.Equal(3, terminal.ProcessedRecords);
        Assert.Equal(2, terminal.FailedRecords);
        Assert.Equal(2, terminal.FirstErrors.Count);
        Assert.Equal((long?)7, terminal.FirstErrors[0].RecordId);
        Assert.Equal("no <EventID> element in the event XML", terminal.FirstErrors[0].Reason);
        Assert.NotNull(terminal.StartedAtUtc);
        Assert.NotNull(terminal.FinishedAtUtc);

        var recent = await _jobStore.ListRecentAsync(10);
        Assert.Single(recent);
        Assert.Equal(job.Id, recent[0].Id);
    }

    [Fact]
    public async Task Get_async_returns_null_for_unknown_job()
    {
        Assert.Null(await _jobStore.GetAsync("does-not-exist"));
    }

    [Fact]
    public async Task List_by_status_returns_only_matching_states_oldest_first()
    {
        var older = new IngestJob { FileName = "older.evtx", FilePath = "/staged/older.evtx" };
        await _jobStore.CreateAsync(older);
        var newer = new IngestJob { FileName = "newer.evtx", FilePath = "/staged/newer.evtx" };
        await _jobStore.CreateAsync(newer);

        var finished = await _jobStore.GetAsync(newer.Id);
        Assert.NotNull(finished);
        finished!.Status = IngestJobStatus.Completed;
        finished.FinishedAtUtc = DateTimeOffset.UtcNow;
        await _jobStore.SetTerminalAsync(finished);

        var nonTerminal = await _jobStore.ListByStatusAsync([IngestJobStatus.Queued, IngestJobStatus.Running]);
        Assert.Single(nonTerminal);
        Assert.Equal(older.Id, nonTerminal[0].Id);

        var terminal = await _jobStore.ListByStatusAsync([IngestJobStatus.Completed]);
        Assert.Single(terminal);
        Assert.Equal(newer.Id, terminal[0].Id);

        Assert.Empty(await _jobStore.ListByStatusAsync([]));
    }
}