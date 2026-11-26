# Reporte de sesión — Hito A, R5 (2026-11-23)

**R5 completada:** respaldo y restauración desde la aplicación (RF10), con interfaz, persistencia,
pruebas y recorrido en el ejecutable publicado en dos carpetas de datos. **R6–R9 no iniciadas.**
No hubo commits ni push.

## Qué hace

Pestaña **Resultados y respaldos** (antes deshabilitada), sección *Respaldos*:

- **Crear respaldo ahora**, lista de respaldos (fecha local, motivo, tamaño, archivo), **Abrir carpeta
  de respaldos**, **Restaurar el seleccionado…** y **Restaurar desde otro archivo…** (para un respaldo
  traído de otra computadora).
- **Respaldo automático** antes de iniciar y al finalizar cada partido, con una casilla para
  desactivarlo. Si el previo falla, la mesa pregunta si iniciar igualmente.
- **Restaurar** examina el archivo (integridad, tablas del marcador, versión de esquema, contenido),
  muestra qué trae y pide confirmación. Se impide con un partido en juego, con un archivo dañado,
  ajeno o de una versión más nueva y, en producción, con datos demo. Antes guarda la base en uso
  (`antes-de-restaurar-….db`), copia el respaldo encima con la API de backup de SQLite, migra si es
  de un esquema anterior y verifica; si algo falla, vuelve a la base anterior. Después la mesa
  recarga partidos, eventos y nóminas sin reiniciar el programa.
- La sección *Resultados* es un texto provisional hasta R6.

## Archivos

| Área | Creados o modificados |
|---|---|
| Aplicación | Nuevos `Abstractions/BackupPorts.cs` (`IBackupStore`, `BackupFile`, `BackupInspection`, `RestoreOutcome`, `RestoreFailedException`) y `Backups/BackupOperations.cs`. |
| Persistencia | Nuevo `Persistence/SqliteBackupStore.cs`. Sin migración. |
| Interfaz | Nuevos `ViewModels/BackupsViewModel.cs` y `Services/BackupDialogs.cs`. Cambiados `MesaViewModel.cs` (propiedad `Backups`, respaldos automáticos, `ReloadAfterRestoreAsync`), `Views/MesaWindow.xaml` (pestaña habilitada) y `App.xaml.cs` (composición). |
| Pruebas | Nuevos `Infrastructure.Tests/BackupRestoreTests.cs` (8) y `Desktop.Tests/BackupsViewModelTests.cs` (6). `IncidentMigrationTests.CreateVersionOneAsync` pasa a `internal` para reutilizar la base v1. |
| Recorrido | Nuevo `scripts/verificar-r5-ui.ps1`. |
| Documentación | `README.md`, `docs/requisitos.md` (RF10, §2.2), `docs/aceptacion.md` (§1.9, P13), `docs/plan-entrega.md`, `docs/arquitectura.md` (D30), `Prompt-Codex-Hito-A.md` y este reporte. Evidencia: `docs/evidencia/respaldos-*.png`. |

## Evidencia ejecutada

- Base al empezar: **229/229** (dominio 71, infraestructura 106, ViewModels 52).
- Final: compilación Release **0 errores, 0 advertencias**; **243/243** (dominio 71,
  infraestructura 114, ViewModels 58).
- `.\scripts\publicar.ps1`: pruebas en verde y paquete `artifacts\publish\win-x64` regenerado.
- `scripts\verificar-r5-ui.ps1` completo sobre el `bin` Release y sobre el paquete publicado, con dos
  carpetas de datos temporales. Detalle en [aceptacion.md §1.9](aceptacion.md).

## Decisiones

En requisitos §2.2 y arquitectura D30. Las principales:

- Restaurar copia **sobre** la base en uso con la API de backup (una transacción), no reemplaza
  archivos: atómico, sin cerrar el programa ni tocar el WAL a mano.
- Respaldo automático al iniciar y finalizar, activado por defecto, en lugar de un cuadro de
  diálogo más en cada partido.
- Los respaldos no se borran solos; los nombres llevan la hora en UTC y la lista la muestra en hora local.
- Un respaldo que trae un partido en juego sí se restaura (caso de falla del equipo a mitad de partido).

## Sin verificar

- Restaurar en **otra computadora** (P13, hito C): se probó otra carpeta de datos en el mismo equipo.
- Imágenes en el respaldo: aún no existen (R7); estarán en la misma base.
- Un respaldo en un medio de solo lectura.
- Si los datos restaurados no tienen ningún partido, el marcador público conserva el último partido
  mostrado hasta que se abra otro (la mesa sí se limpia).
- El texto del cuadro de confirmación no quedó registrado en el recorrido (sí se respondió); su
  contenido está cubierto por `Restaurar_desde_la_pestaña_recarga_la_mesa_y_el_catalogo`.

## Qué necesita la FDNDA

Cuántos respaldos conservar y dónde copiarlos al final de cada jornada (memoria USB, otro equipo).
