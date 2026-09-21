[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',
    [switch]$FrameworkDependent
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskOutput = Join-Path $taskRoot "artifacts\publish\$Runtime"
$taskArchive = Join-Path $taskRoot "artifacts\Pausely-$Runtime.zip"
$taskArguments = @(
    'publish', (Join-Path $taskRoot 'src\Pausely\Pausely.csproj'),
    '-c', 'Release', '-r', $Runtime,
    '--self-contained', (-not $FrameworkDependent).ToString().ToLowerInvariant(),
    '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=None', '-p:DebugSymbols=false',
    '--configfile', (Join-Path $taskRoot 'NuGet.Config'),
    '-o', $taskOutput
)
& dotnet @taskArguments
if ($LASTEXITCODE -ne 0) { throw "Publish failed with exit code $LASTEXITCODE." }
Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md') -Destination $taskOutput -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'LICENSE') -Destination $taskOutput -Force
New-Item -ItemType Directory -Path (Join-Path $taskOutput 'docs\images') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $taskRoot 'docs\images\dashboard.png') -Destination (Join-Path $taskOutput 'docs\images\dashboard.png') -Force
# Archive only the explicit deliverables so an older publish cannot add stale DLLs.
Compress-Archive -LiteralPath (Join-Path $taskOutput 'Pausely.exe'), (Join-Path $taskOutput 'README.md'), (Join-Path $taskOutput 'LICENSE'), (Join-Path $taskOutput 'docs') -DestinationPath $taskArchive -Force
Write-Output "Ready: $taskOutput\Pausely.exe"
Write-Output "Portable package: $taskArchive"
