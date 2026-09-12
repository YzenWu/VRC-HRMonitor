# Run local Vite development from a temporary root because `#` in the project path breaks URL parsing.
# Source files are copied there and node_modules is reused through a junction; API and WebSocket traffic is proxied to port 9460.
# Usage: .\dev-webui.ps1
$ErrorActionPreference = "Stop"

$WebUi = Split-Path -Parent $MyInvocation.MyCommand.Path
$TempRoot = Join-Path $env:TEMP "hrm-webui-dev"

if (-not (Test-Path (Join-Path $WebUi "node_modules"))) {
    Write-Host "==> npm install ..."
    Push-Location $WebUi
    & npm.cmd install --no-fund --no-audit
    if ($LASTEXITCODE -ne 0) { Pop-Location; exit 1 }
    Pop-Location
}

if (Test-Path $TempRoot) { Remove-Item $TempRoot -Recurse -Force }
New-Item -ItemType Directory -Path $TempRoot | Out-Null
Copy-Item -Recurse (Join-Path $WebUi "src") $TempRoot
Copy-Item (Join-Path $WebUi "index.html") $TempRoot
Copy-Item (Join-Path $WebUi "vite.config.ts") $TempRoot
Copy-Item (Join-Path $WebUi "tsconfig.json") $TempRoot
New-Item -ItemType Junction -Path (Join-Path $TempRoot "node_modules") -Target (Join-Path $WebUi "node_modules") | Out-Null

Write-Host "==> vite dev (http://127.0.0.1:5173/) from $TempRoot ... (Ctrl+C 退出)"
Push-Location $TempRoot
& node (Join-Path $WebUi "node_modules\vite\bin\vite.js") serve $TempRoot --host 127.0.0.1 --port 5173
