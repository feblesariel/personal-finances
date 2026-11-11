$ErrorActionPreference='Stop'
$sourceRoot=Split-Path -Parent $PSScriptRoot
$testRoot=Join-Path ([IO.Path]::GetTempPath()) ('PersonalFinancesInstallerTest_'+[Guid]::NewGuid().ToString('N'))
$snapshot=Join-Path $testRoot 'snapshot';$clone=Join-Path $testRoot 'cloned repo with spaces'
$programs=Join-Path $testRoot 'Programs\PersonalFinances';$desktop=Join-Path $testRoot 'Desktop'
$results=New-Object 'System.Collections.Generic.List[string]'
function Check([bool]$Condition,[string]$Message){if(!$Condition){throw $Message};$results.Add($Message);Write-Host ('PASS INSTALL: '+$Message)}
function Run-Installer {
    $batch=Join-Path $script:clone 'instalar.bat'
    Push-Location -LiteralPath $env:WINDIR
    try{& $batch -Unattended -NoLaunch -InstallDirectory $script:programs -DesktopDirectory $script:desktop | Out-Host;return $LASTEXITCODE}finally{Pop-Location}
}
New-Item -ItemType Directory -Path $snapshot -Force | Out-Null
try{
    foreach($file in @('.gitignore','README.md','instalar.bat','global.json','PersonalFinances.slnx')){Copy-Item -LiteralPath (Join-Path $sourceRoot $file) -Destination $snapshot}
    foreach($directory in @('src','tests','scripts')){
        Get-ChildItem -LiteralPath (Join-Path $sourceRoot $directory) -File -Recurse | Where-Object {$_.FullName -notmatch '\\(bin|obj)\\'} | ForEach-Object{
            $relative=$_.FullName.Substring($sourceRoot.Length+1);$destination=Join-Path $snapshot $relative
            New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null;Copy-Item -LiteralPath $_.FullName -Destination $destination
        }
    }
    $patterns=@(('G:'+([char]92)+([char]92)+'Other computers'),('root'+'/'+ 'root'),('C:'+([char]92)+([char]92)+'Users'+([char]92)+([char]92)))
    $matches=@(Get-ChildItem -LiteralPath $snapshot -Recurse -File -Include *.cs,*.ps1,*.md,*.json | Select-String -Pattern $patterns)
    Check ($matches.Count -eq 0) 'Copia distribuible sin rutas personales, credenciales fijas ni lote financiero real'
    & git -C $snapshot init --quiet;if($LASTEXITCODE -ne 0){throw 'Fallo git init de prueba'}
    & git -C $snapshot config core.autocrlf false
    & git -C $snapshot add .;if($LASTEXITCODE -ne 0){throw 'Fallo git add de prueba'}
    $tracked=@(& git -C $snapshot ls-files)
    Check (@($tracked | Where-Object {$_ -match '(^|/)(artifacts|bin|obj)/|\.dpapi$|\.sql$|\.exe$|preferences\.json$'}).Count -eq 0) '.gitignore excluye compilaciones, configuracion y datos generados'
    & git -C $snapshot -c user.name='Installer Test' -c user.email='installer@example.invalid' commit --quiet -m 'Sanitized installer test fixture'
    if($LASTEXITCODE -ne 0){throw 'Fallo commit local de prueba'}
    & git clone --quiet --no-hardlinks $snapshot $clone;if($LASTEXITCODE -ne 0){throw 'Fallo clone local de prueba'}
    $code=Run-Installer;Check ($code -eq 0) 'Instalacion desde git clone local, ruta con espacios y directorio de trabajo independiente'
    $exe=Join-Path $programs 'PersonalFinances.exe';$link=Join-Path $desktop 'Personal Finances.lnk'
    Check ((Test-Path -LiteralPath $exe) -and (Test-Path -LiteralPath $link)) 'Ejecutable y acceso directo generados en los destinos de prueba'
    $shell=New-Object -ComObject WScript.Shell
    Check ($shell.CreateShortcut($link).TargetPath -eq $exe -and $shell.CreateShortcut($link).IconLocation.StartsWith($exe)) 'Acceso directo apunta al ejecutable instalado y usa su icono'
    $credential=Join-Path $env:LOCALAPPDATA 'PersonalFinances\connection.dpapi';$credentialHash=if(Test-Path -LiteralPath $credential){(Get-FileHash -LiteralPath $credential).Hash}else{''}
    $code=Run-Installer;Check ($code -eq 0 -and @(Get-ChildItem -LiteralPath $desktop -Filter '*.lnk').Count -eq 1) 'Reinstalacion sin duplicar accesos directos'
    Check (@(Get-ChildItem -LiteralPath (Split-Path -Parent $programs) -Directory).Count -eq 1) 'Una sola version instalada, sin directorios de respaldo residuales'
    $stableHash=(Get-FileHash -LiteralPath $exe).Hash;$shortcutHash=(Get-FileHash -LiteralPath $link).Hash
    $broken=Join-Path $clone 'src\PersonalFinances.App\BrokenBuild.cs';Set-Content -LiteralPath $broken -Value 'invalid source code for rollback test' -Encoding UTF8
    $code=Run-Installer;Check ($code -ne 0 -and (Get-FileHash -LiteralPath $exe).Hash -eq $stableHash -and (Get-FileHash -LiteralPath $link).Hash -eq $shortcutHash) 'Fallo de compilacion conserva ejecutable y acceso directo anteriores'
    Remove-Item -LiteralPath $broken
    $installScript=Join-Path $clone 'scripts\install.ps1';$original=[IO.File]::ReadAllBytes($installScript)
    try{
        $text=[IO.File]::ReadAllText($installScript).Replace('function Find-Sdk {','function Find-Sdk { return $null;')
        [IO.File]::WriteAllText($installScript,$text)
        $code=Run-Installer;Check ($code -ne 0 -and (Get-FileHash -LiteralPath $exe).Hash -eq $stableHash) 'SDK ausente simulado: informa dependencia y conserva instalacion'
    }finally{[IO.File]::WriteAllBytes($installScript,$original)}
    Check ($credentialHash -eq $(if(Test-Path -LiteralPath $credential){(Get-FileHash -LiteralPath $credential).Hash}else{''})) 'Instalacion y reinstalacion conservan credenciales cifradas'
    $removed=Join-Path $testRoot 'source moved after installation'
    $resolved=(Resolve-Path -LiteralPath $clone).ProviderPath
    if(!$resolved.StartsWith($testRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Origen de movimiento fuera del area de pruebas'}
    Move-Item -LiteralPath $resolved -Destination $removed
    $process=Start-Process -FilePath $exe -WorkingDirectory $env:WINDIR -PassThru
    try{
        Check ($process.WaitForInputIdle(15000)) 'Ejecutable instalado abre sin depender de la carpeta clonada'
        $process.Refresh();Check ($process.MainWindowTitle -eq 'Personal Finances' -and $process.Responding) 'Ventana instalada responde correctamente'
    }finally{if(!$process.HasExited){$process.CloseMainWindow() | Out-Null;if(!$process.WaitForExit(10000)){throw 'La aplicacion de prueba no cerro normalmente'}}}
    $output=Join-Path $sourceRoot 'artifacts\portable-update\installer-tests.txt';New-Item -ItemType Directory -Path (Split-Path -Parent $output) -Force | Out-Null
    [IO.File]::WriteAllLines($output,$results,[Text.UTF8Encoding]::new($false));Write-Host ("RESULT INSTALL: {0} passed" -f $results.Count)
}finally{
    if(Test-Path -LiteralPath $testRoot){$resolved=(Resolve-Path -LiteralPath $testRoot).ProviderPath;if((Split-Path -Parent $resolved) -eq [IO.Path]::GetTempPath().TrimEnd('\') -and (Split-Path -Leaf $resolved) -match '^PersonalFinancesInstallerTest_[a-f0-9]{32}$'){Remove-Item -LiteralPath $resolved -Recurse -Force}else{throw 'Se rechazo limpiar un directorio fuera del area temporal de prueba'}}
}
