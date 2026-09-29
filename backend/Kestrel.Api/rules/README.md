# Kestrel Sigma rule pack

Every `*.yml` / `*.yaml` file in this directory tree is parsed, validated and
compiled independently at startup by `Kestrel.Core.Detection.RulePack`
(`Sigma:RulePackPath`, content-root relative — `rules` by default).

- One broken file becomes a **recorded load failure** (see `GET /api/detection/rules`
  → `failures[]`, with the reason). It never stops the rest of the pack.
- Files whose name starts with `_` are treated as partials and skipped.
- Rule IDs must be unique UUIDs; duplicates are rejected as load failures.
- Reload the pack without a restart: `POST /api/detection/compile {"reload": true}`.

## Layout

| Directory           | Contents                                                        |
| ------------------- | --------------------------------------------------------------- |
| `security/`         | Windows Security channel rules (logsource `service: security`)   |
| `system/`           | System channel rules (logsource `service: system`)              |
| `sysmon/`           | Sysmon Operational channel rules (logsource `service: sysmon`)   |
| `powershell/`       | PowerShell Operational/ScriptBlock logging rules                 |
| `process_creation/` | Process-creation rules spanning Security 4688 **and** Sysmon 1   |

Rules that deliberately match several channels or services (for example the
event-log-cleared rule, which covers System 104 and Security 1102) live directly
in `rules/`. Sub-directories are for humans only — the loader walks the tree.

## Adding a rule

1. Drop the Sigma v1.0 YAML file into the matching directory.
2. Validate it before it ever reaches the pack:

   ```http
   POST /api/detection/compile
   Content-Type: application/json

   { "yaml": "<rule yaml>", "name": "my-rule.yml" }
   ```

   A `400` carries one diagnostic per problem (`code` + `message`).
3. Reload and confirm the rule appears in `GET /api/detection/rules`.

## ATT&CK mapping

Technique and tactic IDs are read from the rule's `tags`:

```yaml
tags:
    - attack.defense_evasion   # tactic
    - attack.t1070.001         # technique (sub-technique ids are supported)
```

The IDs are always reported from the tag itself; names, tactics and URLs are
enriched from the STIX bundle at `Attack:StixFilePath`
(`data/enterprise-attack.json`) when that file is present.
