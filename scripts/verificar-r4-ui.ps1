# Recorrido de R4 (reglas por evento, RF03) en el escritorio interactivo.
# Usa exclusivamente datos nuevos bajo artifacts. No envía teclas: opera AutomationId, InvokePattern,
# SelectionItemPattern, ValuePattern y TogglePattern; la confirmación de «Finalizar» (cuadro de
# mensaje de Windows) se responde con un mensaje dirigido a su botón «Sí».
[CmdletBinding()]
param(
    [string]$Ejecutable
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'entorno.ps1')
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -Namespace ReglasR4 -Name Win32 -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool PostMessage(System.IntPtr hWnd, int msg, System.IntPtr wParam, System.IntPtr lParam);
'@

$raizProyecto = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$salida = Join-Path $raizProyecto ('artifacts\verificacion-r4-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $salida
if (-not $Ejecutable) {
    $Ejecutable = Join-Path $raizProyecto 'src\FDNDA.Waterpolo.Desktop\bin\Release\net10.0-windows\FDNDA.Waterpolo.exe'
}
$registro = [Collections.Generic.List[string]]::new()
$proceso = $null
$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]

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

function Filas($control) {
    $control.FindAll($Scope::Descendants, (Condicion $AE::ControlTypeProperty ([System.Windows.Automation.ControlType]::DataItem)))
}

function Ventana-Del-Proceso([scriptblock]$filtro) {
    foreach ($v in $AE::RootElement.FindAll($Scope::Descendants, [System.Windows.Automation.AndCondition]::new(
            (Condicion $AE::ProcessIdProperty $proceso.Id), (Condicion $AE::ControlTypeProperty ([System.Windows.Automation.ControlType]::Window))))) {
        if (& $filtro $v) { return $v }
    }
    return $null
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
    $proceso = Start-Process -FilePath $Ejecutable -ArgumentList @('--perfil', 'demo', '--datos', ('"' + $datos + '"'), '--operador', '"Prueba R4"') -PassThru
    $script:mesa = $null
    Esperar {
        $script:mesa = $AE::RootElement.FindFirst($Scope::Children, [System.Windows.Automation.AndCondition]::new(
            (Condicion $AE::ProcessIdProperty $proceso.Id), (Condicion $AE::NameProperty 'Marcador de Waterpolo FDNDA — Mesa')))
        $null -ne $script:mesa -and (Buscar-Id $script:mesa 'StartMatch').Current.IsEnabled
    } 'abrir la mesa con el partido demo cargado'

    # 1. Se inicia el partido demo 1 con las reglas vigentes del evento (preset DEMO).
    Pulsar $mesa 'StartMatch'
    Esperar { (Texto $mesa 'MatchRules') -like 'Reglas del partido: DEMO*' } 'reglas DEMO copiadas al iniciar'
    $registro.Add('Partido 1 iniciado -> ' + (Texto $mesa 'MatchRules'))

    # 2. Se editan las reglas del evento desde «Eventos y nóminas».
    Pestana 'CatalogTab'
    Esperar { (Filas (Buscar-Id $mesa 'TournamentList')).Count -ge 1 } 'cargar eventos'
    (Filas (Buscar-Id $mesa 'TournamentList'))[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Esperar { (Texto $mesa 'TournamentRules') -like 'Reglas: DEMO*' } 'reglas del evento en la tarjeta'
    $registro.Add('Tarjeta del evento: ' + (Texto $mesa 'TournamentRules'))
    Capturar $mesa 'r4-evento-antes.png'

    Pulsar $mesa 'EditRules'
    $script:reglas = $null
    Esperar { $script:reglas = Ventana-Del-Proceso { param($v) $v.Current.Name -like 'Reglas del evento*' }; $null -ne $script:reglas } 'abrir el editor de reglas'
    $registro.Add('Diálogo: ' + $reglas.Current.Name)
    $registro.Add('  ' + (Texto $reglas 'RulesCurrent'))
    $registro.Add('  ' + (Texto $reglas 'RulesApplicability'))
    $registro.Add('  Competencia habilitado: ' + (Buscar-Id $reglas 'CompetitionRules').Current.IsEnabled)
    $registro.Add('  ' + (Texto $reglas 'RulesOutcome'))
    Capturar $reglas 'r4-dialogo-inicial.png'

    # 2a. Un valor fuera de rango no se guarda.
    Escribir $reglas 'RulesPeriodCount' '0'
    Pulsar $reglas 'SaveRules'
    Esperar { (Texto $reglas 'RulesStatus') -like '*entre 1 y 20*' } 'rechazo de periodos fuera de rango'
    $registro.Add('Periodos = 0 -> ' + (Texto $reglas 'RulesStatus'))
    Capturar $reglas 'r4-dialogo-error.png'

    # 2b. Valores personalizados: 4 × 7:30, posesión 28 s, tanda habilitada.
    Escribir $reglas 'RulesPeriodCount' '4'
    Escribir $reglas 'RulesPeriodDuration' '7:30'
    Escribir $reglas 'RulesShotClock' '28'
    (Buscar-Id $reglas 'RulesShootout').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Esperar { (Texto $reglas 'RulesOutcome') -like '*sin aprobación técnica*' } 'aviso de reglas personalizadas'
    if ((Texto $reglas 'RulesStatus') -ne '') { throw 'El error anterior sigue visible tras corregir el valor.' }
    $registro.Add('Tras editar -> ' + (Texto $reglas 'RulesOutcome'))
    Capturar $reglas 'r4-dialogo-personalizado.png'
    Pulsar $reglas 'SaveRules'
    Esperar { (Texto $mesa 'CatalogStatus') -like 'Reglas de*guardadas*' } 'reglas guardadas'
    Esperar { $null -eq (Ventana-Del-Proceso { param($v) $v.Current.Name -like 'Reglas del evento*' }) } 'cerrar el editor de reglas'
    Start-Sleep -Milliseconds 500   # fin del fundido de cierre antes de capturar
    $registro.Add('Estado: ' + (Texto $mesa 'CatalogStatus'))
    Esperar { (Texto $mesa 'TournamentRules') -like '*7:30*' } 'tarjeta con las reglas nuevas'
    $registro.Add('Tarjeta del evento: ' + (Texto $mesa 'TournamentRules'))
    Capturar $mesa 'r4-evento-despues.png'

    # 3. El partido en juego conserva sus reglas.
    Pestana 'MesaTab'
    $registro.Add('Partido 1 en juego -> ' + (Texto $mesa 'MatchRules'))
    if ((Texto $mesa 'MatchRules') -notlike 'Reglas del partido: DEMO*8:00*') { throw 'El partido en juego cambió de reglas.' }

    # 4. Se finaliza y se inicia el partido 2: toma las reglas nuevas.
    Pulsar $mesa 'FinishMatch'
    $script:confirmar = $null
    Esperar { $script:confirmar = Ventana-Del-Proceso { param($v) $v.Current.ClassName -eq '#32770' }; $null -ne $script:confirmar } 'confirmación de finalizar'
    $si = $confirmar.FindFirst($Scope::Children, [System.Windows.Automation.AndCondition]::new(
        (Condicion $AE::AutomationIdProperty '6'), (Condicion $AE::ClassNameProperty 'Button')))
    $null = [ReglasR4.Win32]::PostMessage([IntPtr]$si.Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)
    Esperar { (Buscar-Id $mesa 'ReopenMatch').Current.IsEnabled } 'partido 1 finalizado'
    $registro.Add('Partido 1 finalizado -> ' + (Texto $mesa 'MatchRules'))

    Pestana 'MatchesTab'
    Esperar { (Filas (Buscar-Id $mesa 'MatchList')).Count -ge 2 } 'lista de partidos'
    $segundo = $null
    foreach ($fila in (Filas (Buscar-Id $mesa 'MatchList'))) {
        if ($fila.FindFirst($Scope::Descendants, (Condicion $AE::NameProperty 'Programado'))) { $segundo = $fila; break }
    }
    if ($null -eq $segundo) { throw 'No hay un partido programado en la lista.' }
    $segundo.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Pulsar $mesa 'OpenMatch'
    Pestana 'MesaTab'
    Esperar { (Buscar-Id $mesa 'StartMatch').Current.IsEnabled } 'partido 2 abierto en la mesa'
    Pulsar $mesa 'StartMatch'
    Esperar { (Texto $mesa 'MatchRules') -like '*Personalizadas*7:30*' } 'partido 2 con las reglas nuevas'
    $registro.Add('Partido 2 iniciado -> ' + (Texto $mesa 'MatchRules'))
    # El simulador hace de consola: toma la duración y la posesión del partido al cargar un periodo.
    Pulsar $mesa 'SimLoadPeriod'
    Esperar { (Texto $mesa 'GameClock') -eq '7:30' -and (Texto $mesa 'ShotClock') -eq '28' } 'simulador cargado con las reglas del partido'
    $registro.Add('Simulador tras «Cargar periodo nuevo» -> ' + (Texto $mesa 'GameClock') + ' / ' + (Texto $mesa 'ShotClock'))
    Capturar $mesa 'r4-partido2-reglas.png'
    $registro.Add('RECORRIDO COMPLETO')
} finally {
    $registro | Set-Content -Encoding UTF8 (Join-Path $salida 'recorrido.txt')
    if ($null -ne $proceso -and -not $proceso.HasExited) {
        $null = $proceso.CloseMainWindow()
        if (-not $proceso.WaitForExit(5000)) { $proceso.Kill() }
    }
    Write-Output "Salida: $salida"
}
