# Recorrido del rediseño del marcador del público («A · Transmisión», 2026-11-25) en el escritorio interactivo.
# Usa datos nuevos bajo artifacts. No envía teclas ni mueve el ratón: opera AutomationId con UI
# Automation; los cuadros de confirmación de Windows se responden con un mensaje dirigido a «Sí».
# Capturas con PrintWindow: el marcador no toma el foco.
[CmdletBinding()]
param(
    [string]$Ejecutable
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'entorno.ps1')
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -Namespace MarcadorDiseno -Name Win32 -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool PostMessage(System.IntPtr hWnd, int msg, System.IntPtr wParam, System.IntPtr lParam);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool PrintWindow(System.IntPtr hWnd, System.IntPtr hdc, uint flags);
'@

$raizProyecto = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$salida = Join-Path $raizProyecto ('artifacts\verificacion-diseno-' + [guid]::NewGuid().ToString('N'))
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

function Existe($ventana, [string]$id) {
    $control = $ventana.FindFirst($Scope::Descendants, (Condicion $AE::AutomationIdProperty $id))
    return $null -ne $control -and -not $control.Current.IsOffscreen
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
    $item = $null
    if ($control.TryGetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern, [ref]$item)) { $item.ScrollIntoView() }
    if (-not $control.Current.IsEnabled) { throw "La acción $id está deshabilitada." }
    $control.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Habilitado($ventana, [string]$id) { (Buscar-Id $ventana $id).Current.IsEnabled }

function Pestana([string]$id) {
    (Buscar-Id $mesa $id).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
}

function Texto($ventana, [string]$id) { (Buscar-Id $ventana $id).Current.Name }

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

# Nombre de la opción elegida en un ComboBox (su texto visible).
function Elegido($ventana, [string]$id) {
    $seleccion = (Buscar-Id $ventana $id).GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
    if ($seleccion.Count -eq 0) { return '' }
    $texto = $seleccion[0].FindFirst($Scope::Descendants, (Condicion $AE::ControlTypeProperty $CT::Text))
    if ($null -ne $texto) { return $texto.Current.Name }
    return $seleccion[0].Current.Name
}

function Fila-Con-Texto($control, [string]$texto) {
    foreach ($fila in (Elementos $control)) {
        if ($fila.FindFirst($Scope::Descendants, (Condicion $AE::NameProperty $texto))) { return $fila }
    }
    return $null
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
    $null = [MarcadorDiseno.Win32]::PostMessage([IntPtr]$si.Current.NativeWindowHandle, $BM_CLICK, [IntPtr]::Zero, [IntPtr]::Zero)
    Esperar { $null -eq (Ventana-Del-Proceso { param($v) $v.Current.ClassName -eq '#32770' }) } "cerrar: $descripcion"
}

# PrintWindow copia el contenido de la ventana aunque otra la tape: el marcador no toma el foco.
function Capturar($ventana, [string]$nombre) {
    $rect = $ventana.Current.BoundingRectangle
    $bitmap = [Drawing.Bitmap]::new([int]$rect.Width, [int]$rect.Height)
    $grafico = [Drawing.Graphics]::FromImage($bitmap)
    $hdc = $grafico.GetHdc()
    try {
        $null = [MarcadorDiseno.Win32]::PrintWindow([IntPtr]$ventana.Current.NativeWindowHandle, $hdc, 2)
    } finally {
        $grafico.ReleaseHdc($hdc)
        $grafico.Dispose()
    }
    try { $bitmap.Save((Join-Path $salida $nombre), [Drawing.Imaging.ImageFormat]::Png) } finally { $bitmap.Dispose() }
}

function Se-Solapan($a, $b) { $a.Current.BoundingRectangle.IntersectsWith($b.Current.BoundingRectangle) }

function Gol([string]$lado, [int]$jugador) {
    $antes = Texto $mesa 'StatusMessage'
    Elegir $mesa "${lado}Roster" $jugador
    Pulsar $mesa "Goal$lado"
    Esperar { (Texto $mesa 'StatusMessage') -ne $antes -and (Texto $mesa 'StatusMessage') -like 'Gol de*' } "gol $lado"
}

function Lanzamiento([string]$lado, [int]$jugador, [int]$opcion) {
    $antes = Texto $mesa 'StatusMessage'
    Elegir $mesa 'IncidentType' $opcion
    Elegir $mesa "${lado}Roster" $jugador
    $boton = if ($lado -eq 'Home') { 'IncidentHome' } else { 'IncidentAway' }
    Pulsar $mesa $boton
    Esperar { (Texto $mesa 'StatusMessage') -ne $antes -and (Texto $mesa 'StatusMessage') -like 'Tanda:*' } "lanzamiento $lado"
    $registro.Add('  ' + (Texto $mesa 'StatusMessage'))
}

function Etapa([string]$accion, [string]$esperada) {
    Pulsar $mesa $accion
    Esperar { (Texto $mesa 'StageText') -eq $esperada } "etapa $esperada"
}


function Estado([string]$nombre) {
    $reloj = if (Existe $publico 'PublicGameClock') { (Texto $publico 'PublicGameClock') + ' / ' + (Texto $publico 'PublicShotClock') } else { 'ocultos' }
    $resultado = if (Existe $publico 'PublicHomeScore') { (Texto $publico 'PublicHomeScore') + '–' + (Texto $publico 'PublicAwayScore') } else { 'oculto' }
    $registro.Add(('{0}: «{1}», relojes {2}, resultado {3}' -f $nombre, (Texto $publico 'PublicStage'), $reloj, $resultado))
}

# El aviso cubre el lado del equipo y nunca la columna central de los relojes.
function Sin-Tapar-Relojes([string]$aviso) {
    foreach ($reloj in 'PublicGameClock', 'PublicShotClock', 'PublicStage') {
        if (Se-Solapan (Buscar-Id $publico 'PublicActionBanner') (Buscar-Id $publico $reloj)) { throw "El aviso de $aviso tapa $reloj." }
    }
    $registro.Add("  El aviso de $aviso no se superpone con el reloj, la posesión ni el periodo (rectángulos en pantalla).")
}

function Aviso([string]$boton, [int]$opcion, [string]$lado, [int]$jugador, [string]$esperado, [string]$archivo) {
    Elegir $mesa 'IncidentType' $opcion
    if ($jugador -ge 0) { Elegir $mesa "${lado}Roster" $jugador }
    Pulsar $mesa $boton
    Esperar { (Texto $publico 'PublicActionBanner') -like "$esperado*" } "aviso $esperado"
    Start-Sleep -Milliseconds 1500
    $registro.Add('Aviso «' + (Texto $publico 'PublicActionBanner') + '»')
    Sin-Tapar-Relojes $esperado
    Capturar $publico $archivo
    Start-Sleep -Milliseconds 3200
}

try {
    $datos = Join-Path $salida 'datos'
    $proceso = Start-Process -FilePath $Ejecutable -ArgumentList @('--perfil', 'demo', '--datos', ('"' + $datos + '"'), '--operador', '"Prueba diseño"') -PassThru
    $script:mesa = $null
    Esperar {
        $script:mesa = $AE::RootElement.FindFirst($Scope::Children, [System.Windows.Automation.AndCondition]::new(
            (Condicion $AE::ProcessIdProperty $proceso.Id), (Condicion $AE::NameProperty 'Marcador de Waterpolo FDNDA — Mesa')))
        $null -ne $script:mesa -and (Buscar-Id $script:mesa 'StartMatch').Current.IsEnabled
    } 'abrir la mesa con el partido demo cargado'
    $registro.Add('Mesa abierta con datos aislados: ' + $datos)

    Pulsar $mesa 'OpenScoreboard'
    $script:publico = Esperar-Ventana { param($v) $v.Current.Name -eq 'Marcador — Waterpolo FDNDA' } 'abrir el marcador'
    Esperar { (Texto $publico 'PublicStage') -eq 'PRÓXIMO PARTIDO' } 'previa'
    Start-Sleep -Milliseconds 600
    Estado 'Previa'
    Capturar $publico 'diseno-01-previa.png'

    Pulsar $mesa 'StartMatch'
    Esperar { (Texto $publico 'PublicStage') -eq '1.er PERIODO' } 'primer periodo'
    Pulsar $mesa 'SimStart'
    Esperar { (Texto $publico 'PublicGameClock') -ne '--:--' } 'reloj vigente'
    Start-Sleep -Milliseconds 1500
    Estado 'En juego'
    Capturar $publico 'diseno-02-en-juego.png'

    Gol 'Home' 4
    Esperar { (Texto $publico 'PublicHomeScore') -eq '1' } 'resultado local 1'
    Start-Sleep -Milliseconds 1400
    $registro.Add('Gol -> aviso «' + (Texto $publico 'PublicActionBanner') + '»')
    Sin-Tapar-Relojes 'gol'
    Capturar $publico 'diseno-03-gol.png'
    Start-Sleep -Milliseconds 3100
    Capturar $publico 'diseno-04-latido.png'
    Start-Sleep -Milliseconds 1200

    Aviso 'IncidentAway' 0 'Away' 1 'EXCLUSIÓN' 'diseno-05-exclusion.png'
    Aviso 'IncidentHome' 1 'Home' 2 'EXPULSIÓN' 'diseno-06-expulsion.png'
    Aviso 'IncidentAway' 5 'Away' -1 'TIEMPO MUERTO' 'diseno-07-tiempo-muerto.png'
    Gol 'Away' 3
    Esperar { (Texto $publico 'PublicAwayScore') -eq '1' } 'resultado visita 1'
    Start-Sleep -Milliseconds 1400
    Capturar $publico 'diseno-08-gol-visita.png'
    Start-Sleep -Milliseconds 3200

    Pulsar $mesa 'SimDropSignal'
    Esperar { (Texto $publico 'PublicGameClock') -eq '--:--' -and (Existe $publico 'PublicSignalNotice') } 'reloj sin señal'
    Start-Sleep -Milliseconds 500
    Estado 'Sin señal'
    Capturar $publico 'diseno-09-sin-senal.png'
    Pulsar $mesa 'SimRestoreSignal'
    Esperar { (Texto $publico 'PublicGameClock') -ne '--:--' } 'señal restablecida'

    Pulsar $mesa 'StartBreak'
    Esperar { (Texto $publico 'PublicStage') -eq 'FIN DEL 1.er PERIODO' } 'descanso'
    Start-Sleep -Milliseconds 500
    Estado 'Descanso'
    Capturar $publico 'diseno-10-descanso.png'

    Pulsar $mesa 'NextPeriod'
    Esperar { (Texto $publico 'PublicStage') -eq '2.º PERIODO' } 'segundo periodo'
    Gol 'Home' 5
    Start-Sleep -Milliseconds 4800
    Pulsar $mesa 'FinishMatch'
    Responder-Si 'confirmar el cierre del partido'
    Esperar { (Texto $publico 'PublicStage') -eq 'RESULTADO FINAL' } 'resultado final'
    Start-Sleep -Milliseconds 500
    Estado 'Final'
    Capturar $publico 'diseno-11-final.png'
    $registro.Add('RECORRIDO COMPLETO')
} finally {
    $registro | Set-Content -Encoding UTF8 (Join-Path $salida 'recorrido.txt')
    if ($null -ne $proceso -and -not $proceso.HasExited) {
        $null = $proceso.CloseMainWindow()
        if (-not $proceso.WaitForExit(5000)) { $proceso.Kill() }
    }
    Write-Output "Salida: $salida"
}
