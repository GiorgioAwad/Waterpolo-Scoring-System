# Rediseño del marcador del público — plan de implementación

> **Para agentes:** sub-habilidad requerida: superpowers:executing-plans (o subagent-driven-development).
> Los pasos usan casillas (`- [ ]`). Spec: `docs/superpowers/specs/2026-11-25-marcador-publico-design.md`.

**Objetivo:** reemplazar la presentación del marcador del público por el diseño «A · Transmisión» con
avisos animados «toma del lado», sin cambiar dominio, base ni reglas.

**Arquitectura:** la lógica calculable (modo central, puntos de periodo, anillo, atenuados, avisos,
colores) vive en `MarcadorViewModel`, `ActionBanner` y dos clases puras de presentación
(`ColorTools`, `ScoreboardTexts`), probadas sin WPF. La vista (`MarcadorWindow.xaml`) solo dibuja y
anima; el código de la ventana arranca el `Storyboard` del aviso cuando sube `BannerGeneration`.

**Tecnología:** .NET 10, WPF, CommunityToolkit.Mvvm, xUnit; pruebas con `TempStorage`/`TestDesk`.

## Restricciones globales

- Interfaz, mensajes y nombres de pruebas en español; presentación `es-PE`.
- Sin señal vigente: `--:--` / `--`; no se extrapolan tiempos.
- Avisos una vez por acción nueva; nunca al corregir, anular, reabrir ni abrir la pantalla.
- Los avisos **nunca** tapan la columna central (relojes, periodo, anillo).
- `TreatWarningsAsErrors` activo. En Desktop, `System.Windows.Application` explícito; sin `System.IO`
  implícito.
- Sin commits (AGENTS.md: solo si el usuario lo pide). Sin red.
- Entorno: `. .\scripts\entorno.ps1` antes de `dotnet`. Compilar/probar:
  `dotnet build FDNDA.Waterpolo.sln -c Release`, `dotnet test FDNDA.Waterpolo.sln -c Release`.

## Archivos

| Archivo | Responsabilidad |
|---|---|
| `src/FDNDA.Waterpolo.Desktop/Presentation/ColorTools.cs` (nuevo) | Luminancia, texto legible, oscurecer, opacidad sobre `#RRGGBB`. Pública. |
| `src/FDNDA.Waterpolo.Desktop/Presentation/ScoreboardTexts.cs` (nuevo) | Ordinales y líneas de periodo del público. Pública. |
| `src/FDNDA.Waterpolo.Desktop/Presentation/ShotRingGeometryConverter.cs` (nuevo) | Fracción → arco del anillo. |
| `src/FDNDA.Waterpolo.Desktop/ViewModels/MarcadorViewModel.cs` | Estado central, puntos, anillo, atenuados, brillos, aviso enriquecido. |
| `src/FDNDA.Waterpolo.Desktop/Views/MarcadorWindow.xaml(.cs)` | Diseño y animaciones. |
| `tests/FDNDA.Waterpolo.Desktop.Tests/MarcadorDisenoTests.cs` (nuevo) | Pruebas del rediseño. |
| `tests/FDNDA.Waterpolo.Desktop.Tests/OvertimeShootoutViewModelTests.cs` | `ShowClocks` en tanda pasa a falso (cambio aprobado). |

---

### Tarea 1: `ColorTools` y `ScoreboardTexts`

**Produce:**
- `ColorTools.Luminance(string hex) : double` (luminancia relativa sRGB 0–1; inválido → 0).
- `ColorTools.TextOn(string hex) : string` → `"#000000"` si luminancia > 0,3; si no `"#FFFFFF"`
  (inválido → blanco).
- `ColorTools.Darken(string hex, double amount) : string` (`#RRGGBB`, cada canal × (1 − amount);
  inválido → `ColorTools.Fallback` = `"#37474F"` oscurecido).
- `ColorTools.WithAlpha(string hex, double alpha) : string` → `#AARRGGBB` (inválido → `#00000000`).
- `ScoreboardTexts.Ordinal(int n)` → «1.er», «2.º», «3.er», «4.º», «5.º»…
- `ScoreboardTexts.PeriodLine(MatchSnapshot)` → «3.er PERIODO» o «PRÓRROGA 1».
- `ScoreboardTexts.BreakLine(MatchSnapshot)` → «FIN DEL 2.º PERIODO» o «FIN DE LA PRÓRROGA 1».

- [ ] Prueba (falla): `MarcadorDisenoTests.Los_colores_claros_llevan_texto_negro_y_los_oscuros_blanco`
  con `#F2C200`, `#74ACDF` → negro; `#D91023`, `#005AAA` → blanco; `"azul"` → blanco;
  `Darken("#FF8040", 0.5) == "#804020"` (redondeo `Math.Round` a par → usar
  `(int)Math.Round(v * (1 - a), MidpointRounding.AwayFromZero)`); `WithAlpha("#D91023", 0.38) == "#61D91023"`.
- [ ] Prueba (falla): `Los_ordinales_y_las_lineas_de_periodo_son_los_del_publico`: 1→«1.er», 2→«2.º»,
  3→«3.er», 4→«4.º», 11→«11.º».
- [ ] Implementar; ejecutar `dotnet test … --filter MarcadorDisenoTests`; pasa.

### Tarea 2: estado central, puntos de periodo y atenuados

**Produce en `MarcadorViewModel`:** `enum ScoreboardCenter { Preview, Clocks, Break, Shootout, Final }`,
`record PeriodPip(bool Played)`, propiedades `CenterMode`, `PeriodText`, `CenterHeadline`,
`CenterNote`, `HasCenterNote`, `PeriodPips`, `ShowPeriodPips`, `HomeDimmed`, `AwayDimmed`,
`HomeGlowColor`, `AwayGlowColor`. `ShowClocks` = `CenterMode == Clocks`.

Reglas:
- Programado → `Preview`, «PRÓXIMO PARTIDO», «VS».
- En juego, etapa periodo → `Clocks`, `PeriodLine`.
- Etapa descanso → `Break`, `BreakLine`, «DESCANSO».
- Etapa tanda → `Shootout`, «TANDA DE PENALES», «PENALES», nota «Reglamentario h – a».
- Finalizado → `Final`, «RESULTADO FINAL», «FINAL», nota «Penales h – a» si hubo tanda, «Tras
  prórroga» si terminó en prórroga, si no vacía.
- Puntos: uno por periodo reglamentario; `Played` = número ≤ periodo actual. `ShowPeriodPips` solo en
  `Clocks`/`Break`, con reglas y sin prórroga.
- Atenuado solo en `Final`: por tanda si la hubo y no está empatada, si no por resultado; empate → nadie.
- Brillos: `ColorTools.WithAlpha(color del equipo, 0.38)`.

- [ ] Pruebas (fallan) en `MarcadorDisenoTests`:
  `El_marcador_pasa_por_previa_juego_descanso_y_final_con_sus_textos` (programado → previa;
  iniciar → «1.er PERIODO», 4 puntos con 1 jugado; descanso → «FIN DEL 1.er PERIODO» y «DESCANSO»;
  siguiente periodo → «2.º PERIODO»; gol local y finalizar → `Final`, visita atenuada);
  `Un_final_empatado_no_atenua_a_nadie`;
  `En_la_tanda_no_hay_relojes_y_el_final_atenua_al_que_pierde_la_tanda` (reglas con tanda, 4 periodos
  0–0, tanda, visita convierte, finalizar → local atenuado, nota «Penales 0 – 1»);
  `En_la_prorroga_no_hay_puntos_de_periodo`.
- [ ] Ajustar `OvertimeShootoutViewModelTests` línea `Assert.True(scoreboard.ShowClocks)` en tanda →
  `Assert.False` (diseño aprobado: sin relojes en la tanda).
- [ ] Implementar; pruebas pasan.

### Tarea 3: anillo de posesión y relojes atenuados

**Produce:** `ShotClockFraction : double`, `ClocksDimmed : bool`. Fracción = ms de posesión vigente ÷
(`Rules.ShotClockSeconds` × 1000), limitada a [0, 1]; 0 sin lectura vigente o sin reglas. Se recalcula
al llegar cronometraje y al llegar un snapshot. `ClocksDimmed` = reloj de partido sin lectura vigente.

- [ ] Prueba (falla) `El_anillo_de_posesion_es_la_fraccion_de_la_posesion_completa_y_sin_senal_se_vacia`
  con un `ITimingFeed` manual: 18 000 ms → 0,6; 45 000 ms → 1; sin lectura → 0 y `ClocksDimmed`.
- [ ] Implementar; pasa.

### Tarea 4: aviso enriquecido

**Produce:** `ActionBannerKind.Expulsion = 4`. `ActionBanner(Kind, Side, Headline, Cap, PlayerName,
TeamName, Background, BackgroundDark, Foreground)` con `Text` (mismo formato de hoy), `IsGoal`,
`ShowsCap` (exclusión/expulsión), `IsTimeout`, `IsExpulsion`, `IsHome`. En el VM: `Banner`,
`BannerGeneration`, `BannerTeamImage` (bandera o, si no hay, escudo del lado); `BannerDuration` = 4,3 s.
Colores: gol = color del equipo / `Darken(color, 0.55)` / `TextOn(color)`; exclusión `#E53935`/`#6A100E`;
expulsión `#9E1220`/`#2E0308`; tiempo muerto `#2F80ED`/`#0B2A63`; texto blanco.
Titulares: «¡GOL!», «¡GOL DE PENAL!», «EXCLUSIÓN», «EXPULSIÓN», «TIEMPO\nMUERTO».

- [ ] Pruebas (fallan): `El_gol_sale_del_lado_del_equipo_con_su_color_y_el_jugador`;
  `La_exclusion_y_la_expulsion_usan_rojos_fijos_y_muestran_el_gorro`;
  `El_tiempo_muerto_sale_en_azul_sin_jugador`;
  `Cada_aviso_nuevo_sube_la_generacion_y_corregir_o_desactivar_no` (corrección, anulación y tipo
  desactivado no suben `BannerGeneration`).
- [ ] Las pruebas existentes de `IdentityViewModelTests` siguen pasando sin cambios (mismo `Text`).
- [ ] Implementar; pasa la solución completa.

### Tarea 5: vista y animaciones

- [ ] `ShotRingGeometryConverter`: fracción → `Geometry` (centro 134,5; radio 126,5; desde arriba en
  sentido horario; ≥ 0,9999 → `EllipseGeometry`; ≤ 0 → `Geometry.Empty`). Registrar en `App.xaml`.
- [ ] Reescribir `MarcadorWindow.xaml` con las medidas del spec §3 (cabecera 134, cuerpo 154/749 con
  columnas 1*·576·1*, franja 144; brillos; columna central por `CenterMode`; previa; tanda; final).
  Conservar todos los `AutomationId` y agregar `PublicPeriodPips`, `PublicShotRing`,
  `PublicCenterMode`, `PublicTakeover`.
- [ ] Panel «toma del lado» (691 × 946, recorte inclinado del 14 % hacia el centro) con titular,
  círculo de gorro/ícono, palabra, línea de jugador, ondas y brillo; colores y textos desde `Banner`.
- [ ] `MarcadorWindow.xaml.cs`: al cambiar `BannerGeneration`, ubicar el panel en el lado del aviso,
  fijar el recorte y lanzar el `Storyboard` (entrada 0–0,5 s; pop 0,4–1,3 s; línea 0,8–1,6 s; ondas
  solo en gol; brillo 1,1–2 s; salida 3,7–4,3 s; latido del número 4,3–5,3 s solo en gol). Un aviso
  nuevo reemplaza al anterior.
- [ ] Compilar en Release sin advertencias.
- [ ] Verificación visual: `.\scripts\demo.ps1 -Datos <temporal> -Operador "Prueba"`; abrir el marcador
  en ventana; recorrer previa, juego, gol, exclusión, expulsión, tiempo muerto, descanso, sin señal y
  final; capturas con PrintWindow. Corregir lo que no coincida con las maquetas.

### Tarea 6: documentación y cierre

- [ ] Spec: umbral de texto oscuro 0,3 (no 0,5) y `ScoreboardTexts` en lugar de ampliar `MatchTexts`.
- [ ] `docs/requisitos.md`: RF07, RF08 y la decisión «Avisos de 4 s en la franja inferior».
- [ ] `docs/aceptacion.md` §1.17 con lo ejecutado (pruebas, capturas) y lo pendiente (LED, hito C).
- [ ] `dotnet test FDNDA.Waterpolo.sln -c Release` completo en verde; anotar el total.
