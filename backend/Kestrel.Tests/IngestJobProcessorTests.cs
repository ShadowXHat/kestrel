using System.IO;
using Kestrel.Core.Ingest;
using Kestrel.Core.Jobs;
using Kestrel.Core.Models;
using Kestrel.Core.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kestrel.Tests.Ingest;

public sealed class IngestJobProcessorTests : IDisposable
{
    private readonly string _directory;
    private readonly EventStore _eventStore;
    private readonly IngestJobStore _jobStore;
    private readonly IngestJobQueue _queue = new();

    public IngestJobProcessorTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "kestrel-tests", "jobs-" + Guid.NewGuid().ToString("N"));
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

    private static IngestOptions Options(TimeSpan? timeout = null, int maxPerRecordErrorsRetained = 50) => new()
    {
        JobTimeout = timeout ?? TimeSpan.FromMinutes(1),
        MaxPerRecordErrorsRetained = maxPerRecordErrorsRetained,
    };

    private sealed class StubIngestService : IEvtxIngestService
    {
        private readonly Func<IngestJob, CancellationToken, Task> _behavior;

        public StubIngestService(Func<IngestJob, CancellationToken, Task> behavior) => _behavior = behavior;

        public Task IngestAsync(IngestJob job, CancellationToken cancellationToken) => _behavior(job, cancellationToken);
    }

    /// <summary>Enqueues the job and runs a processor until it is terminal.</summary>
    private async Task<IngestJob> RunJobAsync(
        IngestJob job,
        Func<IngestJob, CancellationToken, Task> behavior,
        IngestOptions? options = null)
    {
        Assert.True(_queue.TryEnqueue(job));

        var processor = new IngestJobProcessor(
            _queue,
            _jobStore,
            new StubIngestService(behavior),
            options ?? Options(),
            NullIngestProgressSink.Instance,
            NullLogger<IngestJobProcessor>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await processor.StartAsync(cts.Token);
        try
        {
            return await WaitForTerminalAsync(job.Id);
        }
        finally
        {
            await processor.StopAsync(cts.Token);
        }
    }

    private async Task<IngestJob> WaitForTerminalAsync(string jobId)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var job = await _jobStore.GetAsync(jobId);
            if (job is { } found &&
                found.Status is not (IngestJobStatus.Queued or IngestJobStatus.Running))
            {
                return found;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Job {jobId} did not reach a terminal state within 15 seconds.");
    }

    [Fact]
    public async Task Successful_job_persists_completed_state_and_events()
    {
        var job = new IngestJob { FileName = "good.evtx", FilePath = Path.Combine(_directory, "good.evtx") };
        await _jobStore.CreateAsync(job);

        var terminal = await RunJobAsync(job, async (j, ct) =>
        {
            var now = DateTimeOffset.UtcNow;
            await _eventStore.InsertBatchAsync(
                j.Id,
                [EventFixtures.MakeEvent(1, time: now), EventFixtures.MakeEvent(2, time: now), EventFixtures.MakeEvent(3, time: now)],
                ct);
            j.TotalRecords = 3;
            j.ProcessedRecords = 3;
            await _jobStore.SetCountersAsync(j, ct);
        });

        Assert.Equal(IngestJobStatus.Completed, terminal.Status);
        Assert.Null(terminal.Error);
        Assert.Equal(3, terminal.TotalRecords);
        Assert.Equal(3, terminal.ProcessedRecords);
        Assert.Equal(3, await _eventStore.CountByJobAsync(job.Id));
        Assert.NotNull(terminal.StartedAtUtc);
        Assert.NotNull(terminal.FinishedAtUtc);
    }

    [Fact]
    public async Task Per_record_failures_produce_completed_with_errors()
    {
        var job = new IngestJob { FileName = "mixed.evtx", FilePath = Path.Combine(_directory, "mixed.evtx") };
        await _jobStore.CreateAsync(job);

        var terminal = await RunJobAsync(job, (j, ct) =>
        {
            j.TotalRecords = 5;
            j.ProcessedRecords = 3;
            j.FailedRecords = 2;
            j.FirstErrors = [new IngestRecordError(4, "no <EventID> element in the event XML")];
            return _jobStore.SetCountersAsync(j, ct);
        });

        Assert.Equal(IngestJobStatus.CompletedWithErrors, terminal.Status);
        Assert.Equal(2, terminal.FailedRecords);
        Assert.Single(terminal.FirstErrors);
        Assert.Equal((long?)4, terminal.FirstErrors[0].RecordId);
        Assert.Contains("EventID", terminal.FirstErrors[0].Reason);
    }

    [Fact]
    public async Task Fatal_error_marks_job_failed_with_reason()
    {
        var job = new IngestJob { FileName = "broken.evtx", FilePath = Path.Combine(_directory, "broken.evtx") };
        await _jobStore.CreateAsync(job);

        var terminal = await RunJobAsync(job, async (j, ct) =>
        {
            await Task.Yield();
            throw new InvalidDataException("The event log file is corrupted.");
        });

        Assert.Equal(IngestJobStatus.Failed, terminal.Status);
        Assert.NotNull(terminal.Error);
        Assert.Contains("InvalidDataException", terminal.Error);
        Assert.Contains("corrupted", terminal.Error);
        Assert.Equal(IngestJobStatusText.ToDb(IngestJobStatus.Failed), IngestJobStatusText.ToApi(terminal.Status));
    }

    [Fact]
    public async Task Timeout_cancels_the_job_with_a_reason()
    {
        var job = new IngestJob { FileName = "slow.evtx", FilePath = Path.Combine(_directory, "slow.evtx") };
        await _jobStore.CreateAsync(job);

        var terminal = await RunJobAsync(
            job,
            async (j, ct) =>
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(50, ct);
                }
            },
            Options(timeout: TimeSpan.FromSeconds(1)));

        Assert.Equal(IngestJobStatus.Canceled, terminal.Status);
        Assert.NotNull(terminal.Error);
        Assert.Contains("timed out", terminal.Error);
    }

    [Fact]
    public async Task Jobs_queued_before_start_are_processed()
    {
        var job = new IngestJob { FileName = "queued.evtx", FilePath = Path.Combine(_directory, "queued.evtx") };
        await _jobStore.CreateAsync(job);
        Assert.True(_queue.TryEnqueue(job));

        var processor = new IngestJobProcessor(
            _queue,
            _jobStore,
            new StubIngestService((j, ct) =>
            {
                j.TotalRecords = 1;
                j.ProcessedRecords = 1;
                return _jobStore.SetCountersAsync(j, ct);
            }),
            Options(),
            NullIngestProgressSink.Instance,
            NullLogger<IngestJobProcessor>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await processor.StartAsync(cts.Token);
        try
        {
            var terminal = await WaitForTerminalAsync(job.Id);
            Assert.Equal(IngestJobStatus.Completed, terminal.Status);
            Assert.Equal(1, terminal.ProcessedRecords);
        }
        finally
        {
            await processor.StopAsync(cts.Token);
        }
    }

    // ---- Startup recovery of jobs orphaned by a process restart ----------------
    // The queue is in-memory; a restart leaves persisted 'queued'/'running'
    // rows behind. Recovery must re-drive them or fail them loudly.

    [Fact]
    public async Task Orphaned_queued_job_with_staged_file_is_recovered_after_restart()
    {
        var stagedPath = Path.Combine(_directory, "orphan-queued.evtx");
        await File.WriteAllTextAsync(stagedPath, "staged-bytes");
        var job = new IngestJob { FileName = "orphan-queued.evtx", FilePath = stagedPath };
        await _jobStore.CreateAsync(job); // persisted 'queued', but NOT in the in-memory queue

        var processor = new IngestJobProcessor(
            _queue,
            _jobStore,
            new StubIngestService((j, ct) =>
            {
                j.TotalRecords = 1;
                j.ProcessedRecords = 1;
                return _jobStore.SetCountersAsync(j, ct);
            }),
            Options(),
            NullIngestProgressSink.Instance,
            NullLogger<IngestJobProcessor>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await processor.StartAsync(cts.Token);
        try
        {
            var terminal = await WaitForTerminalAsync(job.Id);
            Assert.Equal(IngestJobStatus.Completed, terminal.Status);
            Assert.Equal(1, terminal.ProcessedRecords);
        }
        finally
        {
            await processor.StopAsync(cts.Token);
        }
    }

    [Fact]
    public async Task Orphaned_queued_job_without_staged_file_fails_with_reason()
    {
        var job = new IngestJob { FileName = "orphan-lost.evtx", FilePath = Path.Combine(_directory, "never-staged.evtx") };
        await _jobStore.CreateAsync(job);

        var processor = new IngestJobProcessor(
            _queue,
            _jobStore,
            new StubIngestService((j, ct) => throw new InvalidOperationException("the ingest service must not run for a lost staged file")),
            Options(),
            NullIngestProgressSink.Instance,
            NullLogger<IngestJobProcessor>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await processor.StartAsync(cts.Token);
        try
        {
            var terminal = await WaitForTerminalAsync(job.Id);
            Assert.Equal(IngestJobStatus.Failed, terminal.Status);
            Assert.NotNull(terminal.Error);
            Assert.Contains("re-upload", terminal.Error);
        }
        finally
        {
            await processor.StopAsync(cts.Token);
        }
    }

    [Fact]
    public async Task Orphaned_running_job_fails_as_interrupted_after_restart()
    {
        var job = new IngestJob { FileName = "orphan-running.evtx", FilePath = Path.Combine(_directory, "orphan-running.evtx") };
        await _jobStore.CreateAsync(job);
        await _jobStore.SetRunningAsync(job.Id, DateTimeOffset.UtcNow);

        var processor = new IngestJobProcessor(
            _queue,
            _jobStore,
            new StubIngestService((j, ct) => throw new InvalidOperationException("the ingest service must not run for an interrupted job")),
            Options(),
            NullIngestProgressSink.Instance,
            NullLogger<IngestJobProcessor>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await processor.StartAsync(cts.Token);
        try
        {
            var terminal = await WaitForTerminalAsync(job.Id);
            Assert.Equal(IngestJobStatus.Failed, terminal.Status);
            Assert.NotNull(terminal.Error);
            Assert.Contains("interrupted", terminal.Error);
        }
        finally
        {
            await processor.StopAsync(cts.Token);
        }
    }
}