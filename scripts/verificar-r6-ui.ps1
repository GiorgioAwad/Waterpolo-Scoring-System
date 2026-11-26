# Recorrido de R6 (resultados y exportación, RF09) en el escritorio interactivo.
# Usa exclusivamente datos nuevos bajo artifacts. Los PDF exportados se abren fuera del programa con
# PyMuPDF (Python) para comprobar que un lector de PDF los entiende.
# No envía teclas: opera AutomationId y patrones de UIA; los cuadros de mensaje y el diálogo «Abrir» de
# Windows y el diálogo «Guardar como» se manejan con mensajes dirigidos (WM_SETTEXT y BM_CLICK).
[CmdletBinding()]
param(
    [string]$Ejecutable
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'entorno.ps1')
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -Namespace ResultadosR6 -Name Win32 -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
public static extern System.IntPtr SendMessage(System.IntPtr hWnd, int msg, System.IntPtr wParam, string lParam);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool PostMessage(System.IntPtr hWnd, int msg, System.IntPtr wParam, System.IntPtr lParam);
'@

$raizProyecto = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$salida = Join-Path $raizProyecto ('artifacts\verificacion-r6-' + [guid]::NewGuid().ToString('N'))
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
    $null = [ResultadosR6.Win32]::PostMessage([IntPtr]$si.Current.NativeWindowHandle, $BM_CLICK, [IntPtr]::Zero, [IntPtr]::Zero)
    Esperar { $null -eq (Ventana-Proceso { param($v) $v.Current.ClassName -eq '#32770' }) } "cerrar: $descripcion"
}

# Escribe la ruta en el diálogo «Guardar como» y lo acepta, sin recorrer ni registrar la carpeta.
function Guardar-Como([string]$ruta) {
    $dialogo = Esperar-Ventana { param($v) $v.Current.ClassName -eq '#32770' } 'abrir el diálogo de archivo'
    $script:campo = $null
    Esperar {
        $script:campo = $dialogo.FindFirst($Scope::Descendants, [System.Windows.Automation.AndCondition]::new(
            [System.Windows.Automation.OrCondition]::new((Condicion $AE::AutomationIdProperty '1001'), (Condicion $AE::AutomationIdProperty '1148')),
            (Condicion $AE::ClassNameProperty 'Edit')))
        $null -ne $script:campo
    } 'campo Nombre de archivo del diálogo'
    $null = [ResultadosR6.Win32]::SendMessage([IntPtr]$script:campo.Current.NativeWindowHandle, $WM_SETTEXT, [IntPtr]::Zero, $ruta)
    $abrir = $dialogo.FindFirst($Scope::Children, [System.Windows.Automation.AndCondition]::new(
        (Condicion $AE::AutomationIdProperty '1'), (Condicion $AE::ClassNameProperty 'Button')))
    $null = [ResultadosR6.Win32]::PostMessage([IntPtr]$abrir.Current.NativeWindowHandle, $BM_CLICK, [IntPtr]::Zero, [IntPtr]::Zero)
    Esperar { $null -eq (Ventana-Proceso { param($v) $v.Current.ClassName -eq '#32770' }) } 'cerrar el diálogo Guardar como'
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

function Exportar([string]$boton, [string]$archivo) {
    Pulsar $mesa $boton
    $ruta = Join-Path $exportes $archivo
    Guardar-Como $ruta
    Esperar { (Texto $mesa 'ResultsStatus') -like 'Resumen exportado*' -and (Test-Path $ruta) } "exportar $archivo"
    $registro.Add("$boton -> " + (Texto $mesa 'ResultsStatus'))
    return $ruta
}

try {
    $exportes = Join-Path $salida 'exportes'
    $null = New-Item -ItemType Directory -Path $exportes
    Abrir-Programa (Join-Path $salida 'datos') 'Prueba R6'
    Esperar { (Buscar-Id $mesa 'StartMatch').Current.IsEnabled } 'partido demo 1 cargado'

    # 1. Partido: 2-1 y finalizado.
    Pulsar $mesa 'StartMatch'
    Esperar { (Texto $mesa 'StatusMessage') -like 'Partido iniciado*' } 'partido iniciado'
    Seleccionar (Elementos (Buscar-Id $mesa 'HomeRoster'))[0]
    Pulsar $mesa 'GoalHome'
    Esperar { (Texto $mesa 'HomeScore') -eq '1' } 'gol local 1'
    Seleccionar (Elementos (Buscar-Id $mesa 'HomeRoster'))[1]
    Pulsar $mesa 'GoalHome'
    Esperar { (Texto $mesa 'HomeScore') -eq '2' } 'gol local 2'
    Seleccionar (Elementos (Buscar-Id $mesa 'AwayRoster'))[0]
    Pulsar $mesa 'GoalAway'
    Esperar { (Texto $mesa 'AwayScore') -eq '1' } 'gol visita'
    Pulsar $mesa 'FinishMatch'
    Responder-Si 'confirmar finalizar'
    Esperar { (Texto $mesa 'StatusMessage') -like 'Partido finalizado*' } 'partido finalizado'
    Estado-Mesa 'Mesa'

    # 2. La pestaña muestra el resultado, los goleadores y los totales del partido.
    Pestana 'BackupsTab'
    Esperar { $null -ne (Fila-Con-Texto (Buscar-Id $mesa 'ResultsMatches') '2 – 1') } 'resultado 2-1 en la lista'
    Seleccionar (Fila-Con-Texto (Buscar-Id $mesa 'ResultsMatches') '2 – 1')
    Esperar { (Texto $mesa 'ResultsMatchTitle') -like 'Partido demo 1:*2 – 1*' } 'resumen del partido'
    $registro.Add('Evento: ' + (Texto $mesa 'ResultsSummary'))
    $registro.Add('Partido: ' + (Texto $mesa 'ResultsMatchTitle'))
    $registro.Add('Goleadores en la lista: ' + (Elementos (Buscar-Id $mesa 'ResultsScorers')).Count)
    Capturar $mesa 'r6-resultados.png'

    # 3. Exportación por el diálogo «Guardar como».
    $pdfEvento = Exportar 'ExportTournamentPdf' 'evento.pdf'
    $csvEvento = Exportar 'ExportTournamentCsv' 'evento.csv'
    $pdfPartido = Exportar 'ExportMatchPdf' 'partido.pdf'
    $csvPartido = Exportar 'ExportMatchCsv' 'partido.csv'

    # 4. Los archivos se abren fuera del programa.
    $lector = Join-Path $salida 'leer_pdf.py'
    @'
import sys, fitz
out = open(sys.argv[1], "w", encoding="utf-8")
for path in sys.argv[2:]:
    doc = fitz.open(path)
    name = path.replace("\\", "/").split("/")[-1]
    out.write(f"{name}: {doc.page_count} página(s), título «{doc.metadata.get('title')}»" + chr(10))
    for line in doc[0].get_text().splitlines()[:12]:
        out.write("    " + line + chr(10))
    doc[0].get_pixmap(dpi=90).save(path[:-4] + ".png")
out.close()
'@ | Set-Content -Encoding UTF8 $lector
    $textoPdf = Join-Path $salida 'pdf-texto.txt'
    & python -I $lector $textoPdf $pdfEvento $pdfPartido
    if ($LASTEXITCODE -ne 0) { throw 'PyMuPDF no pudo abrir los PDF.' }
    foreach ($l in Get-Content -Encoding UTF8 $textoPdf) { $registro.Add('[PDF] ' + $l) }
    foreach ($csv in $csvEvento, $csvPartido) {
        $lineas = Get-Content -Encoding UTF8 $csv
        $registro.Add('[CSV] ' + (Split-Path $csv -Leaf) + ": $($lineas.Count) líneas; primeras:")
        foreach ($l in $lineas | Select-Object -First 6) { $registro.Add('    ' + $l) }
    }
    $registro.Add('RECORRIDO COMPLETO')
} finally {
    $registro | Set-Content -Encoding UTF8 (Join-Path $salida 'recorrido.txt')
    Cerrar-Programa
    Write-Output "Salida: $salida"
}
