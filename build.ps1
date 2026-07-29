##############################################################################
# build.ps1 -- GlobalAutoTranslator build script
# Run: powershell -ExecutionPolicy Bypass -File build.ps1
##############################################################################

$ErrorActionPreference = "Stop"

if (Get-Process RimWorldWin64 -ErrorAction SilentlyContinue) {
    throw "RimWorld is running. Close the game before building."
}

$MANAGED  = "D:\SteamLibrary\steamapps\common\RimWorld\RimWorldWin64_Data\Managed"
$HARMONY  = "D:\SteamLibrary\steamapps\workshop\content\294100\2009463077\Current\Assemblies\0Harmony.dll"
if (-not (Test-Path $HARMONY)) {
    $HARMONY = "D:\SteamLibrary\steamapps\workshop\content\294100\839005762\1.6\Assemblies\0Harmony.dll"
    Write-Warning "Harmony (2009463077) not found, using HugsLib copy. Subscribe: https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077"
}

$SCRIPT_DIR = Split-Path -Parent $MyInvocation.MyCommand.Path
$CSC        = "$SCRIPT_DIR\..\roslyn\tasks\net472\csc.exe"
$SRC_DIR    = "$SCRIPT_DIR\Source"
$OUT_DLL    = "$SCRIPT_DIR\Assemblies\GlobalAutoTranslator.dll"
$MODS_DST   = "D:\SteamLibrary\steamapps\common\RimWorld\Mods\GlobalAutoTranslator"

foreach ($p in @($MANAGED, $HARMONY, $CSC)) {
    if (-not (Test-Path $p)) { throw "Not found: $p" }
}

# ---- Compile ----------------------------------------------------------------
Write-Host "[BUILD] Compiling..." -ForegroundColor Cyan
$SRC = Get-ChildItem "$SRC_DIR\*.cs" | Select-Object -ExpandProperty FullName

& $CSC `
    "/target:library" `
    "/r:$MANAGED\Assembly-CSharp.dll" `
    "/r:$MANAGED\Assembly-CSharp-firstpass.dll" `
    "/r:$MANAGED\UnityEngine.dll" `
    "/r:$MANAGED\UnityEngine.CoreModule.dll" `
    "/r:$MANAGED\UnityEngine.IMGUIModule.dll" `
    "/r:$MANAGED\netstandard.dll" `
    "/r:$MANAGED\System.Net.Http.dll" `
    "/r:$HARMONY" `
    "/out:$OUT_DLL" `
    $SRC

if ($LASTEXITCODE -ne 0) { throw "Compilation failed (exit $LASTEXITCODE)" }
Write-Host "[BUILD] DLL OK: $OUT_DLL" -ForegroundColor Green

# ---- Install ----------------------------------------------------------------
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
