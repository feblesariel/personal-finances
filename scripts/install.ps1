[CmdletBinding()]
param(
    [switch]$Unattended,
    [switch]$InstallSdk,
    [switch]$NoLaunch,
    [string]$InstallDirectory=(Join-Path $env:LOCALAPPDATA 'Programs\PersonalFinances'),
    [string]$DesktopDirectory=[Environment]::GetFolderPath('DesktopDirectory')
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2
$repoRoot=Split-Path -Parent $PSScriptRoot
$project=Join-Path $repoRoot 'src\PersonalFinances.App\PersonalFinances.App.csproj'
$sdkLink='https://dotnet.microsoft.com/download/dotnet/10.0'
$installParent=$null;$stage=$null;$backup=$null;$buildRoot=$null;$oldMoved=$false;$newMoved=$false;$shortcutPath=$null;$shortcutOriginal=$null;$success=$false
function Find-Sdk {
    $candidates=New-Object 'System.Collections.Generic.List[string]'
    $command=Get-Command dotnet -ErrorAction SilentlyContinue
    if($command){$candidates.Add($command.Source)}
    foreach($candidate in @((Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'),(Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'))){$candidates.Add($candidate)}
    foreach($candidate in ($candidates | Select-Object -Unique)){
        if(Test-Path -LiteralPath $candidate){$versions=@(& $candidate --list-sdks 2>$null);if($LASTEXITCODE -eq 0 -and ($versions | Where-Object {$_ -match '^10\.0\.\d+ \['})){return $candidate}}
    }
    return $null
}
function Invoke-DotNet([string[]]$Arguments){& $script:sdk @Arguments;if($LASTEXITCODE -ne 0){throw "Fallo dotnet $($Arguments[0]). La instalacion anterior no fue reemplazada."}}
function Remove-WorkDirectory([string]$Path){
    if(!$Path -or !(Test-Path -LiteralPath $Path)){return}
    $resolved=(Resolve-Path -LiteralPath $Path).ProviderPath
    $leaf=Split-Path -Leaf $resolved;$parent=Split-Path -Parent $resolved
    $installWork=($parent -eq $script:installParent -and $leaf -match '^PersonalFinances\.(install|previous)-[a-f0-9]{32}$')
    $buildWork=($parent -eq (Join-Path $script:repoRoot 'artifacts') -and $leaf -match '^install-build-[a-f0-9]{32}$')
    if(!$installWork -and !$buildWork){throw "Se rechazo limpiar una ruta fuera del area temporal del instalador: $resolved"}
    if((Get-Item -LiteralPath $resolved -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'No se eliminan enlaces o uniones de directorios.'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
try{
    Write-Host 'PERSONAL FINANCES - Instalacion / actualizacion' -ForegroundColor Cyan
    if([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT -or [Environment]::OSVersion.Version.Major -lt 10 -or [Environment]::OSVersion.Version.Build -lt 17763){throw 'Se requiere Windows de escritorio 10 (1809 o posterior) o Windows 11. Recomendado: Windows 11 con soporte vigente.'}
    $arch=if($env:PROCESSOR_ARCHITEW6432){$env:PROCESSOR_ARCHITEW6432}else{$env:PROCESSOR_ARCHITECTURE}
    if($arch -ne 'AMD64'){throw 'Esta distribucion requiere Windows x64 sobre un procesador Intel/AMD de 64 bits.'}
    if(!(Test-Path -LiteralPath $project)){throw 'Faltan archivos del proyecto. Descargue o clone el repositorio completo.'}
    $InstallDirectory=[IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\');$installParent=Split-Path -Parent $InstallDirectory
    if((Split-Path -Leaf $InstallDirectory) -ne 'PersonalFinances' -or !$installParent){throw 'El directorio de instalacion debe ser una carpeta PersonalFinances, nunca la raiz de una unidad.'}
    if($InstallDirectory.StartsWith($repoRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'La instalacion debe quedar fuera del repositorio.'}
    $DesktopDirectory=[IO.Path]::GetFullPath($DesktopDirectory)
    $sdk=Find-Sdk
    if(!$sdk){
        Write-Host "Para compilar se necesita el SDK .NET 10 x64. Descarga oficial: $sdkLink"
        $approved=$InstallSdk
        if(!$Unattended -and !$approved){$approved=(Read-Host 'Instalar el SDK oficial para este usuario, sin administrador? [S/N]') -match '^(s|si|y|yes)$'}
        if(!$approved){throw "Instale el SDK .NET 10 y vuelva a ejecutar instalar.bat. $sdkLink"}
        $sdkDir=Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet';$scriptDir=Join-Path $env:LOCALAPPDATA 'PersonalFinances\Installer'
        New-Item -ItemType Directory -Path $scriptDir -Force | Out-Null
        $official=Join-Path $scriptDir 'dotnet-install.ps1'
        [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -UseBasicParsing -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $official
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $official -Channel 10.0 -Quality GA -Architecture x64 -InstallDir $sdkDir -NoPath
        if($LASTEXITCODE -ne 0){throw "No se pudo instalar el SDK. Descarga oficial: $sdkLink"}
        $sdk=Find-Sdk;if(!$sdk){throw "El SDK no se detecto. Descarga oficial: $sdkLink"}
    }
    Write-Host "SDK: $sdk"
    $targetExe=Join-Path $InstallDirectory 'PersonalFinances.exe'
    while(@(Get-Process PersonalFinances -ErrorAction SilentlyContinue | Where-Object {try{$_.Path -eq $targetExe}catch{$false}}).Count -gt 0){
        if($Unattended){throw 'Personal Finances esta abierto. Cierrelo antes de actualizar. No se reemplazaron archivos.'}
        $answer=Read-Host 'Personal Finances esta abierto. Cierre la aplicacion y presione Enter para reintentar, o C para cancelar'
        if($answer -match '^[cC]$'){throw 'Actualizacion cancelada. La instalacion anterior se conserva.'}
    }
    $buildRoot=Join-Path $repoRoot ('artifacts\install-build-'+[Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $buildRoot -Force | Out-Null
    Push-Location -LiteralPath $repoRoot
    try{
        Write-Host '[1/4] Restaurando paquetes NuGet...';Invoke-DotNet @('restore',$project,'-r','win-x64')
        Write-Host '[2/4] Compilando Release...';Invoke-DotNet @('build',$project,'-c','Release','-r','win-x64','--no-restore','-p:DebugType=None','-p:DebugSymbols=false')
        Write-Host '[3/4] Publicando Windows x64 con runtime incluido...';Invoke-DotNet @('publish',$project,'-c','Release','-r','win-x64','--self-contained','true','--no-build','--no-restore','-o',$buildRoot,'-p:DebugType=None','-p:DebugSymbols=false')
    }finally{Pop-Location}
    $published=Join-Path $buildRoot 'PersonalFinances.exe'
    if(!(Test-Path -LiteralPath $published) -or (Get-Item -LiteralPath $published).Length -lt 10000000){throw 'La publicacion no genero un ejecutable self-contained completo.'}
    $reader=New-Object IO.BinaryReader([IO.File]::OpenRead($published))
    try{$reader.BaseStream.Position=0x3c;$pe=$reader.ReadInt32();$reader.BaseStream.Position=$pe+4;$machine=$reader.ReadUInt16();$reader.BaseStream.Position=$pe+92;$subsystem=$reader.ReadUInt16()}finally{$reader.Dispose()}
    if($machine -ne 0x8664 -or $subsystem -ne 2){throw 'El ejecutable publicado no es una aplicacion Windows GUI x64.'}
    $hash=(Get-FileHash -LiteralPath $published -Algorithm SHA256).Hash
    New-Item -ItemType Directory -Path $installParent,$DesktopDirectory -Force | Out-Null
    $stage=Join-Path $installParent ('PersonalFinances.install-'+[Guid]::NewGuid().ToString('N'))
    $backup=Join-Path $installParent ('PersonalFinances.previous-'+[Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage | Out-Null
    Copy-Item -LiteralPath $published -Destination (Join-Path $stage 'PersonalFinances.exe')
    Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') -Destination $stage
    $shortcutPath=Join-Path $DesktopDirectory 'Personal Finances.lnk'
    if(Test-Path -LiteralPath $shortcutPath){$shortcutOriginal=[IO.File]::ReadAllBytes($shortcutPath)}
    Write-Host '[4/4] Instalando y verificando acceso directo...'
    if(Test-Path -LiteralPath $InstallDirectory){Move-Item -LiteralPath $InstallDirectory -Destination $backup;$oldMoved=$true}
    Move-Item -LiteralPath $stage -Destination $InstallDirectory;$newMoved=$true
    $shell=New-Object -ComObject WScript.Shell;$shortcut=$shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath=$targetExe;$shortcut.WorkingDirectory=$InstallDirectory;$shortcut.IconLocation=$targetExe+',0';$shortcut.Description='Personal Finances';$shortcut.Save()
    if((Get-FileHash -LiteralPath $targetExe).Hash -ne $hash -or $shell.CreateShortcut($shortcutPath).TargetPath -ne $targetExe){throw 'No coincide la verificacion de la instalacion o el acceso directo.'}
    $release=Join-Path $repoRoot 'artifacts\Release';New-Item -ItemType Directory -Path $release -Force | Out-Null;Copy-Item -LiteralPath $published -Destination $release -Force
    Remove-WorkDirectory $backup;$oldMoved=$false;$success=$true
    Write-Host "Instalacion completada: $targetExe" -ForegroundColor Green
    Write-Host "Acceso directo: $shortcutPath"
    Write-Host 'Sus datos MySQL, credenciales y preferencias se conservaron. No se necesita .NET Runtime para abrir la aplicacion.'
    Write-Host 'En el primer inicio configure MySQL y la carpeta del resumen desde la aplicacion.'
    if(!$NoLaunch -and !$Unattended){if((Read-Host 'Abrir Personal Finances ahora? [S/N]') -match '^(s|si|y|yes)$'){Start-Process -FilePath $targetExe}}
}catch{
    Write-Host ('ERROR: '+$_.Exception.Message) -ForegroundColor Red
    if(!$success){
        try{
            if($newMoved -and (Test-Path -LiteralPath $InstallDirectory)){Move-Item -LiteralPath $InstallDirectory -Destination $stage}
            if($oldMoved -and (Test-Path -LiteralPath $backup)){Move-Item -LiteralPath $backup -Destination $InstallDirectory;$oldMoved=$false}
            if($shortcutPath){if($shortcutOriginal){[IO.File]::WriteAllBytes($shortcutPath,$shortcutOriginal)}elseif(Test-Path -LiteralPath $shortcutPath){Remove-Item -LiteralPath $shortcutPath}}
        }catch{Write-Host ('No se pudo completar la restauracion automatica: '+$_.Exception.Message) -ForegroundColor Red}
    }
    exit 1
}finally{
    foreach($temporary in @($stage,$buildRoot)){try{Remove-WorkDirectory $temporary}catch{Write-Warning $_.Exception.Message}}
}
exit 0
