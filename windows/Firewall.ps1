# Se ejecuta como administrador (lo llama Instalar.ps1). Deja entrar al celular solo en redes privadas.
param([string]$Exe)

$ErrorActionPreference = "Continue"
Write-Host "Permiso en el firewall de Windows para El Mago" -ForegroundColor Cyan

Get-NetFirewallRule -DisplayName "El Mago*" -ErrorAction SilentlyContinue | Remove-NetFirewallRule
New-NetFirewallRule -DisplayName "El Mago (teclado desde el celular)" -Direction Inbound -Action Allow `
    -Program $Exe -Protocol TCP -LocalPort 47800 -Profile Private | Out-Null
New-NetFirewallRule -DisplayName "El Mago (buscar el PC)" -Direction Inbound -Action Allow `
    -Program $Exe -Protocol UDP -LocalPort 47801 -Profile Private | Out-Null
Write-Host "Listo: reglas creadas (solo redes privadas, puertos 47800 TCP y 47801 UDP)."

# Si el Wi-Fi actual está como "Público", el firewall no deja pasar al celular.
$perfiles = Get-NetConnectionProfile | Where-Object { $_.NetworkCategory -eq "Public" -and $_.InterfaceAlias -like "*Wi*Fi*" }
foreach ($p in $perfiles) {
    Write-Host ""
    Write-Host "Tu Wi-Fi '$($p.Name)' está marcado como red PÚBLICA." -ForegroundColor Yellow
    Write-Host "En una red pública Windows bloquea al celular. Si es la red de tu casa, conviene marcarla como privada."
    $r = Read-Host "¿Marcar '$($p.Name)' como red privada? (S/N)"
    if ($r -match '^[sS]') {
        Set-NetConnectionProfile -InterfaceIndex $p.InterfaceIndex -NetworkCategory Private
        Write-Host "Hecho: '$($p.Name)' ahora es privada." -ForegroundColor Green
    } else {
        Write-Host "No se cambió. El celular no podrá conectarse mientras la red sea pública."
    }
}
Write-Host ""
Read-Host "Pulsa Enter para cerrar"
