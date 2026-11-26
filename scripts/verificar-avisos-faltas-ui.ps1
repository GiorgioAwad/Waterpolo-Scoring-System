# Recorrido de los avisos de falta, fin de tiempo muerto y periodo, y de las faltas por jugador (2026-11-25).
# Spec: docs/superpowers/specs/2026-11-25-avisos-y-faltas-design.md.
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
Add-Type -Namespace MarcadorAvisos -Name Win32 -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool PostMessage(System.IntPtr hWnd, int msg, System.IntPtr wParam, System.IntPtr lParam);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool PrintWindow(System.IntPtr hWnd, System.IntPtr hdc, uint flags);
'@

$raizProyecto = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$salida = Join-Path $raizProyecto ('artifacts\verificacion-avisos-' + [guid]::NewGuid().ToString('N'))
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
    $null = [MarcadorAvisos.Win32]::PostMessage([IntPtr]$si.Current.NativeWindowHandle, $BM_CLICK, [IntPtr]::Zero, [IntPtr]::Zero)
    Esperar { $null -eq (Ventana-Del-Proceso { param($v) $v.Current.ClassName -eq '#32770' }) } "cerrar: $descripcion"
}

# PrintWindow copia el contenido de la ventana aunque otra la tape: el marcador no toma el foco.
function Capturar($ventana, [string]$nombre) {
    $rect = $ventana.Current.BoundingRectangle
    $bitmap = [Drawing.Bitmap]::new([int]$rect.Width, [int]$rect.Height)
    $grafico = [Drawing.Graphics]::FromImage($bitmap)
    $hdc = $grafico.GetHdc()
    try {
        $null = [MarcadorAvisos.Win32]::PrintWindow([IntPtr]$ventana.Current.NativeWindowHandle, $hdc, 2)
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


function Incidencia([string]$boton, [int]$opcion, [string]$lado, [int]$jugador, [string]$esperado) {
    Elegir $mesa 'IncidentType' $opcion
    if ($jugador -ge 0) { Elegir $mesa "${lado}Roster" $jugador }
    Pulsar $mesa $boton
    Esperar { (Texto $publico 'PublicActionBanner') -like "$esperado*" } "aviso $esperado"
}

function Faltas([string]$id) {
    $lista = Buscar-Id $publico $id
    $nombres = foreach ($e in $lista.FindAll($Scope::Descendants, (Condicion $AE::ControlTypeProperty $CT::Text))) { $e.Current.Name }
    return ($nombres | Where-Object { $_ }) -join ' | '
}

try {
    $datos = Join-Path $salida 'datos'
    $proceso = Start-Process -FilePath $Ejecutable -ArgumentList @('--perfil', 'demo', '--datos', ('"' + $datos + '"'), '--operador', '"Prueba avisos"') -PassThru
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

    # Inicio del partido: anuncio del centro
    Pulsar $mesa 'StartMatch'
    Esperar { (Texto $publico 'PublicStage') -eq '1.er PERIODO' } 'primer periodo'
    Start-Sleep -Milliseconds 350
    Capturar $publico 'avisos-01-anuncio-inicio.png'
    Pulsar $mesa 'SimStart'
    Esperar { (Texto $publico 'PublicGameClock') -ne '--:--' } 'reloj vigente'
    Start-Sleep -Milliseconds 2500

    # Falta personal y exclusión del mismo jugador: 1.ª y 2.ª falta
    Incidencia 'IncidentHome' 2 'Home' 2 'FALTA'
    Start-Sleep -Milliseconds 1500
    $registro.Add('Aviso «' + (Texto $publico 'PublicActionBanner') + '»')
    Sin-Tapar-Relojes 'falta personal'
    Capturar $publico 'avisos-02-falta-personal.png'
    Start-Sleep -Milliseconds 3300
    Capturar $publico 'avisos-03-gorro-late.png'
    Start-Sleep -Milliseconds 1200

    Incidencia 'IncidentHome' 0 'Home' 2 'EXCLUSIÓN'
    Start-Sleep -Milliseconds 1500
    $registro.Add('Aviso «' + (Texto $publico 'PublicActionBanner') + '»')
    Capturar $publico 'avisos-04-exclusion-2-falta.png'
    Start-Sleep -Milliseconds 3300

    Incidencia 'IncidentAway' 2 'Away' 4 'FALTA'
    Start-Sleep -Milliseconds 4500
    Incidencia 'IncidentAway' 1 'Away' 6 'EXPULSIÓN'
    Start-Sleep -Milliseconds 4500
    Incidencia 'IncidentHome' 2 'Home' 1 'FALTA'
    Start-Sleep -Milliseconds 5500
    $registro.Add('Faltas local: ' + (Faltas 'PublicHomeFouls'))
    $registro.Add('Faltas visita: ' + (Faltas 'PublicAwayFouls'))
    Capturar $publico 'avisos-05-faltas-en-pantalla.png'

    # Tiempo muerto: registro en la mesa, cuenta en el anillo y fin desde la «consola» simulada
    Incidencia 'IncidentAway' 5 'Away' -1 'TIEMPO MUERTO'
    Start-Sleep -Milliseconds 4500
    Pulsar $mesa 'SimStartTimeout'
    Esperar { Existe $publico 'PublicTimeoutClock' } 'cuenta del tiempo muerto'
    Start-Sleep -Milliseconds 3000
    $registro.Add('Cuenta del tiempo muerto: ' + (Texto $publico 'PublicTimeoutClock') + ' (reloj de partido ' + (Texto $publico 'PublicGameClock') + ')')
    Capturar $publico 'avisos-06-cuenta-tiempo-muerto.png'
    Pulsar $mesa 'SimClearTimeout'
    Esperar { (Texto $publico 'PublicActionBanner') -like 'FIN DEL TIEMPO MUERTO*' } 'aviso de fin del tiempo muerto'
    Start-Sleep -Milliseconds 1500
    $registro.Add('Aviso «' + (Texto $publico 'PublicActionBanner') + '»')
    Sin-Tapar-Relojes 'fin del tiempo muerto'
    Capturar $publico 'avisos-07-fin-tiempo-muerto.png'
    Start-Sleep -Milliseconds 3300

    # Descanso, segundo periodo y final: anuncios del centro
    Pulsar $mesa 'StartBreak'
    Esperar { (Texto $publico 'PublicStage') -eq 'FIN DEL 1.er PERIODO' } 'descanso'
    Start-Sleep -Milliseconds 450
    Capturar $publico 'avisos-08-anuncio-descanso.png'
    Start-Sleep -Milliseconds 2000
    Capturar $publico 'avisos-09-descanso.png'

    Pulsar $mesa 'NextPeriod'
    Esperar { (Texto $publico 'PublicStage') -eq '2.º PERIODO' } 'segundo periodo'
    Start-Sleep -Milliseconds 2500
    Gol 'Home' 1
    Start-Sleep -Milliseconds 5600
    Pulsar $mesa 'FinishMatch'
    Responder-Si 'confirmar el cierre del partido'
    Esperar { (Texto $publico 'PublicStage') -eq 'RESULTADO FINAL' } 'resultado final'
    Start-Sleep -Milliseconds 950
    Capturar $publico 'avisos-10-anuncio-final.png'
    Start-Sleep -Milliseconds 2000
    Capturar $publico 'avisos-11-final.png'
    $registro.Add('RECORRIDO COMPLETO')
} finally {
    if ($null -ne $mesa) { try { Capturar $mesa 'mesa-al-terminar.png' } catch { } }
    $registro | Set-Content -Encoding UTF8 (Join-Path $salida 'recorrido.txt')
    if ($null -ne $proceso -and -not $proceso.HasExited) {
        $null = $proceso.CloseMainWindow()
        if (-not $proceso.WaitForExit(5000)) { $proceso.Kill() }
    }
    Write-Output "Salida: $salida"
}
