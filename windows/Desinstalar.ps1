# Cierra El Mago, quita el arranque automático, las reglas del firewall y el programa compilado.
# Los celulares emparejados quedan en %APPDATA%\ElMago (bórralo a mano si quieres empezar de cero).

Get-Process -Name "ElMago" -ErrorAction SilentlyContinue | Stop-Process -Force

$acceso = Join-Path ([Environment]::GetFolderPath("Startup")) "El Mago.lnk"
if (Test-Path $acceso) { Remove-Item $acceso; Write-Host "Ya no arranca con Windows." }

$bin = Join-Path $env:LOCALAPPDATA "ElMago"
if (Test-Path $bin) { Remove-Item $bin -Recurse -Force; Write-Host "Programa compilado quitado de $bin." }

Write-Host "Para quitar las reglas del firewall, Windows pedirá permiso de administrador."
try {
    Start-Process powershell.exe -Verb RunAs -Wait -ArgumentList '-NoProfile -Command "Get-NetFirewallRule -DisplayName ''El Mago*'' | Remove-NetFirewallRule"'
    Write-Host "Reglas del firewall quitadas."
} catch { Write-Host "No se quitaron las reglas del firewall (permiso no dado)." }
