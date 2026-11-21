[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist')
)

$ErrorActionPreference = 'Stop'
$compiler = (Get-Command cl.exe -CommandType Application).Source
$linker = (Get-Command link.exe -CommandType Application).Source
$buildDirectory = (New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot 'build')).FullName
$destination = (New-Item -ItemType Directory -Force -Path $OutputDirectory).FullName
$source = Join-Path $PSScriptRoot 'core/sdmmc_inspect.cpp'
$object = Join-Path $buildDirectory 'sdmmc_inspect.obj'
$core = Join-Path $buildDirectory 'sdmmc_inspect_x64.exe'

& $compiler /nologo /c /O2 /GS- /W4 /EHsc /std:c++17 "/Fo:$object" $source
if ($LASTEXITCODE -ne 0) { throw "C++ compilation failed (exit $LASTEXITCODE)." }
& $linker /nologo /machine:x64 /subsystem:console /entry:mainCRTStartup /nodefaultlib "/out:$core" $object kernel32.lib shell32.lib
if ($LASTEXITCODE -ne 0) { throw "C++ linking failed (exit $LASTEXITCODE)." }

$project = Join-Path $PSScriptRoot 'gui/SDMMC.Inspector.csproj'
dotnet publish $project -c Release -r win-x64 -p:IlcUseEnvironmentalTools=true -o $destination
if ($LASTEXITCODE -ne 0) { throw "Native AOT publish failed (exit $LASTEXITCODE)." }
Copy-Item -LiteralPath $core -Destination $destination -Force
Write-Output "Windows x64 executables: $destination"
