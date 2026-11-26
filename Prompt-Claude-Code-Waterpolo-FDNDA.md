# Prompt para Claude Code: Marcador de Waterpolo FDNDA

Actúa como arquitecto de software y desarrollador senior de aplicaciones de escritorio. Trabaja en el repositorio abierto. Quiero que dejes el producto **mapeado y con un primer corte funcional**, con código y pruebas. Empieza leyendo `CLAUDE.md`, `AGENTS.md`, README, solución, estado de Git y estructura si existen. Conserva cambios ajenos. Si el repositorio está vacío, inicializa la solución. No publiques, no hagas push y no uses datos personales reales como ejemplos.

## 1. Producto y alcance

Construye **Marcador de Waterpolo FDNDA**, un programa instalable para Windows, con ejecutable `FDNDA.Waterpolo.exe`. La primera entrega apunta a **Windows 11 x64**; confirma el equipo de producción en la puesta en marcha. Todo se ejecuta en una laptop maestra. El operador usa una **ventana de mesa** y abre una **ventana de marcador a pantalla completa** en un segundo monitor o pantalla LED conectada como escritorio extendido. El marcador es de solo lectura. Mesa y marcador comparten estado en memoria dentro del mismo proceso y operan sin internet.

La aplicación administra eventos, equipos, jugadores y partidos; registra incidencias; muestra un marcador gráfico; genera resultados; guarda y restaura datos localmente. En operación real, el **Colorado Time Systems System 6** es la fuente oficial del tiempo de partido y del reloj de posesión (*shot clock*). El software lee esos datos y nunca controla la consola. El resultado que muestra la aplicación y sus estadísticas se derivan de las incidencias que registra el operador.

La FDNDA debe poder usar la misma aplicación en sus torneos y en el **Panamericano de Waterpolo Sub-17 de 2027**. Prepara una plantilla configurable para ese evento, con selecciones, banderas y auspiciadores. Sus reglas y necesidades de visualización se confirman con el área técnica y el organizador. No declares homologación ni cumplimiento de reglas no verificadas. La propuesta comercial contempla licencia perpetua institucional de la versión entregada: sin suscripción, activación en línea, caducidad, cuentas en la nube ni cobros por evento. El repositorio fuente es del desarrollador y no se empaqueta para el usuario final.

Fuera de esta versión: transmisión de video u OBS, portal público, fixture generado automáticamente, inscripciones/pagos, nube y sincronización, canchas simultáneas, equipos remotos, integración con otras consolas o plataformas oficiales, telemetría externa, control del Colorado y analítica avanzada. No añadas funciones para otras disciplinas.

## 2. Arquitectura obligatoria

Usa **monolito modular de escritorio**, con **Clean Architecture** para el sentido de las dependencias, **MVVM** en WPF, **puertos y adaptadores** para cronometraje y almacenamiento, y **DDD táctico ligero** para el dominio del partido. Estas decisiones se complementan: monolito define el despliegue; Clean Architecture define dependencias; DDD modela las reglas de negocio; MVVM organiza las ventanas.

- **Stack:** C# con .NET 10 LTS, WPF/XAML, CommunityToolkit.Mvvm, Microsoft.Data.Sqlite con SQL parametrizado y migraciones versionadas, xUnit. Usa versiones fijas y compatibles. Para Excel y PDF selecciona librerías con licencia apta para distribución comercial, documenta versión y licencia y evita dependencias con activación en línea. Excel debe poder importarse sin Microsoft Office instalado.
- **Domain** (`net10.0`): modelo, valores e invariantes. Agregado `Match` para estado e incidencias; valores como `CapNumber`, `PeriodNumber` y `RulesetSnapshot` cuando protejan una regla. Módulos lógicos: competiciones y plantillas, operación de partidos y estadísticas. El dominio no conoce UI, puertos COM, SQLite ni librerías de archivos.
- **Application** (`net10.0`): casos de uso, DTO, puertos de persistencia, reloj y publicación de snapshots. Importar plantilla, programar partido, iniciar/cerrar/reabrir, registrar/corregir/anular incidencia, proyectar marcador y estadísticas, respaldar/restaurar. Implementa interfaces en infraestructura. Separa comandos que cambian estado de lecturas, sin agregar un bus ni CQRS distribuido.
- **Infrastructure** (`net10.0`): SQLite, importación/exportación, reloj simulado y reproducción. Aísla la pieza que dependa de Windows o hardware si lo requiere la implementación. Inicializa el proveedor y aplica migraciones una vez antes de habilitar la mesa. Implementa repositorios concretos y transacciones; no añadas repositorio genérico.
- **Desktop** (`net10.0-windows`): WPF, ventanas, ViewModels, navegación, ciclo de vida y composición de dependencias. El punto de arranque registra las implementaciones. Los ViewModels invocan casos de uso y observan snapshots inmutables; nunca escriben SQL, leen hardware ni recalculan el marcador por su cuenta. Despacha cambios de UI al hilo de WPF.
- **Dependencias permitidas:** `Application → Domain`; `Infrastructure → Application y Domain`; `Desktop → Application`, e `Infrastructure` desde el punto de composición. Tests de dominio y de SQLite ejecutables sin WPF. Un proceso, un escritor de partido y una base local. Sin API HTTP, servidor de base de datos, contenedores, microservicios ni intermediarios de mensajería.

Estructura sugerida (respeta la existente si es equivalente):

    FDNDA.Waterpolo.sln
    src/
      FDNDA.Waterpolo.Domain/
      FDNDA.Waterpolo.Application/
      FDNDA.Waterpolo.Infrastructure/
      FDNDA.Waterpolo.Desktop/
    tests/
      FDNDA.Waterpolo.Domain.Tests/
      FDNDA.Waterpolo.Infrastructure.Tests/
    docs/
      requisitos.md
      arquitectura.md
      modelo-datos.md
      colorado-system6.md
      plan-entrega.md
      aceptacion.md
    samples/
    scripts/

En `docs/arquitectura.md`, incluye C4 de contexto y contenedores: actores, consola externa, aplicación de escritorio y SQLite embebido. Las dos ventanas son componentes del mismo ejecutable; SQLite no es un servicio. Agrega una secuencia breve de «registro de gol → commit → nuevo marcador» y otra de «Colorado → lectura → pérdida de señal → reconexión». Documenta las decisiones y sus motivos.

## 3. Requisitos funcionales

Cada RF debe aparecer en `docs/requisitos.md` con prioridad, fase, criterio verificable y estado. Distingue reglas confirmadas, decisiones de diseño y pendientes de validación.

| ID | Requisito | Criterio verificable |
|---|---|---|
| RF01 | Eventos y equipos: crear/editar evento, nombre, equipos o selecciones, colores, escudos, banderas, auspiciadores y encuentros. Asignar local/visita y colores/gorros de cada equipo por partido. | Se guardan dos equipos distintos y se programa un encuentro para el evento correcto. |
| RF02 | Registro e importación de nóminas desde CSV UTF-8 y XLSX. Plantilla con columnas `equipo_codigo,equipo_nombre,jugador_nombre,numero_gorro`. Edición manual. Vista previa con filas válidas, errores y conflictos. | Reporta fila y columna de errores; valida requeridos y gorros duplicados dentro del mismo equipo. El mismo gorro en equipos distintos es válido. Cancelar no escribe. Confirmar importa en una transacción. Reimportar la misma plantilla no duplica. Cambios ambiguos requieren resolución explícita; no deduzcas identidad de un jugador por su nombre. |
| RF03 | Configurar reglas por evento y copiarlas al partido al iniciarlo: cantidad/duración de periodos, descansos, tiempos muertos, parámetros de posesión para simulación y desempate por penales cuando corresponda. Preset demo identificado; preset de competencia pendiente de aprobación técnica. | Cambiar configuración del evento no altera un partido iniciado o cerrado. Los parámetros locales no cambian los relojes del Colorado. |
| RF04 | Operación en mesa: programado → en juego → finalizado. Cambiar periodo y distinguir pausas/descanso de la etapa del partido; pausa del reloj se toma de la fuente cuando está disponible. Un partido activo a la vez. Identificar al operador al abrir sesión local; reabrir un cierre con motivo registrado. | Tras reiniciar se recupera el partido y el último estado confirmado. Reapertura y cambios quedan atribuidos. Identificación local no equivale a autenticación fuerte. |
| RF05 | Registrar gol y jugador, exclusión/expulsión, falta personal, penal (resultado cuando aplique) y tiempo muerto. Anotar equipo, jugador cuando corresponda, periodo, contexto de reloj, hora y operador. Desempate por penales separado del marcador reglamentario. | Se rechaza un jugador ajeno a la nómina del partido. Un penal convertido en juego suma una sola vez; la tanda se muestra y exporta por separado. No automatices sanciones ni decisiones arbitrales sin reglas aprobadas. |
| RF06 | Corregir o anular una incidencia con motivo, responsable, fecha, valor anterior y posterior. Mantener historial de revisiones y derivar marcador/estadísticas de la última revisión válida de cada incidencia. | Corregir goleador actualiza su estadística sin duplicar goles. Anular un gol resta exactamente uno y el historial original permanece. No hay borrado físico de incidencias confirmadas. |
| RF07 | Marcador de audiencia separado: nombres, logos/banderas, color de equipo, resultado, periodo, reloj de partido, reloj de posesión y avisos. Mostrar presentación previa del encuentro y resultado final. Ventana seleccionable por monitor, pantalla completa sin bordes y retorno seguro a ventana si se desconecta el monitor. | El público ve solo el marcador; la mesa sigue operable. Abrir/reabrir pantalla no reinicia partido ni vuelve a reproducir animaciones pasadas. |
| RF08 | Animaciones locales de gol, exclusión y tiempo muerto. Disparar solo después de confirmar la escritura y una vez por acción nueva. Desactivar animaciones desde mesa. Plantilla visual configurable con nombre del torneo, identidad FDNDA, selecciones, banderas y auspiciadores. | Las animaciones no tapan los relojes ni bloquean la mesa. Corregir/anular actualiza datos sin celebrar de nuevo; reabrir la pantalla no repite animaciones. |
| RF09 | Resumen por partido y evento de resultados, goles, goleadores, exclusiones y tiempos muertos, a partir de las incidencias efectivas. Exportar PDF y CSV localmente. | Totales y goleadores coinciden con el marcador e historial después de correcciones. PDF y CSV abren fuera del programa y distinguen resultado reglamentario de tanda. |
| RF10 | Guardado local automático, recuperación de partido, respaldo verificable y restauración desde la app. | Cerrar por fuerza tras una operación confirmada y reabrir no pierde la operación. Restaurar un respaldo en otra instalación conserva partidos, incidencias, imágenes y configuración incluida. |
| RF11 | Leer **ambos relojes** del Colorado System 6 y reflejarlos en mesa y público. Alertar ante pérdida de señal, manejar reconexión y ofrecer modo demo separado. | Se comprueba con la consola real inicio/pausa, reinicio del reloj de partido y reset del shot clock. Tras desconectar, aparece aviso y ambos tiempos afectados dejan de mostrarse como vigentes; la recuperación usa datos nuevos válidos. |
| RF12 | Distribuir un programa Windows ejecutable con instalador, instrucciones, diagnóstico local y datos demo separados. | En una laptop Windows compatible y sin SDK ni conexión a internet, se puede instalar, abrir por doble clic, operar el demo, proyectar y exportar. Actualizar el programa no borra datos de usuario. |

### Modelo y persistencia

Modelo mínimo: `Tournament`, `Team`, `Player`, `TournamentRosterEntry`, `Match`, `MatchRosterEntry`, `RulesetSnapshot`, `MatchIncident`, `IncidentRevision`, `OperatorProfile`, `MatchStatusChange`, `EventBranding` y `Asset`. Congela nombres, números y reglas usados en el partido para preservar el historial cuando cambie el catálogo. Guarda assets locales acotados en SQLite o justifica otro esquema que garantice un respaldo coherente. No incluyas DNI, fecha de nacimiento, correo ni otra información que el marcador no necesita.

En una transacción valida estado y versión esperada, persiste incidencia/revisión y auditoría, y confirma. Solo después del commit publica el nuevo `MatchSnapshot`; si falla, conserva el marcador anterior y explica el error en la mesa. Usa un ID idempotente por comando para reintentos y bloquea doble envío accidental en UI. Mantén una secuencia de incidencias por partido; horas en UTC y presentación `es-PE`. El contexto de reloj puede ser desconocido; persiste su origen/calidad, nunca inventes una hora de juego oficial. Al arrancar deriva el resultado de los datos persistidos; no uses una proyección vieja como fuente de verdad.

SQLite en una ruta por usuario de Windows bajo `%LocalAppData%\FDNDA\Waterpolo`; binarios en ubicación distinta. Usa claves foráneas, restricciones y transacciones. Separa demo de producción por base de datos. Impide abrir dos instancias operadoras sobre los mismos datos. Si no se puede escribir por disco lleno, permisos o archivo dañado, el programa no debe reportar éxito ni cambiar el tanteo. Respalda antes de migrar y ofrece respaldo previo al partido y al cierre. Usa la API de backup de SQLite para una copia consistente, no copies un archivo activo ignorando WAL. Valida integridad y versión antes de restaurar; realiza la restauración con las conexiones cerradas, respaldo previo y retorno a la base anterior si falla. Impide restaurar en pleno partido.

### Interfaz de mesa y de público

UI en español. Mesa: **Eventos y nóminas**, **Partidos**, **Mesa de control**, **Resultados y respaldos**, **Configuración/diagnóstico**. En mesa muestra equipos lado a lado, resultado, periodo, relojes, fuente y estado de señal. Acciones accesibles por teclado y ratón, con etiquetas claras y corrección accesible sin navegar fuera del partido. Solicita motivo en corrección, anulación y reapertura; no pongas un modal innecesario en cada gol.

Público: formato 16:9 como base, escalado al área útil, márgenes seguros configurables, números grandes y alto contraste. Prueba al menos 1280×720 y 1920×1080, nombres largos y manejo de una resolución de LED que se confirmará con el equipo real. No muestres datos de operación, controles ni logs en la pantalla pública. No dependas de fuentes web o imágenes remotas. Entrega assets demo originales o genéricos; usa marca oficial solo si te la entrego.

## 4. Colorado System 6: diseño y verificación

Define `ITimingSource` con inicio, detención y un flujo cancelable de snapshots, independiente de WPF y SQLite. `TimingSnapshot`: fuente, identificador de sesión, tiempos restantes de partido y posesión **por separado** (milisegundos enteros y opcionales), precisión disponible, estado de marcha si existe evidencia, periodo si está disponible, número de secuencia interno, instante de recepción y calidad. No des por hecho que el equipo entrega todos esos campos ni que dos valores se reciben en la misma trama. Si llegan por separado, conserva validez y frescura por reloj. Usa un reloj monotónico para medir latencia/antigüedad y la hora UTC para auditoría.

Implementa primero `SimulatedTimingSource` con guion reproducible de juego/pausa/reset/pérdida/reconexión y marca visible **DEMO / SIMULACIÓN** en ambas ventanas. Planea `ReplayTimingSource` para reproducir fixtures normalizados y luego capturas autorizadas, marcado como **REPRODUCCIÓN**. El programa arranca en la fuente configurada; en competencia no puede pasar a demo ni a un reloj manual por sí solo. Si falta un reloj o no está vigente, muestra `--:--` o `--` con señal no disponible, en lugar de aparentar continuidad. No extrapoles tiempos en vivo en esta primera versión. El tanteo puede seguir registrándose con advertencia y contexto temporal no confirmado. Al reconectar, acepta datos válidos nuevos de la sesión vigente y descarta lecturas antiguas; no borres el partido ni reanudes a partir de un valor simulado.

Diseña `ColoradoSystem6TimingSource` como adaptador **de lectura** con transporte separado del decodificador. Antes de implementar la conexión, documenta el equipo exacto, número de serie, firmware, versión del módulo de waterpolo, puertos disponibles, salida habilitada, protocolo, cables/adaptadores y si requiere documentación o licencia de terceros. Consulta las fuentes oficiales:

- [Ficha System 6 Water Polo](https://coloradotime.com/products/system-6-water-polo).
- [Manual oficial F872](https://coloradotime.com/hubfs/CTS%20Website%20%20Assets/Manuals/Swim%20Timing%20Components/System6/System_6_Water_Polo_Manual_F872.pdf?hsLang=en).

La ficha advierte diferencias de soporte de COM2 según la versión de hardware; no presupongas RS-232, COM2, USB, baud rate, mensajes, handshakes ni acceso simultáneo a ambos relojes. Verifica con la consola y documentación disponible la ruta de datos que permite **recibir** ambos relojes sin interferir con los dispositivos oficiales. No envíes comandos ni hagas pruebas activas en una consola en uso. Si la interfaz disponible requiere enviar comandos, registrar esa limitación y escalarla; no la implementes bajo el supuesto de solo lectura. Si no hay evidencia, deja el modo Colorado como **PENDIENTE DE VALIDACIÓN EN HARDWARE**, con un resultado explícito de no disponible. No simules éxito en el adaptador real.

Una vez obtenido el acceso autorizado, captura una muestra acotada con valores observados en la consola: reposo y pausa prolongada, inicio, fin de periodo, reset de posesión, reloj de posesión apagado si aplica, desconexión y reconexión. Valida si se emite continuamente o solo cuando hay cambios; diferencia un reloj detenido de una conexión perdida con las señales que existan. Si no se puede detectar pérdida de forma fiable, documéntalo como bloqueo de aceptación. Fija umbrales y frecuencia de visualización con medidas reales. Prepara pruebas con tramas válidas, fragmentadas, concatenadas, inválidas y fuera de rango según el protocolo confirmado. Usa un registro de diagnóstico local, acotado y sin datos personales. El loop de lectura/reconexión no bloquea la UI; solo publica muestras válidas y limpia sus recursos al cerrar.

## 5. Requisitos de calidad y entrega

- **Offline:** el programa instalado funciona con Wi-Fi apagado. Sin llamadas externas, CDN, servidores ni comprobación de licencia. La descarga de paquetes es un paso de desarrollo/compilación; no un requisito de uso.
- **Respuesta:** actualización del tanteo y del público en menos de 200 ms desde commit como objetivo provisional en la laptop objetivo. Mide la demora de relojes por separado en hardware; no declares un SLA antes del ensayo. Aísla importación, backup y exportación del hilo de UI.
- **Datos y archivos:** límites razonables en peso/dimensiones de imágenes y cantidad de filas; valida formato antes de importar. Usa SQL parametrizado, neutraliza fórmulas al exportar campos de texto no confiables a CSV y no uses archivos XLSM ni macros. Incluye mensajes de error en español y log rotado local sin nóminas.
- **Distribución:** entrega publicable `win-x64` self-contained (runtime incluido), sin depender de que el operador tenga .NET SDK, Node o Visual Studio. Usa como base la carpeta publicada con su `.exe` y dependencias; un único binario físico no es requisito. Prepara un instalador offline `FDNDA-Waterpolo-Setup.exe` después de validar el paquete portátil. Conserva datos al actualizar, agrega acceso directo, documenta desinstalación y no afirmes firma digital si no existe certificado. Mantén versiones de release y lista de licencias de dependencias distribuidas.

Comandos base (ajusta al nombre real de los proyectos y documenta comandos verificados):

    dotnet restore
    dotnet build -c Release
    dotnet test -c Release
    dotnet publish src/FDNDA.Waterpolo.Desktop/FDNDA.Waterpolo.Desktop.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64

Configura el nombre de ensamblado para generar `FDNDA.Waterpolo.exe`. Si el entorno de Claude no es Windows, ejecuta las pruebas de dominio e infraestructura que puedan correr allí. Intenta compilación cruzada de WPF solo si el entorno y sus paquetes lo permiten; deja ejecución visual, segundo monitor, instalador y hardware pendientes de Windows. No digas que una prueba se ejecutó si solo la escribiste.

## 6. Orden de trabajo para esta sesión

1. **Mapea primero.** Después de inspeccionar el repo, escribe requisitos trazables (RF01–RF12), arquitectura, modelo y decisiones, plan de entregas, matriz de pruebas, lista de supuestos/preguntas y checklist de hardware. Si hay instrucciones previas en el repo, intégralas. No esperes respuesta para decisiones reversibles que puedas documentar.
2. **Implementa el primer corte vertical ahora.** Solución y proyectos, SQLite con migración y datos demo, dos equipos con nóminas ficticias y un partido, ventana de mesa y ventana de marcador compartiendo estado. Arranca el partido, muestra los dos relojes simulados, registra un gol con jugador, corrige o anula con motivo y actualiza la pantalla. Guarda y reabre el partido sin perder el resultado. Demuestra pérdida/reconexión simulada con aviso y sin mostrar relojes vencidos como vigentes. Implementa apertura del marcador en monitor elegido con alternativa de ventana si solo hay un monitor. No necesitas implementar importación real, todos los tipos de incidencias, diseño visual final, reportes o instalador para dar por completado este corte.
3. **Verifica y deja el siguiente paso listo.** Prueba que se rechaza un jugador ajeno al equipo, que el mismo comando de gol no suma dos veces, que corregir o anular conserva auditoría y que el partido se recupera desde SQLite temporal. Verifica las transiciones de señal de demo con un reloj controlable en las pruebas. Ejecuta las comprobaciones disponibles. Si no hay toolchain o faltan permisos, deja el código y comandos concretos y detalla el bloqueo. En README explica cómo levantar la demo desde cero y cómo reproducir el flujo principal.
4. **Planifica los siguientes hitos sin fingir que ya se hicieron.** (A) Nóminas, reglas, todas las incidencias, reportes y respaldo. (B) Prueba técnica del System 6 a coordinar desde el inicio, implementación del adaptador según evidencia y verificación de ambos relojes. (C) Instalación offline, validación en Windows y pantalla real, capacitación y partido piloto. (D) Ensayo previo al Panamericano con la laptop, consola y pantalla del evento, requisitos del organizador y acta de observaciones. Los hitos A y B pueden avanzar en paralelo cuando haya acceso al equipo.

## 7. Criterios del piloto y reporte final

Deja en `docs/aceptacion.md` pasos, resultado esperado, evidencia y estado para: instalación limpia offline; importación con errores, cancelación y reimportación; operación de un partido con cambios de periodo; gol/corrección/exclusión/tiempo muerto y animaciones; resultado y tanda; exportación PDF/CSV; dos pantallas; corte de la aplicación y recuperación; respaldo y restauración; ensayo con el Colorado real para inicio, pausa y reset de ambos relojes, dato inválido, pérdida y reconexión; y operación sin internet durante el partido. Separa verificación automatizada, validación visual en Windows y aceptación con equipo real. El simulador permite desarrollar y demostrar, pero no valida RF11 en producción.

Cuando termines esta sesión, resume en español:

- Qué archivos creaste o cambiaste y dónde quedó el mapeo.
- Qué funciona en la demo y el comando exacto para abrirla.
- Qué pruebas ejecutaste y resultado; qué falta verificar en Windows o hardware.
- Qué falta para el siguiente hito y cuáles son las decisiones o accesos que necesita la FDNDA.

Empieza ahora: **inspecciona, documenta, implementa el corte vertical y verifica**. No entregues únicamente un plan ni marques módulos incompletos como listos.
