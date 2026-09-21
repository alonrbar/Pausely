[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
& dotnet build (Join-Path $taskRoot 'Pausely.slnx') -c Release --configfile (Join-Path $taskRoot 'NuGet.Config')
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
& dotnet (Join-Path $taskRoot 'tests\Pausely.Core.Tests\bin\Release\net10.0\Pausely.Core.Tests.dll')
if ($LASTEXITCODE -ne 0) { throw 'Timer checks failed.' }
$taskOutput = Join-Path $taskRoot 'artifacts\smoke'
$taskExe = Join-Path $taskRoot 'src\Pausely\bin\Release\net10.0-windows\Pausely.exe'
$taskProcess = Start-Process -FilePath $taskExe -ArgumentList '--smoke-test', ('"' + $taskOutput + '"') -PassThru -Wait -WindowStyle Hidden
if ($taskProcess.ExitCode -ne 0) { throw "WPF smoke test failed. See $taskOutput\result.txt." }
Get-Content -LiteralPath (Join-Path $taskOutput 'result.txt')
