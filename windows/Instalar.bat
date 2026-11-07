@echo off
rem Doble clic: El Mago arranca con Windows, pide el permiso del firewall y se abre.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Instalar.ps1"
pause
