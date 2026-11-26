# Marcador de Waterpolo FDNDA

Programa de escritorio para Windows (`FDNDA.Waterpolo.exe`) que administra partidos de waterpolo,
registra incidencias y muestra un marcador para el público en una segunda pantalla. Todo corre en
una laptop, sin internet. En operación real, el **Colorado Time Systems System 6** es la fuente
oficial del tiempo de partido y del reloj de posesión; el programa solo lee esos datos.

**Estado: primer corte vertical + hito A completo, R1 a R9 (2026-11-24).** Funciona la demo con reloj **simulado**. La lectura del
Colorado está **pendiente de validación en hardware** y varios módulos aún no existen. El detalle
está en [docs/requisitos.md](docs/requisitos.md).

## Qué funciona hoy

- Mesa de control y marcador de audiencia en dos ventanas del mismo programa, con estado compartido.
- Partido: iniciar, descanso, siguiente periodo, finalizar y reabrir con motivo. Un partido en juego a la vez.
- Gol, exclusión, expulsión, falta personal, penal convertido/no convertido y tiempo muerto.
  Corrección y anulación con motivo e historial de revisiones; un penal convertido aporta un solo gol.
- Eventos y equipos: crear y editar; programar partidos eligiendo evento, local, visita, fecha y
  color y gorro de cada equipo para ese partido. La nómina del evento se copia al programar.
- Nóminas por evento: edición manual e importación de CSV UTF-8 o XLSX con vista previa, errores
  por fila y columna y decisión explícita en cada conflicto. Reimportar no duplica.
- Resultados en *Resultados y respaldos*: por evento (resultados, goleadores, totales por equipo) y
  por partido; exportación a PDF y CSV. Todo sale de las incidencias vigentes, igual que el marcador.
- Respaldos en *Resultados y respaldos*: crear, ver la lista, abrir la carpeta y restaurar (de la lista
  o de un archivo traído de otra computadora). Se respalda solo antes de iniciar y al finalizar cada
  partido. No se restaura con un partido en juego; antes se guarda la base actual y, si algo falla, se vuelve a ella.
- Reglas por evento (*Reglas del evento…*): periodos, duraciones, descansos, tiempos muertos,
  posesión, prórroga (periodos y duración) y tanda. Se copian al partido al iniciarlo; editar el evento no cambia partidos iniciados
  ni cerrados. Lo escrito a mano queda marcado «sin aprobación técnica»; el preset DEMO no es reglamentario.
- Identidad visual: escudo y bandera de cada equipo (PNG o JPEG, hasta 2 MB) en la mesa y el marcador;
  por evento, *Identidad y plantilla…* con título, subtítulo, colores, logo del evento, logo de la
  organización y hasta 8 auspiciadores, más una **plantilla de ejemplo del Panamericano Sub-17 2027**
  con recursos genéricos (no oficiales). Las imágenes viven en la base y viajan en el respaldo.
- Prórroga y tanda de penales: la prórroga se juega con empate al terminar los periodos; la tanda
  registra cada lanzamiento por jugador y se muestra, se guarda y se exporta **aparte** del resultado
  reglamentario (que no cambia). Ambas son configurables y quedan pendientes de reglas aprobadas.
- Avisos en el marcador de gol, exclusión y tiempo muerto, una vez por acción nueva y sin tapar los
  relojes; se desactivan desde la mesa en general o por tipo.
- Ambos relojes simulados, con el distintivo **DEMO / SIMULACIÓN** en las dos ventanas.
- **Reproducción** de una grabación de cronometraje en formato normalizado (perfil demo): distintivo
  **REPRODUCCIÓN**, avance y *Reiniciar* en la mesa. Para ensayar sin consola; no es la lectura del Colorado.
- Pérdida y reconexión de señal simuladas: los relojes pasan a `--:--` y `--` con aviso.
- Guardado en SQLite en cada operación; recuperación del partido tras un cierre forzado.
- Marcador en la pantalla elegida a pantalla completa, o en ventana si solo hay una pantalla.
- **Estado de conexiones** en *Configuración / diagnóstico*: consola, señal, cada reloj, marcador
  público, pantallas y almacenamiento en verde, ámbar o rojo, con historial de contingencias. La
  barra inferior lo resume desde cualquier pestaña (clic para ir al panel).
- Color de equipo con paleta de un clic, *Más colores…* (selector de Windows) o código escrito.
- Carpeta de datos en cualquier disco con la variable de entorno de usuario
  `FDNDA_WATERPOLO_DATOS` (por ejemplo `G:\FDNDA\Waterpolo`); el diagnóstico muestra cuál se usa.
  *Volver a detectar pantallas* relee las pantallas tras enchufar el proyector o el LED.

## Qué no está hecho

Preset de reglas de competencia (espera aprobación técnica), identidad gráfica oficial (FDNDA u organizador),
reglas aprobadas de prórroga y tanda, instalador, diseño visual final y
lectura del Colorado System 6. El plan está en [docs/plan-entrega.md](docs/plan-entrega.md).

## Levantar la demo desde cero

Requisitos de **desarrollo**: Windows 10/11 x64 y .NET SDK 10. El programa entregado no necesita SDK.

```powershell
# 1. (Solo si el equipo no tiene .NET SDK 10) instala el SDK dentro del repo, en .tools\dotnet.
#    Necesita internet; no requiere administrador ni cambia el PATH del sistema.
.\scripts\instalar-sdk-local.ps1

# 2. En cada sesión de PowerShell, prepara el entorno:
. .\scripts\entorno.ps1

# 3. Compila y prueba:
dotnet build FDNDA.Waterpolo.sln -c Release
dotnet test FDNDA.Waterpolo.sln -c Release

# 4. Abre la demo:
.\scripts\demo.ps1
```

`demo.ps1` equivale a:

```powershell
dotnet run --project src\FDNDA.Waterpolo.Desktop\FDNDA.Waterpolo.Desktop.csproj -c Release -- --perfil demo
```

Para empezar siempre con datos limpios, use una carpeta aparte y bórrela cuando quiera reiniciar:

```powershell
.\scripts\demo.ps1 -Datos C:\temp\waterpolo-demo
```

Para ensayar con una grabación de cronometraje en lugar del simulador (solo perfil demo; con el
ejecutable publicado, `FDNDA.Waterpolo.exe --perfil demo --reproduccion "archivo.csv"`):

```powershell
.\scripts\demo.ps1 -Reproduccion samples\cronometraje\guion-sintetico.csv
```

La grabación de ejemplo es **sintética** (40 s con un corte de señal y una reconexión). El formato
está descrito en `samples/README.md`. Un archivo inválido deja la fuente «no disponible» con la línea
del error; el programa no pasa al simulador.

Si PowerShell bloquea los scripts: `powershell -ExecutionPolicy Bypass -File .\scripts\demo.ps1`.

## Reproducir el flujo principal

1. **Identifíquese.** Escriba un nombre de operador y pulse *Entrar*.
2. Se abre la **Mesa de control** con «Partido demo 1» en estado PROGRAMADO.
3. **Abrir marcador.** Con dos pantallas se abre a pantalla completa en la que elija; con una, en
   ventana. *Esc* sobre el marcador lo devuelve a ventana.
4. **Iniciar partido.** Pasa a PERIODO 1 y copia las reglas del evento al partido.
5. En el panel naranja del simulador, **Iniciar reloj**: descuentan el tiempo y la posesión en ambas ventanas.
6. Elija un jugador en la nómina local y pulse **GOL local** (o `F5`; visita: `F8`). El resultado
   cambia en la mesa y en el marcador, que muestra el aviso de gol.
7. En *Historial*, seleccione el gol y abra **Corregir o anular**. Elija otro jugador, escriba un **motivo** y pulse
   **Corregir incidencia**; o escriba un motivo y pulse **Anular incidencia**. El marcador se actualiza
   sin repetir el aviso y el historial conserva cada revisión.
8. **Simular pérdida de señal.** En unos 2 s ambos relojes pasan a `--:--` y `--` con aviso en las
   dos ventanas. Se puede seguir registrando goles; quedan «sin reloj».
9. **Reconectar.** Vuelven los relojes con los valores actuales de la «consola».
10. Cierre el programa (o termínelo desde el Administrador de tareas) y ábralo de nuevo: recupera el
    partido en juego con su resultado, periodo e incidencias.
11. **Finalizar partido** y, si hace falta, **Reabrir…** con un motivo.

*Reproducir guion demo (30 s)* ejecuta solo una secuencia de juego, reset, pausa, pérdida y reconexión.

**Programar un partido nuevo:** en *Eventos y nóminas* cree un evento (*Nuevo evento*, nombre,
*Guardar evento*) y los equipos que falten (código, nombre, color `#RRGGBB` y color de gorro). En
*Partidos*, sección *Programar partido*, elija evento, local y visita; ajuste si quiere el color y el
gorro de cada equipo para ese partido, escriba una descripción y, opcionalmente, la fecha
(`14/03/2027 15:30`), y pulse **Programar partido**. Aparece en la lista; *Abrir en mesa* lo lleva a
la mesa. Cargue la nómina **antes** de programar: el partido copia la nómina del evento en ese
momento y no recibe cambios posteriores por su cuenta. Sin nómina solo se pueden registrar tiempos muertos.

**Reprogramar, actualizar nómina o anular** (solo partidos aún no iniciados): en *Partidos* elija el
partido en la lista. **Reprogramar…** lo carga en el formulario de abajo para cambiar descripción,
fecha, colores y gorros (evento y equipos no cambian) y se guarda con *Guardar reprogramación*.
**Actualizar nómina** le copia, tras confirmar, la nómina vigente de ambos equipos en el evento.
**Anular partido…** pide un motivo; el partido no se borra: queda como «Anulado», no se puede iniciar
y no cuenta en los resultados.

**Nóminas:** en *Eventos y nóminas* elija el evento y el equipo. *Agregar jugador* (nombre y gorro;
se propone el primer gorro libre), seleccione un jugador para corregirlo o *Quitar*lo. Para cargar
muchas, pulse **Importar nómina…** y elija un `.csv` (UTF-8) o `.xlsx` con las columnas
`equipo_codigo,equipo_nombre,jugador_nombre,numero_gorro` (ver `samples/`). La vista previa
muestra cada fila como *Nuevo*, *Sin cambios* o *Decidir*; los errores aparecen con fila y columna y
bloquean la importación. Nada se guarda hasta pulsar **Importar**. La importación no quita
jugadores y crea los equipos que falten (con color y gorro por definir).

Para las demás incidencias, elija **Tipo de incidencia**, seleccione un jugador del equipo y pulse
**Registrar local (F6)** o **Registrar visita (F9)**. El **tiempo muerto** solo lleva equipo, incluso
si hay un jugador seleccionado. El penal exige elegir **convertido** o **no convertido**: no registre
otro gol por el mismo penal. Para corregir, seleccione la incidencia en **Historial**, abra
**Corregir o anular** dentro de la mesa y cambie jugador/equipo o detalle
y escriba el motivo. El tipo, periodo y contexto de reloj originales se conservan; si el tipo era
incorrecto, anule y registre la incidencia correcta. No se aplican sanciones ni límites de tiempos
muertos automáticos. Las letras de acceso siguen requiriendo Alt.

**Prórroga y tanda:** primero configúrelas en *Reglas del evento…* (periodos de prórroga, su duración
y *Tanda de penales habilitada*); el partido las toma al iniciarse. Con empate al terminar el último
periodo reglamentario, el botón de siguiente periodo dice **Iniciar prórroga**. Con empate al terminar
todos los periodos, **Iniciar tanda** se habilita (si no, su ayuda dice por qué). Durante la tanda
F5/F8 quedan deshabilitados y F6/F9 registran **Tanda: convertido** o **Tanda: no convertido** del
jugador elegido; se corrigen y anulan como cualquier incidencia. No se puede finalizar con la tanda
empatada. Si la tanda se inició por error, anule sus lanzamientos y pulse **Anular tanda…** con un
motivo: el partido vuelve al descanso. El programa no controla el orden de lanzadores ni la muerte
súbita.

**Identidad (escudos, banderas, logos y auspiciadores):** en *Eventos y nóminas* elija un equipo y
use *Cargar…* junto a **Escudo** o **Bandera** (PNG o JPEG, hasta 2 MB, de 16 a 4096 px por lado); se
guarda al elegir el archivo y se ve enseguida en la mesa y el marcador. Para el título, los colores,
los logos y los auspiciadores del marcador, elija el evento y pulse **Identidad y plantilla…**;
*Usar plantilla de ejemplo* carga la del Panamericano Sub-17 2027 con imágenes genéricas de
`samples/identidad-ejemplo`. Nada se guarda hasta **Guardar identidad**. Use solo marcas que la FDNDA
o el organizador hayan entregado. En la mesa, las casillas **Animaciones: gol · exclusión · tiempo
muerto** activan o desactivan cada aviso del marcador.

La primera apertura de una base de una versión anterior crea un respaldo íntegro en `respaldos` antes
de aplicar las migraciones pendientes (0002, 0003). No abra la base migrada con el ejecutable anterior.

## Opciones de arranque

| Opción | Efecto |
|---|---|
| `--perfil demo` | (Por defecto.) Base de demostración y reloj simulado. |
| `--perfil produccion` | Base de producción, sin datos demo, y fuente Colorado. Hoy muestra los relojes sin señal y el aviso «PENDIENTE DE VALIDACIÓN EN HARDWARE». |
| `--operador "Nombre"` | Omite el diálogo de identificación. |
| `--datos "carpeta"` | Usa otra raíz de datos (pruebas y diagnóstico). |
| `--reproduccion "archivo"` | Solo con `--perfil demo`: reproduce una grabación normalizada de cronometraje en lugar del simulador. |
| `--puerto COMx` | Solo con `--perfil produccion`: puerto serie del adaptador USB a RS-232 conectado a **COM PORT 2** de la consola Colorado System 6 (115200 8-N-1, solo lectura; ver [docs/colorado-system6.md §7](docs/colorado-system6.md)). Sin esta opción, la consola figura «no disponible». |

## Herramienta de captura de la consola (hito B)

`tools/FDNDA.Waterpolo.Captura` es un programa **aparte** para diagnosticar la conexión con la consola:
**solo lee** el puerto serie (nunca escribe; DTR y RTS apagados), graba lo que llega, lo resume y lo
convierte en una grabación para `--reproduccion`. Por defecto lee COM PORT 2 de la consola (115200
8-N-1, tramas CTSWP observadas con la consola de la FDNDA, [docs/colorado-system6.md §7](docs/colorado-system6.md));
también entiende el formato de tablero descrito por terceros (§6, sin validar).
`.\scripts\publicar.ps1` la deja en `artifacts\publish\captura-win-x64\` (sin SDK).

```powershell
FDNDA.Waterpolo.Captura.exe puertos
FDNDA.Waterpolo.Captura.exe capturar --puerto COM3 --salida prueba-01.txt --nota "autorizó: …"
FDNDA.Waterpolo.Captura.exe analizar --entrada prueba-01.txt
FDNDA.Waterpolo.Captura.exe convertir --entrada prueba-01.txt --salida prueba-01.csv
```

Úsela solo con autorización y con la consola fuera de competencia. Para probarla sin consola:
`analizar --entrada samples\cronometraje\captura-sintetica-cts.txt` (captura sintética).

## Dónde quedan los datos

`%LocalAppData%\FDNDA\Waterpolo\demo` y `%LocalAppData%\FDNDA\Waterpolo\produccion`: base SQLite,
respaldos y registro de diagnóstico, separados de los binarios. La pestaña *Configuración /
diagnóstico* muestra las rutas. Para borrar la demo, cierre el programa y elimine la carpeta `demo`.

## Paquete para distribuir

```powershell
.\scripts\publicar.ps1
```

Ejecuta las pruebas y genera `artifacts\publish\win-x64\` con `FDNDA.Waterpolo.exe` y el runtime
incluido (self-contained, unos 135 MB). Equivale a:

```powershell
dotnet publish src/FDNDA.Waterpolo.Desktop/FDNDA.Waterpolo.Desktop.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
```

Esa carpeta se puede copiar a otro equipo y abrir por doble clic. El instalador
`FDNDA-Waterpolo-Setup.exe` aún no existe (hito C). El ejecutable no está firmado digitalmente.

## Estructura

```
FDNDA.Waterpolo.sln
src/
  FDNDA.Waterpolo.Domain/           modelo y reglas del partido
  FDNDA.Waterpolo.Application/      casos de uso, snapshots, puertos, vigencia de relojes
  FDNDA.Waterpolo.Infrastructure/   SQLite, simulador, adaptador Colorado (pendiente), log
  FDNDA.Waterpolo.Desktop/          WPF: mesa, marcador y composición
tools/
  FDNDA.Waterpolo.Captura/          captura de solo lectura de la consola (hito B; no va con la mesa)
tests/
  FDNDA.Waterpolo.Domain.Tests/
  FDNDA.Waterpolo.Infrastructure.Tests/
  FDNDA.Waterpolo.Desktop.Tests/    ViewModels sin abrir ventanas
docs/                               requisitos, arquitectura, modelo, Colorado, plan, aceptación
samples/                            nóminas (ficticias), identidad genérica y cronometraje sintético
scripts/                            entorno, SDK local, demo, publicación y recorridos de interfaz
```

## Documentación

- [Requisitos](docs/requisitos.md) — RF01–RF12 con estado, decisiones y pendientes.
- [Arquitectura](docs/arquitectura.md) — C4, secuencias, decisiones y licencias.
- [Modelo de datos](docs/modelo-datos.md)
- [Colorado System 6](docs/colorado-system6.md) — evidencia, checklist de hardware y plan de prueba.
- [Plan de entregas](docs/plan-entrega.md) — hitos A–D y decisiones que necesita la FDNDA.
- [Aceptación](docs/aceptacion.md) — qué se verificó y qué falta, por nivel de prueba.
