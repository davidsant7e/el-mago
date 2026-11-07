# El Mago para Windows: recibe lo que escribes en el celular y lo teclea en este PC.
#
# Compila el programa la primera vez (o cuando cambia el código) con el compilador de C#
# que ya trae Windows, y lo abre. No hay que instalar nada.
#
#   -Consola   muestra los mensajes de compilación (lo usa Probar.bat)
#   -Oculto    arranca solo con el ícono junto al reloj (lo usa el inicio de Windows)

param([switch]$Consola, [switch]$Oculto)

$ErrorActionPreference = "Stop"

$fuentes = @("Protocolo.cs", "Config.cs", "Teclado.cs", "Servidor.cs", "App.cs") | ForEach-Object { Join-Path $PSScriptRoot "src\$_" }
$icono = Join-Path $PSScriptRoot "assets\mago.ico"
$bin = Join-Path $env:LOCALAPPDATA "ElMago"
$exe = Join-Path $bin "ElMago.exe"

function Escribir($texto) { if ($Consola) { Write-Host $texto } }

$compilar = -not (Test-Path $exe)
if (-not $compilar) {
    $fechaExe = (Get-Item $exe).LastWriteTime
    $compilar = @(($fuentes + $icono) | Where-Object { (Get-Item $_).LastWriteTime -gt $fechaExe }).Count -gt 0
}

if ($compilar) {
    $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe" }
    if (-not (Test-Path $csc)) { throw "No se encontró el compilador de C# de .NET Framework 4 ($csc)." }

    Get-Process -Name "ElMago" -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500

    New-Item -ItemType Directory -Force -Path $bin | Out-Null
    Escribir "Compilando El Mago..."
    Copy-Item $icono (Join-Path $bin "mago.ico") -Force
    $salida = & $csc /nologo /target:winexe /optimize+ /codepage:65001 "/out:$exe" "/win32icon:$icono" `
        /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
        /r:System.Web.Extensions.dll /r:System.Security.dll `
        $fuentes 2>&1
    if ($LASTEXITCODE -ne 0) {
        $texto = ($salida | Out-String)
        if ($Consola) { Write-Host $texto -ForegroundColor Red }
        else {
            Add-Type -AssemblyName System.Windows.Forms
            [System.Windows.Forms.MessageBox]::Show("No se pudo compilar El Mago:`n`n$texto", "El Mago") | Out-Null
        }
        exit 1
    }
    Escribir "Listo: $exe"
}

if (Get-Process -Name "ElMago" -ErrorAction SilentlyContinue) {
    Escribir "El Mago ya está abierto (ícono junto al reloj)."
    exit 0
}

if ($Oculto) { Start-Process -FilePath $exe -ArgumentList "--oculto" } else { Start-Process -FilePath $exe }
Escribir "El Mago abierto: busca el ícono del teclado con varita junto al reloj (puede estar en la flecha ^)."
