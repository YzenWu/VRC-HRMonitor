# Build the Vue 3 frontend with Vite into the release webui directory.
#
# Why build from a temporary root:
#   Vite misparses module URLs when the project path contains `#`, as in `C# Test`.
#   Source files are copied to a path under %TEMP%, while node_modules is reused through a junction.
#   After the build completes, dist is copied back to the release directory.
#
# Usage:
#   .\build-webui.ps1          # Build, installing dependencies first when needed.
#   .\build-webui.ps1 -SkipWeb # Skip the frontend build.

param(
    [switch]$SkipWeb
)

$ErrorActionPreference = "Stop"
if ($SkipWeb) {
    Write-Host "==> skip web build (--skip-web)"
    exit 0
}

$WebUi = Split-Path -Parent $MyInvocation.MyCommand.Path
$TempRoot = Join-Path $env:TEMP "hrm-webui"

# ---------- 0) Ensure node_modules exists ----------
if (-not (Test-Path (Join-Path $WebUi "node_modules"))) {
    Write-Host "==> npm install ..."
    Push-Location $WebUi
    & npm.cmd install --no-fund --no-audit
    if ($LASTEXITCODE -ne 0) { Pop-Location; Write-Host "!! npm install failed"; exit 1 }
    Pop-Location
}

# ---------- 1) Prepare a temporary root without `#` ----------
if (Test-Path $TempRoot) { Remove-Item $TempRoot -Recurse -Force }
New-Item -ItemType Directory -Path $TempRoot | Out-Null
Copy-Item -Recurse (Join-Path $WebUi "src") $TempRoot
Copy-Item (Join-Path $WebUi "index.html") $TempRoot
Copy-Item (Join-Path $WebUi "vite.config.ts") $TempRoot
Copy-Item (Join-Path $WebUi "tsconfig.json") $TempRoot
New-Item -ItemType Junction -Path (Join-Path $TempRoot "node_modules") -Target (Join-Path $WebUi "node_modules") | Out-Null

# ---------- 2) vite build ----------
# Vite writes progress/warnings to stderr; under $ErrorActionPreference=Stop a non-interactive host
# turns the first stderr line into a terminating NativeCommandError. Merge stderr into stdout and
# relax the preference for the duration of the call so only a real non-zero exit fails the build.
Write-Host "==> vite build (temp root: $TempRoot) ..."
$ErrorActionPreference = 'Continue'
& node (Join-Path $WebUi "node_modules\vite\bin\vite.js") build $TempRoot 2>&1 | ForEach-Object { Write-Host $_ }
$exit = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
if ($exit -ne 0) {
    Write-Host "!! vite build failed"
    exit 1
}

$Dist = Join-Path $TempRoot "dist"
if (-not (Test-Path $Dist)) {
    Write-Host "!! web dist not found: $Dist"
    exit 1
}
Write-Host "==> web build ok: $Dist"
