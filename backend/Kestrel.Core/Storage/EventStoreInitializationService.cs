using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kestrel.Core.Storage;

/// <summary>
/// Creates the event-store schema (WAL mode, forward-compatible tables,
/// version guard) before the HTTP server binds — mirrors
/// IdentityInitializationService on the Api side. Hosted services run in
/// registration order, so this must be registered before the ingest processor.
/// </summary>
public sealed class EventStoreInitializationService : IHostedService
{
    private readonly EventDatabase _database;
    private readonly ILogger<EventStoreInitializationService> _logger;

    public EventStoreInitializationService(EventDatabase database, ILogger<EventStoreInitializationService> logger)
    {
        _database = database;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _database.InitializeAsync(cancellationToken);
        await _database.RebuildFts5Async(cancellationToken);
        _logger.LogInformation(
            "Event store ready at {DbFilePath} (schema v{SchemaVersion}, WAL mode, FTS5 rebuilt).",
            _database.DbFilePath, EventDatabase.SchemaVersion);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}