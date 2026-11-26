# Rediseño del marcador del público — diseño «A · Transmisión»

**Fecha:** 2026-11-25 · **Estado:** aprobado por el usuario en maquetas e **implementado** el mismo día (aceptación §1.17).
**Alcance:** RF07 (presentación del marcador) y RF08 (avisos animados). Fase A; la validación en la
pantalla LED sigue en el hito C (C3).

## 1. Objetivo

Reemplazar la presentación actual del marcador del público (paneles con bordes redondeados, franjas y
bloques) por un diseño de estilo transmisión deportiva, más profesional y moderno, y hacer los avisos de
gol, exclusión, expulsión y tiempo muerto animados y mucho más visibles, sin tapar nunca los relojes.

Las maquetas aprobadas están en `.superpowers/brainstorm/` (fuera del repositorio): `direcciones.html`
(elegida la A), `a-estados.html` (siete estados) y `avisos-lado-v2.html` (avisos «toma del lado»).

## 2. Lo que no cambia

- Solo cambian `Views/MarcadorWindow.xaml` (+ `.xaml.cs`) y `ViewModels/MarcadorViewModel.cs`, más
  ayudas de presentación en `Desktop/Presentation`. No cambian la mesa, el dominio, la base, las
  migraciones, las reglas ni la lectura de la consola.
- El marcador sigue siendo de solo lectura y observa snapshots publicados después del commit.
- Los avisos salen **una vez por acción nueva** (gol, penal convertido, exclusión, expulsión, tiempo
  muerto) y nunca al corregir, anular, reabrir ni abrir la pantalla; se desactivan en general y por
  tipo desde la mesa (`ScoreboardSettings`), como hoy.
- Sin señal vigente: `--:--` y `--`; no se extrapola ni se inventa ningún tiempo.
- Identidad del evento (título, subtítulo, logos, auspiciadores, escudos, banderas) desde
  *Identidad y plantilla…*, sin cambios de datos. Distintivo «DEMO / SIMULACIÓN» o «REPRODUCCIÓN»
  solo con fuente simulada.
- Lienzo base 1920×1080 dentro de un `Viewbox` con el margen seguro configurable actual.

## 3. Diseño visual

Medidas en píxeles del lienzo base 1920×1080.

### 3.1 Fondo y tipografía

- Fondo `#06090F`. Sin paneles con borde ni cajas: el color de cada equipo entra como un **brillo
  radial** desde su borde (elipse de 1152 px, centro a −346 px del borde lateral y 96 px desde arriba,
  color del equipo con opacidad 0,38 que se desvanece al 62 % del radio).
- Tipografía **Bahnschrift** (incluida en Windows 10/11; sin descargas). Respaldo: Segoe UI. Números
  con alineación tabular (`Typography.NumeralAlignment="Tabular"`).

### 3.2 Cabecera (alto 134 px, márgenes laterales 50 px)

Logo de la organización (81 px) · título del torneo (40 px, SemiBold, espaciado amplio, en mayúsculas;
se reduce para entrar completo) con el subtítulo debajo (20 px, `#8FA3BC`, mayúsculas) · etiqueta del
partido (22 px, `#8FA3BC`) · logo del evento · distintivo de modo (ámbar `#FFB020`, texto negro).

### 3.3 Cuerpo (desde y = 154, alto 749; columnas 1* · 576 · 1*)

**Columna de cada equipo** (centrada): bandera (104×69, esquinas 7 px) o escudo junto al nombre
(77 px, Bold); debajo el resultado (403 px, Bold) y una barra del color del equipo (269×13, extremos
redondeados).

- Nombres largos: hasta dos líneas y se reducen para entrar (prueba: «Club Deportivo Tiburones del
  Norte»); nunca «…».
- Local a la izquierda con bandera/escudo a la izquierda del nombre; visita en espejo.

**Columna central:**

- Línea de periodo (29 px, `#C9D4E3`): «3.er PERIODO» + **puntos de periodo** (uno por periodo
  reglamentario, 31×9 px; blancos los jugados y el actual, tenues los restantes). En prórroga:
  «PRÓRROGA 1» sin puntos.
- Reloj de partido (221 px, SemiBold).
- **Anillo de posesión** (269 px de diámetro, trazo 16 px, ámbar `#FFB020` sobre pista blanca al
  10 %): fracción = posesión recibida ÷ posesión completa de las reglas del partido
  (`ShotClockSeconds`), limitada a [0, 1]. Número en el centro (127 px, ámbar) y «POSESIÓN» debajo.
- Aviso de señal: «SEÑAL DE TIEMPO NO DISPONIBLE» en rojo `#C62828` **debajo** del anillo, sin
  taparlo.

### 3.4 Franja inferior (alto 144 px)

Auspiciadores repartidos a lo ancho sobre un degradado muy suave (blanco al 4,5 %). Sin franja ni
borde.

### 3.5 Estados

| Estado | Columna central | Equipos |
|---|---|---|
| Sin partido | Pantalla de espera actual («Marcador de Waterpolo»), con el fondo nuevo. | — |
| Programado (previa) | «PRÓXIMO PARTIDO» y «VS» grande y fino (`#8FA3BC`). Sin relojes. | Bandera o escudo grande (288×192) y nombre (96 px); sin resultado. |
| En juego | Periodo + puntos, reloj y anillo. | Resultado. |
| Sin señal | Reloj y anillo atenuados (blanco al 28 %), `--:--` y `--`, anillo vacío; aviso rojo debajo. | Resultado. |
| Descanso | «FIN DEL 2.º PERIODO» + puntos y «DESCANSO» grande en lugar de los relojes. | Resultado. |
| Tanda | «TANDA DE PENALES» (ámbar), «PENALES» grande y «Reglamentario 9 – 9». Sin relojes. | Resultado más chico (288 px) y, debajo, los lanzamientos (círculos verdes convertidos, rojos huecos fallados; hasta 10) con el total en ámbar. |
| Final | «RESULTADO FINAL» y «FINAL» grande; «Tras prórroga» si la hubo. Si hubo tanda, se mantienen los lanzamientos. | El número del perdedor se atenúa (blanco al 38 %). Empate sin tanda: ninguno se atenúa. |

El «perdedor» se deriva del resultado reglamentario o, si hubo tanda, del de la tanda; es
presentación, no una decisión arbitral.

## 4. Avisos «toma del lado»

### 4.1 Forma

Un panel de 691 px de ancho cubre la columna del equipo del aviso desde la cabecera hasta abajo
(y = 134 a 1080), con el borde hacia el centro inclinado (corte del 14 % del ancho). **Nunca invade la
columna central**: relojes, periodo y anillo siguen visibles. El contenido se aparta 115 px del borde
inclinado.

| Aviso | Lado | Fondo | Contenido |
|---|---|---|---|
| Gol y penal convertido | Equipo que anota | Degradado del **color del equipo** (del color a una versión oscurecida) | «¡GOL!» (230 px, Bold, cursiva) · gorro en círculo blanco + nombre del jugador + equipo. Penal: «¡GOL DE PENAL!» con el mismo trato reducido para entrar. |
| Exclusión | Equipo sancionado | Rojo `#E53935 → #6A100E` | **Número de gorro** en círculo blanco grande (221 px; número 134 px) · «EXCLUSIÓN» (88 px, espaciado amplio) · bandera/escudo + jugador + equipo. |
| Expulsión | Equipo sancionado | Rojo oscuro `#9E1220 → #2E0308` | Igual, con el círculo en rojo `#C62828`, número blanco y borde blanco · «EXPULSIÓN». |
| Tiempo muerto | Equipo que lo pide | Azul `#2F80ED → #0B2A63` | Ícono de cronómetro en círculo blanco · «TIEMPO / MUERTO» en dos líneas · bandera/escudo + equipo. |

Los colores de exclusión, expulsión y tiempo muerto son **fijos** (el color dice el tipo a distancia);
el gol usa el del equipo. Si el color del equipo es claro (luminancia relativa > 0,5), el texto del
panel de gol pasa a negro (umbral implementado: luminancia > 0,3, que da negro también sobre celestes como `#74ACDF`). Todo texto del panel se reduce para entrar (`Viewbox` con
`StretchDirection="DownOnly"`); nunca se corta.

### 4.2 Movimiento (≈ 4,3 s)

1. 0–0,5 s: el panel entra deslizándose desde el borde de su lado (curva de salida suave).
2. 0,4–1,3 s: la palabra o el círculo «aparece» (escala 2,2 → 1 con desenfoque que se aclara y un
   pequeño rebote).
3. 0,8–1,6 s: suben la línea del jugador/equipo.
4. Solo en gol: dos ondas concéntricas blancas se expanden desde el centro del panel (hasta 1344 px)
   y se desvanecen.
5. 1,1–2 s: un brillo diagonal cruza el panel.
6. 3,7–4,3 s: el panel sale por donde entró.
7. Solo en gol, al salir: el número del equipo que anotó (ya actualizado detrás del panel) late en
   dorado (escala 1,12 y resplandor `#FFD600`, ~1 s).

Si llega otra acción mientras hay un aviso, el nuevo reemplaza al anterior y la animación arranca de
nuevo. Con las animaciones desactivadas no se muestra nada (como hoy).

## 5. Cambios en el código

### 5.1 `Desktop/Presentation`

- `ScoreboardTexts` (nueva, pública para probarla): ordinales de periodo para el público («1.er», «2.º»,
  «3.er», «4.º», …), «FIN DEL n.º PERIODO», «PRÓRROGA n».
- `ColorTools`: luminancia relativa de `#RRGGBB`, color de texto legible (negro/blanco), versión
  oscurecida para degradados y color con opacidad para los brillos. Entradas inválidas → valor
  predeterminado, sin excepción.

### 5.2 `MarcadorViewModel`

Propiedades nuevas (todas derivadas del snapshot, del cronometraje y de la identidad):

- `CenterMode` (`Preview`, `Clocks`, `Break`, `Shootout`, `Final`) y `PeriodText`, `BreakText`,
  `FinalNote`.
- `PeriodPips` (lista de puntos: jugado/actual/pendiente) y `ShowPeriodPips` (falso en prórroga o sin
  reglas).
- `ShotClockFraction` (0–1; 0 sin lectura vigente o sin reglas) y `ClocksDimmed` (sin señal).
- `HomeDimmed` / `AwayDimmed` (solo en `Final`).
- `HomeGlowColor` / `AwayGlowColor` (color del equipo con opacidad).
- Aviso: `ActionBanner` pasa a llevar `Kind` (`Goal`, `Exclusion`, **`Expulsion`** nuevo, `Timeout`),
  `Side`, `Headline` («¡GOL!», «¡GOL DE PENAL!», «EXCLUSIÓN», «EXPULSIÓN», «TIEMPO MUERTO»),
  `Cap`, `PlayerName`, `TeamName`, `BackgroundTop`/`BackgroundBottom` y `Foreground`. Se mantiene
  `BannerText` (texto completo en una línea) para accesibilidad y para las pruebas existentes, que se
  ajustan si cambia su formato. Se agrega `BannerGeneration` (entero que sube con cada aviso nuevo):
  la vista arranca la animación al cambiar. `IsBannerVisible` dura lo mismo que la animación
  (`BannerDuration` pasa de 4 s a 4,3 s).

Se conservan los `AutomationId` existentes (`PublicHomeScore`, `PublicGameClock`, `PublicShotClock`,
`PublicStage`, `PublicActionBanner`, `PublicSignalNotice`, `PublicModeBadge`, logos, escudos,
banderas, auspiciadores, tanda). Nuevos: `PublicPeriodPips`, `PublicCenterHeadline`, `PublicCenterNote`. `PublicActionBanner` pasa a un texto
transparente que ocupa el panel del aviso (para medir que no tape los relojes). `PublicShotRing` y
`PublicCenterMode` están en paneles, que UI Automation no expone.

### 5.3 Vista

- `MarcadorWindow.xaml` reescrito con el diseño de §3. Brillos con `RadialGradientBrush`; anillo con
  un `Path` de arco calculado por un convertidor (fracción → `ArcSegment`).
- Panel de aviso: `Grid` con `Clip` poligonal, `TranslateTransform`, `ScaleTransform`, `BlurEffect`
  y elipses para las ondas; un `Storyboard` por fase. `MarcadorWindow.xaml.cs` arranca el
  `Storyboard` del lado correcto cuando cambia `BannerGeneration` (lógica de vista solamente).
- Bahnschrift: verificado en el ejecutable con los pesos Light, Regular, SemiBold y Bold; respaldo Segoe UI.

## 6. Pruebas (sin WPF, en `FDNDA.Waterpolo.Desktop.Tests`)

Con `TempStorage`, `TestDesk` y cronometraje fijo, sin esperas reales (`ManualTimeProvider`):

- Puntos de periodo: 4 periodos, periodo 3 → jugados 1–2, actual 3, pendiente 4; prórroga → sin
  puntos y «PRÓRROGA 1».
- Ordinales: «1.er», «2.º», «3.er», «4.º» y «FIN DEL 2.º PERIODO».
- Anillo: 18 de 30 → 0,6; lectura mayor que la posesión completa → 1; sin señal → 0 y `ClocksDimmed`.
- Modo central: programado → `Preview`; en juego → `Clocks`; descanso → `Break`; tanda → `Shootout`;
  finalizado → `Final`.
- Atenuado: final 11–8 atenúa al visitante; final empatado y tanda 4–3 atenúa al que perdió la tanda;
  empate sin tanda no atenúa.
- Avisos: gol del local → lado local, color del equipo, «¡GOL!», gorro y jugador; penal convertido →
  «¡GOL DE PENAL!»; exclusión y expulsión → `Exclusion` / `Expulsion` con sus colores fijos; tiempo
  muerto → sin jugador, azul; cada aviso nuevo sube `BannerGeneration`; corregir, anular o reabrir no
  lo sube; tipo desactivado no lo sube.
- `ColorTools`: amarillo `#F2C200` y celeste `#74ACDF` → texto negro; rojo `#D91023` y azul
  `#005AAA` → texto blanco; hex inválido → predeterminado.
- Se ajustan las pruebas existentes de `IdentityViewModelTests` y `OvertimeShootoutViewModelTests`
  solo donde cambie un texto; no se debilita ninguna aserción.

Verificación visual: ejecutable con `--datos <carpeta temporal>`, capturas de los siete estados y de
los cuatro avisos en modo ventana (1280×720 y 1920×1080 simulados por tamaño de ventana).

## 7. Documentación al terminar

- `docs/requisitos.md`: RF07 y RF08 (presentación nueva; avisos sobre el lado del equipo) y la
  decisión de §2.2 sobre «avisos de 4 s en la franja inferior», que pasa a «toma del lado» (los
  relojes siguen libres).
- `docs/aceptacion.md`: nueva sección §1.17 con lo **ejecutado** (pruebas y capturas) y lo pendiente
  (pantalla LED, segundo monitor: hito C).

## 8. Fuera de alcance

Tiempos muertos restantes y exclusiones activas por equipo; colores de aviso configurables por evento;
otras proporciones de pantalla (se decide cuando la FDNDA confirme el LED); prueba en el LED real
(hito C).
