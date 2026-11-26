# Reporte de sesión — Hito A, R8 (2026-11-24)

**R8 completada:** prórroga y tanda de penales (RF04, RF05, RF09; parámetros en RF03), con interfaz,
pruebas y recorrido en el `bin` Release y en el paquete publicado. **Pendiente dentro de R8:** las
reglas aprobadas de prórroga y tanda (hoy son parámetros de prueba «sin aprobación técnica») y ver la
tanda a pantalla completa en un segundo monitor o en la pantalla LED. **R9 no iniciada.** No hubo
commits ni push.

## Qué hace

- **Reglas del evento:** *Periodos de prórroga* (0 = sin prórroga, por defecto) y *Duración de la
  prórroga* (3:00 por defecto, ilustrativa), junto a *Tanda de penales habilitada*. Lo que no sea el
  DEMO queda «Personalizadas del evento (sin aprobación técnica)».
- **Prórroga:** con empate al terminar el último periodo reglamentario el botón dice *Iniciar
  prórroga*; mesa y marcador muestran «PRÓRROGA 1». Sus periodos se juegan completos y sus goles
  suman al resultado. Sin empate o sin prórroga en las reglas, la mesa lo explica.
- **Tanda:** *Iniciar tanda* (con confirmación) se habilita con tanda en las reglas, todos los periodos
  jugados y empate; si no, su ayuda dice por qué. Durante la tanda F5/F8 quedan deshabilitados y F6/F9
  registran «Tanda: convertido / no convertido» del jugador elegido; cada lanzamiento se corrige y anula
  con motivo como cualquier incidencia. **Nunca suma al resultado reglamentario, a los goleadores ni a
  los totales.** No se finaliza con la tanda empatada. *Anular tanda…* (con motivo) vuelve al descanso
  si se inició por error, una vez anulados sus lanzamientos. Con tanda, no se acepta una corrección
  que desempate el reglamentario.
- **Marcador:** «PENALES» en el centro y, dentro del panel de cada equipo y bajo su resultado,
  «PENALES n» con un círculo por lanzamiento (verde = convertido, aro rojo = no). Los relojes no se
  mueven y los lanzamientos no disparan avisos animados. Al finalizar, la tanda sigue visible.
- **Mesa:** «Penales: DEL 2 – 1 TIB» bajo los relojes; historial con periodo «Pr1» o «Tanda».
- **Resultados y exportación:** resultado con «(pr.)» si hubo prórroga y columna *Penales/Tanda*
  aparte; en el resumen del partido (PDF y CSV), sección «Tanda de penales» con cada lanzamiento y
  periodos de prórroga como «Pr. 1».

## Archivos

| Área | Creados o modificados |
|---|---|
| Dominio | `Matches/MatchTypes.cs` (`MatchStage.Shootout`, `IncidentType.ShootoutAttempt`, `ShootoutStarted`/`ShootoutCancelled`), `Matches/Match.cs` (prórroga, `StartShootout`, `CancelShootout`, `ShootoutScore`, `ShootoutBlocker`, regla del empate), `Competitions/RulesetSnapshot.cs` (`OvertimePeriodCount`, `OvertimePeriodDurationSeconds`, `MaxPeriods`, `IsOvertime`). |
| Aplicación | `Matches/Commands.cs` (`StartShootout`, `CancelShootout`), `Matches/MatchOperations.cs`, `Matches/MatchSnapshot.cs` (`ShootoutSnapshot`, `ShootoutBlocker`, `IsOvertime`, reglas ampliadas), `Reports/ReportModels.cs` y `Reports/ReportDocument.cs` (tanda aparte, «(pr.)», «Pr. n»), `Catalog/CatalogOperations.cs` (guardaba las reglas sin la prórroga). |
| Infraestructura | Nueva `Persistence/Migrations/0004_prorroga_tanda.sql`; `Persistence/MigrationRunner.cs` (marca `@desactivar-claves-foraneas` y `foreign_key_check`); `SqliteCatalogRepository.cs` y `SqliteMatchRepository.cs` (columnas de prórroga). |
| Interfaz | `MesaViewModel.cs` (botones de tanda, opciones de F6/F9 según la etapa, textos, simulador con la duración de la prórroga), `MarcadorViewModel.cs` (`ShootoutMark`, tanda por equipo), `RulesEditorViewModel.cs`, `ResultsViewModel.cs`, `Items.cs` (`IncidentOption.DuringPlay/DuringShootout`), `Presentation/MatchTexts.cs`, `Presentation/RulesTexts.cs`, `Views/MesaWindow.xaml`, `Views/MarcadorWindow.xaml`, `Views/ReglasEventoWindow.xaml`. |
| Pruebas | Nuevos `Domain.Tests/OvertimeShootoutTests.cs` (25 casos), `Infrastructure.Tests/OvertimeShootoutOperationsTests.cs` (7), `Desktop.Tests/OvertimeShootoutViewModelTests.cs` (5). Cambiados `Domain.Tests/MatchFixture.cs` (ayudas de prórroga y tanda) e `Infrastructure.Tests/IncidentMigrationTests.cs` (el fixture v1 ya no depende del esquema actual). |
| Recorrido | Nuevo `scripts/verificar-r8-ui.ps1`. |
| Documentación | `README.md`, `docs/requisitos.md` (RF03, RF04, RF05, RF09, §2.2, §2.3), `docs/aceptacion.md` (§1.12, P05, P09, P10), `docs/modelo-datos.md` (esquema v4), `docs/arquitectura.md` (D37–D39), `docs/plan-entrega.md`, `Prompt-Codex-Hito-A.md` y este reporte. Evidencia: `docs/evidencia/tanda-*.png` y `prorroga-marcador.png`. |

## Evidencia ejecutada

- Base al empezar: **296/296** (dominio 86, infraestructura 140, ViewModels 70).
- Final: compilación Release **0 errores, 0 advertencias**; **333/333** (dominio 111,
  infraestructura 147, ViewModels 75).
- `.\scripts\publicar.ps1`: paquete `artifacts\publish\win-x64` regenerado.
- `scripts\verificar-r8-ui.ps1` completo sobre el `bin` Release y sobre el paquete publicado, con
  datos temporales: reglas con prórroga y tanda, 1–1, *Iniciar prórroga*, 2–2, tanda 2–1 con el
  reglamentario intacto, sin superposición con los relojes ni avisos, final, resultados con «(pr.)»
  y «penales 2 – 1». Detalle en [aceptacion.md §1.12](aceptacion.md).

## Decisiones

En requisitos §2.2 y arquitectura D37–D39. Las principales:

- **El programa impide lo incoherente, no decide el desempate:** prórroga solo con empate al terminar
  el reglamentario, tanda solo con empate tras todos los periodos, no se finaliza con la tanda
  empatada. No controla orden de lanzadores, rondas ni muerte súbita.
- **El lanzamiento es una incidencia más** (`ShootoutAttempt`), no un modelo aparte: hereda
  revisiones, idempotencia, auditoría y respaldo. `CountsAsGoal` lo excluye del resultado.
- **Salida para errores de mesa:** *Anular tanda…* con motivo, solo sin lanzamientos vigentes. Sin
  esto, un gol olvidado del cuarto periodo dejaría la mesa sin forma de corregir el resultado.
- **Migración por reconstrucción** de tres tablas (no se pueden cambiar `CHECK` en SQLite) con claves
  foráneas verificadas antes de confirmar; el respaldo previo a migración sigue ocurriendo. **La base
  real del usuario (`G:\FDNDA\Waterpolo`) pasará de v3 a v4 al abrir el programa nuevo**, con su
  respaldo `pre-migracion-v3-….db`.

## Hallazgos (corregidos)

- Guardar las reglas del evento descartaba la prórroga: `UpdateTournamentRulesAsync` normalizaba sin
  esos parámetros (lo detectó la lectura del código antes de probarlo).
- Al entrar en la tanda quedaba preseleccionada «Exclusión» (lo detectó la prueba de ViewModels).
- El botón decía «Siguiente prórroga» sin prórrogas pendientes y la columna «Per.» recortaba «Tanda»
  (lo detectó el recorrido).
- El fixture de base v1 dejó de funcionar porque el sembrador escribe columnas nuevas: ahora copia a
  v1 solo las columnas de v1.

## Sin verificar

- La tanda a pantalla completa en un segundo monitor y en la pantalla LED.
- Una tanda larga con muerte súbita en pantalla (se muestran los últimos 10 lanzamientos por equipo).
- El PDF con la sección de la tanda abierto en un lector de escritorio (el CSV sí se comprobó; el PDF
  usa el mismo documento y el escritor de R6).

## Qué necesita la FDNDA

- Reglas de desempate del evento: cuándo hay prórroga y tanda (¿solo eliminatorias?), cuántos periodos
  de prórroga y de cuánto, si hay gol de oro, y reglas de la tanda (lanzadores por ronda, orden,
  muerte súbita). Hoy son parámetros de prueba sin aprobación técnica.
- Si el acta debe mostrar la tanda de otra forma (por ejemplo, ronda por ronda).
