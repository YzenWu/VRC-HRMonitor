#   build.ps1 [--releases | --debug] [-SkipWeb]
#   (no arg -> interactive prompt, pick by number + Enter)
#
#   PE icons (resource icons for the engine/webui/cli/dump executables plus the tray icon):
#   Single source of truth = the icons section in Windows/Release.json (relative paths resolve from the Windows/ source root).
#   The build only reads and applies the manifest; undeclared roles and missing files are skipped.
#
# Directory conventions (#22 moved the focus back to Windows; Shared has been merged):
#   This script  Workspace root (Windows/ is the clean source directory)
#   Windows/    All source: App (main C# program) / Shells / WebUI (Vue frontend) / Engine (C OSC engine)
#               / config / image / Skills / docs / Release.json
#   Built/      Artifacts (repository root, not committed)
#
# branches:
#   releases   : framework-dependent release, no console window
#   debug      : debug config, console window, most detailed logs
param(
    [string]$Mode = "",
    [switch]$SkipWeb
)
$Mode = $Mode.TrimStart('-')
$ErrorActionPreference = "Stop"
# Do not add concurrency or mutual-exclusion guards: the build entry point is a critical workflow and must never be blocked (user instruction dated 2026-09-07).
# Parallel builds share obj/ and may produce incomplete artifacts; callers must avoid running two builds simultaneously.
# #22 moved the focus back to Windows: the script is at the workspace root and all source is under Windows/ (Shared has been merged)
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Win = Join-Path $Root "Windows"
$Engine = Join-Path $Win "Engine"
$App    = Join-Path $Win "App"
$WebUi  = Join-Path $Win "WebUI"
$OutBase = Join-Path $Root "Built"
# ---------- resolve branch ----------
if (-not $Mode) {
    Write-Host "Select build branch (HeartRateMonitor4Windows):"
    Write-Host "  0) releases    - release, no console window"
    Write-Host "  1) debug       - debug, console + most detailed logs"
    $choice = Read-Host "Enter number and press Enter"
    switch ($choice) {
        "0" { $Mode = "releases" }
        "1" { $Mode = "debug" }
        default {
            Write-Host "Invalid choice, default to releases."
            $Mode = "releases"
        }
    }
}
switch ($Mode) {
    "releases"   {}
    "debug"      {}
    default {
        Write-Host "Unknown mode '$Mode'. Use --releases / --debug"
        exit 1
    }
}
# ---------- 0.5) release manifest (P0: single source of truth - icons, versions, release metadata) ----------
# Windows/Release.json is the only editing entry: the build validates it, injects the declared versions
# into all four executables, and embeds it as an assembly resource. The product no longer ships the file,
# nor default config.json/config_webhook.json (defaults are compiled in; first start writes the user
# config into the data directory).
$RelSrc = Join-Path $Win "Release.json"
$ManifestIcons = @{}          # role -> declared relative path (preserved verbatim for deployment)
$ManifestIconFiles = @{}      # role -> actual file resolved under the source root (valid only when present)
$RelComponents = @{}          # component role -> declared version
$BuildUtc = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
# Trailing ".0" segments are insignificant when comparing file versions against manifest declarations.
function Normalize-Version([string]$v) {
    $p = @($v -split '\.' | Where-Object { $_ -ne '' })
    while ($p.Count -gt 2 -and $p[-1] -eq '0') { $p = $p[0..($p.Count - 2)] }
    return ($p -join '.')
}
if (-not (Test-Path $RelSrc)) {
    Write-Host "!! Windows/Release.json missing: it is the single release-metadata source"
    exit 1
}
try {
    # Allow // comment lines at the top of the manifest (MIT notice, etc.); strip them because ConvertFrom-Json in PS 5.1 and 7 rejects comments
    $relObj = (Get-Content -LiteralPath $RelSrc | Where-Object { $_ -notmatch '^\s*//' }) -join "`n" | ConvertFrom-Json
} catch {
    Write-Host "!! Release.json unreadable: $($_.Exception.Message)"
    exit 1
}
# Required fields fail the build instead of shipping an incomplete manifest.
foreach ($field in 'project_name','repo','repo_name','release_name','release_version','build_target','license') {
    if ([string]::IsNullOrWhiteSpace([string]$relObj.$field)) {
        Write-Host "!! Release.json missing required field: $field"
        exit 1
    }
}
$RelVersion   = ([string]$relObj.release_version).Trim()
$RelName      = ([string]$relObj.release_name).Trim()
$RelTarget    = ([string]$relObj.build_target).Trim()
$Rid = switch ($RelTarget.ToLowerInvariant()) {
    "x64"   { "win-x64" }
    "arm64" { "win-arm64" }
    "x86"   { "win-x86" }
    default {
        Write-Host "!! Unknown Release.json build_target '$RelTarget'. Use x64 / arm64 / x86"
        exit 1
    }
}
$RelBuildNote = [string]$relObj.build_note
$AssetPattern = ([string]$relObj.asset_pattern).Trim()
foreach ($role in 'engine','webui','cli','dump') {
    $v = if ($relObj.components) { [string]$relObj.components.$role } else { "" }
    if ([string]::IsNullOrWhiteSpace($v)) {
        Write-Host "!! Release.json components.$role missing"
        exit 1
    }
    $RelComponents[$role] = $v.Trim()
}
# Package name: substitute release_version into asset_pattern's wildcard; fall back to a generated name.
$ZipName = if ($AssetPattern -and $AssetPattern.Contains('*')) { $AssetPattern.Replace('*', $RelVersion) }
           elseif ($AssetPattern) { $AssetPattern }
           else { "HeartRateMonitor-$RelVersion-$RelTarget.zip" }
if ($relObj.icons) {
    foreach ($p in $relObj.icons.PSObject.Properties) {
        $rel = ([string]$p.Value).Trim()
        if (-not $rel) { continue }
        $ManifestIcons[$p.Name] = $rel
        $src = Join-Path $Win ($rel -replace '^[.][\\/]', '')
        if (Test-Path $src) { $ManifestIconFiles[$p.Name] = $src }
    }
}
$IcoApp   = [string]$ManifestIconFiles['engine']
$IcoWebui = [string]$ManifestIconFiles['webui']
$IcoCli   = [string]$ManifestIconFiles['cli']
$IcoDump  = [string]$ManifestIconFiles['dump']
$IcoTray  = [string]$ManifestIconFiles['tray']
$IcoTrayRel = [string]$ManifestIcons['tray']
Write-Host ("==> manifest : {0} v{1} target={2} components engine={3} webui={4} cli={5} dump={6}" -f `
    $RelName, $RelVersion, $RelTarget, $RelComponents['engine'], $RelComponents['webui'], $RelComponents['cli'], $RelComponents['dump'])
Write-Host ("==> icons    : engine={0} webui={1} cli={2} dump={3} tray={4}" -f `
    ($IcoApp -ne ""), ($IcoWebui -ne ""), ($IcoCli -ne ""), ($IcoDump -ne ""), ($IcoTray -ne ""))
# P0: a missing component is a broken release; debug keeps iterating for development.
$Strict = ($Mode -ne "debug")
$Ts = Get-Date -Format "yyyyMMdd_HHmmss"
$Out = Join-Path $OutBase "$Mode-$Ts"
New-Item -ItemType Directory -Path $Out -Force | Out-Null
Write-Host "==> branch: $Mode"
Write-Host "==> output: $Out"
# ---------- 1) compile the C OSC engine ----------
Push-Location $Engine
Write-Host "==> gcc compile osc_engine.dll ..."
& gcc -shared -O2 -Wall -Wextra -o osc_engine.dll osc_engine.c -lws2_32
if ($LASTEXITCODE -ne 0) {
    Write-Host "!! gcc failed"
    Pop-Location
    exit 1
}
Pop-Location
# ---------- 1b) build the Vue Web frontend ----------
& (Join-Path $WebUi "build-webui.ps1") -SkipWeb:$SkipWeb
if ($LASTEXITCODE -ne 0) {
    Write-Host "!! web build failed"
    exit 1
}
# ---------- 2) dotnet publish ----------
$Conf = if ($Mode -eq "debug") { "Debug" } else { "Release" }
$publishArgs = @("publish", $App, "-c", $Conf, "-r", $Rid, "-o", $Out,
                 "-p:DebugType=embedded")
if ($Mode -eq "releases") {
    # Release: framework-dependent single file (requires the .NET 10 runtime)
    $publishArgs += "--self-contained", "false", "-p:PublishSingleFile=true"
} else {
    # Debug: retain the multi-file layout for development and debugging
    $publishArgs += "--self-contained", "false", "-p:PublishSingleFile=false"
}
if ($Mode -eq "debug") {
    $publishArgs += "-p:OutputType=Exe"     # show console window in debug
}
if ($IcoApp -and (Test-Path $IcoApp)) { $publishArgs += "-p:ApplicationIcon=$IcoApp" }
# P0: version + build time come from Release.json (single source of truth).
$publishArgs += "-p:Version=$($RelComponents['engine'])", "-p:ReleaseBuildTimeUtc=$BuildUtc"

Write-Host "==> dotnet publish ..."
& dotnet $publishArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host "!! dotnet publish failed"
    exit 1
}

# ---------- 3) copy the C engine DLL ----------
# P0: config.json / config_webhook.json / Release.json are no longer shipped beside the executables:
# defaults are compiled in, the manifest is embedded as an assembly resource, and first start writes the
# user config into the data directory (legacy exe-dir files still migrate on upgrade).
Copy-Item (Join-Path $Engine "osc_engine.dll") (Join-Path $Out "osc_engine.dll") -Force

# ---------- 3a) include LICENSE with artifacts (#24): deploy the open-source license to the output root ----------
# P0: Release.json itself is no longer deployed (embedded into every assembly at compile time).
$LicSrc = Join-Path $Win "LICENSE"
if (Test-Path $LicSrc) {
    Copy-Item $LicSrc (Join-Path $Out "LICENSE") -Force
    Write-Host "==> license copied to output"
} else {
    Write-Host "!! Windows/LICENSE missing (open-source notice not shipped)"
}

# ---------- 3b) copy webui (Vue frontend) ----------
$WebDist = Join-Path $env:TEMP "hrm-webui\dist"
if (Test-Path $WebDist) {
    Copy-Item -Recurse $WebDist (Join-Path $Out "webui")
    Write-Host "==> webui copied to output"
} else {
    Write-Host "!! webui dist missing (run build without -SkipWeb)"
}

# ---------- 3d) copy image resources (app icon / tray icon / promotional images; only when the directory exists) ----------
$ImgSrc = Join-Path $Win "image"
if (Test-Path $ImgSrc) {
    Copy-Item -Recurse $ImgSrc (Join-Path $Out "image")
    Write-Host "==> image copied to output"
} else {
    Write-Host "!! image dir missing (optional)"
}

# ---------- 3d2) deploy the tray icon (manifest icons.tray) to its declared output path ----------
# Step 3d already copied all icons under image/; this also deploys icons outside image/ or renamed icons
# to matching output paths, ensuring executable-relative manifest paths are available at runtime.
if ($IcoTray -and $IcoTrayRel) {
    $dst = Join-Path $Out ($IcoTrayRel -replace '^[.][\\/]', '')
    $dstDir = Split-Path -Parent $dst
    if (-not (Test-Path $dstDir)) { New-Item -ItemType Directory -Path $dstDir -Force | Out-Null }
    Copy-Item $IcoTray $dst -Force
    Write-Host "==> tray icon deployed: $IcoTrayRel"
}

# ---------- 3e) build + copy hrm-webui (embedded WebView2 shell; primary Release UI entry point) ----------
$ShellProj = Join-Path $Win "Shells\WebView2Host\WebView2Host.csproj"
if (Test-Path $ShellProj) {
    Write-Host "==> dotnet publish hrm-webui (WebView2 shell) ..."
    $ShellOut = Join-Path $Out "_shell"
    $shellArgs = @("publish", $ShellProj, "-c", $Conf, "-r", $Rid, "--self-contained", "false",
                   "-p:PublishSingleFile=false", "-p:DebugType=embedded", "-o", $ShellOut, "-v", "q", "--nologo",
                   "-p:Version=$($RelComponents['webui'])", "-p:ReleaseBuildTimeUtc=$BuildUtc")
    if ($IcoWebui -and (Test-Path $IcoWebui)) { $shellArgs += "-p:ApplicationIcon=$IcoWebui" }
    & dotnet $shellArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Host "!! hrm-webui shell publish failed"
    } else {
        # Copy only the shell and WebView2 dependencies; do not overwrite runtime files duplicated by the main program
        Get-ChildItem $ShellOut -File | Where-Object {
            $_.Name -like 'hrm-webui*' -or $_.Name -like 'Microsoft.Web.WebView2*'
        } | ForEach-Object { Copy-Item $_.FullName (Join-Path $Out $_.Name) -Force }
        $native = Join-Path $ShellOut "runtimes"
        if (Test-Path $native) { Copy-Item -Recurse $native (Join-Path $Out "runtimes") -Force }
        Remove-Item $ShellOut -Recurse -Force
        Write-Host "==> hrm-webui copied to output"
    }
}

$CliProj = Join-Path $Win "Shells\CliHost\CliHost.csproj"
if (Test-Path $CliProj) {
    Write-Host "==> dotnet publish hrmcli (CLI entry) ..."
    $CliOut = Join-Path $Out "_cli"
    # Packaging must match the main program: single-file output has no loose HeartRateMonitor.dll,
    # so a framework-dependent multi-file hrmcli would fail to find the application assembly.
    $cliArgs = @("publish", $CliProj, "-c", $Conf, "-r", $Rid, "-o", $CliOut,
                 "-p:DebugType=embedded", "-v", "q", "--nologo",
                 "-p:Version=$($RelComponents['cli'])", "-p:ReleaseBuildTimeUtc=$BuildUtc")
    if ($Mode -eq "releases") {
        $cliArgs += "--self-contained", "false", "-p:PublishSingleFile=true"
    } else {
        $cliArgs += "--self-contained", "false", "-p:PublishSingleFile=false"
    }
    if ($IcoCli -and (Test-Path $IcoCli)) { $cliArgs += "-p:ApplicationIcon=$IcoCli" }
    & dotnet $cliArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Host "!! hrmcli publish failed"
    } else {
        # Debug uses a multi-file layout; the main program already publishes the application assembly, so copy only hrmcli itself
        Get-ChildItem $CliOut -File | Where-Object { $_.Name -like 'hrmcli*' } |
            ForEach-Object { Copy-Item $_.FullName (Join-Path $Out $_.Name) -Force }
        Remove-Item $CliOut -Recurse -Force
        Write-Host "==> hrmcli copied to output"
    }
}

$DumpProj = Join-Path $Win "Shells\DumpHost\DumpHost.csproj"
if (Test-Path $DumpProj) {
    Write-Host "==> dotnet publish hrmdump (crash watchdog) ..."
    $DumpOut = Join-Path $Out "_dump"
    $dumpArgs = @("publish", $DumpProj, "-c", $Conf, "-r", $Rid, "-o", $DumpOut,
                  "-p:DebugType=embedded", "-v", "q", "--nologo")
    if ($Mode -eq "releases") {
        $dumpArgs += "--self-contained", "false", "-p:PublishSingleFile=true"
    } else {
        $dumpArgs += "--self-contained", "false", "-p:PublishSingleFile=false"
    }
    if ($IcoDump -and (Test-Path $IcoDump)) { $dumpArgs += "-p:ApplicationIcon=$IcoDump" }
    $dumpArgs += "-p:Version=$($RelComponents['dump'])", "-p:ReleaseBuildTimeUtc=$BuildUtc"
    & dotnet $dumpArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Host "!! hrmdump publish failed"
        if ($Strict) { exit 1 }
    } else {
        Get-ChildItem $DumpOut -File | Where-Object { $_.Name -like 'hrmdump*' } |
            ForEach-Object { Copy-Item $_.FullName (Join-Path $Out $_.Name) -Force }
        Remove-Item $DumpOut -Recurse -Force
        Write-Host "==> hrmdump copied to output"
    }
}

# ---------- 3h) component version cross-check (P0): every EXE must match Release.json ----------
$VersionOk = $true
$ExeByRole = @{ engine = "HeartRateMonitor.exe"; webui = "hrm-webui.exe"; cli = "hrmcli.exe"; dump = "hrmdump.exe" }
Write-Host "==> component versions:"
foreach ($role in 'engine','webui','cli','dump') {
    $exePath = Join-Path $Out $ExeByRole[$role]
    $expect = $RelComponents[$role]
    if (-not (Test-Path $exePath)) {
        Write-Host ("    {0,-22} MISSING (manifest {1})" -f $ExeByRole[$role], $expect)
        $VersionOk = $false
        continue
    }
    $actual = [string](Get-Item $exePath).VersionInfo.FileVersion
    if ((Normalize-Version $actual) -ne (Normalize-Version $expect)) {
        $shown = $(if ($actual -ne "") { $actual } else { "?" })
        Write-Host ("    {0,-22} {1}  != manifest {2}" -f $ExeByRole[$role], $shown, $expect)
        $VersionOk = $false
    } else {
        Write-Host ("    {0,-22} {1}" -f $ExeByRole[$role], $actual)
    }
}
if (-not $VersionOk) {
    Write-Host "!! component version check failed (output does not match Release.json)"
    if ($Strict) { exit 1 }
    Write-Host "   (debug build continues; fix before releasing)"
}

# ---------- 3i) package + checksum (P0): zip the release output and write a SHA-256 sidecar ----------
$ZipMade = ""
if ($Mode -ne "debug") {
    $ZipPath = Join-Path $OutBase $ZipName
    if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
    Compress-Archive -Path (Join-Path $Out '*') -DestinationPath $ZipPath -CompressionLevel Optimal
    $hash = (Get-FileHash $ZipPath -Algorithm SHA256).Hash
    [IO.File]::WriteAllText("$ZipPath.sha256", "$hash  $ZipName`r`n", (New-Object Text.UTF8Encoding($false)))
    $ZipMade = $ZipName
    Write-Host ("==> package : {0} ({1:N0} bytes)  sha256 {2}..." -f $ZipName, (Get-Item $ZipPath).Length, $hash.Substring(0, 16))
}

# ---------- 4) summary ----------
Write-Host ""
Write-Host "========== BUILD SUMMARY =========="
$exe = Join-Path $Out "HeartRateMonitor.exe"
$dll = Join-Path $Out "osc_engine.dll"
$fileCount = (Get-ChildItem $Out -File).Count
Write-Host ("app       : {0} ({1} bytes)" -f $exe, (Get-Item $exe).Length)
Write-Host ("engine    : {0} ({1} bytes)" -f $dll, (Get-Item $dll).Length)
$web = Test-Path (Join-Path $Out "webui\index.html")
$img = Test-Path (Join-Path $Out "image")
$shl = Test-Path (Join-Path $Out "hrm-webui.exe")
$cli = Test-Path (Join-Path $Out "hrmcli.exe")
$dump = Test-Path (Join-Path $Out "hrmdump.exe")
Write-Host ("webui     : {0}" -f $(if ($web) { "ok (Vue V3)" } else { "MISSING" }))
Write-Host ("hrm-webui : {0}" -f $(if ($shl) { "ok (WebView2 shell)" } else { "MISSING" }))
Write-Host ("hrmcli    : {0}" -f $(if ($cli) { "ok (CLI entry)" } else { "MISSING" }))
Write-Host ("hrmdump   : {0}" -f $(if ($dump) { "ok (crash watchdog)" } else { "MISSING" }))
Write-Host ("image     : {0}" -f $(if ($img) { "ok (resources)" } else { "-" }))
Write-Host ("release   : {0} v{1}  build {2} UTC  target {3}" -f $RelName, $RelVersion, $BuildUtc, $RelTarget)
Write-Host ("versions  : engine={0} webui={1} cli={2} dump={3}" -f `
    $RelComponents['engine'], $RelComponents['webui'], $RelComponents['cli'], $RelComponents['dump'])
Write-Host ("shipped   : no default config, no Release.json (embedded)")
Write-Host ("package   : {0}" -f $(if ($ZipMade) { $ZipMade } else { "-" }))
Write-Host ("dll count : {0} file(s) in output" -f $fileCount)
Write-Host ("output    : {0}" -f $Out)
Write-Host "==================================="
