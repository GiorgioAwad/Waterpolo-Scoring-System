# Recorrido de R1 en el escritorio interactivo. Usa exclusivamente datos nuevos bajo artifacts.
# No envía teclas: opera AutomationId, InvokePattern, SelectionItemPattern y ValuePattern.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'entorno.ps1')
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing

$raizProyecto = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$salida = Join-Path $raizProyecto ('artifacts\verificacion-r1-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $salida
$ejecutable = Join-Path $raizProyecto 'src\FDNDA.Waterpolo.Desktop\bin\Release\net10.0-windows\FDNDA.Waterpolo.exe'
$registro = [Collections.Generic.List[string]]::new()
$proceso = $null

function Buscar-Id($ventana, [string]$id) {
    $condicion = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    $control = $ventana.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condicion)
    if ($null -eq $control) { throw "No se encontró el control $id." }
    return $control
}

function Esperar([scriptblock]$condicion, [string]$descripcion) {
    for ($intento = 0; $intento -lt 100; $intento++) {
        if (& $condicion) { return }
        Start-Sleep -Milliseconds 100
    }
    throw "No se cumplió: $descripcion."
}

function Pulsar($ventana, [string]$id) {
    $control = Buscar-Id $ventana $id
    Mostrar-Control $ventana $control
    if (-not $control.Current.IsEnabled) { throw "La acción $id está deshabilitada." }
    $control.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Elementos($control) {
    $lista = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    $fila = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::DataItem)
    $condicion = [System.Windows.Automation.OrCondition]::new($lista, $fila)
    return ,$control.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condicion)
}

function Elegir($ventana, [string]$id, [int]$indice) {
    $control = Buscar-Id $ventana $id
    Mostrar-Control $ventana $control
    $expansion = $null
    if ($control.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$expansion)) {
        $expansion.Expand()
    }
    Esperar { (Elementos $control).Count -gt $indice } "cargar las opciones de $id"
    $items = Elementos $control
    if ($items.Count -le $indice) { throw "No existe opción $indice en $id (hay $($items.Count))." }
    $items[$indice].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    if ($null -ne $expansion) { $expansion.Collapse() }
}

function Motivo($ventana, [string]$texto) {
    $control = Buscar-Id $ventana 'ReasonText'
    Mostrar-Control $ventana $control
    $control.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($texto)
}

function Pestana($ventana, [string]$id) {
    (Buscar-Id $ventana $id).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    if ($id -eq 'IncidentCorrectionTab') {
        $scroll = (Buscar-Id $ventana 'CorrectionPanel').GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
        if ($scroll.Current.VerticallyScrollable) { $scroll.SetScrollPercent(-1, 0) }
    }
}

function Mostrar-Control($ventana, $control) {
    if ($control.Current.IsOffscreen) {
        $scroll = (Buscar-Id $ventana 'CorrectionPanel').GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
        for ($paso = 0; $paso -lt 20 -and $control.Current.IsOffscreen; $paso++) {
            $scroll.Scroll([System.Windows.Automation.ScrollAmount]::NoAmount, [System.Windows.Automation.ScrollAmount]::SmallIncrement)
        }
    }
    if ($control.Current.IsOffscreen) { throw "El control $($control.Current.AutomationId) no es visible ni accesible al desplazar." }
}

function Esperar-Estado($ventana, [string]$texto) {
    Esperar { (Buscar-Id $ventana 'StatusMessage').Current.Name -like "*$texto*" } $texto
    $registro.Add((Buscar-Id $ventana 'StatusMessage').Current.Name)
}

function Capturar($ventana, [string]$nombre) {
    $rect = $ventana.Current.BoundingRectangle
    $bitmap = [Drawing.Bitmap]::new([int]$rect.Width, [int]$rect.Height)
    $grafico = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $grafico.CopyFromScreen([int]$rect.X, [int]$rect.Y, 0, 0, $bitmap.Size)
        $bitmap.Save((Join-Path $salida $nombre), [Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $grafico.Dispose()
        $bitmap.Dispose()
    }
}

try {
    $datos = Join-Path $salida 'datos'
    $proceso = Start-Process -FilePath $ejecutable -ArgumentList @('--perfil', 'demo', '--datos', ('"' + $datos + '"'), '--operador', '"Prueba R1"') -PassThru -WindowStyle Hidden
    $condicionProceso = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proceso.Id)
    $script:mesa = $null
    Esperar {
        $script:mesa = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
            [System.Windows.Automation.TreeScope]::Children, $condicionProceso)
        $null -ne $script:mesa
    } 'abrir la mesa con datos aislados'
    Esperar { (Buscar-Id $mesa 'StartMatch').Current.IsEnabled } 'cargar partido demo'
    Pulsar $mesa 'StartMatch'
    Esperar-Estado $mesa 'Partido iniciado'
    Elegir $mesa 'HomeRoster' 0
    Pulsar $mesa 'GoalHome'
    Esperar-Estado $mesa 'Gol de'
    Esperar { (Buscar-Id $mesa 'HomeScore').Current.Name -eq '1' } 'gol local'

    # Opciones: exclusión, expulsión, falta, penal convertido, penal no convertido y tiempo muerto.
    for ($opcion = 0; $opcion -lt 6; $opcion++) {
        Elegir $mesa 'IncidentType' $opcion
        Pulsar $mesa 'IncidentHome'
        Esperar-Estado $mesa 'registrado'
        Elegir $mesa 'IncidentList' 0
        Pestana $mesa 'IncidentCorrectionTab'
        if ($opcion -lt 2) { Elegir $mesa 'CorrectionDetail' (1 - $opcion) }
        elseif ($opcion -eq 2) { Elegir $mesa 'CorrectionPlayer' 1 }
        elseif ($opcion -eq 3) { Elegir $mesa 'CorrectionDetail' 1 }
        elseif ($opcion -eq 4) { Elegir $mesa 'CorrectionDetail' 0 }
        else { Elegir $mesa 'CorrectionTeam' 1 }
        Motivo $mesa 'Corrección ficticia de R1'
        Pulsar $mesa 'CorrectIncident'
        Esperar-Estado $mesa 'corregida'
        if ($opcion -eq 0) { Capturar $mesa 'correccion-r1.png' }
        if ($opcion -eq 3) {
            Esperar { (Buscar-Id $mesa 'HomeScore').Current.Name -eq '1' } 'penal corregido a no convertido'
        }
        Motivo $mesa 'Anulación ficticia de R1'
        Pulsar $mesa 'VoidIncident'
        Esperar-Estado $mesa 'anulada'
        Pestana $mesa 'IncidentHistoryTab'
    }

    Pulsar $mesa 'SimDropSignal'
    Esperar { (Buscar-Id $mesa 'GameClock').Current.Name -eq '--:--' } 'reloj no vigente'
    Elegir $mesa 'IncidentType' 2
    if ((Buscar-Id $mesa 'IncidentAway').Current.IsEnabled) { throw 'Se permitió una falta sin jugador.' }
    Elegir $mesa 'AwayRoster' 0
    Pulsar $mesa 'IncidentAway'
    Esperar-Estado $mesa 'SIN reloj'
    Esperar { (Buscar-Id $mesa 'HomeScore').Current.Name -eq '1' } 'resultado final local'
    if ((Buscar-Id $mesa 'AwayScore').Current.Name -ne '0') { throw 'Resultado de visita inesperado.' }
    Capturar $mesa 'mesa-r1.png'

    Pulsar $mesa 'OpenScoreboard'
    $script:publico = $null
    Esperar {
        $ventanas = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Children, $condicionProceso)
        $script:publico = $ventanas | Where-Object { $_.Current.Name -eq 'Marcador — Waterpolo FDNDA' } | Select-Object -First 1
        $null -ne $script:publico
    } 'abrir marcador'
    Esperar { (Buscar-Id $publico 'PublicHomeScore').Current.Name -eq '1' } 'resultado del público'
    if ((Buscar-Id $publico 'PublicAwayScore').Current.Name -ne '0') { throw 'Resultado público de visita inesperado.' }
    if ((Buscar-Id $publico 'PublicGameClock').Current.Name -ne '--:--') { throw 'El público muestra un tiempo no vigente.' }
    Capturar $publico 'publico-r1.png'
    Pulsar $mesa 'CloseScoreboard'
    $registro.Add('CORRECTO: seis opciones registradas, corregidas y anuladas; penal contado una vez; falta sin reloj; público 1–0.')
} catch {
    $registro.Add('ERROR: ' + $_.Exception.Message)
    if ($null -ne $mesa) {
        try { Capturar $mesa 'mesa-error.png' } catch { $registro.Add('Sin captura: ' + $_.Exception.Message) }
    }
    throw
} finally {
    $registro | Set-Content -Encoding UTF8 (Join-Path $salida 'recorrido.txt')
    if ($null -ne $proceso -and -not $proceso.HasExited) {
        $null = $proceso.CloseMainWindow()
        if (-not $proceso.WaitForExit(5000)) { Stop-Process -Id $proceso.Id }
    }
    Write-Host "Evidencia: $salida"
}
