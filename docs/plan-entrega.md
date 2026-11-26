# Plan de entregas — Marcador de Waterpolo FDNDA

Los hitos A y B pueden avanzar en paralelo en cuanto haya acceso a la consola. Nada de lo listado
como pendiente está hecho: el estado real de cada requisito está en [requisitos.md](requisitos.md).

## Estado al 2026-11-25

**514 pruebas automatizadas en verde** (dominio 122, infraestructura 262, ViewModels 130); compilación
sin advertencias; R10 y los avisos nuevos recorridos en pantalla con datos temporales. Sin commits en git todavía.

| Entrega | Qué quedó hecho | Evidencia |
|---|---|---|
| Hito 0 (2026-11-11) | Primer corte vertical: partido, gol con corrección y anulación, relojes simulados, mesa y marcador, recuperación tras cierre forzado. | aceptación §1.1–§1.3 |
| R1 · A5 (2026-11-12) | Exclusión/expulsión, falta personal, penal con resultado y tiempo muerto, con corrección y anulación. | aceptación §1.4, `hito-a-r1.md` |
| R2 · A1 (2026-11-18) | Eventos y equipos (crear y editar); programar partidos con colores y gorros por partido. | aceptación §1.5, `hito-a-r2.md` |
| R3 · A3 (2026-11-18) | Nóminas por evento: edición manual e importación CSV/XLSX con vista previa, errores por fila y columna, conflictos con decisión explícita y confirmación en una transacción. | aceptación §1.6, `hito-a-r3.md` |
| R4 · A4 (2026-11-18) | Reglas por evento editables desde *Reglas del evento…*: periodos, duraciones, descansos, tiempos muertos, posesión y tanda. Preset DEMO identificado; lo escrito a mano queda «sin aprobación técnica»; el preset de competencia aparece pendiente de aprobación. Los partidos iniciados o cerrados conservan sus reglas. | aceptación §1.8 y P21, `hito-a-r4.md` |
| R5 · A9 (2026-11-23) | Pestaña *Resultados y respaldos* con respaldos: crear, listar, abrir carpeta, restaurar de la lista o de otro archivo; respaldo automático antes de iniciar y al finalizar. Restauración validada, impedida en pleno partido, con respaldo previo y vuelta atrás. | aceptación §1.9 y P13, `hito-a-r5.md` |
| R6 · A8 (2026-11-23) | Sección *Resultados*: resultados, goleadores y totales por evento y por partido, derivados de las incidencias vigentes; exportación a PDF (escritor propio) y CSV con fórmulas neutralizadas. | aceptación §1.10 y P10, `hito-a-r6.md` |
| R7 · A2 + A7 (2026-11-23) | Escudo y bandera por equipo; *Identidad y plantilla…* por evento (título, subtítulo, colores, logo del evento y de la organización, hasta 8 auspiciadores) con plantilla de ejemplo del Panamericano Sub-17 2027 de recursos genéricos; imágenes PNG/JPEG validadas por contenido, en la base y en el respaldo; identidad en mesa y marcador; avisos de exclusión y tiempo muerto, una vez y desactivables por tipo. | aceptación §1.11, P08, `hito-a-r7.md` |
| R8 · A6 (2026-11-24) | Prórroga configurable por evento (periodos y duración) que solo empieza con empate; tanda de penales como etapa propia con lanzamientos por jugador, corregibles y anulables, que nunca suman al resultado reglamentario; *Anular tanda…* con motivo; tanda aparte en mesa, marcador, resultados, PDF y CSV. Migración 0004 (reconstrucción de tablas con claves foráneas verificadas). Reglas de prórroga y tanda pendientes de aprobación técnica. | aceptación §1.12, P05 y P09, `hito-a-r8.md` |
| R9 · A10 (2026-11-24) | `ReplayTimingSource`: reproducción determinista de grabaciones en un formato normalizado propio (CSV UTF-8), con `--reproduccion` solo en el perfil demo, etiqueta «REPRODUCCIÓN» en ambas ventanas, panel de avance y *Reiniciar* en la mesa, y grabación sintética de ejemplo en `samples/cronometraje`. No valida RF11. | aceptación §1.13 y P22, `hito-a-r9.md` |
| A11 (2026-11-18, pedido del operador) | Panel **Estado de conexiones** (consola, señal, relojes, marcador, pantallas, almacenamiento) con historial e indicador en la barra inferior; colores de equipo con paleta y selector de Windows; carpeta de datos en otro disco (`FDNDA_WATERPOLO_DATOS`, en este equipo `G:\FDNDA\Waterpolo`); botón *Volver a detectar pantallas*. | aceptación §1.7, anexo de `hito-a-r3.md` |
| R10 (2026-11-25) | Reprogramar (descripción, fecha, colores, gorros), actualizar la nómina desde el evento y anular con motivo un partido no iniciado; el anulado queda como registro y no suma. Colores del formulario conservados al *Actualizar*; nombres largos recortados con «…» y ayuda; XLSX guardado por Excel 16 importado. Migración 0005. 488 pruebas. | aceptación §1.18 y P19, `hito-a-r10.md` |
| Avisos y faltas (2026-11-25, pedido del usuario) | Aviso de falta personal y «n.ª FALTA» en falta, exclusión y expulsión; fin del tiempo muerto leído del reloj de tiempo muerto de la consola (con cuenta en el anillo) y en el simulador; anuncios de inicio de periodo, descanso, tanda y final; faltas por jugador (personales + exclusiones + expulsiones, sin sanción automática) bajo el resultado. 514 pruebas. | aceptación §1.19, spec en `docs/superpowers/specs/` |
| Rediseño del marcador (2026-11-25, pedido del usuario) | Marcador del público «A · Transmisión» elegido en maquetas: fondo sin cajas con brillo del color de cada equipo, periodo con puntos, anillo de posesión, previa/descanso/tanda/final propios y perdedor atenuado; avisos animados «toma del lado» (gol en el color del equipo; exclusión, expulsión y tiempo muerto en colores fijos) sin tapar los relojes. 460 pruebas. | aceptación §1.17 y P08, spec y plan en `docs/superpowers/` |

**Hito A completo, sin pendientes propios desde R10.** Pendientes al 2026-11-25:

- **Hito B (consola System 6):** P16 en vivo, contadores de tramas descartadas en el diagnóstico,
  latencia medida, motivo concreto en la mesa cuando falta el puerto, número de serie. El Colorado WTTC
  inalámbrico (segundo sistema de la FDNDA) no está estudiado.
- **Hito C:** instalador offline, equipo limpio sin internet, actualización conservando datos, segundo
  monitor y pantalla LED (también el marcador rediseñado y sus avisos; formato del LED aún desconocido),
  restauración en otra computadora, elegir el puerto COM desde la interfaz, abrir PDF/CSV con una persona.
- **Depende de la FDNDA:** preset de competencia (duraciones, prórroga y tanda), tratamiento reglamentario
  de incidencias, identidad visual oficial y proporción de la pantalla LED del evento.
- **Repositorio:** sin ningún commit todavía. Paquete `artifacts\publish\win-x64` regenerado con R10
  el 2026-11-25.

## Hito 0 — Primer corte vertical (entregado, v0.1.0)

Solución y proyectos; SQLite con migración y datos demo (dos equipos con nóminas ficticias y dos
partidos); ventana de mesa y ventana de marcador compartiendo estado; inicio del partido, periodos
y descansos, cierre y reapertura con motivo; gol con jugador, corrección y anulación con auditoría;
ambos relojes simulados; pérdida y reconexión simuladas; recuperación tras cierre forzado; marcador
en pantalla elegida con alternativa de ventana; paquete `win-x64` self-contained; 74 pruebas automatizadas.

No incluye: importación, gestión de eventos y nóminas, los demás tipos de incidencia, tanda de
penales, reportes, restauración de respaldos, instalador, diseño visual final ni lectura del Colorado.

## Hito A — Nóminas, reglas, todas las incidencias, reportes y respaldo

| N.º | Trabajo | RF | Criterio de salida |
|---|---|---|---|
| A1 | R2: gestión de eventos, equipos y partidos (crear, editar, programar; local/visita y gorros por partido). **Implementado y verificado** (aceptación §1.5); reprogramar, actualizar nómina y anular desde R10 (§1.18). | RF01 | Dos equipos distintos guardados y un encuentro programado en el evento correcto, desde la interfaz. |
| A2 | Imágenes: escudos, banderas, logo del evento y auspiciadores, con límites de peso y dimensiones. **Implementado y verificado** (R7, migración 0003, aceptación §1.11). Pendiente: identidad oficial. | RF01, RF07 | Las imágenes se ven en mesa y marcador y viajan en el respaldo. El respaldo previo a migración ya se ejercitó en R1. |
| A3 | R3: edición manual de nóminas e importación CSV UTF-8 y XLSX con vista previa, errores por fila y columna, conflictos con decisión explícita y confirmación transaccional. XLSX con lector propio, sin librería (arquitectura D25). **Implementado y verificado** (aceptación §1.6); desde R10, con un XLSX guardado por Excel 16 y la nómina de partidos no iniciados actualizable (§1.18). | RF02 | Los seis criterios de RF02, cada uno con prueba automatizada. |
| A4 | Edición de reglas por evento y preset de competencia (cuando el área técnica lo apruebe). **Edición implementada y verificada** (R4, aceptación §1.8). Pendiente: preset de competencia. | RF03 | Editar el evento no altera partidos iniciados o cerrados. |
| A5 | R1: exclusión/expulsión, falta personal, penal con resultado y tiempo muerto, con corrección y anulación. Implementado y probado; evidencia y alcance visual en aceptación §1.4. | RF05, RF06 | Un penal convertido suma una sola vez; cada tipo se corrige y anula con auditoría. |
| A6 | Prórroga y tanda de penales separada del resultado reglamentario. **Implementado y verificado** (R8, migración 0004, aceptación §1.12) con reglas de prueba. Pendiente: reglas aprobadas de prórroga y tanda; ver la tanda en la pantalla LED. | RF04, RF05 | La tanda se muestra y exporta aparte. |
| A7 | Animaciones de exclusión y tiempo muerto; plantilla visual configurable (torneo, identidad, selecciones, banderas, auspiciadores). Plantilla del Panamericano Sub-17 2027 con contenido de ejemplo. **Implementado y verificado en modo ventana** (R7, aceptación §1.11 y P08). Pendiente: pantalla LED (C3) e identidad oficial. | RF08 | No tapan los relojes; no se repiten al corregir, anular ni reabrir la pantalla. |
| A8 | Resumen por partido y evento; exportación PDF y CSV con neutralización de fórmulas. **Implementado y verificado** (R6, aceptación §1.10) con un escritor PDF propio (D31). Falta abrir los archivos en Excel y en un lector de escritorio con una persona. La tanda sale aparte desde R8. | RF09 | Totales y goleadores coinciden con el marcador tras correcciones; los archivos abren fuera del programa. |
| A9 | Respaldo desde la interfaz (antes del partido y al cierre) y restauración segura. **Implementado y verificado** (R5, aceptación §1.9) en otra carpeta de datos, también con imágenes (R7, §1.11); falta probar en otro equipo (C3). | RF10 | Restaurar en otra instalación conserva partidos, incidencias, imágenes y configuración; se impide en pleno partido; si falla se vuelve a la base anterior. |
| A10 | `ReplayTimingSource` con fixtures normalizados, marcado como «REPRODUCCIÓN». **Implementado y verificado** (R9, aceptación §1.13) con una grabación sintética. Pendiente (hito B): convertir capturas autorizadas de la consola a este formato. | RF11 | Reproduce un guion grabado de forma determinista. |
| A11 | Diagnóstico de conexiones (pedido del operador): panel con estado de consola, señal, cada reloj, marcador público, pantallas y almacenamiento; indicador en la barra inferior; historial de contingencias. Colores de equipo con paleta, selector de Windows y código sin «#». **Implementado y verificado** con simulador (aceptación §1.7). | RF07, RF11, RF12 | Toda desconexión se ve en rojo o ámbar con hora, desde cualquier pestaña. |

Depende de la FDNDA: reglas de competencia y tratamiento de cada tipo de incidencia (A4–A6);
identidad gráfica, banderas y auspiciadores si se quieren los reales (A2, A7).

## Hito B — Colorado System 6

| N.º | Trabajo | Criterio de salida |
|---|---|---|
| B1 | **Coordinar desde ya** el acceso a la consola fuera de competencia y completar el checklist del equipo. | Checklist de [colorado-system6.md](colorado-system6.md) completo, con evidencia. |
| B2 | Consulta a Colorado Time Systems sobre la salida de datos y su documentación. | Respuesta escrita o documentación oficial obtenida. |
| B3 | Prueba técnica de solo observación y captura acotada autorizada. **Herramienta lista:** `tools/FDNDA.Waterpolo.Captura` (solo lectura; procedimiento en colorado-system6.md §6). Hace falta: cable en Y de 1/4", adaptador USB a RS-232, conector DB9 y acceso autorizado. | Protocolo confirmado, o limitación registrada y escalada. |
| B4 | Transporte y decodificador según la evidencia, con sus pruebas de tramas. **Hecho (2026-11-24)** con la ruta observada en la consola de la FDNDA: COM PORT 2, tramas CTSWP (colorado-system6.md §7); decodificador, transporte serie de solo lectura y fuente con reconexión, probados con capturas reales (aceptación §1.15). El decodificador del flujo de tablero de terceros (§6) queda solo en la herramienta. | Pruebas de tramas válidas, fragmentadas, concatenadas, inválidas y fuera de rango. |
| B5 | Verificación con la consola real de ambos relojes. | Inicio/pausa, reinicio del reloj, reset de posesión, dato inválido, pérdida y reconexión, según [aceptacion.md](aceptacion.md). El panel *Estado de conexiones* refleja cada caso (P20). |
| B6 | Umbrales y frecuencia definitivos; medición de latencia por reloj. | Valores medidos y documentados. |

Riesgo principal del proyecto: si la consola no ofrece una salida que se pueda **solo escuchar**
sin interferir con el tablero oficial, RF11 no se puede cumplir como está escrito y la FDNDA deberá
decidir la alternativa. Por eso B1 y B2 no deben esperar al hito A.

## Hito C — Instalación offline, validación en Windows y piloto

| N.º | Trabajo | Criterio de salida |
|---|---|---|
| C1 | Instalador offline `FDNDA-Waterpolo-Setup.exe` sobre el paquete portátil validado: acceso directo, perfil configurado, carpeta de datos elegible (variable `FDNDA_WATERPOLO_DATOS`), desinstalación documentada, licencias adjuntas. | Instala y abre por doble clic en un equipo limpio, sin SDK ni internet. |
| C2 | Actualización conservando datos. | Instalar una versión nueva no borra la carpeta de datos. |
| C3 | Validación en la laptop de producción con segundo monitor y con la pantalla LED real: pantalla completa, márgenes seguros, 1280×720, 1920×1080 y la resolución del LED, nombres largos, desconexión del monitor (con el panel *Estado de conexiones* en ámbar y su historial). | Acta de validación visual. |
| C4 | Medición de respuesta (objetivo provisional: menos de 200 ms desde el commit). | Valor medido en la laptop objetivo. |
| C5 | Capacitación de operadores e instrucciones de uso. | Operadores registran un partido completo sin ayuda. |
| C6 | Partido piloto. | Acta con observaciones. |

Sin certificado de firma de código el instalador **no** estará firmado y Windows puede mostrar una
advertencia; se documenta, no se disimula.

## Hito D — Ensayo previo al Panamericano Sub-17 2027

Ensayo con la laptop, la consola y la pantalla del evento; requisitos del organizador (reglas,
visualización, selecciones, banderas, auspiciadores); operación sin internet durante un partido
completo; acta de observaciones y correcciones antes del torneo.

## Decisiones y accesos que necesita la FDNDA

1. **Acceso a la consola System 6** fuera de competencia, con una persona autorizada, y su número de
   serie, firmware y versión del módulo de waterpolo.
2. **Consulta formal a Colorado Time Systems** (o autorización para hacerla en su nombre).
3. **Reglas de competencia** aprobadas por el área técnica.
4. **Equipo de producción:** laptop, versión de Windows y datos de la pantalla LED.
5. **Identidad gráfica** oficial, si se quiere usar; mientras tanto se usan recursos genéricos.
6. **Certificado de firma de código**, si se quiere un instalador firmado.
7. **Organizador del Panamericano:** reglas y requisitos de visualización.
