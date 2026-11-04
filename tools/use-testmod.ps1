param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('v1', 'v2', 'v3', 'v4', 'v5', 'v6', 'v7', 'none')]
    [string]$Version,
    [string]$Game = 'G:\SteamLibrary\steamapps\common\Software Inc'
)

if (Get-Process -Name 'Software Inc' -ErrorAction SilentlyContinue) {
    Write-Output 'Close the game first: data mods are read when it starts.'
    exit 1
}

foreach ($name in 'SyncTest', 'SyncTestB') {
    $target = Join-Path $Game "Mods\$name"
    if (Test-Path $target) {
        Remove-Item -Path $target -Recurse -Force -Confirm:$false
    }
}
if ($Version -eq 'none') {
    Write-Output 'SyncTest removed.'
    exit 0
}

$source = Join-Path $PSScriptRoot "..\testmods\SyncTest\$Version"
Copy-Item -Path $source -Destination (Join-Path $Game 'Mods\SyncTest') -Recurse
if ($Version -eq 'v6' -or $Version -eq 'v7') {
    $second = Join-Path $PSScriptRoot '..\testmods\SyncTestB'
    Copy-Item -Path $second -Destination (Join-Path $Game 'Mods\SyncTestB') -Recurse
}
Write-Output "SyncTest $Version installed."
