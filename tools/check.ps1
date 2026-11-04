param(
    [string]$Game = 'G:\SteamLibrary\steamapps\common\Software Inc'
)

$sourceDir = Join-Path $PSScriptRoot '..\mod\SaveSync'
$rules = @(
    @{ Pattern = 'System\.IO';          Reason = 'banned by the mod sandbox' },
    @{ Pattern = 'System\.Reflection';  Reason = 'banned by the mod sandbox' },
    @{ Pattern = 'UnityEngine\.WWW';    Reason = 'banned by the mod sandbox' },
    @{ Pattern = '\bApplication\.';     Reason = 'UnityEngine.Application is banned by the mod sandbox' },
    @{ Pattern = '@"';                  Reason = 'verbatim strings break the error handling injector' },
    @{ Pattern = "'[{}""]'";            Reason = 'these char literals break the error handling injector' },
    @{ Pattern = '\\"';                 Reason = 'escaped quotes break the error handling injector' },
    @{ Pattern = '\benum\b';            Reason = 'the docs say enums crash the game compiler' },
    @{ Pattern = 'System\.Linq';        Reason = 'System.Core is not confirmed for the game compiler' }
)

$failed = $false
foreach ($rule in $rules) {
    $hits = Select-String -Path (Join-Path $sourceDir '*.cs') -Pattern $rule.Pattern
    foreach ($hit in $hits) {
        Write-Output "$($hit.Filename):$($hit.LineNumber): $($rule.Reason)"
        $failed = $true
    }
}
if ($failed) {
    exit 1
}

$managed = Join-Path $Game 'Software Inc_Data\Managed'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output = Join-Path $env:TEMP 'SaveSync.check.dll'

$references = Get-ChildItem $managed -Filter *.dll |
    Where-Object { $_.Name -match '^(mscorlib|System|System\.Core|netstandard|Assembly-CSharp|Assembly-CSharp-firstpass|UnityEngine.*)\.dll$' } |
    ForEach-Object { '/r:' + $_.FullName }

& $csc /nologo /noconfig /nostdlib+ /target:library /warn:0 "/out:$output" $references (Join-Path $sourceDir '*.cs')
$code = $LASTEXITCODE
Remove-Item $output -ErrorAction SilentlyContinue
if ($code -eq 0) {
    Write-Output 'check passed'
}
exit $code
