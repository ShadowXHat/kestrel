using System.Globalization;
using Kestrel.Core.Models;

namespace Kestrel.Tests.Ingest;

/// <summary>Shared builders for the Phase 2 tests.</summary>
internal static class EventFixtures
{
    public static readonly DateTimeOffset EventTime =
        DateTimeOffset.Parse("2026-03-01T12:00:00.0000000Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public static StoredEvent MakeEvent(
        long eventId,
        string channel = "Security",
        DateTimeOffset? time = null,
        long? recordId = null,
        byte level = 4) => new(
        RecordId: recordId ?? eventId,
        Channel: channel,
        EventId: eventId,
        Provider: "Microsoft-Windows-Security-Auditing",
        Level: level,
        LevelText: EventLevels.ToText(level),
        TimeCreatedUtc: time ?? EventTime,
        Computer: "WORKSTATION1",
        UserSid: "S-1-5-18",
        Keywords: null,
        CorrelationId: null,
        ThreadId: 111,
        ProcessId: 222,
        Xml: $"<Event><System><EventID>{eventId}</EventID><Channel>{channel}</Channel></System></Event>",
        IngestedAtUtc: time ?? EventTime);
}