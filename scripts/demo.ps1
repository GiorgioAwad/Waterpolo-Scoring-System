# Compila y abre la demo con el simulador.
#
#   .\scripts\demo.ps1                          # datos demo en %LocalAppData%\FDNDA\Waterpolo\demo
#   .\scripts\demo.ps1 -Datos C:\temp\wp-demo   # datos en una carpeta aparte (se puede borrar para empezar de cero)
#   .\scripts\demo.ps1 -Operador "Mesa 1"       # sin diálogo de identificación
#   .\scripts\demo.ps1 -Reproduccion samples\cronometraje\guion-sintetico.csv
#                                               # reproduce una grabación normalizada («REPRODUCCIÓN»)

param(
    [string]$Datos,
    [string]$Operador,
    [string]$Reproduccion
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'entorno.ps1')
$raiz = Split-Path -Parent $PSScriptRoot
$proyecto = Join-Path $raiz 'src\FDNDA.Waterpolo.Desktop\FDNDA.Waterpolo.Desktop.csproj'

$argumentos = @('--perfil', 'demo')
if ($Datos) { $argumentos += @('--datos', $Datos) }
if ($Operador) { $argumentos += @('--operador', $Operador) }
if ($Reproduccion) { $argumentos += @('--reproduccion', (Resolve-Path $Reproduccion).Path) }

& dotnet run --project $proyecto -c Release -- @argumentos
