# Recorrido de R7 (imágenes e identidad, RF01/RF07/RF08) en el escritorio interactivo.
# Usa exclusivamente datos nuevos bajo artifacts y las imágenes genéricas de samples\identidad-ejemplo.
# No envía teclas ni mueve el ratón: opera AutomationId con UI Automation; el diálogo «Abrir» de
# Windows recibe mensajes dirigidos (WM_SETTEXT al campo y BM_CLICK al botón).
[CmdletBinding()]
param(
    [string]$Ejecutable
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'entorno.ps1')
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -Namespace IdentidadR7 -Name Win32 -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
public static extern System.IntPtr SendMessage(System.IntPtr hWnd, int msg, System.IntPtr wParam, string lParam);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool PostMessage(System.IntPtr hWnd, int msg, System.IntPtr wParam, System.IntPtr lParam);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool PrintWindow(System.IntPtr hWnd, System.IntPtr hdc, uint flags);
'@

$raizProyecto = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$muestras = Join-Path $raizProyecto 'samples\identidad-ejemplo'
$salida = Join-Path $raizProyecto ('artifacts\verificacion-r7-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $salida
if (-not $Ejecutable) {
    $Ejecutable = Join-Path $raizProyecto 'src\FDNDA.Waterpolo.Desktop\bin\Release\net10.0-windows\FDNDA.Waterpolo.exe'
}
$registro = [Collections.Generic.List[string]]::new()
$proceso = $null
$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]

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

function Mostrar($control) {
    $item = $null
    if ($control.TryGetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern, [ref]$item)) { $item.ScrollIntoView() }
}

function Pulsar($ventana, [string]$id) {
    $control = Buscar-Id $ventana $id
    Mostrar $control
    if (-not $control.Current.IsEnabled) { throw "La acción $id está deshabilitada." }
    $control.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Alternar($ventana, [string]$id) {
    (Buscar-Id $ventana $id).GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
}

function Pestana([string]$id) {
    (Buscar-Id $mesa $id).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
}

function Texto($ventana, [string]$id) { (Buscar-Id $ventana $id).Current.Name }

function Valor($ventana, [string]$id) {
    (Buscar-Id $ventana $id).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
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

function Elegir-Fila($ventana, [string]$id, [string]$contiene) {
    foreach ($fila in (Elementos (Buscar-Id $ventana $id))) {
        if ($fila.FindFirst($Scope::Descendants, (Condicion $AE::NameProperty $contiene))) {
            $fila.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
            return
        }
    }
    throw "No hay una fila «$contiene» en $id."
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

# Elige un archivo en el diálogo «Abrir» que ya abrió la acción anterior. No se recorre ni se
# registra el contenido de la carpeta que muestra el diálogo.
function Abrir-Archivo([string]$ruta) {
    $dialogo = Esperar-Ventana { param($v) $v.Current.ClassName -eq '#32770' } 'abrir el diálogo de archivo'
    $script:campo = $null
    Esperar {
        $script:campo = $dialogo.FindFirst($Scope::Descendants, [System.Windows.Automation.AndCondition]::new(
            (Condicion $AE::AutomationIdProperty '1148'), (Condicion $AE::ClassNameProperty 'Edit')))
        $null -ne $script:campo
    } 'campo Nombre de archivo del diálogo'
    $null = [IdentidadR7.Win32]::SendMessage([IntPtr]$script:campo.Current.NativeWindowHandle, 0x000C, [IntPtr]::Zero, $ruta)
    $abrir = $dialogo.FindFirst($Scope::Children, [System.Windows.Automation.AndCondition]::new(
        (Condicion $AE::AutomationIdProperty '1'), (Condicion $AE::ClassNameProperty 'Button')))
    $null = [IdentidadR7.Win32]::PostMessage([IntPtr]$abrir.Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)
    Esperar { $null -eq (Ventana-Del-Proceso { param($v) $v.Current.ClassName -eq '#32770' }) } 'cerrar el diálogo de archivo'
}

# PrintWindow copia el contenido de la ventana aunque otra la tape: el marcador no toma el foco.
function Capturar($ventana, [string]$nombre) {
    $rect = $ventana.Current.BoundingRectangle
    $bitmap = [Drawing.Bitmap]::new([int]$rect.Width, [int]$rect.Height)
    $grafico = [Drawing.Graphics]::FromImage($bitmap)
    $hdc = $grafico.GetHdc()
    try {
        $null = [IdentidadR7.Win32]::PrintWindow([IntPtr]$ventana.Current.NativeWindowHandle, $hdc, 2)
    } finally {
        $grafico.ReleaseHdc($hdc)
        $grafico.Dispose()
    }
    try { $bitmap.Save((Join-Path $salida $nombre), [Drawing.Imaging.ImageFormat]::Png) } finally { $bitmap.Dispose() }
}

function Se-Solapan($a, $b) {
    $r1 = $a.Current.BoundingRectangle; $r2 = $b.Current.BoundingRectangle
    return $r1.IntersectsWith($r2)
}

function Abrir-Marcador {
    Pulsar $mesa 'OpenScoreboard'
    return Esperar-Ventana { param($v) $v.Current.Name -eq 'Marcador — Waterpolo FDNDA' } 'abrir el marcador'
}

try {
    $datos = Join-Path $salida 'datos'
    $proceso = Start-Process -FilePath $Ejecutable -ArgumentList @('--perfil', 'demo', '--datos', ('"' + $datos + '"'), '--operador', '"Prueba R7"') -PassThru
    $script:mesa = $null
    Esperar {
        $script:mesa = $AE::RootElement.FindFirst($Scope::Children, [System.Windows.Automation.AndCondition]::new(
            (Condicion $AE::ProcessIdProperty $proceso.Id), (Condicion $AE::NameProperty 'Marcador de Waterpolo FDNDA — Mesa')))
        $null -ne $script:mesa -and (Buscar-Id $script:mesa 'StartMatch').Current.IsEnabled
    } 'abrir la mesa con el partido demo cargado'
    $registro.Add('Mesa abierta con datos aislados: ' + $datos)

    # 1. Escudo y bandera desde «Eventos y nóminas».
    Pestana 'CatalogTab'
    Esperar { (Elementos (Buscar-Id $mesa 'TeamList')).Count -ge 2 } 'cargar equipos'
    Elegir-Fila $mesa 'TeamList' 'TIB'
    Esperar { (Texto $mesa 'TeamCrestText') -like 'escudo-tiburones-ejemplo.png*' } 'escudo demo de TIB'
    $registro.Add('TIB: escudo -> ' + (Texto $mesa 'TeamCrestText') + ' | bandera -> ' + (Texto $mesa 'TeamFlagText'))

    Pulsar $mesa 'LoadTeamFlag'
    Abrir-Archivo (Join-Path $muestras 'bandera-ficticia-1.png')
    Esperar { (Texto $mesa 'CatalogStatus') -like 'Bandera de TIB guardada*' } 'bandera guardada'
    $registro.Add('Cargar bandera -> ' + (Texto $mesa 'CatalogStatus'))

    $falsa = Join-Path $salida 'no-es-imagen.png'
    Set-Content -Path $falsa -Value 'Esto es texto, no una imagen.' -Encoding UTF8
    Pulsar $mesa 'LoadTeamCrest'
    Abrir-Archivo $falsa
    Esperar { (Texto $mesa 'CatalogStatus') -like '*no es una imagen reconocible*' } 'rechazo del archivo que no es imagen'
    if ((Texto $mesa 'TeamCrestText') -notlike 'escudo-tiburones-ejemplo.png*') { throw 'El archivo inválido cambió el escudo.' }
    $registro.Add('Archivo inválido -> ' + (Texto $mesa 'CatalogStatus'))
    Capturar $mesa 'r7-equipo.png'

    # 2. Identidad del evento con la plantilla de ejemplo y un auspiciador más.
    (Elementos (Buscar-Id $mesa 'TournamentList'))[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Pulsar $mesa 'EditIdentity'
    $identidad = Esperar-Ventana { param($v) $v.Current.Name -like 'Identidad y plantilla*' } 'abrir el editor de identidad'
    $registro.Add('Diálogo: ' + $identidad.Current.Name + ' | ' + (Texto $identidad 'IdentityStatus'))
    Pulsar $identidad 'UseExampleIdentity'
    Esperar { (Valor $identidad 'IdentityTitle') -eq 'Panamericano de Waterpolo Sub-17 2027' } 'plantilla de ejemplo cargada'
    $registro.Add('Plantilla de ejemplo -> ' + (Texto $identidad 'IdentityStatus'))
    $registro.Add('  ' + (Texto $identidad 'IdentityLogoText') + ' | ' + (Texto $identidad 'IdentityOrganizerLogoText'))
    Pulsar $identidad 'AddSponsor'
    Abrir-Archivo (Join-Path $muestras 'auspiciador-b-ejemplo.png')
    Esperar { (Texto $identidad 'IdentitySponsorsTitle') -like 'Auspiciadores (4 de 8)*' } 'auspiciador agregado'
    $registro.Add('  ' + (Texto $identidad 'IdentitySponsorsTitle'))
    Capturar $identidad 'r7-identidad.png'
    Pulsar $identidad 'SaveIdentity'
    Esperar { (Texto $mesa 'CatalogStatus') -like 'Identidad de*guardada*' } 'identidad guardada'
    Esperar { $null -eq (Ventana-Del-Proceso { param($v) $v.Current.Name -like 'Identidad y plantilla*' }) } 'cerrar el editor'
    $registro.Add('Guardar -> ' + (Texto $mesa 'CatalogStatus'))

    # 3. Mesa y marcador muestran la identidad (presentación previa).
    Pestana 'MesaTab'
    Esperar { (Existe $mesa 'HomeCrest') -and (Existe $mesa 'AwayCrest') -and (Existe $mesa 'AwayFlag') } 'escudos y bandera en la cabecera de la mesa'
    $registro.Add('Mesa: escudos local y visita y bandera de visita visibles en la cabecera.')
    $publico = Abrir-Marcador
    Esperar { (Texto $publico 'PublicTitle') -eq 'Panamericano de Waterpolo Sub-17 2027' } 'título de la plantilla en el marcador'
    foreach ($id in 'PublicOrganizerLogo', 'PublicEventLogo', 'PublicHomeCrest', 'PublicAwayCrest', 'PublicAwayFlag') {
        if (-not (Existe $publico $id)) { throw "El marcador no muestra $id." }
    }
    $auspiciadores = (Buscar-Id $publico 'PublicSponsors').FindAll($Scope::Descendants, (Condicion $AE::ControlTypeProperty $CT::Image)).Count
    if ($auspiciadores -ne 4) { throw "El marcador muestra $auspiciadores auspiciadores en lugar de 4." }
    $registro.Add('Marcador previa: «' + (Texto $publico 'PublicTitle') + '» / «' + (Texto $publico 'PublicSubtitle') + '»; logos, escudos, bandera y ' + $auspiciadores + ' auspiciadores visibles.')
    Start-Sleep -Milliseconds 300
    Capturar $publico 'r7-marcador-previa.png'

    # 4. Avisos de exclusión y tiempo muerto, una vez y sin tapar los relojes.
    Pulsar $mesa 'StartMatch'
    Esperar { (Texto $publico 'PublicGameClock') -ne '' -and (Existe $publico 'PublicGameClock') } 'relojes visibles en el marcador'
    Elegir $mesa 'IncidentType' 0
    Elegir $mesa 'HomeRoster' 4
    Pulsar $mesa 'IncidentHome'
    Esperar { (Texto $publico 'PublicActionBanner') -like 'EXCLUSIÓN*' } 'aviso de exclusión'
    Start-Sleep -Milliseconds 450
    $registro.Add('Exclusión -> aviso «' + (Texto $publico 'PublicActionBanner') + '»')
    foreach ($reloj in 'PublicGameClock', 'PublicShotClock') {
        if (Se-Solapan (Buscar-Id $publico 'PublicActionBanner') (Buscar-Id $publico $reloj)) { throw "El aviso tapa $reloj." }
    }
    $registro.Add('  El aviso no se superpone con PublicGameClock ni PublicShotClock (rectángulos en pantalla).')
    Capturar $publico 'r7-aviso-exclusion.png'

    Elegir $mesa 'IncidentType' 5
    Pulsar $mesa 'IncidentAway'
    Esperar { (Texto $publico 'PublicActionBanner') -like 'TIEMPO MUERTO*' } 'aviso de tiempo muerto'
    Start-Sleep -Milliseconds 450
    $registro.Add('Tiempo muerto -> aviso «' + (Texto $publico 'PublicActionBanner') + '»')
    Capturar $publico 'r7-aviso-tiempo-muerto.png'

    # 5. Con el aviso de exclusión desactivado en la mesa, una exclusión nueva no se anuncia.
    Alternar $mesa 'ExclusionAnimations'
    Elegir $mesa 'IncidentType' 0
    Elegir $mesa 'AwayRoster' 1
    Pulsar $mesa 'IncidentAway'
    Esperar { (Texto $mesa 'StatusMessage') -like '*registrad*' } 'exclusión registrada'
    Start-Sleep -Milliseconds 600
    if ((Texto $publico 'PublicActionBanner') -notlike 'TIEMPO MUERTO*') { throw 'Se anunció una exclusión con su aviso desactivado.' }
    $registro.Add('Exclusión con su aviso desactivado -> el marcador no la anuncia (sigue «' + (Texto $publico 'PublicActionBanner') + '»).')

    # 6. Reabrir el marcador no repite avisos pasados.
    Pulsar $mesa 'CloseScoreboard'
    Esperar { $null -eq (Ventana-Del-Proceso { param($v) $v.Current.Name -eq 'Marcador — Waterpolo FDNDA' }) } 'cerrar el marcador'
    $publico = Abrir-Marcador
    Esperar { (Texto $publico 'PublicTitle') -eq 'Panamericano de Waterpolo Sub-17 2027' } 'marcador reabierto con la identidad'
    Start-Sleep -Milliseconds 600
    if ((Texto $publico 'PublicActionBanner') -ne '') { throw 'El marcador reabierto repitió un aviso.' }
    $registro.Add('Marcador reabierto -> sin aviso; identidad y resultado intactos.')
    Capturar $publico 'r7-marcador-reabierto.png'
    Capturar $mesa 'r7-mesa.png'
    $registro.Add('RECORRIDO COMPLETO')
} finally {
    $registro | Set-Content -Encoding UTF8 (Join-Path $salida 'recorrido.txt')
    if ($null -ne $proceso -and -not $proceso.HasExited) {
        $null = $proceso.CloseMainWindow()
        if (-not $proceso.WaitForExit(5000)) { $proceso.Kill() }
    }
    Write-Output "Salida: $salida"
}
