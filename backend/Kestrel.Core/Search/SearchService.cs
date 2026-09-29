using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Kestrel.Core.Models;
using Kestrel.Core.Storage;
using Microsoft.Data.Sqlite;

namespace Kestrel.Core.Search;

public sealed class SearchService
{
    private readonly EventDatabase _database;

    public SearchService(EventDatabase database) => _database = database;

    public async Task<SearchResult> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);

        var ftsQuery = BuildFtsQuery(query);
        var sql = BuildSearchSql(query, ftsQuery);

        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = BuildCountSql(query, ftsQuery);
        SetSearchParameters(countCommand, query);
        var totalCount = Convert.ToInt64(await countCommand.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        SetSearchParameters(command, query);

        var events = new List<StoredEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(MapRow(reader));
        }

        return new SearchResult
        {
            Events = events,
            TotalCount = totalCount,
            Query = query.Query,
            TookMs = 0
        };
    }

    private static string BuildFtsQuery(SearchQuery query)
    {
        if (string.IsNullOrWhiteSpace(query.Query))
        {
            return "*";
        }

        // Escape FTS5 special characters in the user query and build a multi-field search
        var escaped = EscapeFts5(query.Query.Trim());
        return escaped;
    }

    private static string EscapeFts5(string input)
    {
        // FTS5 special chars: " ( ) * - AND NOT OR NEAR ;
        // Use quotes for phrase safety
        if (input.Contains(' ', StringComparison.Ordinal))
        {
            // Build a phrase-escaped version
            return "\"" + input.Replace("\"", "\"\"") + "\"";
        }

        return input;
    }

    private string BuildSearchSql(SearchQuery query, string ftsQuery)
    {
        var sb = new StringBuilder();
        sb.Append("SELECT e.id, e.job_id, e.record_id, e.channel, e.event_id, e.provider, e.level, e.level_text, ");
        sb.Append("e.time_created_utc, e.computer, e.user_sid, e.keywords, e.correlation_id, e.thread_id, ");
        sb.Append("e.process_id, e.xml, e.fp_state, e.ingested_at_utc ");
        sb.Append("FROM events e ");
        sb.Append("INNER JOIN event_fts fts ON e.id = fts.rowid ");
        sb.Append($"WHERE fts MATCH {Parameterize("fts_query")}");

        AppendFilters(sb, query);
        sb.Append(" ORDER BY bm25(fts)");
        sb.Append($" LIMIT {query.PageSize} OFFSET {query.Skip}");

        return sb.ToString();
    }

    private string BuildCountSql(SearchQuery query, string ftsQuery)
    {
        var sb = new StringBuilder();
        sb.Append("SELECT COUNT(*) ");
        sb.Append("FROM events e ");
        sb.Append("INNER JOIN event_fts fts ON e.id = fts.rowid ");
        sb.Append($"WHERE fts MATCH {Parameterize("fts_query")}");
        AppendFilters(sb, query);
        return sb.ToString();
    }

    private static void AppendFilters(StringBuilder sb, SearchQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Channel))
        {
            sb.Append(" AND e.channel = ").Append(Parameterize("channel"));
        }
        if (query.Level is not null)
        {
            sb.Append(" AND e.level = ").Append(Parameterize("level"));
        }
        if (!string.IsNullOrWhiteSpace(query.Computer))
        {
            sb.Append(" AND e.computer = ").Append(Parameterize("computer"));
        }
        if (query.TimeStart is not null)
        {
            sb.Append(" AND e.time_created_utc >= ").Append(Parameterize("time_start"));
        }
        if (query.TimeEnd is not null)
        {
            sb.Append(" AND e.time_created_utc <= ").Append(Parameterize("time_end"));
        }
    }

    private static void SetSearchParameters(SqliteCommand command, SearchQuery query)
    {
        command.Parameters.AddWithValue("fts_query", BuildFtsQuery(query));
        if (!string.IsNullOrWhiteSpace(query.Channel))
        {
            command.Parameters.AddWithValue("channel", query.Channel);
        }
        if (query.Level is not null)
        {
            command.Parameters.AddWithValue("level", query.Level.Value);
        }
        if (!string.IsNullOrWhiteSpace(query.Computer))
        {
            command.Parameters.AddWithValue("computer", query.Computer);
        }
        if (query.TimeStart is not null)
        {
            command.Parameters.AddWithValue("time_start", query.TimeStart.Value.ToString("o", CultureInfo.InvariantCulture));
        }
        if (query.TimeEnd is not null)
        {
            command.Parameters.AddWithValue("time_end", query.TimeEnd.Value.ToString("o", CultureInfo.InvariantCulture));
        }
    }

    private static string Parameterize(string name) => $"@{name}";

    private static StoredEvent MapRow(SqliteDataReader reader)
    {
        return new StoredEvent(
            RecordId: reader.IsDBNull(0) ? null : reader.GetInt64(0),
            Channel: reader.GetString(3),
            EventId: reader.GetInt64(4),
            Provider: reader.GetString(5),
            Level: reader.GetByte(6),
            LevelText: reader.GetString(7),
            TimeCreatedUtc: DateTimeOffset.Parse(reader.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            Computer: reader.GetString(9),
            UserSid: reader.IsDBNull(10) ? null : reader.GetString(10),
            Keywords: reader.IsDBNull(11) ? null : reader.GetInt64(11),
            CorrelationId: reader.IsDBNull(12) ? null : reader.GetString(12),
            ThreadId: reader.IsDBNull(13) ? null : reader.GetInt32(13),
            ProcessId: reader.IsDBNull(14) ? null : reader.GetInt32(14),
            Xml: reader.GetString(15),
            IngestedAtUtc: DateTimeOffset.Parse(reader.GetString(17), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
        );
    }
}

public sealed record SearchQuery(
    string Query,
    int PageSize = 50,
    int Skip = 0,
    string? Channel = null,
    byte? Level = null,
    string? Computer = null,
    DateTimeOffset? TimeStart = null,
    DateTimeOffset? TimeEnd = null);

public sealed class SearchResult
{
    public required List<StoredEvent> Events { get; set; }
    public long TotalCount { get; set; }
    public required string Query { get; set; }
    public long TookMs { get; set; }
}
