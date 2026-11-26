# Reporte de sesión — Hito A, R1 (2026-11-12)

**R1 completada:** registro, corrección y anulación de goles, exclusión/expulsión, falta personal,
penal con resultado y tiempo muerto, con persistencia, mesa y pruebas. **R2–R9 no iniciadas.**
RF05 sigue parcial por la tanda de R8; resúmenes estadísticos de exclusiones/tiempos muertos y
animaciones corresponden a R6/R7. No se generó un paquete publicado, no hubo commits ni push.

## Archivos de R1

| Área | Creados o modificados |
|---|---|
| Dominio | `src/FDNDA.Waterpolo.Domain/Matches/Match.cs`, `MatchIncident.cs`, `MatchTypes.cs`; `Statistics/MatchStatistics.cs`. |
| Aplicación | `src/FDNDA.Waterpolo.Application/Matches/Commands.cs`, `MatchOperations.cs`, `MatchSnapshot.cs`, `MatchSnapshotHub.cs`. |
| Persistencia | `src/FDNDA.Waterpolo.Infrastructure/Persistence/SqliteMatchRepository.cs`; nueva `Migrations/0002_detalles_incidencias.sql`. `0001` y sus disparadores se conservaron. |
| Mesa | `src/FDNDA.Waterpolo.Desktop/ViewModels/MesaViewModel.cs`, `Items.cs`; `Presentation/MatchTexts.cs`; `Views/MesaWindow.xaml`. El marcador recibe el resultado desde el snapshot existente. |
| Pruebas nuevas | `tests/FDNDA.Waterpolo.Domain.Tests/IncidentTests.cs`; `tests/FDNDA.Waterpolo.Infrastructure.Tests/IncidentOperationsTests.cs`, `IncidentMigrationTests.cs`; nuevo proyecto `tests/FDNDA.Waterpolo.Desktop.Tests` con `MesaIncidentTests.cs`, añadido a `FDNDA.Waterpolo.sln`. |
| Recorrido de interfaz | Nuevo `scripts/verificar-r1-ui.ps1`: datos nuevos dentro de artifacts, automatización por controles sin SendKeys, comprobación de visibilidad/desplazamiento, capturas y cierre del proceso. |
| Documentación | `README.md`, `docs/requisitos.md`, `docs/aceptacion.md`, `docs/modelo-datos.md`, `docs/arquitectura.md`, `docs/plan-entrega.md` y este reporte. |

## Evidencia ejecutada

- Base antes del cambio: **74/74** (dominio 29, infraestructura 45).
- Compilación Release final: **0 errores, 0 advertencias**.
- Pruebas finales: **124/124**, **0 fallos, 0 omitidas** (dominio **54**, infraestructura **62**,
  ViewModels **8**). Dominio/SQLite sin WPF; ViewModels Windows sin abrir ventanas.
- Migración probada de una base creada con el SQL 0001, con dos partidos y un gol corregido:
  respaldo íntegro aún v1 antes del cambio, base v2 con el mismo resultado e historial. Si no
  puede crear el respaldo no migra. No se editó una migración publicada.
- La restauración con fuentes NuGet remotas falló por NU1301; se restauró desde la caché local,
  sin incorporar dependencias ni cambiar versiones. Compilación/pruebas con `--no-restore`.

Comandos finales ejecutados, con SDK local 10.0.404:

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
. .\scripts\entorno.ps1
dotnet build FDNDA.Waterpolo.sln -c Release --no-restore
dotnet test FDNDA.Waterpolo.sln -c Release --no-build --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verificar-r1-ui.ps1
```

**Interfaz ejecutada:** Windows 11, 1920×1080 al 125 %, una pantalla, ejecutable Release de `bin`
y datos temporales. Se registraron, corrigieron y anularon las seis opciones nuevas; penal
convertido/no convertido; tiempo muerto sin jugador con cambio de equipo; bloqueo de falta sin
jugador; pérdida de señal y falta «SIN reloj»; resultado final 1–0 en ambas ventanas. El proceso
se cerró al terminar. Las comprobaciones se hacen por AutomationId y patrones UI Automation,
incluido desplazamiento cuando un control queda fuera de vista.

Evidencia local (artefactos ignorados por Git):

- [Registro del recorrido](../artifacts/verificacion-r1-8d9b792ddc8f4284955fb1a4e871f160/recorrido.txt).
- [Mesa con historial](../artifacts/verificacion-r1-8d9b792ddc8f4284955fb1a4e871f160/mesa-r1.png).
- [Corrección con historial de revisiones](../artifacts/verificacion-r1-8d9b792ddc8f4284955fb1a4e871f160/correccion-r1.png).
- [Marcador del público](../artifacts/verificacion-r1-8d9b792ddc8f4284955fb1a4e871f160/publico-r1.png).

La primera ejecución aislada no permitió capturas del escritorio. El recorrido final se ejecutó
en el escritorio interactivo con datos nuevos. Las capturas revelaron falta de espacio para la
corrección; se resolvió alternando historial y edición dentro de la mesa (H2 de aceptación).

**No verificado:** teclas físicas F5/F8, F6/F9 o Alt+letra; segunda pantalla/LED; otras escalas y
resoluciones; equipo limpio sin red; instalador; latencia; Colorado. Las animaciones nuevas de
exclusión/tiempo muerto siguen pendientes de R7. No se declara homologación.

## Decisiones

- Penal convertido = una sola incidencia que aporta uno al marcador y a goleadores. Resultado y
  detalle de exclusión/expulsión se guardan en cada revisión. No se genera otro gol.
- Tipo, periodo y reloj originales son inmutables. Si se eligió otro tipo por error, se anula y se
  registra la incidencia correcta. No se inventan tiempos retrospectivos.
- No se aplican sanciones por acumulación ni límites automáticos de tiempos muertos.
- Un selector común elige el tipo; F6/F9 registran para local/visita. F5/F8 conservan el gol directo.
  Corrección y anulación usan un formulario dentro de la mesa, con motivo obligatorio.
- La migración 0002 queda destinada a incidencias; imágenes necesitarán una migración posterior.

Estas decisiones están en requisitos §2.2, arquitectura D17–D19 y modelo de datos versión 2.

## Para seguir

La siguiente rebanada es **R2: eventos, equipos y programación de partidos**. No necesita una
respuesta de la FDNDA para empezar. Para etapas posteriores siguen pendientes las reglas de
competencia y tratamiento de sanciones (área técnica), identidad oficial (si se desea usarla),
equipo/LED de producción y acceso autorizado/documentación de la consola Colorado.
