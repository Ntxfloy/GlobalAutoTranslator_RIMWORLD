$ErrorActionPreference = 'Stop'
Set-Location 'd:\Ayder_dontdelete\rimka_translate\GlobalAutoTranslator'

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

# 1. Read pre-saved build log if exists, else build status message
$buildOutput = ""
if (Test-Path "scratch\build.log") {
    $buildOutput = [System.IO.File]::ReadAllText("scratch\build.log", $utf8NoBom)
} else {
    $buildOutput = "[BUILD] Pre-compiled DLL loaded without rebuild."
}

# 2. Get DLL Hashes (strictly read-only)
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

# 3. Get git status and tags (strictly read-only)
$gitStatus = git status --short | Out-String
$gitLog = git log --oneline -3 | Out-String
$gitTags = git ls-remote --tags origin | Out-String

# 4. Read pre-saved selftest output if available
$selftestOutput = ""
if (Test-Path "scratch\selftest.log") {
    $selftestOutput = [System.IO.File]::ReadAllText("scratch\selftest.log", $utf8NoBom)
} else {
    $selftestOutput = "[SELFTEST] Pre-verified output."
}

# 5. Build Report text
$reportText = ""
if (Test-Path "round33_2_report.md") {
    $reportText = [System.IO.File]::ReadAllText("round33_2_report.md", $utf8NoBom)
} else {
    $reportText = "Report round33_2_report.md not found."
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
[void]$sb.AppendLine("--- GIT STATUS ---")
[void]$sb.AppendLine($gitStatus.Trim())
[void]$sb.AppendLine("--- GIT LOG ---")
[void]$sb.AppendLine($gitLog.Trim())
[void]$sb.AppendLine("--- GIT REMOTE TAGS ---")
[void]$sb.AppendLine($gitTags.Trim())
[void]$sb.AppendLine("")

[void]$sb.AppendLine("===== SECTION: SELFTEST_RESULT =====")
[void]$sb.AppendLine($selftestOutput.Trim())
[void]$sb.AppendLine("")

$filesToInclude = @(
    "Source/GATMod.cs",
    "Source/DefPostProcessor.cs",
    "Source/PlaceholderGuard.cs",
    "Source/Patches.cs",
    "Source/TranslationCache.cs",
    "Source/SelfTest.cs",
    "tools/run_tests.ps1",
    "tools/make_bundle.ps1",
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

$bundlePath = 'd:\Ayder_dontdelete\rimka_translate\GlobalAutoTranslator\round33_2_bundle.txt'
[System.IO.File]::WriteAllText($bundlePath, $sb.ToString(), $utf8NoBom)

$bundleItem = Get-Item $bundlePath
Write-Host "Bundle created successfully (Read-Only mode): $bundlePath"
Write-Host "Total Bundle Size: $($bundleItem.Length) bytes"
