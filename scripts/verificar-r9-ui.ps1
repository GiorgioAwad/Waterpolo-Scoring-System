# Recorrido de R9 (reproducción de cronometraje, RF11 preparación) en el escritorio interactivo.
# Usa datos nuevos bajo artifacts y la grabación SINTÉTICA de samples\cronometraje. No envía teclas
# ni mueve el ratón: opera AutomationId con UI Automation. Espera en tiempo real porque la grabación
# dura 38 s; las pruebas automatizadas usan un reloj controlable.
[CmdletBinding()]
param(
    [string]$Ejecutable
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'entorno.ps1')
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -Namespace ReproduccionR9 -Name Win32 -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool PostMessage(System.IntPtr hWnd, int msg, System.IntPtr wParam, System.IntPtr lParam);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool PrintWindow(System.IntPtr hWnd, System.IntPtr hdc, uint flags);
'@

$raizProyecto = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$salida = Join-Path $raizProyecto ('artifacts\verificacion-r9-' + [guid]::NewGuid().ToString('N'))
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
    $null = [ReproduccionR9.Win32]::PostMessage([IntPtr]$si.Current.NativeWindowHandle, $BM_CLICK, [IntPtr]::Zero, [IntPtr]::Zero)
    Esperar { $null -eq (Ventana-Del-Proceso { param($v) $v.Current.ClassName -eq '#32770' }) } "cerrar: $descripcion"
}

# PrintWindow copia el contenido de la ventana aunque otra la tape: el marcador no toma el foco.
function Capturar($ventana, [string]$nombre) {
    $rect = $ventana.Current.BoundingRectangle
    $bitmap = [Drawing.Bitmap]::new([int]$rect.Width, [int]$rect.Height)
    $grafico = [Drawing.Graphics]::FromImage($bitmap)
    $hdc = $grafico.GetHdc()
    try {
        $null = [ReproduccionR9.Win32]::PrintWindow([IntPtr]$ventana.Current.NativeWindowHandle, $hdc, 2)
    } finally {
        $grafico.ReleaseHdc($hdc)
        $grafico.Dispose()
    }
    try { $bitmap.Save((Join-Path $salida $nombre), [Drawing.Imaging.ImageFormat]::Png) } finally { $bitmap.Dispose() }
}

function Se-Solapan($a, $b) { $a.Current.BoundingRectangle.IntersectsWith($b.Current.BoundingRectangle) }


function Abrir-Mesa([string[]]$extra, [string]$nombreDatos) {
    $datos = Join-Path $salida $nombreDatos
    $argumentos = @('--perfil', 'demo', '--datos', ('"' + $datos + '"'), '--operador', '"Prueba R9"') + $extra
    $script:proceso = Start-Process -FilePath $Ejecutable -ArgumentList $argumentos -PassThru
    $script:mesa = $null
    Esperar {
        $script:mesa = $AE::RootElement.FindFirst($Scope::Children, [System.Windows.Automation.AndCondition]::new(
            (Condicion $AE::ProcessIdProperty $proceso.Id), (Condicion $AE::NameProperty 'Marcador de Waterpolo FDNDA — Mesa')))
        $null -ne $script:mesa -and (Buscar-Id $script:mesa 'StartMatch').Current.IsEnabled
    } 'abrir la mesa'
}

function Cerrar-Programa {
    if ($null -ne $proceso -and -not $proceso.HasExited) {
        $null = $proceso.CloseMainWindow()
        if (-not $proceso.WaitForExit(5000)) { $proceso.Kill() }
    }
}

# Espera en tiempo real (hasta $segundos) a que se cumpla la condición.
function Esperar-Hasta([scriptblock]$condicion, [string]$descripcion, [int]$segundos) {
    $limite = [DateTime]::UtcNow.AddSeconds($segundos)
    while ([DateTime]::UtcNow -lt $limite) {
        try { if (& $condicion) { return } } catch { }
        Start-Sleep -Milliseconds 100
    }
    throw "No se cumplió en $segundos s: $descripcion."
}

$grabacion = Join-Path $raizProyecto 'samples\cronometraje\guion-sintetico.csv'
try {
    # 1. Reproducción de la grabación sintética.
    Abrir-Mesa @('--reproduccion', ('"' + $grabacion + '"')) 'datos'
    $registro.Add('Mesa abierta con --reproduccion guion-sintetico.csv')
    $registro.Add('  Distintivo de la mesa: «' + (Texto $mesa 'DemoBadge') + '»; simulador visible: ' + (Existe $mesa 'SimStart') + '; panel: «' + (Texto $mesa 'ReplayText') + '»')
    if ((Texto $mesa 'DemoBadge') -ne 'REPRODUCCIÓN') { throw 'La mesa no muestra REPRODUCCIÓN.' }
    if (Existe $mesa 'SimStart') { throw 'El simulador aparece durante una reproducción.' }
    Pulsar $mesa 'OpenScoreboard'
    $publico = Esperar-Ventana { param($v) $v.Current.Name -eq 'Marcador — Waterpolo FDNDA' } 'abrir el marcador'
    Esperar { (Texto $publico 'PublicModeBadge') -eq 'REPRODUCCIÓN' } 'distintivo REPRODUCCIÓN en el marcador'
    Esperar-Hasta { (Texto $mesa 'GameClock') -ne '--:--' -and (Texto $mesa 'GameClock') -ne '8:00' } 'reloj de partido en marcha' 20
    $registro.Add('En juego: mesa ' + (Texto $mesa 'GameClock') + ' / ' + (Texto $mesa 'ShotClock') + ', marcador «' + (Texto $publico 'PublicModeBadge') + '», panel «' + (Texto $mesa 'ReplayText') + '»')

    Pulsar $mesa 'StartMatch'
    Esperar { (Texto $mesa 'StageText') -eq 'PERIODO 1' } 'partido iniciado'
    Elegir $mesa 'HomeRoster' 0
    Pulsar $mesa 'GoalHome'
    Esperar { (Texto $mesa 'StatusMessage') -like 'Gol de*' } 'gol registrado'
    $registro.Add('Gol durante la reproducción -> «' + (Texto $mesa 'StatusMessage') + '»')
    Start-Sleep -Milliseconds 300
    Capturar $publico 'r9-marcador-reproduccion.png'

    # 2. El hueco de la grabación (20-24 s) es un corte de señal; la sesión 2 la recupera.
    Esperar-Hasta { Existe $publico 'PublicSignalNotice' } 'aviso de señal perdida en el marcador' 40
    $registro.Add('Corte: mesa ' + (Texto $mesa 'GameClock') + ' / ' + (Texto $mesa 'ShotClock') + ', señal «' + (Texto $mesa 'SignalText') + '», marcador «' + (Texto $publico 'PublicGameClock') + '» y aviso «' + (Texto $publico 'PublicSignalNotice') + '»')
    if ((Texto $publico 'PublicGameClock') -ne '--:--') { throw 'El marcador sigue mostrando un tiempo sin señal.' }
    Capturar $publico 'r9-marcador-sin-senal.png'
    Esperar-Hasta { -not (Existe $publico 'PublicSignalNotice') -and (Texto $mesa 'GameClock') -ne '--:--' } 'reconexión' 15
    $registro.Add('Reconexión (sesión 2): mesa ' + (Texto $mesa 'GameClock') + ' / ' + (Texto $mesa 'ShotClock') + ', señal «' + (Texto $mesa 'SignalText') + '»')

    # 3. Fin de la grabación y reinicio.
    Esperar-Hasta { (Texto $mesa 'ReplayText') -like '*terminada*' } 'fin de la reproducción' 30
    Esperar-Hasta { (Texto $mesa 'GameClock') -eq '--:--' } 'señal perdida al terminar' 10
    $registro.Add('Fin: panel «' + (Texto $mesa 'ReplayText') + '», relojes ' + (Texto $mesa 'GameClock') + ' / ' + (Texto $mesa 'ShotClock'))
    Capturar $mesa 'r9-mesa-terminada.png'
    Pulsar $mesa 'RestartReplay'
    Esperar-Hasta { (Texto $mesa 'GameClock') -eq '8:00' } 'reloj de vuelta al comienzo' 5
    $registro.Add('Reiniciar -> «' + (Texto $mesa 'StatusMessage') + '»; relojes ' + (Texto $mesa 'GameClock') + ' / ' + (Texto $mesa 'ShotClock') + '; panel «' + (Texto $mesa 'ReplayText') + '»')
    Capturar $mesa 'r9-mesa-reiniciada.png'
    Cerrar-Programa

    # 4. Una grabación inválida: la fuente queda no disponible, sin pasar al simulador.
    $rota = Join-Path $salida 'grabacion-rota.csv'
    [IO.File]::WriteAllText($rota, "formato,fdnda-cronometraje-normalizado,1`nesto no es un encabezado`n", [Text.UTF8Encoding]::new($false))
    Abrir-Mesa @('--reproduccion', ('"' + $rota + '"')) 'datos-rota'
    Esperar { (Texto $mesa 'TimingNotice') -like '*Línea 2*' } 'aviso del archivo inválido'
    $registro.Add('Archivo inválido: señal «' + (Texto $mesa 'SignalText') + '»; aviso «' + (Texto $mesa 'TimingNotice') + '»; panel «' + (Texto $mesa 'ReplayText') + '»; simulador visible: ' + (Existe $mesa 'SimStart'))
    if (Existe $mesa 'SimStart') { throw 'Con un archivo inválido apareció el simulador.' }
    Capturar $mesa 'r9-mesa-archivo-invalido.png'
    Cerrar-Programa

    # 5. Con el perfil de producción la opción se rechaza antes de abrir datos.
    $script:proceso = Start-Process -FilePath $Ejecutable -ArgumentList @('--perfil', 'produccion', '--datos', ('"' + (Join-Path $salida 'datos-prod') + '"'), '--reproduccion', ('"' + $grabacion + '"')) -PassThru
    $cuadro = Esperar-Ventana { param($v) $v.Current.ClassName -eq '#32770' } 'mensaje de error de arranque'
    $script:boton = $null
    Esperar { $script:boton = $cuadro.FindFirst($Scope::Descendants, (Condicion $AE::ClassNameProperty 'Button')); $null -ne $script:boton } 'botón Aceptar del mensaje'
    foreach ($t in $cuadro.FindAll($Scope::Descendants, (Condicion $AE::ClassNameProperty 'Static'))) {
        if ($t.Current.Name) { $registro.Add('Producción + --reproduccion -> «' + $t.Current.Name + '»') }
    }
    $null = [ReproduccionR9.Win32]::PostMessage([IntPtr]$script:boton.Current.NativeWindowHandle, $BM_CLICK, [IntPtr]::Zero, [IntPtr]::Zero)
    if (-not $proceso.WaitForExit(10000)) { throw 'El programa siguió abierto tras el error de arranque.' }
    $registro.Add('  El programa se cerró sin abrir la mesa; carpeta de datos creada: ' + (Test-Path (Join-Path $salida 'datos-prod')))
    $registro.Add('RECORRIDO COMPLETO')
} finally {
    $registro | Set-Content -Encoding UTF8 (Join-Path $salida 'recorrido.txt')
    Cerrar-Programa
    Write-Output "Salida: $salida"
}
