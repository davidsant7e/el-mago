# Compila y ejecuta PruebaCaja.cs: escribe por El Mago en una caja de texto propia y compara.
# Resultado en tests\resultado-prueba.txt. Requiere El Mago abierto.
$ErrorActionPreference = "Stop"
# Cierra una prueba anterior que haya quedado colgada.
Get-CimInstance Win32_Process | Where-Object { $_.ProcessId -ne $PID -and $_.CommandLine -like "*PruebaCaja.ps1*" } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Get-Process PruebaCaja -ErrorAction SilentlyContinue | Stop-Process -Force

$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$exe = Join-Path $env:TEMP "PruebaCaja.exe"
& $csc /nologo /target:winexe /codepage:65001 "/out:$exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll (Join-Path $PSScriptRoot "PruebaCaja.cs")
if ($LASTEXITCODE -ne 0) { Write-Host "No compiló la prueba"; exit 1 }

$cfg = Get-Content (Join-Path $env:APPDATA "ElMago\config.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$salida = Join-Path $PSScriptRoot "resultado-prueba.txt"
$p = Start-Process -FilePath $exe -ArgumentList $cfg.Pin, "`"$salida`"" -PassThru -Wait
Get-Content $salida -Encoding UTF8 | Select-Object -First 6
