# Run the Vue frontend unit tests with Vitest from a temporary root.
#
# Why use a temporary root:
#   Vite misparses module URLs when the project path contains `#`, as in `C# Test`.
#   This can produce `Cannot find module '/@vite/env'` before any test runs.
#   Source files and node_modules must be copied to a path without `#`; a junction still resolves to the original path.
#
# Usage:
#   .\test-webui.ps1              # Run all *.spec.ts files.
#   .\test-webui.ps1 -Filter i18n # Run test files whose names contain i18n.

param(
    [string]$Filter = ""
)

$ErrorActionPreference = "Stop"
$WebUi = Split-Path -Parent $MyInvocation.MyCommand.Path
$TempRoot = Join-Path $env:TEMP "hrm-webui-test"

# ---------- 0) Ensure node_modules exists ----------
if (-not (Test-Path (Join-Path $WebUi "node_modules"))) {
    Write-Host "==> npm install ..."
    Push-Location $WebUi
    & npm.cmd install --no-fund --no-audit
    if ($LASTEXITCODE -ne 0) { Pop-Location; Write-Host "!! npm install failed"; exit 1 }
    Pop-Location
}

# ---------- 1) Synchronize to the temporary root ----------
if (-not (Test-Path $TempRoot)) { New-Item -ItemType Directory -Path $TempRoot | Out-Null }
& robocopy (Join-Path $WebUi "src") (Join-Path $TempRoot "src") /MIR /NFL /NDL /NJH /NJS /MT:16 | Out-Null
& robocopy (Join-Path $WebUi "node_modules") (Join-Path $TempRoot "node_modules") /MIR /NFL /NDL /NJH /NJS /MT:16 | Out-Null
foreach ($f in @("vitest.config.ts", "tsconfig.json", "package.json")) {
    Copy-Item (Join-Path $WebUi $f) $TempRoot -Force
}

# ---------- 2) vitest run ----------
Write-Host "==> vitest run (temp root: $TempRoot) ..."
$args = @((Join-Path $TempRoot "node_modules\vitest\vitest.mjs"), "run", "--root", $TempRoot)
if ($Filter -ne "") { $args += $Filter }
& node @args
if ($LASTEXITCODE -ne 0) {
    Write-Host "!! vitest failed"
    exit 1
}
Write-Host "==> tests ok"
