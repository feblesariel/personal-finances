param([switch]$Install)
$ErrorActionPreference='Stop'
if($Install){& (Join-Path $PSScriptRoot 'install.ps1') -NoLaunch;exit $LASTEXITCODE}
$projectRoot=Split-Path -Parent $PSScriptRoot
$dotnet=(Get-Command dotnet -ErrorAction SilentlyContinue).Source
if(!$dotnet){$dotnet=Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'}
if(!(Test-Path -LiteralPath $dotnet)){throw 'Use instalar.bat para verificar o instalar el SDK .NET 10.'}
& $dotnet publish (Join-Path $projectRoot 'src\PersonalFinances.App\PersonalFinances.App.csproj') -c Release -r win-x64 --self-contained true -o (Join-Path $projectRoot 'artifacts\Release') -p:DebugType=None -p:DebugSymbols=false
exit $LASTEXITCODE