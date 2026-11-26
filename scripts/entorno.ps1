# Prepara la sesión de PowerShell para compilar. Uso:  . .\scripts\entorno.ps1
#
# Si existe el SDK local (.tools\dotnet, creado por instalar-sdk-local.ps1) lo antepone al PATH
# solo en esta sesión; no modifica el sistema. Si no existe, usa el dotnet instalado en el equipo,
# que debe ser .NET SDK 10 (ver global.json).

$raiz = Split-Path -Parent $PSScriptRoot
$sdkLocal = Join-Path $raiz '.tools\dotnet'

if (Test-Path (Join-Path $sdkLocal 'dotnet.exe')) {
    $env:DOTNET_ROOT = $sdkLocal
    if (-not ($env:PATH -split ';' | Where-Object { $_ -eq $sdkLocal })) {
        $env:PATH = "$sdkLocal;$env:PATH"
    }
}

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

$version = & dotnet --version 2>$null
if ($LASTEXITCODE -ne 0 -or -not $version -or -not $version.StartsWith('10.')) {
    Write-Warning "No se encontró .NET SDK 10. Ejecute .\scripts\instalar-sdk-local.ps1 o instale el SDK 10 en el equipo."
} else {
    Write-Host "SDK de .NET en uso: $version"
}
