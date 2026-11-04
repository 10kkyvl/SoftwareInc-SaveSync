param(
    [string]$Game = 'G:\SteamLibrary\steamapps\common\Software Inc'
)

& (Join-Path $PSScriptRoot 'check.ps1') -Game $Game
if ($LASTEXITCODE -ne 0) {
    Write-Output 'Check failed, nothing installed.'
    exit 1
}

$source = Join-Path $PSScriptRoot '..\mod\SaveSync'
$target = Join-Path $Game 'DLLMods\SaveSync'
New-Item -ItemType Directory -Force -Path $target | Out-Null

# The game writes the Workshop item ID into meta.tyd after the first upload and keeps TypeInfo.txt and its compile cache here.
# meta.tyd is copied only when missing, so a later install never loses the item ID.
$gameFiles = @('meta.tyd', 'TypeInfo.txt', 'SaveSync.dcache', 'SaveSyncH.bin')
$modFiles = Get-ChildItem $source -File | Where-Object { $_.Name -match '\.(cs|txt|tyd|png)$' }

foreach ($file in $modFiles) {
    $destination = Join-Path $target $file.Name
    if ($file.Name -eq 'meta.tyd' -and (Test-Path $destination)) {
        continue
    }
    Copy-Item $file.FullName $destination -Force
}

# Anything else left from older builds would be uploaded too, and the uploader refuses some file types.
$keep = @($modFiles | ForEach-Object { $_.Name }) + $gameFiles
Get-ChildItem $target -File | Where-Object { $keep -notcontains $_.Name } | ForEach-Object {
    Remove-Item $_.FullName -Force -Confirm:$false
    Write-Output "Removed old file $($_.Name)"
}
Write-Output "Installed to $target"
