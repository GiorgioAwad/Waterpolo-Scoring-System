# Recorrido de R8 (prórroga y tanda de penales, RF04/RF05/RF09) en el escritorio interactivo.
# Usa datos nuevos bajo artifacts. No envía teclas ni mueve el ratón: opera AutomationId con UI
# Automation; los cuadros de confirmación de Windows se responden con un mensaje dirigido a «Sí».
# Las reglas de prórroga y tanda que se escriben son valores de prueba, no un reglamento aprobado.
[CmdletBinding()]
param(
    [string]$Ejecutable
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'entorno.ps1')
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -Namespace TandaR8 -Name Win32 -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool PostMessage(System.IntPtr hWnd, int msg, System.IntPtr wParam, System.IntPtr lParam);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool PrintWindow(System.IntPtr hWnd, System.IntPtr hdc, uint flags);
'@

$raizProyecto = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$salida = Join-Path $raizProyecto ('artifacts\verificacion-r8-' + [guid]::NewGuid().ToString('N'))
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
    $null = [TandaR8.Win32]::PostMessage([IntPtr]$si.Current.NativeWindowHandle, $BM_CLICK, [IntPtr]::Zero, [IntPtr]::Zero)
    Esperar { $null -eq (Ventana-Del-Proceso { param($v) $v.Current.ClassName -eq '#32770' }) } "cerrar: $descripcion"
}

# PrintWindow copia el contenido de la ventana aunque otra la tape: el marcador no toma el foco.
function Capturar($ventana, [string]$nombre) {
    $rect = $ventana.Current.BoundingRectangle
    $bitmap = [Drawing.Bitmap]::new([int]$rect.Width, [int]$rect.Height)
    $grafico = [Drawing.Graphics]::FromImage($bitmap)
    $hdc = $grafico.GetHdc()
    try {
        $null = [TandaR8.Win32]::PrintWindow([IntPtr]$ventana.Current.NativeWindowHandle, $hdc, 2)
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

try {
    $datos = Join-Path $salida 'datos'
    $proceso = Start-Process -FilePath $Ejecutable -ArgumentList @('--perfil', 'demo', '--datos', ('"' + $datos + '"'), '--operador', '"Prueba R8"') -PassThru
    $script:mesa = $null
    Esperar {
        $script:mesa = $AE::RootElement.FindFirst($Scope::Children, [System.Windows.Automation.AndCondition]::new(
            (Condicion $AE::ProcessIdProperty $proceso.Id), (Condicion $AE::NameProperty 'Marcador de Waterpolo FDNDA — Mesa')))
        $null -ne $script:mesa -and (Buscar-Id $script:mesa 'StartMatch').Current.IsEnabled
    } 'abrir la mesa con el partido demo cargado'
    $registro.Add('Mesa abierta con datos aislados: ' + $datos)

    # 1. Reglas del evento: una prórroga de 3:00 y tanda habilitada (valores de prueba).
    Pestana 'CatalogTab'
    Esperar { (Elementos (Buscar-Id $mesa 'TournamentList')).Count -ge 1 } 'cargar eventos'
    (Elementos (Buscar-Id $mesa 'TournamentList'))[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Pulsar $mesa 'EditRules'
    $reglas = Esperar-Ventana { param($v) $v.Current.Name -like 'Reglas del evento*' } 'abrir el editor de reglas'
    $registro.Add('Editor de reglas: prórroga «' + (Buscar-Id $reglas 'RulesOvertimePeriods').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value +
        '» × «' + (Buscar-Id $reglas 'RulesOvertimeDuration').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value + '»')
    Escribir $reglas 'RulesOvertimePeriods' 'dos'
    Esperar { (Texto $reglas 'RulesOutcome') -like 'Revise:*prórroga*' } 'aviso de prórroga inválida'
    $registro.Add('Prórroga «dos» -> ' + (Texto $reglas 'RulesOutcome'))
    Escribir $reglas 'RulesOvertimePeriods' '1'
    Escribir $reglas 'RulesOvertimeDuration' '3:00'
    (Buscar-Id $reglas 'RulesShootout').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Esperar { (Texto $reglas 'RulesOutcome') -like '*sin aprobación técnica*' } 'aviso de reglas personalizadas'
    $registro.Add('Prórroga 1 × 3:00 y tanda -> ' + (Texto $reglas 'RulesOutcome'))
    Capturar $reglas 'r8-reglas.png'
    Pulsar $reglas 'SaveRules'
    Esperar { (Texto $mesa 'CatalogStatus') -like 'Reglas de*guardadas*' } 'reglas guardadas'
    Esperar { (Texto $mesa 'TournamentRules') -like '*prórroga 1 × 3:00*' } 'tarjeta del evento con la prórroga'
    $registro.Add('Tarjeta del evento: ' + (Texto $mesa 'TournamentRules'))

    # 2. Partido: 1–1 en el reglamentario.
    Pestana 'MesaTab'
    Pulsar $mesa 'OpenScoreboard'
    $publico = Esperar-Ventana { param($v) $v.Current.Name -eq 'Marcador — Waterpolo FDNDA' } 'abrir el marcador'
    Pulsar $mesa 'StartMatch'
    Esperar { (Texto $mesa 'StageText') -eq 'PERIODO 1' } 'partido iniciado'
    $registro.Add('Partido iniciado -> ' + (Texto $mesa 'MatchRules'))
    Gol 'Home' 0
    Gol 'Away' 0
    foreach ($periodo in 2..4) {
        Etapa 'StartBreak' ('DESCANSO · tras periodo ' + ($periodo - 1))
        Etapa 'NextPeriod' ('PERIODO ' + $periodo)
    }
    Etapa 'StartBreak' 'DESCANSO · tras periodo 4'
    $botonSiguiente = (Buscar-Id $mesa 'NextPeriod').Current.Name
    $registro.Add('Tras el cuarto periodo, 1–1: botón «' + $botonSiguiente + '»; «Iniciar tanda» habilitado: ' + (Habilitado $mesa 'StartShootout') +
        ' (ayuda: «' + (Buscar-Id $mesa 'StartShootout').Current.HelpText + '»)')
    if ($botonSiguiente -ne 'Iniciar prórroga') { throw "El botón dice «$botonSiguiente»." }
    if (Habilitado $mesa 'StartShootout') { throw 'La tanda quedó habilitada antes de la prórroga.' }

    # 3. Prórroga: 2–2.
    Etapa 'NextPeriod' 'PRÓRROGA 1'
    Esperar { (Texto $publico 'PublicStage') -eq 'PRÓRROGA 1' } 'marcador en prórroga'
    $registro.Add('Prórroga -> mesa «' + (Texto $mesa 'StageText') + '», marcador «' + (Texto $publico 'PublicStage') + '», estado «' + (Texto $mesa 'StatusMessage') + '»')
    Gol 'Home' 1
    Gol 'Away' 1
    Capturar $publico 'r8-marcador-prorroga.png'
    # El aviso del último gol dura 4 s: se deja terminar para que la captura de la tanda no lo muestre.
    Start-Sleep -Milliseconds 4800
    Etapa 'StartBreak' 'DESCANSO · tras prórroga 1'
    if (-not (Habilitado $mesa 'StartShootout')) { throw 'La tanda no quedó habilitada con 2–2 tras la prórroga.' }

    # 4. Tanda: confirmación, lanzamientos con F6/F9 (aquí, sus botones) y marcador aparte.
    Pulsar $mesa 'StartShootout'
    Responder-Si 'confirmar el inicio de la tanda'
    Esperar { (Texto $mesa 'StageText') -eq 'TANDA DE PENALES' } 'etapa de tanda'
    Esperar { (Texto $publico 'PublicStage') -eq 'PENALES' } 'marcador en tanda'
    $registro.Add('Tanda iniciada -> mesa «' + (Texto $mesa 'StageText') + '», marcador «' + (Texto $publico 'PublicStage') + '», acción elegida «' + ((Elegido $mesa 'IncidentType') -replace '^.*Label = (.*) \}$', '$1') + '»')
    $avisoAntes = Texto $publico 'PublicActionBanner'
    if (Habilitado $mesa 'GoalHome') { throw 'El gol de juego sigue habilitado durante la tanda.' }
    $registro.Add('  GOL local/visita deshabilitados durante la tanda: ' + (-not (Habilitado $mesa 'GoalHome') -and -not (Habilitado $mesa 'GoalAway')))
    Lanzamiento 'Home' 2 0
    Lanzamiento 'Away' 2 1
    Lanzamiento 'Home' 3 0
    Lanzamiento 'Away' 3 0
    Esperar { (Texto $publico 'PublicHomeShootout') -eq '2' -and (Texto $publico 'PublicAwayShootout') -eq '1' } 'tanda 2–1 en el marcador'
    $registro.Add('Marcador: resultado ' + (Texto $publico 'PublicHomeScore') + '–' + (Texto $publico 'PublicAwayScore') + ', penales ' +
        (Texto $publico 'PublicHomeShootout') + '–' + (Texto $publico 'PublicAwayShootout') + '; mesa «' + (Texto $mesa 'ShootoutText') + '»')
    if ((Texto $publico 'PublicHomeScore') -ne '2' -or (Texto $publico 'PublicAwayScore') -ne '2') { throw 'La tanda cambió el resultado reglamentario.' }
    foreach ($tanda in 'PublicHomeShootout', 'PublicAwayShootout') {
        foreach ($reloj in 'PublicGameClock', 'PublicShotClock') {
            if (Se-Solapan (Buscar-Id $publico $tanda) (Buscar-Id $publico $reloj)) { throw "$tanda tapa $reloj." }
        }
    }
    $registro.Add('  La tanda no se superpone con PublicGameClock ni PublicShotClock (rectángulos en pantalla).')
    if ((Texto $publico 'PublicActionBanner') -ne $avisoAntes) { throw 'Un lanzamiento de la tanda disparó un aviso animado.' }
    $registro.Add('  Ningún lanzamiento cambió el aviso animado (sigue el texto del último gol, ya oculto; ver captura).')
    Start-Sleep -Milliseconds 300
    Capturar $publico 'r8-marcador-tanda.png'
    Capturar $mesa 'r8-mesa-tanda.png'

    # 5. Finalizar con la tanda desempatada.
    Pulsar $mesa 'FinishMatch'
    Responder-Si 'confirmar el cierre del partido'
    Esperar { (Texto $publico 'PublicStage') -eq 'RESULTADO FINAL' } 'resultado final'
    $registro.Add('Finalizado -> marcador «' + (Texto $publico 'PublicStage') + '», penales visibles: ' + (Existe $publico 'PublicHomeShootout'))
    Start-Sleep -Milliseconds 300
    Capturar $publico 'r8-marcador-final.png'

    # 6. Resultados: reglamentario con prórroga y tanda en su columna.
    Pestana 'BackupsTab'
    Esperar { $null -ne (Fila-Con-Texto (Buscar-Id $mesa 'ResultsMatches') '2 – 2 (pr.)') } 'resultado con prórroga en la lista'
    $fila = Fila-Con-Texto (Buscar-Id $mesa 'ResultsMatches') '2 – 2 (pr.)'
    if ($null -eq $fila.FindFirst($Scope::Descendants, (Condicion $AE::NameProperty '2 – 1'))) { throw 'La fila no muestra la tanda 2 – 1.' }
    $fila.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Esperar { (Texto $mesa 'ResultsMatchTitle') -like '*penales 2 – 1*' } 'resumen del partido con la tanda'
    $registro.Add('Resultados: ' + (Texto $mesa 'ResultsMatchTitle'))
    Capturar $mesa 'r8-resultados.png'
    $registro.Add('RECORRIDO COMPLETO')
} finally {
    $registro | Set-Content -Encoding UTF8 (Join-Path $salida 'recorrido.txt')
    if ($null -ne $proceso -and -not $proceso.HasExited) {
        $null = $proceso.CloseMainWindow()
        if (-not $proceso.WaitForExit(5000)) { $proceso.Kill() }
    }
    Write-Output "Salida: $salida"
}
