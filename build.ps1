##############################################################################
# build.ps1 -- GlobalAutoTranslator build script
# Run: powershell -ExecutionPolicy Bypass -File build.ps1
# Optional: -RimWorldRoot "E:\Games\RimWorld" -NoInstall
##############################################################################

param(
    [string]$RimWorldRoot = $env:RIMWORLD_ROOT,
    [string]$HarmonyDll   = $env:RIMWORLD_HARMONY_DLL,
    [string]$Csc          = $env:ROSLYN_CSC,
    [switch]$NoInstall
)

$ErrorActionPreference = "Stop"

if (!$NoInstall -and (Get-Process RimWorldWin64 -ErrorAction SilentlyContinue)) {
    throw "RimWorld is running. Close the game before deploying to Mods."
}

$SCRIPT_DIR = Split-Path -Parent $MyInvocation.MyCommand.Path

# ---- Locate RimWorld --------------------------------------------------------
function Find-RimWorldRoot {
    # 1. Steam libraries listed in libraryfolders.vdf
    $steamRoots = @(
        "${env:ProgramFiles(x86)}\Steam",
        "$env:ProgramFiles\Steam"
    ) | Where-Object { $_ -and (Test-Path $_) }

    $libs = New-Object System.Collections.Generic.List[string]
    foreach ($steam in $steamRoots) {
        $libs.Add($steam)
        $vdf = Join-Path $steam "steamapps\libraryfolders.vdf"
        if (Test-Path $vdf) {
            Select-String -Path $vdf -Pattern '"path"\s+"(.+?)"' -AllMatches |
                ForEach-Object { $_.Matches } |
                ForEach-Object { $libs.Add($_.Groups[1].Value.Replace('\\', '\')) }
        }
    }
    # 2. Common manual locations
    foreach ($d in @("C:", "D:", "E:", "F:")) { $libs.Add("$d\SteamLibrary") }

    foreach ($lib in $libs) {
        $candidate = Join-Path $lib "steamapps\common\RimWorld"
        if (Test-Path (Join-Path $candidate "RimWorldWin64_Data\Managed\Assembly-CSharp.dll")) {
            return $candidate
        }
    }
    return $null
}

if (-not $RimWorldRoot) { $RimWorldRoot = Find-RimWorldRoot }
if (-not $RimWorldRoot -or -not (Test-Path $RimWorldRoot)) {
    throw "RimWorld not found. Pass -RimWorldRoot 'X:\path\to\RimWorld' or set `$env:RIMWORLD_ROOT."
}
$MANAGED = Join-Path $RimWorldRoot "RimWorldWin64_Data\Managed"
Write-Host "[BUILD] RimWorld: $RimWorldRoot" -ForegroundColor DarkGray

# ---- Locate Harmony ---------------------------------------------------------
if (-not $HarmonyDll) {
    $workshop = Join-Path (Split-Path -Parent (Split-Path -Parent $RimWorldRoot)) "workshop\content\294100"
    $candidates = @(
        (Join-Path $workshop "2009463077\Current\Assemblies\0Harmony.dll"),   # Harmony
        (Join-Path $workshop "2009463077\1.6\Assemblies\0Harmony.dll"),
        (Join-Path $workshop "2009463077\1.5\Assemblies\0Harmony.dll"),
        (Join-Path $workshop "2009463077\1.4\Assemblies\0Harmony.dll"),
        (Join-Path $RimWorldRoot "Mods\Harmony\Current\Assemblies\0Harmony.dll"),
        (Join-Path $workshop "839005762\1.6\Assemblies\0Harmony.dll")         # HugsLib fallback
    )
    $HarmonyDll = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $HarmonyDll) {
    throw "0Harmony.dll not found. Subscribe to https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077 or pass -HarmonyDll."
}
if ($HarmonyDll -like "*839005762*") {
    Write-Warning "Using the HugsLib copy of Harmony. Prefer the standalone Harmony mod (2009463077)."
}

# ---- Locate compiler --------------------------------------------------------
if (-not $Csc) {
    $cscCandidates = @(
        "$SCRIPT_DIR\..\roslyn\tasks\net472\csc.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe"
    )
    $Csc = $cscCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $Csc) { throw "csc.exe not found. Pass -Csc 'path\to\csc.exe'." }

$CSC      = $Csc
$HARMONY  = $HarmonyDll
$SRC_DIR  = "$SCRIPT_DIR\Source"
$OUT_DLL  = "$SCRIPT_DIR\Assemblies\GlobalAutoTranslator.dll"
$MODS_DST = Join-Path $RimWorldRoot "Mods\GlobalAutoTranslator"

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OUT_DLL) | Out-Null

foreach ($p in @($MANAGED, $HARMONY, $CSC)) {
    if (-not (Test-Path $p)) { throw "Not found: $p" }
}

# ---- Generate AssemblyInfo.cs -------------------------------------------------
$assemblyInfo = @"
using System.Reflection;

[assembly: AssemblyTitle("GlobalAutoTranslator")]
[assembly: AssemblyDescription("AI Translator for RimWorld")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("Ayder")]
[assembly: AssemblyProduct("GlobalAutoTranslator")]
[assembly: AssemblyCopyright("Copyright (c) 2026")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: AssemblyVersion("33.1.0.0")]
[assembly: AssemblyFileVersion("33.1.0.0")]
"@
Set-Content -Path "$SRC_DIR\AssemblyInfo.cs" -Value $assemblyInfo -Encoding UTF8

# ---- Compile ----------------------------------------------------------------
Write-Host "[BUILD] Compiling..." -ForegroundColor Cyan
$SRC = Get-ChildItem "$SRC_DIR\*.cs" -Recurse | Where-Object { $_.FullName -notmatch '\\tests\\' } | Select-Object -ExpandProperty FullName

& $CSC `
    "/target:library" `
    "/r:$MANAGED\Assembly-CSharp.dll" `
    "/r:$MANAGED\Assembly-CSharp-firstpass.dll" `
    "/r:$MANAGED\UnityEngine.dll" `
    "/r:$MANAGED\UnityEngine.CoreModule.dll" `
    "/r:$MANAGED\UnityEngine.IMGUIModule.dll" `
    "/r:$MANAGED\UnityEngine.TextRenderingModule.dll" `
    "/r:$MANAGED\netstandard.dll" `
    "/r:$MANAGED\System.Net.Http.dll" `
    "/r:$HARMONY" `
    "/out:$OUT_DLL" `
    $SRC

if ($LASTEXITCODE -ne 0) { throw "Compilation failed (exit $LASTEXITCODE)" }
Write-Host "[BUILD] DLL OK: $OUT_DLL" -ForegroundColor Green

# ---- Install ----------------------------------------------------------------
if ($NoInstall) {
    Write-Host "[INSTALL] Skipped (-NoInstall)." -ForegroundColor Yellow
    return
}
Write-Host "[INSTALL] Deploying to Mods..." -ForegroundColor Cyan

# Wipe destination and recreate clean — avoids Copy-Item subfolder nesting bug
if (Test-Path $MODS_DST) { Remove-Item $MODS_DST -Recurse -Force }
New-Item -ItemType Directory -Path $MODS_DST | Out-Null

foreach ($dir in @("About", "Assemblies", "Languages", "proxy")) {
    $srcPath = "$SCRIPT_DIR\$dir"
    if (Test-Path $srcPath) {
        Copy-Item -Path $srcPath -Destination "$MODS_DST\$dir" -Recurse
    }
}
if (Test-Path "$SCRIPT_DIR\README.md") {
    Copy-Item "$SCRIPT_DIR\README.md" "$MODS_DST\README.md"
}

Write-Host "[INSTALL] Done: $MODS_DST" -ForegroundColor Green
Write-Host "[INSTALL] Contents:" -ForegroundColor Yellow
Get-ChildItem $MODS_DST -Recurse | Select-Object FullName
