using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Kestrel.Core.Ingest;

/// <summary>
/// Ingest pipeline configuration (<c>KestrelApp:Ingest</c>). Resolved once at
/// startup; invalid values fail fast with an explicit error — never a silent
/// fallback (.clinerules: background parsing with timeout and memory bounds).
/// </summary>
public sealed class IngestOptions
{
    public const string SectionName = "KestrelApp:Ingest";

    public const long DefaultMaxUploadBytes = 2L * 1024 * 1024 * 1024; // 2 GiB
    public const int DefaultJobTimeoutMinutes = 30;
    public const int DefaultBatchSize = 1_000;
    public const long DefaultMaxRecordsPerJob = 5_000_000;
    public const int DefaultMaxPerRecordErrorsRetained = 50;
    public const string DefaultTempDirectory = "data/uploads-tmp";

    /// <summary>Hard cap for one uploaded EVTX file (bytes).</summary>
    public long MaxUploadBytes { get; init; } = DefaultMaxUploadBytes;

    /// <summary>Per-job hard timeout; the processor cancels the ingest beyond it.</summary>
    public TimeSpan JobTimeout { get; init; } = TimeSpan.FromMinutes(DefaultJobTimeoutMinutes);

    /// <summary>Events per transactional batch flush (bounds ingest memory).</summary>
    public int BatchSize { get; init; } = DefaultBatchSize;

    /// <summary>Safety valve: a job stops with a reason after this many records.</summary>
    public long MaxRecordsPerJob { get; init; } = DefaultMaxRecordsPerJob;

    /// <summary>Per-record failures retained on the job for diagnostics (bounded).</summary>
    public int MaxPerRecordErrorsRetained { get; init; } = DefaultMaxPerRecordErrorsRetained;

    /// <summary>Absolute path where uploads are staged before parsing.</summary>
    public string TempDirectory { get; init; } = DefaultTempDirectory;

    public static IngestOptions FromConfiguration(IConfiguration configuration, string contentRootPath)
    {
        var maxUploadBytes = ReadLong(configuration, "MaxUploadBytes", DefaultMaxUploadBytes);
        var jobTimeoutMinutes = ReadInt(configuration, "JobTimeoutMinutes", DefaultJobTimeoutMinutes);
        var batchSize = ReadInt(configuration, "BatchSize", DefaultBatchSize);
        var maxRecordsPerJob = ReadLong(configuration, "MaxRecordsPerJob", DefaultMaxRecordsPerJob);
        var maxPerRecordErrorsRetained = ReadInt(configuration, "MaxPerRecordErrorsRetained", DefaultMaxPerRecordErrorsRetained);
        var tempDirectory = ReadString(configuration, "TempDirectory", DefaultTempDirectory);

        if (maxUploadBytes < 1_024)
        {
            throw new InvalidOperationException(
                $"{SectionName}:MaxUploadBytes must be at least 1024 bytes (got {maxUploadBytes}).");
        }

        if (jobTimeoutMinutes is < 1 or > 1_440)
        {
            throw new InvalidOperationException(
                $"{SectionName}:JobTimeoutMinutes must be between 1 and 1440 (got {jobTimeoutMinutes}).");
        }

        if (batchSize is < 1 or > 100_000)
        {
            throw new InvalidOperationException(
                $"{SectionName}:BatchSize must be between 1 and 100000 (got {batchSize}).");
        }

        if (maxRecordsPerJob < 1)
        {
            throw new InvalidOperationException(
                $"{SectionName}:MaxRecordsPerJob must be positive (got {maxRecordsPerJob}).");
        }

        if (maxPerRecordErrorsRetained is < 0 or > 1_000)
        {
            throw new InvalidOperationException(
                $"{SectionName}:MaxPerRecordErrorsRetained must be between 0 and 1000 (got {maxPerRecordErrorsRetained}).");
        }

        if (!Path.IsPathRooted(tempDirectory))
        {
            tempDirectory = Path.Combine(contentRootPath, tempDirectory);
        }

        return new IngestOptions
        {
            MaxUploadBytes = maxUploadBytes,
            JobTimeout = TimeSpan.FromMinutes(jobTimeoutMinutes),
            BatchSize = batchSize,
            MaxRecordsPerJob = maxRecordsPerJob,
            MaxPerRecordErrorsRetained = maxPerRecordErrorsRetained,
            TempDirectory = tempDirectory,
        };
    }

    private static long ReadLong(IConfiguration configuration, string key, long fallback) =>
        long.TryParse(configuration[$"{SectionName}:{key}"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static int ReadInt(IConfiguration configuration, string key, int fallback) =>
        int.TryParse(configuration[$"{SectionName}:{key}"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static string ReadString(IConfiguration configuration, string key, string fallback)
    {
        var value = configuration[$"{SectionName}:{key}"];
        return string.IsNullOrWhiteSpace(value) ? fallback : value!;
    }
}