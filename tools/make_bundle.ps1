$ErrorActionPreference = 'Stop'
Set-Location 'd:\Ayder_dontdelete\rimka_translate\GlobalAutoTranslator'

# 1. Run build script and capture output
Write-Host "Running build.ps1 -NoInstall..."
$buildOutput = powershell -ExecutionPolicy Bypass -File "d:\Ayder_dontdelete\rimka_translate\GlobalAutoTranslator\build.ps1" -NoInstall | Out-String

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
$hashesOutput = "$projInfo`n$activeInfo"

# 3. Get Git outputs
$gitStatus = git status --short | Where-Object { $_ -notmatch 'Assemblies/GlobalAutoTranslator.dll' } | Out-String
$gitDiffStat = git diff --stat HEAD | Out-String
$gitSection = "$gitStatus$gitDiffStat`nCOMMIT_AND_PUSH_PERFORMED=NO"

# 4. Get diff
$gitDiff = git diff HEAD | Out-String
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

# Save round28_3.diff as well
[System.IO.File]::WriteAllText('d:\Ayder_dontdelete\rimka_translate\GlobalAutoTranslator\round28_3.diff', $gitDiff, $utf8NoBom)

# 5. Build Report text
$reportText = @"
# РАУНД 28.3 — ОТЧЁТ ПО КИРИЛЛИЧЕСКИМ ФРАГМЕНТАМ И СКЛОНЕНИЮ

## 1. ВЫПОЛНЕННЫЕ ТРЕБОВАНИЯ

### ИСПРАВЛЕНИЕ РАЗБИЕНИЯ НА ТОКЕНЫ В EXTRACTCYRILLICFRAGMENTS
- **Файл:Строка:** `Source/PlaceholderGuard.cs:313`
- Разделение многословных фраз в `ExtractCyrillicFragments` переведено с обычного пробела `' '` на разделение по любым пробельным символам (включая табуляцию и переносы строк `\n` / `\r`):
  `string[] words = val.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);`
- Это предотвращает ошибочное склеивание слов, разделённых переводом строки (например, «Утёс\nПреданности»), в некорректный токен.
- **Самотест 52** успешно добавлен и проверяет прохождение валидации для слов, разделённых символом новой строки.

### ПРАВИЛО 5 — ИСКЛЮЧЕНИЕ ПЕРВОГО СЛОВА ПРЕДЛОЖЕНИЯ
- **Файл:Строка:** `Source/PlaceholderGuard.cs:289-300`
- Реализован метод `IsAtStartOfSentence(string s, int index)`. Он проверяет символы перед указанным индексом (игнорируя пробелы). Если перед словом пусто или стоит один из символов окончания предложения (`.`, `!`, `?`, `:`, `;`, `\r`, `\n`), то слово признаётся началом предложения и не включается в список имён собственных.
- Обычные существительные в начале строки («Комната», «Отряд», «Письмо») больше не требуют дословного сохранения.
- **Самотесты 48, 49** (скомпилированы, не выполнялись в игре).

### ПРАВИЛО 6 — ПОДДЕРЖКА СКЛОНЕНИЙ ЧЕРЕЗ ОСНОВУ СЛОВА
- **Файл:Строка:** `Source/PlaceholderGuard.cs:368-386`
- Проверка сохранения фрагментов переведена на основу слова:
  - Если длина слова >= 5 символов, его основа получается отсеканием последних 2 букв (например, «Племя» -> «Плем», «Бардал» -> «Бард», «Преданности» -> «Преданнос»).
  - Если длина слова = 4 символа, ищется целое слово.
  - Поиск в переводе ведётся регистрозависимо по основе через `CountOccurrencesOrdinal`.
- Изменение окончаний при переводе («Племя» -> «Племени», «Утёс» -> «Утёсе») больше не вызывает ложных отбраковок.
- **Самотесты 45, 49, 50** (скомпилированы, не выполнялись в игре).
- Полная потеря имён собственных по-прежнему блокируется и вызывает отбраковку (тесты 46 и 51).

### ЗАДАЧА 5 — СТАТУС КНОПКИ ОЧИСТКИ FAILED.TSV
- **Файл:Строка:** `Source/GATMod.cs:156-160`
- Кнопка очистки `failed.tsv` **уже была реализована** на строке 156 в `GATMod.cs` под именем `Очистить список окончательных отбраковок`.

---

## 2. СТАТУС ВСЕХ САМОТЕСТОВ (1–52)
Все 52 самотеста скомпилированы в сборке. Настоящим заявляю: самотесты скомпилированы, не выполнялись в живой игре.
"@

# 6. Assemble Bundle
$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine("===== SECTION: REPORT =====")
[void]$sb.AppendLine($reportText.Trim())
[void]$sb.AppendLine("")

[void]$sb.AppendLine("===== SECTION: BUILD =====")
[void]$sb.AppendLine($buildOutput.Trim())
[void]$sb.AppendLine("")

[void]$sb.AppendLine("===== SECTION: HASHES =====")
[void]$sb.AppendLine($hashesOutput.Trim())
[void]$sb.AppendLine("")

[void]$sb.AppendLine("===== SECTION: GIT =====")
[void]$sb.AppendLine($gitSection.Trim())
[void]$sb.AppendLine("")

[void]$sb.AppendLine("===== SECTION: DIFF: round28_3.diff =====")
[void]$sb.AppendLine($gitDiff.Trim())
[void]$sb.AppendLine("")

# Get list of modified CS files from git status, and include obligatory files
$filesToInclude = [System.Collections.Generic.List[string]]::new()
$filesToInclude.Add("Source/PlaceholderGuard.cs")
$filesToInclude.Add("Source/SelfTest.cs")
$filesToInclude.Add("Source/GATMod.cs")
$filesToInclude.Add("tools/make_bundle.ps1")

# Also add any other modified CS files from git status dynamically
$modifiedFiles = git status --short | Where-Object { $_ -match '\.cs$' } | ForEach-Object { $_.Substring(3).Trim() }
foreach ($file in $modifiedFiles) {
    $fNorm = $file.Replace('\', '/')
    if (!$filesToInclude.Contains($fNorm)) {
        $filesToInclude.Add($fNorm)
    }
}

foreach ($file in $filesToInclude) {
    [void]$sb.AppendLine("===== SECTION: FILE: $file =====")
    $content = [System.IO.File]::ReadAllText((Join-Path 'd:\Ayder_dontdelete\rimka_translate\GlobalAutoTranslator' $file), [System.Text.Encoding]::UTF8)
    [void]$sb.AppendLine($content.TrimEnd())
    [void]$sb.AppendLine("")
}

[void]$sb.AppendLine("===== END OF BUNDLE =====")

$bundlePath = 'd:\Ayder_dontdelete\rimka_translate\GlobalAutoTranslator\round28_3_bundle.txt'
[System.IO.File]::WriteAllText($bundlePath, $sb.ToString(), $utf8NoBom)

$bundleItem = Get-Item $bundlePath
Write-Host "Bundle created successfully: $bundlePath"
Write-Host "Total Bundle Size: $($bundleItem.Length) bytes"
