# Instala .NET SDK 10 dentro del repositorio (.tools\dotnet), sin permisos de administrador y
# sin tocar el PATH del sistema. Es un paso de DESARROLLO: requiere internet para descargar
# el instalador oficial de Microsoft. El programa entregado no necesita el SDK ni internet.

$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
$tools = Join-Path $raiz '.tools'
$destino = Join-Path $tools 'dotnet'
$instalador = Join-Path $tools 'dotnet-install.ps1'

New-Item -ItemType Directory -Force $tools | Out-Null
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $instalador -UseBasicParsing

& $instalador -Channel 10.0 -InstallDir $destino -NoPath
& (Join-Path $destino 'dotnet.exe') --list-sdks
Write-Host "Listo. En cada sesión nueva ejecute:  . .\scripts\entorno.ps1"
