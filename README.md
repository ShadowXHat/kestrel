# Kestrel — Windows Event Log Analyzer

> A web-based Windows Event Log analyzer for SOC analysts and incident responders.

## Quick Start

### Prerequisites
- Node.js 22+
- .NET 8 SDK
- Docker (optional, for deployment)

### Development

**Backend:**
```bash
cd backend
dotnet restore
dotnet build
dotnet run --project Kestrel.Api/Kestrel.Api.csproj
```

**Frontend:**
```bash
cd frontend
npm install
npm run dev
```

**Deploy with Docker:**
```bash
cd deploy
docker-compose up -d
```

## Architecture
- Backend: ASP.NET Core Web API (.NET 8)
- Frontend: React 18 + TypeScript + Vite + AG Grid + ECharts
- Storage: SQLite + FTS5
- Detection: Custom Sigma rule matcher
- Threat Intel: MITRE ATT&CK embedded STIX

## Project Structure
```
kestrel/
├── backend/          # ASP.NET Core API
├── frontend/         # React + TypeScript SPA
├── deploy/           # Docker + nginx
└── docs/             # Documentation
```

## First Run & Admin Bootstrap
Phase 1 ships the API with cookie auth, RBAC (Viewer/Analyst/Admin) and a
localhost-only bind guard — see **[docs/PHASE-1.md](docs/PHASE-1.md)**.

- **Development:** an `admin` account is bootstrapped automatically with
  password `Dev#Admin-2026!` (logged with a warning — change it immediately).
- **Production:** set `KestrelApp__Seed__AdminPassword` before the first run;
  there is no silent default password.

```bash
cd backend
dotnet run --project Kestrel.Api/Kestrel.Api.csproj   # http://127.0.0.1:5000
```

## EVTX Ingest (Phase 2)
Streaming upload with magic-byte validation and a bounded background job
queue — see **[docs/PHASE-2.md](docs/PHASE-2.md)**.

```bash
curl -b cookies.txt -X POST http://127.0.0.1:5000/api/ingest/upload \
     -H "X-Evtx-Filename: lab.evtx" -H "Content-Type: application/octet-stream" \
     --data-binary @Security.evtx      # 202 { jobId, jobUrl }
```

## Tests
```bash
cd backend
dotnet test
```


## Skills
This project uses [skills.sh](https://skills.sh) agent skills installed in `.agents/skills/`.
Run `npx skills add dotnet/skills` to install .NET development skills for Cline.
