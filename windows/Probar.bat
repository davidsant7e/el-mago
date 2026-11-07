@echo off
rem Compila (si hace falta) y abre El Mago mostrando los mensajes, para ver errores.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0ElMago.ps1" -Consola
pause
