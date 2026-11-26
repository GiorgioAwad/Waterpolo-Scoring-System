# Recorrido del estado de conexiones y de los colores de equipo en el escritorio interactivo.
# Usa exclusivamente datos nuevos bajo artifacts. No envía teclas: opera AutomationId y patrones de UIA;
# el selector de color de Windows se cierra con un mensaje dirigido a su botón «Cancelar».
[CmdletBinding()]
param(
    [string]$Ejecutable
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'entorno.ps1')
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -Namespace Conexiones -Name Win32 -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool PostMessage(System.IntPtr hWnd, int msg, System.IntPtr wParam, System.IntPtr lParam);
'@

$raizProyecto = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$salida = Join-Path $raizProyecto ('artifacts\verificacion-conexiones-' + [guid]::NewGuid().ToString('N'))
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

function Estado {
    $registro.Add('General: ' + (Texto $mesa 'ConnectionOverall'))
    foreach ($clave in 'consola', 'senal', 'reloj_partido', 'reloj_posesion', 'marcador', 'pantallas', 'almacenamiento') {
        $registro.Add("  $clave = " + (Texto $mesa "Estado_$clave"))
    }
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
    $proceso = Start-Process -FilePath $Ejecutable -ArgumentList @('--perfil', 'demo', '--datos', ('"' + $datos + '"'), '--operador', '"Prueba conexiones"') -PassThru
    $script:mesa = $null
    Esperar {
        $script:mesa = $AE::RootElement.FindFirst($Scope::Children, [System.Windows.Automation.AndCondition]::new(
            (Condicion $AE::ProcessIdProperty $proceso.Id), (Condicion $AE::NameProperty 'Marcador de Waterpolo FDNDA — Mesa')))
        $null -ne $script:mesa -and $null -ne $script:mesa.FindFirst($Scope::Descendants, (Condicion $AE::AutomationIdProperty 'ConnectionShort'))
    } 'abrir la mesa con datos aislados'

    # 1. Estado normal con el marcador abierto (con una sola pantalla se abre en ventana).
    Pulsar $mesa 'OpenScoreboard'
    $script:publico = $null
    Esperar {
        $script:publico = $AE::RootElement.FindFirst($Scope::Children, [System.Windows.Automation.AndCondition]::new(
            (Condicion $AE::ProcessIdProperty $proceso.Id), (Condicion $AE::NameProperty 'Marcador — Waterpolo FDNDA')))
        $null -ne $script:publico
    } 'abrir el marcador'
    # Con una sola pantalla el marcador tapa la mesa: se minimiza (sigue abierto) para capturar los paneles.
    $script:publico.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).SetWindowVisualState(
        [System.Windows.Automation.WindowVisualState]::Minimized)
    Pulsar $mesa 'ConnectionShort'   # el indicador de la barra lleva al diagnóstico
    Esperar { (Texto $mesa 'Estado_senal') -eq 'Vigente' } 'señal vigente'
    $registro.Add('Barra: ' + (Texto $mesa 'ConnectionShort'))
    Estado
    Capturar $mesa 'conexiones-normal.png'

    # 2. Pérdida de señal simulada.
    Pestana 'MesaTab'
    Pulsar $mesa 'SimDropSignal'
    Pestana 'DiagnosticsTab'
    Esperar { (Texto $mesa 'Estado_senal') -like 'PERDIDA*' } 'señal perdida'
    $registro.Add('--- tras simular pérdida de señal')
    $registro.Add('Barra: ' + (Texto $mesa 'ConnectionShort'))
    Estado
    Capturar $mesa 'conexiones-contingencia.png'

    # 3. Reconexión.
    Pestana 'MesaTab'
    Pulsar $mesa 'SimRestoreSignal'
    Pestana 'DiagnosticsTab'
    Esperar { (Texto $mesa 'Estado_senal') -eq 'Vigente' } 'señal recuperada'
    $registro.Add('--- tras reconectar')
    Estado
    $historial = (Buscar-Id $mesa 'ConnectionHistory').FindAll($Scope::Descendants, (Condicion $AE::ControlTypeProperty ([System.Windows.Automation.ControlType]::Text)))
    $registro.Add('Historial:')
    foreach ($t in $historial) { $registro.Add('  ' + $t.Current.Name) }
    Capturar $mesa 'conexiones-historial.png'

    # 4. Colores de equipo: paleta y selector de Windows.
    Pestana 'CatalogTab'
    Esperar { (Buscar-Id $mesa 'TeamList').FindAll($Scope::Descendants, (Condicion $AE::ControlTypeProperty ([System.Windows.Automation.ControlType]::DataItem))).Count -ge 2 } 'cargar equipos'
    $filas = (Buscar-Id $mesa 'TeamList').FindAll($Scope::Descendants, (Condicion $AE::ControlTypeProperty ([System.Windows.Automation.ControlType]::DataItem)))
    $filas[1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Esperar { (Valor $mesa 'TeamCode') -eq 'DEL' } 'equipo DEL en el formulario'
    Pulsar $mesa 'Color_Rojo'
    Esperar { (Valor $mesa 'TeamColor') -eq '#C62828' } 'color rojo aplicado desde la paleta'
    $registro.Add('Paleta Rojo -> ' + (Valor $mesa 'TeamColor'))
    Capturar $mesa 'colores-equipo.png'
    Pulsar $mesa 'ChooseTeamColor'
    $script:dialogo = $null
    Esperar {
        foreach ($v in $AE::RootElement.FindAll($Scope::Descendants, (Condicion $AE::ProcessIdProperty $proceso.Id))) {
            if ($v.Current.ClassName -eq '#32770') { $script:dialogo = $v; break }
        }
        $null -ne $script:dialogo
    } 'abrir el selector de color de Windows'
    $registro.Add('Selector de color abierto: ' + $script:dialogo.Current.Name)
    Capturar $script:dialogo 'selector-color.png'
    $cancelar = $script:dialogo.FindFirst($Scope::Children, [System.Windows.Automation.AndCondition]::new(
        (Condicion $AE::AutomationIdProperty '2'), (Condicion $AE::ClassNameProperty 'Button')))
    $null = [Conexiones.Win32]::PostMessage([IntPtr]$cancelar.Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 800
    $registro.Add('Tras cancelar el selector: ' + (Valor $mesa 'TeamColor'))
    $registro.Add('RECORRIDO COMPLETO')
} finally {
    $registro | Set-Content -Encoding UTF8 (Join-Path $salida 'recorrido.txt')
    if ($null -ne $proceso -and -not $proceso.HasExited) {
        $null = $proceso.CloseMainWindow()
        if (-not $proceso.WaitForExit(5000)) { $proceso.Kill() }
    }
    Write-Output "Salida: $salida"
}
