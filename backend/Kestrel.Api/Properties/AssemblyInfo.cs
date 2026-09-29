using System.Runtime.Versioning;

// Kestrel's API hosts the Windows-only EVTX ingest pipeline (see
// Kestrel.Core/Properties/AssemblyInfo.cs) — Windows-only by design.
[assembly: SupportedOSPlatform("windows")]