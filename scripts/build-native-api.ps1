param(
    [Parameter(Mandatory)][string]$GameDir,
    [Parameter(Mandatory)][string]$OutputDir
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$output = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($OutputDir))
if ($output -eq $root -or $output.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Choose an output directory outside the repository'
}

$managed = Join-Path $GameDir 'Probably Stolen_Data/Managed'
$assemblyPath = (Get-Item -LiteralPath (Join-Path $managed 'Assembly-CSharp.dll')).FullName
$cecil = (Get-Item -LiteralPath (Join-Path $managed 'DseCecil.dll')).FullName
$steamApps = Split-Path (Split-Path ([IO.Path]::GetFullPath($GameDir)) -Parent) -Parent
$manifest = Join-Path $steamApps 'appmanifest_4348910.acf'
$buildId = ''
if (Test-Path -LiteralPath $manifest) {
    $match = [regex]::Match((Get-Content -LiteralPath $manifest -Raw), '"buildid"\s+"(\d+)"')
    $buildId = $match.Groups[1].Value
}

$references = (Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName
[Reflection.Assembly]::LoadFrom($cecil) | Out-Null
Add-Type -Path (Join-Path $PSScriptRoot 'NativeApiIndex.cs') -ReferencedAssemblies ($references + $cecil)
[IO.Directory]::CreateDirectory($output) | Out-Null
$destination = Join-Path $output 'native-api-index.json'
$temporary = Join-Path $output ([IO.Path]::GetRandomFileName())
try {
    [NativeApiIndex]::Build($assemblyPath, $buildId, $temporary)
    [IO.File]::Move($temporary, $destination, $true)
} finally {
    [IO.File]::Delete($temporary)
}
Write-Output $destination
