using System.Runtime.Versioning;

// Kestrel parses EVTX via System.Diagnostics.Eventing.Reader — the native
// Windows API (AGENTS.md: "Native Eventing.Reader"). The backend is
// Windows-only by design; the assembly-level annotation documents that fact
// and keeps the platform analyzer (CA1416) quiet for consumers.
[assembly: SupportedOSPlatform("windows")]