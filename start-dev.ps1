# =============================================================================
# start-dev.ps1 — start the Kestrel backend + frontend with the correct .NET
#
# WHY THIS EXISTS: the API targets net8.0, and this machine's PATH `dotnet`
# (C:\Program Files\dotnet) only has the .NET 10 runtime. Running the API with
# it dies before binding with:
#   "You must install or update .NET to run this application.
#    Framework: 'Microsoft.NETCore.App', version '8.0.0' (x64)"
# The repo-local toolchain at .dotnet\ has SDK 8.0.425 + ASP.NET Core runtime
# 8.0.31, which is what the project targets. When the API is down, the Vite
# dev proxy surfaces the dead upstream as HTTP 500 on /api/auth/login, which
# the login page shows as "cannot reach the Kestrel API (HTTP error 500)".
#
# Usage:  powershell -ExecutionPolicy Bypass -File start-dev.ps1
#   - Backend: http://127.0.0.1:5000 (localhost-only, per BindingGuard)
#   - Frontend: http://localhost:3000 (Vite proxies /api and /hubs to :5000)
# =============================================================================

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$dotnetExe = Join-Path $repoRoot '.dotnet\dotnet.exe'

if (-not (Test-Path $dotnetExe)) {
    throw "Repo-local dotnet not found at '$dotnetExe'. Restore the .dotnet toolchain folder."
}

# Pin the runtime root so apphost resolution finds the 8.0.31 shared frameworks
# next to the repo, never the machine-wide install.
$env:DOTNET_ROOT = Join-Path $repoRoot '.dotnet'
$env:DOTNET_MULTILEVEL_LOOKUP = '0'

$apiProject = Join-Path $repoRoot 'backend\Kestrel.Api\Kestrel.Api.csproj'
$apiDir = Join-Path $repoRoot 'backend\Kestrel.Api'

Write-Host "[start-dev] backend: $dotnetExe run --project $apiProject" -ForegroundColor Cyan
Write-Host "[start-dev] binds http://127.0.0.1:5000 (logs: backend\Kestrel.Api\logs\kestrel-*.log)" -ForegroundColor Cyan
Start-Process -FilePath $dotnetExe `
    -ArgumentList @('run', '--project', $apiProject) `
    -WorkingDirectory $apiDir `
    -WindowStyle Minimized

Write-Host "[start-dev] frontend: npm run dev (http://localhost:3000)" -ForegroundColor Cyan
Push-Location (Join-Path $repoRoot 'frontend')
try {
    npm run dev
}
finally {
    Pop-Location
}
