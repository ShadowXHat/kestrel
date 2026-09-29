using System.Linq;
using System.Globalization;
using System.Text;
using Kestrel.Core.Models;
using Kestrel.Core.Storage;
using Microsoft.Data.Sqlite;

namespace Kestrel.Core.Export;

public sealed class ExportService
{
    private readonly EventDatabase _database;

    public ExportService(EventDatabase database) => _database = database;

    public async Task<ExportResult> ExportAsync(ExportQuery query, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);

        var sql = BuildExportSql(query);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        SetParameters(command, query);

        var events = new List<StoredEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(MapRow(reader));
        }

        var format = query.Format.ToLowerInvariant();
        var content = format switch
        {
            "csv" => ToCsv(events),
            "json" => ToJson(events),
            "jsonl" => ToJsonl(events),
            _ => throw new ArgumentException($"Unsupported export format: {query.Format}")
        };

        return new ExportResult
        {
            Content = content,
            ContentType = format switch
            {
                "csv" => "text/csv",
                "json" => "application/json",
                "jsonl" => "application/x-ndjson",
                _ => "text/plain"
            },
            FileName = $"kestrel-export-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.{format}",
            Count = events.Count
        };
    }

    public async Task<long> CountAsync(ExportQuery query, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);

        var sql = BuildCountSql(query);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        SetParameters(command, query);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private string BuildExportSql(ExportQuery query)
    {
        var sb = new StringBuilder();
        sb.Append("SELECT e.id, e.job_id, e.record_id, e.channel, e.event_id, e.provider, e.level, e.level_text, ");
        sb.Append("e.time_created_utc, e.computer, e.user_sid, e.keywords, e.correlation_id, e.thread_id, ");
        sb.Append("e.process_id, e.xml, e.fp_state, e.ingested_at_utc ");
        sb.Append("FROM events e");

        if (!string.IsNullOrWhiteSpace(query.FtsQuery))
        {
            sb.Append(" INNER JOIN event_fts fts ON e.id = fts.rowid");
            sb.Append($" WHERE fts MATCH {Parameterize("fts_query")}");
        }

        AppendFilters(sb, query);

        if (query.OrderBy == "time")
        {
            sb.Append(" ORDER BY e.time_created_utc ");
            sb.Append(query.Descending ? "DESC" : "ASC");
        }
        else
        {
            sb.Append(" ORDER BY e.id ");
            sb.Append(query.Descending ? "DESC" : "ASC");
        }

        sb.Append($" LIMIT {query.PageSize} OFFSET {query.Skip}");
        return sb.ToString();
    }

    private string BuildCountSql(ExportQuery query)
    {
        var sb = new StringBuilder();
        sb.Append("SELECT COUNT(*) FROM events e");

        if (!string.IsNullOrWhiteSpace(query.FtsQuery))
        {
            sb.Append(" INNER JOIN event_fts fts ON e.id = fts.rowid");
            sb.Append($" WHERE fts MATCH {Parameterize("fts_query")}");
        }

        AppendFilters(sb, query);
        return sb.ToString();
    }

    private static void AppendFilters(StringBuilder sb, ExportQuery query)
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
        if (!string.IsNullOrWhiteSpace(query.Provider))
        {
            sb.Append(" AND e.provider = ").Append(Parameterize("provider"));
        }
    }

    private static void SetParameters(SqliteCommand command, ExportQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.FtsQuery))
        {
            command.Parameters.AddWithValue("fts_query", EscapeFts5(query.FtsQuery));
        }
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
        if (!string.IsNullOrWhiteSpace(query.Provider))
        {
            command.Parameters.AddWithValue("provider", query.Provider);
        }
    }

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

    private static string ToCsv(List<StoredEvent> events)
    {
        var sb = new StringBuilder();
        sb.AppendLine("RecordId,Channel,EventId,Provider,Level,LevelText,TimeCreatedUtc,Computer,UserSid,Keywords,CorrelationId,ThreadId,ProcessId,Xml,IngestedAtUtc");
        foreach (var e in events)
        {
            sb.Append(CsvEscape(e.RecordId?.ToString() ?? ""))
              .Append(",")
              .Append(CsvEscape(e.Channel))
              .Append(",")
              .Append(CsvEscape(e.EventId.ToString()))
              .Append(",")
              .Append(CsvEscape(e.Provider))
              .Append(",")
              .Append(CsvEscape(e.Level.ToString()))
              .Append(",")
              .Append(CsvEscape(e.LevelText))
              .Append(",")
              .Append(CsvEscape(e.TimeCreatedUtc.ToString("o")))
              .Append(",")
              .Append(CsvEscape(e.Computer))
              .Append(",")
              .Append(CsvEscape(e.UserSid ?? ""))
              .Append(",")
              .Append(CsvEscape(e.Keywords?.ToString() ?? ""))
              .Append(",")
              .Append(CsvEscape(e.CorrelationId ?? ""))
              .Append(",")
              .Append(CsvEscape(e.ThreadId?.ToString() ?? ""))
              .Append(",")
              .Append(CsvEscape(e.ProcessId?.ToString() ?? ""))
              .Append(",")
              .Append(CsvEscape(e.Xml))
              .Append(",")
              .Append(CsvEscape(e.IngestedAtUtc.ToString("o")))
              .AppendLine();
        }
        return sb.ToString();
    }

    private static string ToJson(List<StoredEvent> events)
    {
        return "[" + string.Join(",", events.Select(e => JsonEscape(e))) + "]";
    }

    private static string ToJsonl(List<StoredEvent> events)
    {
        return string.Join("\n", events.Select(e => JsonEscape(e))) + "\n";
    }

    private static string JsonEscape(StoredEvent e)
    {
        return "{" +
            $"\"recordId\":{e.RecordId?.ToString() ?? "null"}," +
            $"\"channel\":\"{JsonString(e.Channel)}\"," +
            $"\"eventId\":{e.EventId}," +
            $"\"provider\":\"{JsonString(e.Provider)}\"," +
            $"\"level\":{e.Level}," +
            $"\"levelText\":\"{JsonString(e.LevelText)}\"," +
            $"\"timeCreatedUtc\":\"{JsonString(e.TimeCreatedUtc.ToString("o"))}\"," +
            $"\"computer\":\"{JsonString(e.Computer)}\"," +
            $"\"userSid\":{(e.UserSid is null ? "null" : $"\"{JsonString(e.UserSid)}\"")}," +
            $"\"keywords\":{e.Keywords?.ToString() ?? "null"}," +
            $"\"correlationId\":{(e.CorrelationId is null ? "null" : $"\"{JsonString(e.CorrelationId)}\"")}," +
            $"\"threadId\":{e.ThreadId?.ToString() ?? "null"}," +
            $"\"processId\":{e.ProcessId?.ToString() ?? "null"}," +
            $"\"xml\":\"{JsonString(e.Xml)}\"," +
            $"\"ingestedAtUtc\":\"{JsonString(e.IngestedAtUtc.ToString("o"))}\"" +
        "}";
    }

    private static string JsonString(string? value) => value is null ? "" : value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    private static string CsvEscape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }

    private static string EscapeFts5(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "*";
        if (input.Contains(' ', StringComparison.Ordinal))
        {
            return "\"" + input.Replace("\"", "\"\"") + "\"";
        }
        return input;
    }

    private static string Parameterize(string name) => $"@{name}";
}

public sealed record ExportQuery(
    string? FtsQuery,
    string? Channel = null,
    byte? Level = null,
    string? Computer = null,
    string? Provider = null,
    DateTimeOffset? TimeStart = null,
    DateTimeOffset? TimeEnd = null,
    string Format = "csv",
    int PageSize = 10000,
    int Skip = 0,
    bool Descending = true,
    string OrderBy = "time");

public sealed class ExportResult
{
    public required string Content { get; set; }
    public required string ContentType { get; set; }
    public required string FileName { get; set; }
    public int Count { get; set; }
}
