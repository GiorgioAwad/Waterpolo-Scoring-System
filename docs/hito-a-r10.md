# Reporte de sesión — Hito A, R10 (2026-11-25)

**R10 completada:** los cuatro pendientes menores del hito A, con pruebas, migración, interfaz y
recorrido en el ejecutable. Con esto el hito A no tiene pendientes propios; lo que queda depende de la
FDNDA (preset de competencia, reglas de incidencias, identidad oficial) o del hito C (LED, segundo
monitor). No hubo commits ni push.

## Qué hace

- **Reprogramar** un partido aún no iniciado: en la pestaña *Partidos*, *Reprogramar…* carga el partido
  en el formulario de abajo («Reprogramar «…»», botón *Guardar reprogramación* y *Cancelar*). Cambian
  descripción, fecha (puede quedar vacía), color y gorro de cada equipo. **Evento y equipos quedan
  bloqueados**: para cambiar un equipo se anula y se programa otro. Si es el partido abierto en la
  mesa, la mesa y el marcador se actualizan sin animaciones; si no, no cambian.
- **Actualizar nómina**: copia al partido no iniciado la nómina vigente de sus dos equipos en el evento,
  tras confirmar. Un jugador que sigue en la nómina conserva su identificador. Si ya coincide, se
  rechaza sin escribir («La nómina del partido ya coincide con la del evento.»). La vista previa de
  importación y la pestaña de nóminas ahora indican esta acción.
- **Anular partido…** con motivo obligatorio (diálogo de motivo). El partido **no se borra**: queda como
  «Anulado» en la lista, con operador, hora y motivo en su historial; no se puede iniciar, reprogramar
  ni volver a anular; no cuenta en los partidos del evento, ni en resultados, goleadores o totales; el
  resumen del evento lo lista como «Anulado» y lo menciona aparte. En el marcador, «PARTIDO ANULADO».
- Las tres acciones quedan registradas en el historial del partido (`Rescheduled`, `RosterReplaced`,
  `Cancelled`) con operador y hora, y son idempotentes por `CommandId`. Un partido iniciado, finalizado o
  anulado las rechaza en el dominio, y la base impide borrar la nómina de un partido que no esté
  programado.
- **Actualizar** en *Eventos y nóminas* ya no borra los colores y gorros escritos en el formulario de
  programación (las listas se recargan y antes se reponían los del catálogo).
- **Nombres largos** en la lista de equipos: se recortan con «…» y el nombre completo sale en la ayuda
  emergente (antes se cortaban sin aviso).
- **XLSX guardado por Excel**: `samples/plantilla-nomina-excel.xlsx` lo guardó Microsoft Excel 16 de este
  equipo (cadenas compartidas, tema, estilos, extensiones `x14ac`/`xr`, cabecera en negrita) con la misma
  nómina ficticia de la plantilla; el lector propio lo lee igual que la CSV. Se guardó con «quitar
  información personal» (sin autor).

## Archivos

| Área | Creados o modificados |
|---|---|
| Dominio | `Matches/MatchTypes.cs` (`MatchStatus.Cancelled`; cambios `Rescheduled`, `RosterReplaced`, `Cancelled`), `Matches/Match.cs` (`Reschedule`, `ReplaceRoster`, `Cancel`, cabecera mutable antes de iniciar, `PendingChanges.HeaderChanged`/`RosterReplaced`). |
| Aplicación | `Matches/Commands.cs` (`RescheduleMatch`, `RefreshMatchRoster`, `CancelMatch`), `Matches/MatchOperations.cs` (tres casos de uso; publican solo si el partido es el abierto), `Matches/MatchSnapshotHub.cs` (`PreMatchChanged`, `CurrentMatchId`), `Abstractions/Ports.cs` (`MatchSummary.TournamentId`), `Reports/ReportModels.cs` y `ReportDocument.cs` (anulado), `Catalog/CatalogOperations.cs` (aviso), `Abstractions/CatalogPorts.cs`. |
| Infraestructura | Nueva `Persistence/Migrations/0005_reprogramar_anular.sql`; `SqliteMatchRepository.cs` (actualiza cabecera, reemplaza la nómina en la misma transacción, lista con evento), `SqliteCatalogRepository.cs` (el anulado no cuenta). |
| Interfaz | `ViewModels/CatalogViewModel.cs` (modo reprogramación, acciones, `Operator`, `Queries`, colores al *Actualizar*), `ViewModels/MesaViewModel.cs`, `ViewModels/Items.cs`, `ViewModels/MarcadorViewModel.cs`, `ViewModels/RosterImportViewModel.cs` (`AskReason`), `Presentation/MatchTexts.cs`, `Services/CatalogDialogs.cs`, `App.xaml.cs`, `Views/MesaWindow.xaml`. |
| Muestras y scripts | Nuevos `samples/plantilla-nomina-excel.xlsx` y `scripts/verificar-r10-ui.ps1`; `samples/README.md`. |
| Pruebas | Nuevos `Domain.Tests/RescheduleTests.cs` (10), `Infrastructure.Tests/RescheduleOperationsTests.cs` (10), `Desktop.Tests/RescheduleViewModelTests.cs` (7); `RosterImportTests.cs` (+1 y una aserción). |
| Documentación | `docs/requisitos.md`, `docs/aceptacion.md` (§1.18, P19), `docs/modelo-datos.md`, `docs/arquitectura.md` (D44), `docs/plan-entrega.md`, `Prompt-Codex-Hito-A.md` y este reporte. |

## Evidencia ejecutada

- Base al empezar: **460/460**. Final: compilación Release **0 errores, 0 advertencias**;
  **488/488** (dominio 121, infraestructura 249, ViewModels 118).
- `.\scripts\publicar.ps1`: paquete `artifacts\publish\win-x64` regenerado (488/488 en su paso de pruebas).
- `scripts\verificar-r10-ui.ps1` completo sobre el `bin` Release y sobre el paquete publicado, con datos temporales: reprogramar el
  partido demo 2 (equipos bloqueados, nueva fecha 20/03/2027 18:00 en la lista, formulario de vuelta a
  «Programar partido»); *Actualizar nómina* sin cambios rechazada con su mensaje; anular con motivo
  («Anulado», acciones deshabilitadas); colores escritos conservados tras *Actualizar*; equipo de 80
  caracteres recortado con «…». Capturas en `artifacts\verificacion-r10-…` (no versionadas).
- Migración 0005 aplicada sobre una base v1 con un partido en juego: datos, índices y disparadores
  intactos, partido programado anulable, nómina del partido en juego protegida.

## Decisiones

En arquitectura D44 y modelo de datos:

- **Anular no borra.** Estado nuevo `Cancelled` con motivo, como pide la regla de solo inserción del
  historial. El partido queda como registro; no hay «desanular» (se programa otro).
- **Reprogramar no cambia equipos ni evento.** La nómina, los nombres congelados y el evento dependen de
  ellos; cambiar un equipo es otro partido.
- **Actualizar la nómina es una decisión explícita del operador, por partido**, no automática al
  importar: así ningún partido cambia sin que alguien lo pida. Solo antes de iniciar, cuando no hay
  incidencias que la nombren.
- **Publicar solo si es el partido abierto**: reprogramar otro partido no debe cambiar lo que ve el
  público.

## No verificado

- Pulsación física de teclas y otras resoluciones (como en las rebanadas anteriores).
- Al abrir el paquete nuevo con los datos reales (`G:\FDNDA\Waterpolo`), la base se migra a la versión 5;
  el programa crea antes el respaldo previo a migración, como en 0002–0004. No se abrió aquí con esos datos.
