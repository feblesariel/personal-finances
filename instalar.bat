@echo off
setlocal
title Instalar Personal Finances
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\install.ps1" %*
set "pf_exit=%ERRORLEVEL%"
if not "%pf_exit%"=="0" echo La instalacion no se completo. Revise el mensaje de error anterior.
if /I "%~1"=="-Unattended" goto finish
echo.
pause
:finish
exit /b %pf_exit%
