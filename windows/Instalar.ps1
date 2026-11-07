# Deja El Mago arrancando con Windows, pide el permiso del firewall y lo abre ahora.
# Para quitarlo: Desinstalar.bat.

$ErrorActionPreference = "Stop"

$script = Join-Path $PSScriptRoot "ElMago.ps1"
$exe = Join-Path $env:LOCALAPPDATA "ElMago\ElMago.exe"

# 1) Compilar (sin abrir todavía)
& $script -Consola -Oculto
if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne $null) { exit 1 }

# 2) Arranque con Windows (acceso directo en la carpeta Inicio)
$acceso = Join-Path ([Environment]::GetFolderPath("Startup")) "El Mago.lnk"
$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut($acceso)
$lnk.TargetPath = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
$lnk.Arguments = "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$script`" -Oculto"
$lnk.WorkingDirectory = $PSScriptRoot
$lnk.WindowStyle = 7
$lnk.Description = "El Mago: escribe en el PC desde el celular"
$lnk.IconLocation = (Join-Path $PSScriptRoot "assets\mago.ico")
$lnk.Save()
Write-Host "El Mago arrancará solo al iniciar sesión."

# 3) Firewall (pide permiso de administrador: Windows mostrará un aviso, pulsa Sí)
Write-Host ""
Write-Host "Ahora Windows pedirá permiso de administrador para abrir el firewall al celular." -ForegroundColor Cyan
try {
    Start-Process powershell.exe -Verb RunAs -Wait -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$(Join-Path $PSScriptRoot 'Firewall.ps1')`" -Exe `"$exe`""
} catch {
    Write-Host "No se dio el permiso. Si el celular no encuentra el PC, ejecuta Instalar.bat otra vez." -ForegroundColor Yellow
}

# 4) Abrir la ventana para ver el PIN
Get-Process -Name "ElMago" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300
Start-Process -FilePath $exe
Write-Host ""
Write-Host "Listo. En el celular: abre El Mago, pulsa 'Buscar PC' y escribe el PIN que ves en la ventana."
