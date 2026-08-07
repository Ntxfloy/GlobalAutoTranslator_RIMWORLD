$files = @(
    'Assemblies\GlobalAutoTranslator.dll',
    'D:\SteamLibrary\steamapps\common\RimWorld\Mods\GlobalAutoTranslator\Assemblies\GlobalAutoTranslator.dll'
)
foreach ($f in $files) {
    $fi = Get-Item $f
    $h = (Get-FileHash -Path $f -Algorithm SHA256).Hash
    Write-Host ($fi.FullName + ' | ' + $fi.Length + ' bytes | ' + $fi.LastWriteTime.ToString('o') + ' | SHA256=' + $h)
}
