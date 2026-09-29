# Phase 5 - Custom Sigma Detection

> Status: **implemented**. The backend parses Sigma v1.0 YAML rules, compiles
> them to parameterized SQLite predicates, evaluates live event batches, and
> enriches alerts with MITRE ATT&CK technique metadata when a STIX bundle is
> available.

## Endpoints

| Method | Route | Auth | Purpose |
|--------|-------|------|---------|
| GET | `/api/detection/rules` | Viewer+ | List compiled rules, warnings, load failures, and ATT&CK mappings |
| POST | `/api/detection/compile` | Analyst+ | Compile inline YAML or reload the configured rule pack |
| POST | `/api/detection/run` | Analyst+ | Scan stored events and return bounded alerts |

The compile endpoint accepts `{ "yaml": "...", "name": "optional-name" }`.
Use `{ "reload": true }` to reload files from disk. The run endpoint accepts
optional `ruleId`, `jobId`, `channel`, `eventId`, and `limit` filters.

## Rule pack

Rules are loaded recursively from `Sigma:RulePackPath`, which defaults to
`backend/Kestrel.Api/rules`. Both `.yml` and `.yaml` files are supported.
Files beginning with `_` are treated as partials and skipped. Invalid files and
duplicate IDs are reported in the rule-pack response instead of being silently
discarded.

The compiler supports field selectors, keyword selectors, selector lists,
parenthesized boolean conditions, `AND`/`OR`/`NOT`, `all of` and `N of`
quantifiers, Sigma field mappings, `contains`, `startswith`, `endswith`, `re`,
`nocase`, `all`, and numeric comparison modifiers. SQL values are always bound
as parameters.

## ATT&CK mapping

Tags such as `attack.execution` and `attack.t1059.001` are preserved on the
compiled rule. Raw technique IDs are returned even when the configured
`Attack:StixFilePath` bundle is absent. When the bundle is present, alerts also
include technique names, tactics, and URLs.

## Validation

The backend solution builds with:

```text
dotnet build backend/Kestrel.sln
```

The test project targets `net8.0`; running tests requires the .NET 8 runtime in
addition to the SDK. On a machine with only .NET 10 installed, the test host
cannot start until .NET 8 runtime is installed.