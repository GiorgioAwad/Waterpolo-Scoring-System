# Recorrido de R10 (reprogramar, actualizar nómina y anular un partido programado; colores al pulsar
# «Actualizar»; nombres largos en la lista de equipos) en el escritorio interactivo. Usa datos nuevos
# bajo artifacts. No envía teclas ni mueve el ratón: opera AutomationId con UI Automation; los cuadros
# de confirmación de Windows se responden con un mensaje dirigido a «Sí».
[CmdletBinding()]
param(
    [string]$Ejecutable
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'entorno.ps1')
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -Namespace ReprogramarR10 -Name Win32 -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool PostMessage(System.IntPtr hWnd, int msg, System.IntPtr wParam, System.IntPtr lParam);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool PrintWindow(System.IntPtr hWnd, System.IntPtr hdc, uint flags);
'@

$raizProyecto = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$salida = Join-Path $raizProyecto ('artifacts\verificacion-r10-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $salida
if (-not $Ejecutable) {
    $Ejecutable = Join-Path $raizProyecto 'src\FDNDA.Waterpolo.Desktop\bin\Release\net10.0-windows\FDNDA.Waterpolo.exe'
}
$registro = [Collections.Generic.List[string]]::new()
$proceso = $null
$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]
$BM_CLICK = 0x00F5

function Condicion($propiedad, $valor) { [System.Windows.Automation.PropertyCondition]::new($propiedad, $valor) }

function Buscar-Id($ventana, [string]$id) {
    $control = $ventana.FindFirst($Scope::Descendants, (Condicion $AE::AutomationIdProperty $id))
    if ($null -eq $control) { throw "No se encontró el control $id." }
    return $control
}

function Esperar([scriptblock]$condicion, [string]$descripcion) {
    for ($intento = 0; $intento -lt 150; $intento++) {
        try { if (& $condicion) { return } } catch { }
        Start-Sleep -Milliseconds 100
    }
    throw "No se cumplió: $descripcion."
}

function Pulsar($ventana, [string]$id) {
    $control = Buscar-Id $ventana $id
    if (-not $control.Current.IsEnabled) { throw "La acción $id está deshabilitada." }
    $control.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Habilitado($ventana, [string]$id) { (Buscar-Id $ventana $id).Current.IsEnabled }

function Pestana([string]$id) {
    (Buscar-Id $mesa $id).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
}

function Texto($ventana, [string]$id) { (Buscar-Id $ventana $id).Current.Name }

function Valor($ventana, [string]$id) {
    (Buscar-Id $ventana $id).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
}

function Escribir($ventana, [string]$id, [string]$texto) {
    (Buscar-Id $ventana $id).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($texto)
}

function Elementos($control) {
    $condicion = [System.Windows.Automation.OrCondition]::new(
        (Condicion $AE::ControlTypeProperty $CT::ListItem), (Condicion $AE::ControlTypeProperty $CT::DataItem))
    return ,$control.FindAll($Scope::Descendants, $condicion)
}

function Elegir($ventana, [string]$id, [int]$indice) {
    $control = Buscar-Id $ventana $id
    $expansion = $null
    if ($control.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$expansion)) { $expansion.Expand() }
    Esperar { (Elementos $control).Count -gt $indice } "cargar las opciones de $id"
    (Elementos $control)[$indice].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    if ($null -ne $expansion) { $expansion.Collapse() }
}

function Fila-Con-Texto($control, [string]$texto) {
    foreach ($fila in (Elementos $control)) {
        if ($fila.FindFirst($Scope::Descendants, (Condicion $AE::NameProperty $texto))) { return $fila }
    }
    return $null
}

function Elegir-Partido([string]$texto) {
    Esperar { $null -ne (Fila-Con-Texto (Buscar-Id $mesa 'MatchList') $texto) } "partido «$texto» en la lista"
    (Fila-Con-Texto (Buscar-Id $mesa 'MatchList') $texto).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
}

function Ventana-Del-Proceso([scriptblock]$filtro) {
    foreach ($v in $AE::RootElement.FindAll($Scope::Descendants, [System.Windows.Automation.AndCondition]::new(
            (Condicion $AE::ProcessIdProperty $proceso.Id), (Condicion $AE::ControlTypeProperty $CT::Window)))) {
        if (& $filtro $v) { return $v }
    }
    return $null
}

function Esperar-Ventana([scriptblock]$filtro, [string]$descripcion) {
    $script:hallada = $null
    Esperar { $script:hallada = Ventana-Del-Proceso $filtro; $null -ne $script:hallada } $descripcion
    return $script:hallada
}

# Responde «Sí» (id 6) en el cuadro de mensaje del programa y registra su texto.
function Responder-Si([string]$descripcion) {
    $cuadro = Esperar-Ventana { param($v) $v.Current.ClassName -eq '#32770' } $descripcion
    foreach ($t in $cuadro.FindAll($Scope::Descendants, (Condicion $AE::ControlTypeProperty $CT::Text))) {
        if ($t.Current.Name) { $registro.Add('  [cuadro] ' + ($t.Current.Name -replace "`r?`n", ' / ')) }
    }
    $si = $cuadro.FindFirst($Scope::Children, [System.Windows.Automation.AndCondition]::new(
        (Condicion $AE::AutomationIdProperty '6'), (Condicion $AE::ClassNameProperty 'Button')))
    $null = [ReprogramarR10.Win32]::PostMessage([IntPtr]$si.Current.NativeWindowHandle, $BM_CLICK, [IntPtr]::Zero, [IntPtr]::Zero)
    Esperar { $null -eq (Ventana-Del-Proceso { param($v) $v.Current.ClassName -eq '#32770' }) } "cerrar: $descripcion"
}

function Capturar($ventana, [string]$nombre) {
    $rect = $ventana.Current.BoundingRectangle
    $bitmap = [Drawing.Bitmap]::new([int]$rect.Width, [int]$rect.Height)
    $grafico = [Drawing.Graphics]::FromImage($bitmap)
    $hdc = $grafico.GetHdc()
    try {
        $null = [ReprogramarR10.Win32]::PrintWindow([IntPtr]$ventana.Current.NativeWindowHandle, $hdc, 2)
    } finally {
        $grafico.ReleaseHdc($hdc)
        $grafico.Dispose()
    }
    try { $bitmap.Save((Join-Path $salida $nombre), [Drawing.Imaging.ImageFormat]::Png) } finally { $bitmap.Dispose() }
}

function Esperar-Programacion([string]$texto) {
    Esperar { (Texto $mesa 'ScheduleStatus') -like "*$texto*" } "mensaje de la programación «$texto»"
    $registro.Add('  Mensaje: ' + (Texto $mesa 'ScheduleStatus'))
}

try {
    $datos = Join-Path $salida 'datos'
    $proceso = Start-Process -FilePath $Ejecutable -ArgumentList @('--perfil', 'demo', '--datos', ('"' + $datos + '"'), '--operador', '"Prueba R10"') -PassThru
    $script:mesa = $null
    Esperar {
        $script:mesa = $AE::RootElement.FindFirst($Scope::Children, [System.Windows.Automation.AndCondition]::new(
            (Condicion $AE::ProcessIdProperty $proceso.Id), (Condicion $AE::NameProperty 'Marcador de Waterpolo FDNDA — Mesa')))
        $null -ne $script:mesa -and (Buscar-Id $script:mesa 'StartMatch').Current.IsEnabled
    } 'abrir la mesa con el partido demo cargado'
    $registro.Add('Mesa abierta con datos aislados: ' + $datos)

    # 1. Reprogramar el partido demo 2 desde la pestaña «Partidos».
    Pestana 'MatchesTab'
    Elegir-Partido 'Partido demo 2 (vuelta)'
    Esperar { Habilitado $mesa 'RescheduleMatch' } 'Reprogramar habilitado para un partido programado'
    $registro.Add('Ayuda: ' + (Texto $mesa 'ListedMatchHint'))
    Pulsar $mesa 'RescheduleMatch'
    Esperar { (Texto $mesa 'ScheduleFormTitle') -like 'Reprogramar*' } 'formulario en modo reprogramación'
    $registro.Add('Formulario: «' + (Texto $mesa 'ScheduleFormTitle') + '», descripción «' + (Valor $mesa 'ScheduleLabel') +
        '», color local «' + (Valor $mesa 'ScheduleHomeColor') + '», equipos editables: ' + (Habilitado $mesa 'ScheduleHome'))
    if (Habilitado $mesa 'ScheduleHome') { throw 'Al reprogramar no se deben poder cambiar los equipos.' }
    Escribir $mesa 'ScheduleLabel' 'Vuelta reprogramada R10'
    Escribir $mesa 'ScheduleDate' '20/03/2027 18:00'
    Escribir $mesa 'ScheduleHomeColor' '#5D1A2B'
    Escribir $mesa 'ScheduleAwayCap' 'Negro'
    Capturar $mesa 'r10-reprogramar-formulario.png'
    Pulsar $mesa 'ScheduleMatch'
    Esperar-Programacion 'reprogramado'
    Elegir-Partido 'Vuelta reprogramada R10'
    if ($null -eq (Fila-Con-Texto (Buscar-Id $mesa 'MatchList') '20/03/2027 18:00')) { throw 'La lista no muestra la nueva fecha.' }
    if ((Texto $mesa 'ScheduleFormTitle') -ne 'Programar partido') { throw 'El formulario no volvió a «Programar partido».' }
    $registro.Add('Lista: «Vuelta reprogramada R10» · 20/03/2027 18:00; formulario: ' + (Texto $mesa 'ScheduleFormTitle'))

    # 2. Actualizar nómina sin cambios en el evento: se confirma y se rechaza sin escribir.
    Pulsar $mesa 'RefreshMatchRoster'
    Responder-Si 'confirmar actualizar la nómina'
    Esperar-Programacion 'ya coincide'

    # 3. Anular con motivo.
    Pulsar $mesa 'CancelMatch'
    $motivo = Esperar-Ventana { param($v) $v.Current.Name -eq 'Anular partido' } 'abrir el diálogo de motivo'
    Escribir $motivo 'DialogReason' 'Programado por error (prueba R10)'
    Pulsar $motivo 'DialogAccept'
    Esperar-Programacion 'anulado'
    Elegir-Partido 'Vuelta reprogramada R10'
    $fila = Fila-Con-Texto (Buscar-Id $mesa 'MatchList') 'Vuelta reprogramada R10'
    if ($null -eq $fila.FindFirst($Scope::Descendants, (Condicion $AE::NameProperty 'Anulado'))) { throw 'La fila no muestra «Anulado».' }
    Esperar { -not (Habilitado $mesa 'RescheduleMatch') -and -not (Habilitado $mesa 'CancelMatch') } 'acciones deshabilitadas en el anulado'
    $registro.Add('Anulado: estado «Anulado»; Reprogramar/Anular/Actualizar nómina deshabilitados. Ayuda: ' + (Texto $mesa 'ListedMatchHint'))
    Capturar $mesa 'r10-anulado.png'

    # 4. «Actualizar» del catálogo conserva colores y gorros escritos en el formulario.
    Elegir $mesa 'ScheduleHome' 0
    Elegir $mesa 'ScheduleAway' 1
    Escribir $mesa 'ScheduleHomeColor' '#123456'
    Escribir $mesa 'ScheduleAwayCap' 'Gorro escrito a mano'
    Pestana 'CatalogTab'
    Pulsar $mesa 'RefreshCatalog'
    Start-Sleep -Milliseconds 500
    Pestana 'MatchesTab'
    Esperar { (Valor $mesa 'ScheduleHomeColor') -eq '#123456' -and (Valor $mesa 'ScheduleAwayCap') -eq 'Gorro escrito a mano' } 'colores conservados tras Actualizar'
    $registro.Add('Tras «Actualizar»: color local «' + (Valor $mesa 'ScheduleHomeColor') + '», gorro visita «' + (Valor $mesa 'ScheduleAwayCap') + '»')

    # 5. Nombre de equipo largo (80 caracteres) en la lista de equipos: recorte con «…» y nombre completo en la ayuda.
    Pestana 'CatalogTab'
    Pulsar $mesa 'NewTeam'
    Escribir $mesa 'TeamCode' 'LRG'
    Escribir $mesa 'TeamName' 'Club Deportivo de Waterpolo de Prueba con un Nombre Bastante Largo del Litoral'
    Escribir $mesa 'TeamColor' '#00897B'
    Escribir $mesa 'TeamCapColor' 'Blanco'
    Pulsar $mesa 'SaveTeam'
    Esperar { $null -ne (Fila-Con-Texto (Buscar-Id $mesa 'TeamList') 'LRG') } 'equipo largo en la lista'
    $registro.Add('Equipo largo creado; ver captura r10-equipo-largo.png')
    Capturar $mesa 'r10-equipo-largo.png'
    $registro.Add('RECORRIDO COMPLETO')
} finally {
    $registro | Set-Content -Encoding UTF8 (Join-Path $salida 'recorrido.txt')
    if ($null -ne $proceso -and -not $proceso.HasExited) {
        $null = $proceso.CloseMainWindow()
        if (-not $proceso.WaitForExit(5000)) { $proceso.Kill() }
    }
    Write-Output "Salida: $salida"
}
