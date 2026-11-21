param(
    [Parameter(Mandatory=$true)][string]$GameDir,
    [Parameter(Mandatory=$true)][string]$OutputDir
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$output = [IO.Path]::GetFullPath($OutputDir)
if ($output -eq $root -or $output.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Choose an output directory outside the repository' }
$managed = Join-Path $GameDir 'Probably Stolen_Data/Managed'
$version = (dotnet --version).Trim()
$line = dotnet --list-sdks | Where-Object { $_.StartsWith("$version [") } | Select-Object -First 1
$sdkHome = $line.Substring($line.IndexOf('[')+1).TrimEnd(']')
$compiler = Join-Path $sdkHome "$version/Roslyn/bincore/csc.dll"
$target = Join-Path $output 'white_dragon_ledger'
[IO.Directory]::CreateDirectory($target) | Out-Null
$references = Get-ChildItem -LiteralPath $managed -Filter '*.dll' | ForEach-Object { '/reference:'+$_.FullName }
$dll = Join-Path $target 'WhiteDragonLedger.dll'
$sources = (Get-ChildItem -LiteralPath (Join-Path $root 'mod') -Filter '*.cs').FullName
& dotnet $compiler /nologo /nostdlib+ /optimize+ /target:library "/out:$dll" $references $sources
if ($LASTEXITCODE) { throw 'Compilation failed' }
Copy-Item -LiteralPath (Join-Path $root 'mod/manifest.xml') -Destination $target
Write-Output $target
