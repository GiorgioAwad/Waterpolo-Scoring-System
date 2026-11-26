# Prompt para Codex: continuar el Marcador de Waterpolo FDNDA (hito A)

Actúa como desarrollador senior de aplicaciones de escritorio .NET/WPF. Vas a **continuar** un
proyecto que ya tiene un primer corte funcional, verificado y documentado. Tu trabajo es avanzar el
**hito A** por rebanadas completas (código + pruebas + documentación), sin romper lo que existe y
sin declarar hecho lo que no hayas ejecutado.

## 1. Antes de escribir código

Lee, en este orden:

1. `AGENTS.md` — reglas de arquitectura, de producto y de trabajo. Son obligatorias.
2. `README.md` — cómo compilar, probar y abrir la demo.
3. `docs/requisitos.md` — RF01–RF12 con su estado real y las decisiones tomadas.
4. `docs/arquitectura.md` y `docs/modelo-datos.md` — capas, secuencias, esquema e invariantes.
5. `docs/plan-entrega.md` (hito A) y `docs/aceptacion.md` (criterios P02–P13).
6. `Prompt-Claude-Code-Waterpolo-FDNDA.md` — el encargo original completo. Sigue vigente: ante una
   duda de alcance o de regla, manda ese documento.

Después inspecciona el código que vas a tocar. Puntos de entrada:

| Tema | Archivo |
|---|---|
| Reglas del partido | `src/FDNDA.Waterpolo.Domain/Matches/Match.cs`, `MatchIncident.cs`, `MatchTypes.cs` |
| Casos de uso y flujo de escritura | `src/FDNDA.Waterpolo.Application/Matches/MatchOperations.cs`, `Commands.cs` |
| Proyección para las ventanas | `src/FDNDA.Waterpolo.Application/Matches/MatchSnapshot.cs` |
| Persistencia | `src/FDNDA.Waterpolo.Infrastructure/Persistence/SqliteMatchRepository.cs`, `SqliteCatalogRepository.cs`, `Migrations/0001_inicial.sql` |
| Mesa | `src/FDNDA.Waterpolo.Desktop/ViewModels/MesaViewModel.cs`, `Views/MesaWindow.xaml` |
| Marcador | `src/FDNDA.Waterpolo.Desktop/ViewModels/MarcadorViewModel.cs`, `Views/MarcadorWindow.xaml` |
| Composición | `src/FDNDA.Waterpolo.Desktop/App.xaml.cs` |
| Utilidades de prueba | `tests/FDNDA.Waterpolo.Infrastructure.Tests/Support/TestSupport.cs` (`TempStorage`, `TestDesk`, `ManualTimeProvider`) |

## 2. Estado de partida

Hecho y verificado (488 pruebas en verde al 2026-11-25; flujos comprobados en el ejecutable).
Resumen por entrega en `docs/plan-entrega.md` («Estado al 2026-11-18»):

- Solución en cuatro capas, SQLite con migraciones 0001 a 0005 y datos demo ficticios (dos equipos con escudos genéricos, dos partidos).
- Partido: iniciar, descanso, siguiente periodo, finalizar, reabrir con motivo; un partido en juego.
- **R1 (hecha):** gol, exclusión/expulsión, falta personal, penal con resultado y tiempo muerto, con
  corrección y anulación por revisiones. Reporte: `docs/hito-a-r1.md`.
- **R2 (hecha):** eventos y equipos (crear y editar) en la pestaña «Eventos y nóminas»; programar
  partidos desde «Partidos» con colores y gorros por partido y la nómina del evento congelada.
  Casos de uso en `Application/Catalog/CatalogOperations.cs` y `MatchOperations.ScheduleMatchAsync`;
  interfaz en `Desktop/ViewModels/CatalogViewModel.cs`. Reporte: `docs/hito-a-r2.md`.
- **R3 (hecha):** nómina por evento con edición manual e importación CSV UTF-8/XLSX con vista previa,
  errores por fila y columna, conflictos con decisión explícita y confirmación transaccional.
  Planificador puro en `Application/Catalog/RosterImport.cs`; lector en
  `Infrastructure/Import/RosterFileReader.cs` (XLSX propio, sin librería); vista previa en
  `Desktop/ViewModels/RosterImportViewModel.cs` y `Views/ImportarNominaWindow.xaml`.
  Reporte: `docs/hito-a-r3.md`. Recorrido de interfaz: `scripts/verificar-r3-ui.ps1`.
- **R4 (hecha):** reglas por evento desde *Reglas del evento…* en «Eventos y nóminas» (periodos,
  duraciones, descansos, tiempos muertos, posesión y tanda). `RulesetSnapshot.ForEvent` decide el
  nombre: valores DEMO → preset DEMO; otros → «Personalizadas del evento (sin aprobación técnica)».
  El preset de competencia se muestra deshabilitado hasta que el área técnica entregue valores.
  Caso de uso `CatalogOperations.UpdateTournamentRulesAsync`; diálogo `RulesEditorViewModel` /
  `ReglasEventoWindow`. Reporte: `docs/hito-a-r4.md`. Recorrido: `scripts/verificar-r4-ui.ps1`.
- **R5 (hecha):** pestaña *Resultados y respaldos* (sección Respaldos): crear, listar, abrir carpeta y
  restaurar; respaldo automático antes de iniciar y al finalizar. Puerto `IBackupStore`,
  `SqliteBackupStore` (restaura con la API de backup sobre la base en uso, respaldo previo y vuelta
  atrás), `Application/Backups/BackupOperations.cs`, `Desktop/ViewModels/BackupsViewModel.cs`.
  Reporte: `docs/hito-a-r5.md`. Recorrido: `scripts/verificar-r5-ui.ps1`. La sección Resultados de
  esa pestaña es un texto provisional: R6 la reemplaza.
- **R6 (hecha):** sección *Resultados* de la pestaña *Resultados y respaldos*: resultados, goleadores
  y totales por evento y partido, exportación PDF y CSV. Totales en el dominio
  (`MatchStatistics.Totals`), `Application/Reports/` (`ReportBuilder`, `ReportDocuments`,
  `ReportOperations`), escritores en `Infrastructure/Export/` (PDF propio, D31),
  `Desktop/ViewModels/ResultsViewModel.cs`. Reporte: `docs/hito-a-r6.md`. Recorrido:
  `scripts/verificar-r6-ui.ps1` (abre los PDF con PyMuPDF).
- **R7 (hecha):** imágenes e identidad. Escudo y bandera por equipo en *Eventos y nóminas*;
  *Identidad y plantilla…* por evento (título, subtítulo, colores, logos del evento y de la
  organización, hasta 8 auspiciadores) con plantilla de ejemplo del Panamericano Sub-17 2027 de
  recursos genéricos (`samples/identidad-ejemplo`, embebidos). Validación PNG/JPEG por contenido en
  el dominio (`Competitions/Identity.cs`), casos de uso en `Application/Identity/IdentityOperations.cs`,
  repositorio `SqliteIdentityRepository`, migración `0003_identidad.sql`. La identidad del partido
  abierto se publica por `ScoreboardIdentityHub` (aparte de los snapshots) y la ven mesa y marcador.
  Avisos de gol, exclusión y tiempo muerto con `ActionBanner.For` (Desktop) y casillas por tipo en
  la mesa. Reporte: `docs/hito-a-r7.md`. Recorrido: `scripts/verificar-r7-ui.ps1`.
- **R8 (hecha):** prórroga y tanda de penales. Reglas del evento con *Periodos de prórroga* y su
  duración (0 = sin prórroga) junto a la tanda. La prórroga son periodos posteriores a `PeriodCount`
  y solo empieza con empate; la tanda es la etapa `MatchStage.Shootout` con lanzamientos
  `IncidentType.ShootoutAttempt` (jugador y `PenaltyResult`) que nunca suman a `Match.Score`
  (`Match.ShootoutScore` aparte). *Iniciar tanda*, *Anular tanda…* con motivo; no se finaliza con la
  tanda empatada; con tanda no se acepta un cambio que desempate el reglamentario. Marcador: tanda
  dentro del panel de cada equipo; mesa, resultados, PDF y CSV la muestran aparte. Migración
  `0004_prorroga_tanda.sql` reconstruye tres tablas (marca `-- @desactivar-claves-foraneas` en
  `MigrationRunner`). Reporte: `docs/hito-a-r8.md`. Recorrido: `scripts/verificar-r8-ui.ps1`.
- **R9 (hecha):** `Infrastructure/Timing/ReplayTimingSource.cs` reproduce grabaciones en un formato
  normalizado propio (`TimingRecording.cs`: CSV UTF-8, un hueco = corte, sesión nueva = reconexión),
  de forma determinista. `--reproduccion "archivo"` solo con el perfil demo; etiqueta «REPRODUCCIÓN»;
  panel de avance y *Reiniciar* en la mesa (`IReplayControl`). Grabación sintética en
  `samples/cronometraje`. Reporte: `docs/hito-a-r9.md`. Recorrido: `scripts/verificar-r9-ui.ps1`.
- **Hito B, preparación (sin consola):** decodificador del flujo de tablero que describen terceros
  (`Infrastructure/Timing/Colorado/`, `CtsWaterPoloInterpreter` implementa `IColoradoFrameDecoder`) y
  herramienta aparte de captura de solo lectura `tools/FDNDA.Waterpolo.Captura`. Reporte:
  `docs/hito-b-preparacion.md`. Ese formato de terceros **no** es el que se usa en el programa.
- **Hito B, lectura de la consola (2026-11-24, con la consola real):** COM PORT 2 de la System 6 emite
  tramas `CTSWP1.1c` a 115200 8-N-1 (`docs/colorado-system6.md` §7; capturas en `docs/evidencia/capturas`).
  `CtswpFrameDecoder`, `SerialColoradoTransport` (solo lectura) y `ColoradoSystem6TimingSource` con
  reconexión; se activa con `--perfil produccion --puerto COMx`. Reporte: `docs/hito-b-lectura.md`.
  Falta la verificación en vivo (P14–P17): no declares RF11 aceptado sin ella.
- **Estado de conexiones (pedido del operador):** panel en *Configuración / diagnóstico* e indicador
  en la barra inferior (`Desktop/ViewModels/ConnectionStatusViewModel.cs`). Con la reproducción (R9)
  o cualquier otra fuente, el panel no muestra la consola como «Conectada» si no es la fuente activa
  con datos válidos. Colores de equipo con paleta y selector de Windows.
  Recorrido: `scripts/verificar-conexiones-ui.ps1`.
- **Carpeta de datos configurable:** `--datos`, variable de usuario `FDNDA_WATERPOLO_DATOS` o, sin
  ninguna, `%LocalAppData%\FDNDA\Waterpolo` (`Infrastructure/Storage/AppPaths.cs`).
- Escritura transaccional con versión esperada e idempotencia por `CommandId`; publicación tras el commit.
- Reloj simulado de partido y posesión; pérdida y reconexión; relojes no vigentes como `--:--` / `--`.
- Mesa y marcador en dos ventanas; aviso de gol una sola vez; recuperación tras cierre forzado.
- Paquete `win-x64` self-contained con `FDNDA.Waterpolo.exe`.

Cosas que debes saber y que no se ven a simple vista:

- Las imágenes (`asset`) **no** se congelan en el partido: se leen del catálogo vigente al mostrarse.
  El marcador tiene tres franjas: cabecera (identidad), cuerpo (equipos, resultado y relojes) y una
  franja inferior con auspiciadores donde aparecen los avisos. La tanda (R8) va dentro del panel de
  cada equipo, bajo su resultado. «DEMO / SIMULACIÓN» o «REPRODUCCIÓN» van en la cabecera (`ModeBadge`).
- `ICatalogDialogs` tiene métodos con implementación por defecto (`PickImageFile`,
  `ShowEventIdentityAsync`) para no romper los dobles de prueba; los nuevos diálogos pueden seguir ese patrón.
- Las reglas guardan `TimeoutsPerTeam` y descansos y se copian al partido, pero la mesa no los
  aplica: no limita tiempos muertos ni cronometra descansos. Prórroga y tanda sí se usan (R8).
- Una migración que necesite cambiar un `CHECK` debe reconstruir la tabla como `0004` y llevar la
  marca `-- @desactivar-claves-foraneas`; el fixture de base v1 de las pruebas copia solo columnas v1.
- La nómina del evento vive en `tournament_roster_entry` y `ScheduleMatchAsync` la copia al
  programar. Un partido ya programado **no** recibe la nómina editada o importada después (decisión
  de R3, requisitos §2.2): actualizarla, solo en partidos no iniciados, queda pendiente junto con
  reprogramar o anular.
- La pestaña «Resultados y respaldos» tiene Resultados (R6) a la izquierda y Respaldos (R5) a la derecha; la tanda (R8) va en su propia columna y sección.
- Las pruebas de ViewModels están en `tests/FDNDA.Waterpolo.Desktop.Tests` (`net10.0-windows`);
  `CatalogViewModelTests.cs` muestra cómo componer mesa y catálogo sin ventanas.
- El formato de los relojes y los umbrales de vigencia son provisionales; no los cambies sin motivo.
- El marcador a pantalla completa en un segundo monitor y Alt+letra no se han verificado: hace falta
  una persona con el equipo. No los marques como verificados.

## 3. Entorno

- Windows 11, PowerShell. El `dotnet` global del equipo es 8.x y **falla** con `global.json`.
  En cada sesión: `. .\scripts\entorno.ps1` (usa el SDK 10 local de `.tools\dotnet`). Si no existe:
  `.\scripts\instalar-sdk-local.ps1`.
- `dotnet build FDNDA.Waterpolo.sln -c Release` · `dotnet test FDNDA.Waterpolo.sln -c Release` ·
  `.\scripts\publicar.ps1`.
- El `FDNDA.Waterpolo.exe` de `bin` solo arranca con `DOTNET_ROOT` apuntando a `.tools\dotnet`
  (lo fija `entorno.ps1`); lanzado desde una sesión sin eso, Windows pide instalar .NET.
- La unidad C: tiene poco espacio libre; no generes artefactos grandes fuera del repositorio.
- Para ejecutar la interfaz usa **siempre** una carpeta temporal:
  `.\scripts\demo.ps1 -Datos <carpeta temporal> -Operador "Prueba"`. No toques
  `%LocalAppData%\FDNDA\Waterpolo` ni `G:\FDNDA\Waterpolo` (datos del usuario; la variable de
  usuario `FDNDA_WATERPOLO_DATOS` apunta ahí y `--datos` la anula). La ventana se abre en el escritorio del usuario y toma el foco:
  hazlo solo cuando lo necesites, ciérrala al terminar y nunca envíes teclas al sistema
  (`SendKeys`); los controles tienen `AutomationId` para manejarlos por automatización de interfaz.
- Si añades un paquete NuGet: versión fija en `Directory.Packages.props`, licencia apta para
  distribución comercial y sin activación en línea, registrada en `docs/arquitectura.md` §7.
  Verifica la licencia vigente del paquete; no la asumas.

## 4. Qué hacer: hito A por rebanadas

Trabaja **en este orden**. Termina cada rebanada (código, pruebas en verde, documentación al día)
antes de empezar la siguiente. Si la sesión no alcanza para todas, deja las restantes sin tocar y
dilo en el reporte: es preferible entregar tres rebanadas completas que ocho a medias.

### R1 a R10 — hechas

No las rehagas. Si algo de ellas falla, corrígelo y regístralo en el reporte. **El hito A no tiene
rebanadas pendientes:** lo que sigue (hito B con la consola, hito C de instalación y lo que depende
de la FDNDA) está en `docs/plan-entrega.md`; pregunta al usuario por cuál seguir.

### R4 — Reglas por evento (RF03) — hecha

Ver `docs/hito-a-r4.md`. Queda pendiente el preset de competencia: no inventes sus valores; se carga
cuando la FDNDA los entregue.

### R5 — Respaldo y restauración (RF10) — hecha

Ver `docs/hito-a-r5.md`. Desde R7 una prueba confirma que las imágenes viajan en el respaldo.

### R6 — Resultados y exportación (RF09) — hecha

Ver `docs/hito-a-r6.md`. El PDF lo escribe `PdfReportWriter` (solo texto y líneas). R7 decidió no
poner imágenes en el PDF; si la FDNDA las pide, decide entre extender ese escritor (JPEG con
`/DCTDecode`) o incorporar PDFsharp verificando su licencia, y regístralo en arquitectura §7.

### R7 — Imágenes e identidad (RF01, RF07, RF08) — hecha

Ver `docs/hito-a-r7.md`. Queda pendiente la identidad oficial (no se entregó) y validar en el LED.
No reemplaces las imágenes de ejemplo por marcas reales que no te hayan entregado.

### R8 — Prórroga y tanda de penales (RF04, RF05) — hecha

Ver `docs/hito-a-r8.md`. Pendiente: reglas aprobadas de desempate (cuándo hay prórroga y tanda,
duraciones, lanzadores por ronda, muerte súbita); el programa no controla orden ni rondas y no debe
empezar a hacerlo sin esas reglas.

### R9 — Reproducción de cronometraje (RF11, preparación del hito B) — hecha

Ver `docs/hito-a-r9.md`. **No toques `ColoradoSystem6TimingSource`** salvo para conectarlo a un
decodificador cuando exista evidencia del protocolo: hoy no la hay. El formato normalizado no es el
protocolo de la consola; no lo presentes como tal.

### R10 — Pendientes menores del hito A — hecha

Ver `docs/hito-a-r10.md`. Lo que se pidió fue:

1. Reprogramar (fecha, etiqueta, colores y gorros) o anular un partido **programado** (no iniciado),
   con motivo y auditoría; un partido anulado no suma en resultados. Actualizar la nómina de los
   partidos aún no iniciados desde la nómina vigente del evento, por decisión explícita del operador
   (hoy quedan congelados al programar; ver requisitos §2.2 y `docs/hito-a-r3.md`). Probablemente
   necesite una migración nueva (estado `Cancelled` o similar): sigue el patrón de `0004` si cambia un `CHECK`.
2. Conservar colores y gorros del formulario de programación al pulsar *Actualizar* (`docs/hito-a-r2.md`).
3. Nombres de equipo largos recortados en la lista de equipos.
4. Probar la importación con un XLSX guardado por Excel (si no hay Excel en el equipo, dilo: no lo
   marques como verificado).

## 5. Límites que no debes cruzar

- Nada de red, telemetría, activación en línea, API HTTP, contenedores ni servicios.
- No inventes el protocolo, el puerto ni el formato del Colorado. No simules éxito en el adaptador real.
  No escribas ningún código que envíe datos a la consola. Lee `docs/colorado-system6.md`.
- No inventes reglas de competencia ni declares homologación. Si una regla no está aprobada,
  hazla configurable, márcala como pendiente y anótala en `docs/requisitos.md` §2.3.
- Sin datos personales reales. Sin DNI, fechas de nacimiento ni correos en el modelo.
- No edites `0001_inicial.sql`. No quites los disparadores que impiden borrar incidencias.
- No hagas `git push`, no publiques nada y no borres datos del usuario. Conserva los cambios ajenos.
- Fuera de alcance: video/OBS, portal público, fixture automático, inscripciones, nube, canchas
  simultáneas, otras consolas, analítica avanzada y otras disciplinas.

## 6. Cómo verificar cada rebanada

1. Pruebas automatizadas nuevas para cada criterio del RF, ejecutables sin WPF.
2. `dotnet build` sin advertencias y `dotnet test` en verde. Pega en el reporte el total real.
3. Recorre el flujo en la aplicación con datos temporales cuando la rebanada toque la interfaz.
4. Actualiza `docs/requisitos.md` (estado del RF), `docs/aceptacion.md` (criterio, evidencia,
   estado), `docs/modelo-datos.md` si cambió el esquema y `README.md` si cambió el uso.
5. Separa siempre lo **ejecutado** de lo solo escrito. Si algo no se pudo verificar, dilo y explica
   qué hace falta (segundo monitor, equipo limpio, consola, decisión de la FDNDA).

## 7. Reporte al terminar la sesión

En español y sin relleno:

- Rebanadas completadas y rebanadas no iniciadas.
- Archivos creados o cambiados, agrupados por rebanada.
- Pruebas ejecutadas con su resultado exacto; qué se comprobó en pantalla y qué no.
- Decisiones de diseño que tomaste y dónde quedaron documentadas.
- Preguntas o accesos que necesita la FDNDA para seguir.

El hito A está completo: antes de escribir código, confirma con el usuario qué sigue (hito B o C).
