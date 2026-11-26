# Ejecuta las pruebas y genera el paquete portátil win-x64 self-contained (runtime incluido)
# en artifacts\publish\win-x64, con FDNDA.Waterpolo.exe. Esa carpeta es la base del futuro
# instalador offline; el equipo del operador no necesita .NET SDK ni internet.

param([switch]$SinPruebas)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'entorno.ps1')
$raiz = Split-Path -Parent $PSScriptRoot
Set-Location $raiz

if (-not $SinPruebas) {
    & dotnet test FDNDA.Waterpolo.sln -c Release
    if ($LASTEXITCODE -ne 0) { throw "Las pruebas fallaron: no se publica." }
}

$salida = Join-Path $raiz 'artifacts\publish\win-x64'
& dotnet publish src\FDNDA.Waterpolo.Desktop\FDNDA.Waterpolo.Desktop.csproj -c Release -r win-x64 --self-contained true -o $salida
if ($LASTEXITCODE -ne 0) { throw "La publicación falló." }

# Herramienta de captura para la prueba con la consola (hito B): carpeta aparte, no forma parte del
# programa de la mesa ni de su instalador.
$captura = Join-Path $raiz 'artifacts\publish\captura-win-x64'
& dotnet publish tools\FDNDA.Waterpolo.Captura\FDNDA.Waterpolo.Captura.csproj -c Release -r win-x64 --self-contained true -o $captura
if ($LASTEXITCODE -ne 0) { throw "La publicación de la herramienta de captura falló." }

Write-Host "Paquete publicado en: $salida"
Write-Host "Ejecutable: $(Join-Path $salida 'FDNDA.Waterpolo.exe')"
Write-Host "Herramienta de captura (hito B): $(Join-Path $captura 'FDNDA.Waterpolo.Captura.exe')"
