$ErrorActionPreference = 'Stop'
Set-Location 'd:\Ayder_dontdelete\rimka_translate\GlobalAutoTranslator'

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

# 1. Run build script to build the actual DLL
Write-Host "Running build.ps1 -NoInstall..."
$buildOutput = powershell -NoProfile -ExecutionPolicy Bypass -File "d:\Ayder_dontdelete\rimka_translate\GlobalAutoTranslator\build.ps1" -NoInstall | Out-String

# 2. Get DLL Hashes
$projDll = "d:\Ayder_dontdelete\rimka_translate\GlobalAutoTranslator\Assemblies\GlobalAutoTranslator.dll"
$activeDll = "D:\SteamLibrary\steamapps\common\RimWorld\Mods\GlobalAutoTranslator\Assemblies\GlobalAutoTranslator.dll"

$projItem = Get-Item $projDll
$projHash = (Get-FileHash $projDll -Algorithm SHA256).Hash
$projInfo = "Project DLL: Size=$($projItem.Length), Time=$($projItem.LastWriteTime.ToString('o')), SHA256=$projHash"

$activeInfo = ""
if (Test-Path $activeDll) {
    $actItem = Get-Item $activeDll
    $actHash = (Get-FileHash $activeDll -Algorithm SHA256).Hash
    $activeInfo = "Active  DLL: Size=$($actItem.Length), Time=$($actItem.LastWriteTime.ToString('o')), SHA256=$actHash"
} else {
    $activeInfo = "Active  DLL: Not found"
}

# 3. Get diff
$gitDiff = git diff HEAD | Out-String

# 4. Compile and Run SelfTest in standalone mode to capture output
Write-Host "Compiling and running SelfTest runner..."
$testExe = "tests\GATTest.exe"
$selftestOutput = ""
try {
    # Assuming run_tests.ps1 handles compilation and execution correctly
    $selftestOutput = & "tools\run_tests.ps1" | Out-String
} catch {
    $selftestOutput = "Test harness failed: $_"
}

# 5. Build Report text
$reportText = ""
if (Test-Path "round32_report.md") {
    $reportText = [System.IO.File]::ReadAllText("round32_report.md", $utf8NoBom)
} else {
    $reportText = "Report round32_report.md not found."
}

# 6. Assemble Bundle
$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine("===== SECTION: REPORT =====")
[void]$sb.AppendLine($reportText.Trim())
[void]$sb.AppendLine("")

[void]$sb.AppendLine("===== SECTION: BUILD =====")
[void]$sb.AppendLine($buildOutput.Trim())
[void]$sb.AppendLine("")

[void]$sb.AppendLine("===== SECTION: HASHES =====")
[void]$sb.AppendLine($projInfo)
[void]$sb.AppendLine($activeInfo)
[void]$sb.AppendLine("")

[void]$sb.AppendLine("===== SECTION: GIT =====")
[void]$sb.AppendLine($gitDiff.Trim())
[void]$sb.AppendLine("")

[void]$sb.AppendLine("===== SECTION: SELFTEST_RESULT =====")
[void]$sb.AppendLine($selftestOutput.Trim())
[void]$sb.AppendLine("")

$filesToInclude = @(
    "Source/GATMod.cs",
    "Source/Patches.cs",
    "Source/TranslationCache.cs",
    "Source/SelfTest.cs",
    "tools/run_tests.ps1",
    "build.ps1",
    "tests/Mocks.cs",
    "tests/Program.cs"
)

foreach ($file in $filesToInclude) {
    if (Test-Path $file) {
        [void]$sb.AppendLine("===== SECTION: FILE: $file =====")
        $content = [System.IO.File]::ReadAllText((Join-Path 'd:\Ayder_dontdelete\rimka_translate\GlobalAutoTranslator' $file), [System.Text.Encoding]::UTF8)
        [void]$sb.AppendLine($content.TrimEnd())
        [void]$sb.AppendLine("")
    }
}

[void]$sb.AppendLine("===== END OF BUNDLE =====")

$bundlePath = 'd:\Ayder_dontdelete\rimka_translate\GlobalAutoTranslator\round32_bundle.txt'
[System.IO.File]::WriteAllText($bundlePath, $sb.ToString(), $utf8NoBom)

$bundleItem = Get-Item $bundlePath
Write-Host "Bundle created successfully: $bundlePath"
Write-Host "Total Bundle Size: $($bundleItem.Length) bytes"
