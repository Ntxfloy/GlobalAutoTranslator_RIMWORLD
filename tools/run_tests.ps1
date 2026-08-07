$ErrorActionPreference = 'Stop'
Set-Location "$PSScriptRoot\.."

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[Console]::OutputEncoding = $utf8NoBom

$testExe = "tests\GATTest.exe"

# Locate compiler (same as build.ps1)
$cscCandidates = @(
    "$PSScriptRoot\..\..\roslyn\tasks\net472\csc.exe",
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe"
)
$Csc = $cscCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $Csc) { throw "csc.exe not found." }

# Locate RimWorld assemblies (using same logic as build.ps1)
$RimWorldRoot = $env:RIMWORLD_ROOT
if (-not $RimWorldRoot) {
    # Simple hardcoded path for the test runner if env var is missing
    $RimWorldRoot = "D:\SteamLibrary\steamapps\common\RimWorld"
}
$MANAGED = Join-Path $RimWorldRoot "RimWorldWin64_Data\Managed"

# Locate Harmony
$HarmonyDll = $env:RIMWORLD_HARMONY_DLL
if (-not $HarmonyDll) {
    $HarmonyDll = "D:\SteamLibrary\steamapps\workshop\content\294100\2009463077\Current\Assemblies\0Harmony.dll"
}

Write-Host "Compiling test harness..." -ForegroundColor Cyan

# Link Unity/RimWorld assemblies so we don't have to mock everything
& $Csc `
    "/target:exe" `
    "/out:$testExe" `
    "/r:$MANAGED\Assembly-CSharp.dll" `
    "/r:$MANAGED\Assembly-CSharp-firstpass.dll" `
    "/r:$MANAGED\UnityEngine.dll" `
    "/r:$MANAGED\UnityEngine.CoreModule.dll" `
    "/r:$MANAGED\UnityEngine.IMGUIModule.dll" `
    "/r:$MANAGED\UnityEngine.TextRenderingModule.dll" `
    "/r:$MANAGED\netstandard.dll" `
    "/r:$MANAGED\System.Net.Http.dll" `
    "/r:$HarmonyDll" `
    "Source\PlaceholderGuard.cs" `
    "Source\TranslationCache.cs" `
    "Source\SelfTest.cs" `
    "Source\Prompt.cs" `
    "Source\MiniJson.cs" `
    "Source\LlmClient.cs" `
    "Source\TranslateWorker.cs" `
    "Source\GATMod.cs" `
    "Source\DefPostProcessor.cs" `
    "Source\LanguageExporter.cs" `
    "Source\Patches.cs" `
    "Source\GATBoot.cs" `
    "tests\Program.cs"

if ($LASTEXITCODE -ne 0) { throw "Compilation failed (exit $LASTEXITCODE)" }

Write-Host "Running tests..." -ForegroundColor Green
& ".\$testExe"
