# Recorrido de R3 (nóminas) en el escritorio interactivo. Usa exclusivamente datos nuevos bajo artifacts.
# No envía teclas: opera AutomationId, InvokePattern, SelectionItemPattern y ValuePattern,
# también en el diálogo «Abrir» de Windows.
[CmdletBinding()]
param(
    # Ejecutable a recorrer; por omisión el de bin Release (necesita el runtime local de entorno.ps1).
    [string]$Ejecutable
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'entorno.ps1')
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
# Los controles Win32 del diálogo «Abrir» no exponen patrones de UIA a este cliente: se les envían mensajes
# dirigidos (WM_SETTEXT al campo y BM_CLICK al botón). No se sintetiza teclado ni ratón.
Add-Type -Namespace R3 -Name Win32 -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
public static extern System.IntPtr SendMessage(System.IntPtr hWnd, int msg, System.IntPtr wParam, string lParam);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool PostMessage(System.IntPtr hWnd, int msg, System.IntPtr wParam, System.IntPtr lParam);
'@

$raizProyecto = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$salida = Join-Path $raizProyecto ('artifacts\verificacion-r3-' + [guid]::NewGuid().ToString('N'))
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

function Escribir($ventana, [string]$id, [string]$texto) {
    (Buscar-Id $ventana $id).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($texto)
}

function Texto($ventana, [string]$id) { (Buscar-Id $ventana $id).Current.Name }

function Elementos($control) {
    $lista = Condicion $AE::ControlTypeProperty ([System.Windows.Automation.ControlType]::ListItem)
    $fila = Condicion $AE::ControlTypeProperty ([System.Windows.Automation.ControlType]::DataItem)
    return ,$control.FindAll($Scope::Descendants, [System.Windows.Automation.OrCondition]::new($lista, $fila))
}

function Elegir($ventana, [string]$id, [int]$indice) {
    $control = Buscar-Id $ventana $id
    $expansion = $null
    if ($control.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$expansion)) {
        $expansion.Expand()
    }
    Esperar { (Elementos $control).Count -gt $indice } "cargar las opciones de $id"
    (Elementos $control)[$indice].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    if ($null -ne $expansion) { $expansion.Collapse() }
}

function Ventana-Proceso([scriptblock]$filtro) {
    $todas = $AE::RootElement.FindAll($Scope::Descendants, (Condicion $AE::ProcessIdProperty $proceso.Id))
    foreach ($v in $todas) {
        if ($v.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window -and (& $filtro $v)) { return $v }
    }
    return $null
}

function Esperar-Ventana([scriptblock]$filtro, [string]$descripcion) {
    $script:encontrada = $null
    Esperar { $script:encontrada = Ventana-Proceso $filtro; $null -ne $script:encontrada } $descripcion
    return $script:encontrada
}

function Esperar-Catalogo([string]$texto) {
    Esperar { (Texto $mesa 'CatalogStatus') -like "*$texto*" } "mensaje del catálogo «$texto»"
    $registro.Add('Catálogo: ' + (Texto $mesa 'CatalogStatus'))
}

# Abre el diálogo de archivo de la pestaña y elige la ruta indicada.
function Abrir-Archivo([string]$ruta) {
    Pulsar $mesa 'ImportRoster'
    $dialogo = Esperar-Ventana { param($v) $v.Current.ClassName -eq '#32770' } 'abrir el diálogo de archivo'
    # Campo «Nombre de archivo»: el Edit (id 1148) dentro del ComboBoxEx32. No se recorre ni se registra
    # el contenido de la carpeta que muestra el diálogo.
    $script:campo = $null
    Esperar {
        $script:campo = $dialogo.FindFirst($Scope::Descendants, [System.Windows.Automation.AndCondition]::new(
            (Condicion $AE::AutomationIdProperty '1148'), (Condicion $AE::ClassNameProperty 'Edit')))
        $null -ne $script:campo
    } 'campo Nombre de archivo del diálogo'
    $WM_SETTEXT = 0x000C; $BM_CLICK = 0x00F5
    $null = [R3.Win32]::SendMessage([IntPtr]$script:campo.Current.NativeWindowHandle, $WM_SETTEXT, [IntPtr]::Zero, $ruta)
    $abrir = $dialogo.FindFirst($Scope::Children, [System.Windows.Automation.AndCondition]::new(
        (Condicion $AE::AutomationIdProperty '1'), (Condicion $AE::ClassNameProperty 'Button')))
    $null = [R3.Win32]::PostMessage([IntPtr]$abrir.Current.NativeWindowHandle, $BM_CLICK, [IntPtr]::Zero, [IntPtr]::Zero)
    Esperar { $null -eq (Ventana-Proceso { param($v) $v.Current.ClassName -eq '#32770' }) } 'cerrar el diálogo de archivo'
}

function Esperar-VistaPrevia {
    return Esperar-Ventana { param($v) $v.Current.Name -like 'Importar nómina*' } 'abrir la vista previa'
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
    $conflicto = Join-Path $salida 'nomina-conflicto.csv'
    [IO.File]::WriteAllText($conflicto,
        "equipo_codigo,equipo_nombre,jugador_nombre,numero_gorro`r`nDEL,Delfines del Pacífico,Luis Marea Ríos,2`r`nDEL,Delfines del Pacífico,Pablo Oleaje,9`r`n",
        [Text.UTF8Encoding]::new($true))
    $errores = Join-Path $salida 'nomina-errores.csv'
    [IO.File]::WriteAllText($errores,
        "equipo_codigo,equipo_nombre,jugador_nombre,numero_gorro`r`nDEL,Delfines del Pacífico,,2`r`nDEL,Delfines del Pacífico,Pablo Oleaje,cien`r`n",
        [Text.UTF8Encoding]::new($true))

    $proceso = Start-Process -FilePath $Ejecutable -ArgumentList @('--perfil', 'demo', '--datos', ('"' + $datos + '"'), '--operador', '"Prueba R3"') -PassThru
    $script:mesa = $null
    Esperar {
        $script:mesa = $AE::RootElement.FindFirst($Scope::Children, (Condicion $AE::ProcessIdProperty $proceso.Id))
        $null -ne $script:mesa -and $null -ne $script:mesa.FindFirst($Scope::Descendants, (Condicion $AE::AutomationIdProperty 'CatalogTab'))
    } 'abrir la mesa con datos aislados'

    (Buscar-Id $mesa 'CatalogTab').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Esperar { (Elementos (Buscar-Id $mesa 'TeamList')).Count -ge 2 } 'cargar equipos'

    # Evento nuevo, sin nómina.
    Pulsar $mesa 'NewTournament'
    Escribir $mesa 'TournamentName' 'Copa R3 de prueba'
    Pulsar $mesa 'SaveTournament'
    Esperar-Catalogo 'creado'
    Elegir $mesa 'TeamList' 1  # DEL (la lista se ordena por nombre)
    Esperar { (Texto $mesa 'RosterTitle') -like 'Nómina de DEL*0 jugadores' } 'nómina vacía de DEL'
    $registro.Add('Nómina: ' + (Texto $mesa 'RosterTitle'))

    # 1. Archivo con errores: fila y columna, sin poder importar.
    Abrir-Archivo $errores
    $vista = Esperar-VistaPrevia
    Esperar { (Texto $vista 'ImportCounts') -like '*errores*' } 'errores en la vista previa'
    $registro.Add('Errores: ' + (Texto $vista 'ImportCounts'))
    foreach ($e in (Buscar-Id $vista 'ImportErrors').FindAll($Scope::Descendants, (Condicion $AE::ControlTypeProperty ([System.Windows.Automation.ControlType]::Text)))) {
        $registro.Add('  ' + $e.Current.Name)
    }
    if ((Buscar-Id $vista 'ConfirmImport').Current.IsEnabled) { throw 'Importar debía estar deshabilitado con errores.' }
    Capturar $vista 'r3-vista-previa-errores.png'
    Pulsar $vista 'CancelImport'
    Esperar-Catalogo 'Importación cancelada'

    # 2. Plantilla XLSX de muestra: seis jugadores nuevos.
    Abrir-Archivo (Join-Path $raizProyecto 'samples\plantilla-nomina.xlsx')
    $vista = Esperar-VistaPrevia
    Esperar { (Texto $vista 'ImportPlan') -like 'Al importar*' } 'plan de importación'
    $registro.Add('Vista previa: ' + (Texto $vista 'ImportCounts') + ' | ' + (Texto $vista 'ImportPlan'))
    Pulsar $vista 'ConfirmImport'
    Esperar-Catalogo 'Nómina importada'
    Esperar { (Texto $mesa 'RosterTitle') -like '*3 jugadores' } 'nómina de DEL con tres jugadores'
    $registro.Add('Nómina: ' + (Texto $mesa 'RosterTitle'))

    # 3. Conflicto: el gorro 2 tiene otro nombre. Importar queda bloqueado hasta decidir.
    Abrir-Archivo $conflicto
    $vista = Esperar-VistaPrevia
    Esperar { (Texto $vista 'ImportPlan') -like 'Falta decidir*' } 'fila por decidir'
    $registro.Add('Conflicto: ' + (Texto $vista 'ImportCounts') + ' | ' + (Texto $vista 'ImportPlan'))
    if ((Buscar-Id $vista 'ConfirmImport').Current.IsEnabled) { throw 'Importar debía estar deshabilitado sin decisión.' }
    Elegir $vista 'Decision2' 0  # mismo jugador: corregir el nombre
    Esperar { (Buscar-Id $vista 'ConfirmImport').Current.IsEnabled } 'habilitar Importar tras decidir'
    $registro.Add('Decidido: ' + (Texto $vista 'ImportPlan'))
    Capturar $vista 'r3-vista-previa-decision.png'
    Pulsar $vista 'ConfirmImport'
    Esperar-Catalogo 'Nómina importada'
    Esperar { (Texto $mesa 'RosterTitle') -like '*4 jugadores' } 'nómina de DEL con cuatro jugadores'

    # 4. Reimportar la plantilla: nada que importar; se cancela.
    Abrir-Archivo (Join-Path $raizProyecto 'samples\plantilla-nomina.csv')
    $vista = Esperar-VistaPrevia
    Esperar { (Texto $vista 'ImportPlan') -like 'Nada que importar*' -or (Texto $vista 'ImportPlan') -like 'Falta decidir*' } 'reimportación evaluada'
    $registro.Add('Reimportar: ' + (Texto $vista 'ImportCounts') + ' | ' + (Texto $vista 'ImportPlan'))
    Pulsar $vista 'CancelImport'
    Esperar-Catalogo 'Importación cancelada'

    # 5. Edición manual: agregar con el gorro propuesto.
    $registro.Add('Gorro propuesto: ' + (Buscar-Id $mesa 'RosterCap').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value)
    Escribir $mesa 'RosterPlayerName' 'Ana Ola'
    Pulsar $mesa 'SaveRosterPlayer'
    Esperar-Catalogo 'agregado'
    Esperar { (Texto $mesa 'RosterTitle') -like '*5 jugadores' } 'nómina con cinco jugadores'
    $registro.Add('Nómina: ' + (Texto $mesa 'RosterTitle'))
    Capturar $mesa 'r3-mesa-nomina.png'
    $registro.Add('RECORRIDO COMPLETO')
} finally {
    $registro | Set-Content -Encoding UTF8 (Join-Path $salida 'recorrido.txt')
    if ($null -ne $proceso -and -not $proceso.HasExited) {
        $null = $proceso.CloseMainWindow()
        if (-not $proceso.WaitForExit(5000)) { $proceso.Kill() }
    }
    Write-Output "Salida: $salida"
}
