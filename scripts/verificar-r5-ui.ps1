# Recorrido de R5 (respaldo y restauración, RF10) en el escritorio interactivo.
# Usa exclusivamente datos nuevos bajo artifacts, en dos carpetas de datos distintas (dos «instalaciones»).
# No envía teclas: opera AutomationId y patrones de UIA; los cuadros de mensaje y el diálogo «Abrir» de
# Windows se manejan con mensajes dirigidos (WM_SETTEXT al campo de nombre y BM_CLICK a los botones).
[CmdletBinding()]
param(
    [string]$Ejecutable
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'entorno.ps1')
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -Namespace RespaldosR5 -Name Win32 -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
public static extern System.IntPtr SendMessage(System.IntPtr hWnd, int msg, System.IntPtr wParam, string lParam);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool PostMessage(System.IntPtr hWnd, int msg, System.IntPtr wParam, System.IntPtr lParam);
'@

$raizProyecto = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$salida = Join-Path $raizProyecto ('artifacts\verificacion-r5-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $salida
if (-not $Ejecutable) {
    $Ejecutable = Join-Path $raizProyecto 'src\FDNDA.Waterpolo.Desktop\bin\Release\net10.0-windows\FDNDA.Waterpolo.exe'
}
$registro = [Collections.Generic.List[string]]::new()
$proceso = $null
$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$BM_CLICK = 0x00F5
$WM_SETTEXT = 0x000C

function Condicion($propiedad, $valor) { [System.Windows.Automation.PropertyCondition]::new($propiedad, $valor) }

function Buscar-Id($ventana, [string]$id) {
    $control = $ventana.FindFirst($Scope::Descendants, (Condicion $AE::AutomationIdProperty $id))
    if ($null -eq $control) { throw "No se encontró el control $id." }
    return $control
}

function Esperar([scriptblock]$condicion, [string]$descripcion) {
    for ($intento = 0; $intento -lt 200; $intento++) {
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

function Elementos($control) {
    $lista = Condicion $AE::ControlTypeProperty ([System.Windows.Automation.ControlType]::ListItem)
    $fila = Condicion $AE::ControlTypeProperty ([System.Windows.Automation.ControlType]::DataItem)
    return ,$control.FindAll($Scope::Descendants, [System.Windows.Automation.OrCondition]::new($lista, $fila))
}

function Seleccionar($elemento) {
    $elemento.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
}

function Fila-Con-Texto($control, [string]$texto, [switch]$Ultima) {
    $encontrada = $null
    foreach ($fila in (Elementos $control)) {
        if ($fila.FindFirst($Scope::Descendants, (Condicion $AE::NameProperty $texto))) {
            $encontrada = $fila
            if (-not $Ultima) { break }
        }
    }
    if ($null -eq $encontrada) { throw "No hay una fila con «$texto»." }
    return $encontrada
}

function Ventana-Proceso([scriptblock]$filtro) {
    foreach ($v in $AE::RootElement.FindAll($Scope::Descendants, [System.Windows.Automation.AndCondition]::new(
            (Condicion $AE::ProcessIdProperty $proceso.Id), (Condicion $AE::ControlTypeProperty ([System.Windows.Automation.ControlType]::Window))))) {
        if (& $filtro $v) { return $v }
    }
    return $null
}

function Esperar-Ventana([scriptblock]$filtro, [string]$descripcion) {
    $script:encontrada = $null
    Esperar { $script:encontrada = Ventana-Proceso $filtro; $null -ne $script:encontrada } $descripcion
    return $script:encontrada
}

# Responde «Sí» (id 6) en el cuadro de mensaje del programa y registra su texto.
function Responder-Si([string]$descripcion) {
    $cuadro = Esperar-Ventana { param($v) $v.Current.ClassName -eq '#32770' } $descripcion
    $mensaje = $cuadro.FindAll($Scope::Descendants, (Condicion $AE::ControlTypeProperty ([System.Windows.Automation.ControlType]::Text)))
    foreach ($t in $mensaje) { if ($t.Current.Name) { $registro.Add('  [cuadro] ' + ($t.Current.Name -replace "`r?`n", ' / ')) } }
    $si = $cuadro.FindFirst($Scope::Children, [System.Windows.Automation.AndCondition]::new(
        (Condicion $AE::AutomationIdProperty '6'), (Condicion $AE::ClassNameProperty 'Button')))
    $null = [RespaldosR5.Win32]::PostMessage([IntPtr]$si.Current.NativeWindowHandle, $BM_CLICK, [IntPtr]::Zero, [IntPtr]::Zero)
    Esperar { $null -eq (Ventana-Proceso { param($v) $v.Current.ClassName -eq '#32770' }) } "cerrar: $descripcion"
}

# Elige un archivo en el diálogo «Abrir» sin recorrer ni registrar el contenido de la carpeta.
function Elegir-Archivo([string]$ruta) {
    $dialogo = Esperar-Ventana { param($v) $v.Current.ClassName -eq '#32770' } 'abrir el diálogo de archivo'
    $script:campo = $null
    Esperar {
        $script:campo = $dialogo.FindFirst($Scope::Descendants, [System.Windows.Automation.AndCondition]::new(
            (Condicion $AE::AutomationIdProperty '1148'), (Condicion $AE::ClassNameProperty 'Edit')))
        $null -ne $script:campo
    } 'campo Nombre de archivo del diálogo'
    $null = [RespaldosR5.Win32]::SendMessage([IntPtr]$script:campo.Current.NativeWindowHandle, $WM_SETTEXT, [IntPtr]::Zero, $ruta)
    $abrir = $dialogo.FindFirst($Scope::Children, [System.Windows.Automation.AndCondition]::new(
        (Condicion $AE::AutomationIdProperty '1'), (Condicion $AE::ClassNameProperty 'Button')))
    $null = [RespaldosR5.Win32]::PostMessage([IntPtr]$abrir.Current.NativeWindowHandle, $BM_CLICK, [IntPtr]::Zero, [IntPtr]::Zero)
    Esperar { $null -eq (Ventana-Proceso { param($v) $v.Current.ClassName -eq '#32770' -and $null -ne $v.FindFirst($Scope::Descendants, (Condicion $AE::AutomationIdProperty '1148')) }) } 'cerrar el diálogo de archivo'
}

function Capturar($ventana, [string]$nombre) {
    Start-Sleep -Milliseconds 400
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

function Abrir-Programa([string]$datos, [string]$operador) {
    $script:proceso = Start-Process -FilePath $Ejecutable -ArgumentList @('--perfil', 'demo', '--datos', ('"' + $datos + '"'), '--operador', ('"' + $operador + '"')) -PassThru
    $script:mesa = $null
    Esperar {
        $script:mesa = $AE::RootElement.FindFirst($Scope::Children, [System.Windows.Automation.AndCondition]::new(
            (Condicion $AE::ProcessIdProperty $script:proceso.Id), (Condicion $AE::NameProperty 'Marcador de Waterpolo FDNDA — Mesa')))
        $null -ne $script:mesa -and $null -ne $script:mesa.FindFirst($Scope::Descendants, (Condicion $AE::AutomationIdProperty 'StartMatch'))
    } 'abrir la mesa con datos aislados'
}

function Cerrar-Programa {
    if ($null -ne $script:proceso -and -not $script:proceso.HasExited) {
        $null = $script:proceso.CloseMainWindow()
        if (-not $script:proceso.WaitForExit(5000)) { $script:proceso.Kill() }
    }
}

function Estado-Mesa([string]$etiqueta) {
    $registro.Add("$etiqueta -> " + (Texto $mesa 'StageText') + ' · ' + (Texto $mesa 'HomeScore') + '-' + (Texto $mesa 'AwayScore') + ' · ' + (Texto $mesa 'StatusMessage'))
}

try {
    $datosA = Join-Path $salida 'datos-a'
    $datosB = Join-Path $salida 'datos-b'

    # ---------- Instalación A ----------
    Abrir-Programa $datosA 'Prueba R5 A'
    Esperar { (Buscar-Id $mesa 'StartMatch').Current.IsEnabled } 'partido demo 1 cargado'

    # 1. Partido 1 con respaldo automático previo y de cierre.
    Pulsar $mesa 'StartMatch'
    Esperar { (Texto $mesa 'StatusMessage') -like 'Partido iniciado*Respaldo previo creado*' } 'inicio con respaldo previo'
    Estado-Mesa 'Partido 1 iniciado'
    Seleccionar (Elementos (Buscar-Id $mesa 'HomeRoster'))[0]
    Pulsar $mesa 'GoalHome'
    Esperar { (Texto $mesa 'HomeScore') -eq '1' } 'gol local'
    Pulsar $mesa 'FinishMatch'
    Responder-Si 'confirmar finalizar'
    Esperar { (Texto $mesa 'StatusMessage') -eq 'Partido finalizado. Respaldo de cierre creado.' } 'respaldo de cierre'
    Estado-Mesa 'Partido 1 finalizado'

    # 2. La pestaña lista los respaldos; se crea uno manual.
    Pestana 'BackupsTab'
    Esperar { (Texto $mesa 'BackupCount') -eq '2 respaldos en la carpeta' } 'dos respaldos automáticos'
    Pulsar $mesa 'CreateBackup'
    Esperar { (Texto $mesa 'BackupStatus') -like 'Respaldo creado: respaldo-manual-*' } 'respaldo manual'
    $registro.Add('Respaldos: ' + (Texto $mesa 'BackupStatus') + ' · ' + (Texto $mesa 'BackupCount'))
    Capturar $mesa 'r5-respaldos-lista.png'

    # 3. Con un partido en juego no se restaura.
    Pestana 'MatchesTab'
    Seleccionar (Fila-Con-Texto (Buscar-Id $mesa 'MatchList') 'Programado')
    Pulsar $mesa 'OpenMatch'
    Pestana 'MesaTab'
    Esperar { (Buscar-Id $mesa 'StartMatch').Current.IsEnabled } 'partido 2 abierto'
    Pulsar $mesa 'StartMatch'
    Esperar { (Texto $mesa 'StatusMessage') -like 'Partido iniciado*' } 'partido 2 iniciado'
    Pestana 'BackupsTab'
    Esperar { (Texto $mesa 'BackupCount') -eq '4 respaldos en la carpeta' } 'respaldo previo del partido 2'
    Seleccionar (Fila-Con-Texto (Buscar-Id $mesa 'BackupList') 'Manual')
    Pulsar $mesa 'RestoreSelectedBackup'
    Esperar { (Texto $mesa 'BackupStatus') -like '*Hay un partido en juego*' } 'rechazo con partido en juego'
    $registro.Add('Con partido en juego: ' + (Texto $mesa 'BackupStatus'))
    Capturar $mesa 'r5-rechazo-en-juego.png'
    Pestana 'MesaTab'
    Pulsar $mesa 'FinishMatch'
    Responder-Si 'confirmar finalizar partido 2'
    Esperar { (Texto $mesa 'StatusMessage') -like 'Partido finalizado*' } 'partido 2 finalizado'

    # 4. Se restaura el respaldo previo al partido 1: la mesa vuelve a 0-0 y programado.
    Pestana 'BackupsTab'
    Esperar { (Texto $mesa 'BackupCount') -eq '5 respaldos en la carpeta' } 'cinco respaldos'
    Seleccionar (Fila-Con-Texto (Buscar-Id $mesa 'BackupList') 'Antes del partido' -Ultima)
    Pulsar $mesa 'RestoreSelectedBackup'
    Responder-Si 'confirmar restauración'
    Esperar { (Texto $mesa 'BackupStatus') -like 'Respaldo restaurado.*' } 'restauración en A'
    $registro.Add('Restauración en A: ' + (Texto $mesa 'BackupStatus'))
    Capturar $mesa 'r5-restaurado-a.png'
    Pestana 'MesaTab'
    Esperar { (Texto $mesa 'HomeScore') -eq '0' -and (Buscar-Id $mesa 'StartMatch').Current.IsEnabled } 'mesa recargada en 0-0'
    Estado-Mesa 'Mesa A tras restaurar'
    Cerrar-Programa

    # ---------- Instalación B (otra carpeta de datos) ----------
    $cierre = Get-ChildItem (Join-Path $datosA 'demo\respaldos') -Filter 'respaldo-fin-partido-*.db' | Sort-Object Name | Select-Object -First 1
    $registro.Add('Respaldo llevado a B: ' + $cierre.Name)
    Abrir-Programa $datosB 'Prueba R5 B'
    Pestana 'BackupsTab'
    Esperar { (Texto $mesa 'BackupCount') -eq '0 respaldos en la carpeta' } 'B sin respaldos'
    Pulsar $mesa 'RestoreBackupFromFile'
    Elegir-Archivo $cierre.FullName
    Responder-Si 'confirmar restauración en B'
    Esperar { (Texto $mesa 'BackupStatus') -like 'Respaldo restaurado.*' } 'restauración en B'
    $registro.Add('Restauración en B: ' + (Texto $mesa 'BackupStatus'))
    Pestana 'MesaTab'
    Esperar { (Texto $mesa 'HomeScore') -eq '1' } 'partido 1 con su gol en B'
    Estado-Mesa 'Mesa B tras restaurar'
    $incidencias = (Elementos (Buscar-Id $mesa 'IncidentList')).Count
    $registro.Add("Incidencias del partido en B: $incidencias")
    Capturar $mesa 'r5-restaurado-b.png'
    $registro.Add('RECORRIDO COMPLETO')
} finally {
    $registro | Set-Content -Encoding UTF8 (Join-Path $salida 'recorrido.txt')
    Cerrar-Programa
    Write-Output "Salida: $salida"
}
