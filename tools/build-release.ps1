param(
    [string]$Out = 'D:\SaveSync-Release',
    [string]$Game = 'G:\SteamLibrary\steamapps\common\Software Inc'
)

# Builds the folder that goes to the Workshop: only the mod files, nothing from the project around them.

& (Join-Path $PSScriptRoot 'check.ps1') -Game $Game
if ($LASTEXITCODE -ne 0) {
    Write-Output 'Check failed, nothing built.'
    exit 1
}

$source = Join-Path $PSScriptRoot '..\mod\SaveSync'
$modOut = Join-Path $Out 'SaveSync'
if (Test-Path $modOut) {
    Remove-Item -Path $modOut -Recurse -Force -Confirm:$false
}
New-Item -ItemType Directory -Force -Path $modOut | Out-Null

# The Workshop uploader accepts only some file types in a code mod, xml is not one of them.
$allowed = '\.(cs|txt|tyd|png)$'
Get-ChildItem $source -File | Where-Object { $_.Name -match $allowed } | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $modOut $_.Name)
}

# Nothing that names local notes or test material may ship. Extra words can be listed one per line in tools/release-banned.txt.
$words = @('SyncTest', 'RealTest', 'testsaves', 'testmods')
$extra = Join-Path $PSScriptRoot 'release-banned.txt'
if (Test-Path $extra) {
    $words += Get-Content $extra | Where-Object { $_.Trim() -ne '' } | ForEach-Object { $_.Trim() }
}
$banned = ($words | ForEach-Object { [regex]::Escape($_) }) -join '|'
$hits = Get-ChildItem $modOut -File | Where-Object { $_.Name -match '\.(cs|txt|tyd)$' } | Select-String -Pattern $banned
if ($hits) {
    foreach ($hit in $hits) {
        Write-Output "$($hit.Filename):$($hit.LineNumber): $($hit.Line.Trim())"
    }
    Write-Output 'Release contains project references, fix them first.'
    exit 1
}

$workshop = Join-Path $PSScriptRoot '..\workshop'
$workshopOut = Join-Path $Out 'workshop'
New-Item -ItemType Directory -Force -Path $workshopOut | Out-Null
Get-ChildItem $workshop -File | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $workshopOut $_.Name) -Force
}

Write-Output "Mod files in $modOut"
Get-ChildItem $modOut | ForEach-Object { '  ' + $_.Name }
Write-Output "Workshop page files in $workshopOut"
Get-ChildItem $workshopOut | ForEach-Object { '  ' + $_.Name }
