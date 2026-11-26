# Aceptación — Marcador de Waterpolo FDNDA

Criterios del piloto, con pasos, resultado esperado, evidencia y estado. Se separan tres niveles que
**no son intercambiables**:

- **Automatizada:** dominio y SQLite con `dotnet test` sin WPF; ViewModels en proyecto Windows sin
  abrir ventanas. Ninguna requiere hardware.
- **Visual en Windows:** el programa publicado, observado en pantalla.
- **Con equipo real:** laptop de producción, pantalla LED y consola Colorado System 6.

El simulador permite desarrollar y demostrar, pero **no valida RF11 en producción**.

## 1. Lo verificado en esta entrega (v0.1.0, 2026-11-11)

Equipo de desarrollo: Windows 11 Home 10.0.26200, una sola pantalla de 1920×1080, SDK .NET 10.0.404.

### 1.1 Automatizada — 74 pruebas, 0 fallos

`dotnet test FDNDA.Waterpolo.sln -c Release` → Dominio: 29 superadas. Infraestructura y casos de uso: 45 superadas.

| Comprobación pedida | Prueba |
|---|---|
| Se rechaza un jugador ajeno al equipo. | `Se_rechaza_un_jugador_ajeno_a_la_nomina_del_partido`, `Se_rechaza_un_jugador_del_equipo_contrario` (dominio); `Se_rechaza_un_jugador_ajeno_al_equipo_sin_escribir_ni_publicar` (SQLite). |
| El mismo comando de gol no suma dos veces. | `El_mismo_comando_de_gol_no_suma_dos_veces`; `La_misma_incidencia_no_suma_dos_veces`. |
| Corregir o anular conserva auditoría. | `Corregir_y_anular_conservan_la_auditoria_en_la_base`; `Corregir_goleador_actualiza_la_estadistica_sin_duplicar_goles_y_conserva_auditoria`; `Anular_un_gol_resta_exactamente_uno_y_conserva_el_historial`. |
| El partido se recupera desde SQLite temporal. | `El_partido_se_recupera_desde_sqlite_tras_reiniciar`. |
| Transiciones de señal de demo con reloj controlable. | `SimulatedTimingTests` (8 pruebas: detenido, en juego y pausa, reset, pérdida, reconexión, guion reproducible, fin de periodo, carga de periodo) y `TimingStateTrackerTests`. |
| Si la escritura falla no se reporta éxito ni cambia el marcador. | `Si_la_escritura_falla_no_se_reporta_exito_ni_cambia_el_marcador`. |
| Un solo partido en juego; versión esperada; escrituras concurrentes. | `Solo_puede_haber_un_partido_en_juego`, `Una_version_esperada_antigua_se_rechaza`, `El_repositorio_detecta_escrituras_concurrentes_sobre_la_misma_version`. |
| Las reglas del evento no alteran un partido iniciado. | `Cambiar_las_reglas_del_evento_no_altera_un_partido_iniciado`. |
| Sin señal, el gol se guarda sin hora de juego inventada. | `Sin_senal_vigente_el_gol_se_guarda_con_contexto_temporal_no_confirmado`. |
| Reapertura con motivo y atribuida. | `Reabrir_un_cierre_exige_motivo_y_queda_atribuido`. |
| Sin borrado físico. | `La_base_impide_borrar_o_modificar_incidencias_confirmadas`. |
| Migración, datos demo sin duplicar, demo separado de producción, base más nueva, respaldo íntegro, archivo dañado, instancia única, log acotado. | `StorageTests` (7 pruebas). |
| El adaptador Colorado no simula éxito. | `Colorado_pendiente_devuelve_no_disponible_y_nunca_muestra_relojes`. |

### 1.2 Visual en Windows — paquete publicado, manejado con automatización de interfaz

Se ejecutó `artifacts\publish\win-x64\FDNDA.Waterpolo.exe` **sin el SDK en el PATH**, con datos en una
carpeta temporal, y se leyeron los valores de ambas ventanas. Capturas en `docs/evidencia/`.

| Paso | Resultado observado | Estado |
|---|---|---|
| Abrir el programa con datos vacíos. | Crea la base, carga el demo y abre «Partido demo 1» en PROGRAMADO, 8:00 / 30, distintivo «DEMO / SIMULACIÓN». | Verificado |
| Abrir el marcador con una sola pantalla. | Se abre en ventana y la mesa avisa: «Solo hay una pantalla conectada…». Muestra «PRÓXIMO PARTIDO» y «VS» (`publico-previa.png`). | Verificado |
| Iniciar partido e iniciar el reloj simulado. | PERIODO 1; ambos relojes descuentan en mesa y público. | Verificado |
| Registrar un gol con jugador. | Mesa 1-0 y público 1-0; aviso «¡GOL!» en la franja inferior, sin tapar los relojes (`publico-gol.png`). | Verificado |
| Corregir el goleador con motivo. | Resultado sin cambio (2-1), historial con las dos revisiones, motivo, operador y hora; el público no repite el aviso (`mesa-correccion-anulacion.png`). | Verificado |
| Anular un gol con motivo. | Resta exactamente uno (2-1 → 2-0) en mesa y público; la fila queda como ANULADO. | Verificado |
| Corregir o anular sin motivo. | Botones deshabilitados. | Verificado |
| Simular pérdida de señal. | Tras el umbral: `--:--` y `--` en mesa y público, con aviso en ambas (`publico-sin-senal.png`, `mesa-sin-senal.png`). | Verificado |
| Registrar un gol sin señal. | Se registra, con el mensaje «registrado SIN reloj» y la columna de reloj «sin reloj». | Verificado |
| Reconectar. | Vuelven valores nuevos (6:43, no el 6:50 anterior al corte). | Verificado |
| Descanso y siguiente periodo. | PERIODO 2 en mesa y público. | Verificado |
| Cierre forzado del proceso y reapertura. | «Se recuperó el partido en juego…»: mismo resultado, periodo e incidencias. | Verificado |
| Reabrir el marcador tras recuperar. | Muestra el estado actual; el aviso de gol no reaparece. | Verificado |
| Segunda instancia sobre los mismos datos. | Mensaje «ya está abierto con estos datos» y termina. | Verificado |
| Finalizar partido. | Pide confirmación; FINALIZADO en mesa y «RESULTADO FINAL» sin relojes en público; goles deshabilitados (`publico-final.png`). | Verificado |
| Reabrir con motivo. | El diálogo no acepta sin motivo; al aceptar vuelve a PERIODO 1 y el motivo queda en la base con el operador. | Verificado |
| Cerrar la mesa. | Se cierra también el marcador y el proceso termina; el bloqueo se libera. | Verificado |
| Teclas de letra sin Alt (D, S, F, R) sobre la mesa. | No cambian el partido (ver hallazgo H1). | Verificado |
| Abrir con `--perfil produccion` y datos vacíos. | Base aparte sin datos demo; relojes `--:--` y `--`; «FUENTE DE TIEMPO NO DISPONIBLE» con el aviso «PENDIENTE DE VALIDACIÓN EN HARDWARE»; sin distintivo DEMO ni controles del simulador. No cambia a demo por su cuenta. | Verificado |

**Hallazgo H1 (corregido).** Durante la prueba apareció en la base un «Iniciar descanso» que el guion
no había pedido. Causa reproducida: WPF ejecuta las teclas de acceso con solo la letra cuando el foco
no está en un cuadro de texto; al enviar «S» a la mesa, el partido pasó de descanso a PERIODO 3.
Corrección: las letras de acceso exigen Alt (`App.RequireAltForAccessKeys`). Reverificado enviando
D, S, F y R: el partido no cambió. No se pudo comprobar con automatización que Alt+letra siga
funcionando; queda para la validación manual.

### 1.3 Lo que **no** se pudo verificar aquí

- Marcador a **pantalla completa en un segundo monitor**, selección entre varias pantallas y retorno
  a ventana al desconectar el monitor: el código existe, pero el equipo tiene una sola pantalla.
- 1280×720 y la resolución del LED real; márgenes seguros sobre la pantalla física.
- Instalación en un equipo limpio sin SDK ni internet. (El paquete es self-contained y se ejecutó
  sin el SDK en el PATH, pero en un equipo que sí tiene .NET instalado.)
- Tiempo de respuesta medido y cualquier dato de latencia.
- Todo lo relativo a la consola Colorado.

### 1.4 Hito A, R1 — 2026-11-12

Comprobado sobre SDK local **10.0.404**, compilación Release. La entrega portátil del primer corte
no se regeneró ni se publicó en esta sesión.

**Ejecutado:** `dotnet build FDNDA.Waterpolo.sln -c Release --no-restore` → **0 errores, 0 advertencias**.
`dotnet test FDNDA.Waterpolo.sln -c Release --no-build --no-restore` → **124 superadas, 0 fallidas,
0 omitidas**: dominio 54, infraestructura 62, ViewModels 8. Antes del cambio se ejecutaron las
74 originales (29 + 45), todas superadas. Se añadieron 50 casos ejecutados.

La restauración inicial con fuentes remotas falló por red no accesible (NU1301). Se restauró desde
la caché local existente hacia `.tools/nuget-packages`, sin paquetes nuevos ni cambios de versiones;
los comandos anteriores usan esa restauración. No se cambió la configuración NuGet del proyecto.

| Criterio R1 | Evidencia automatizada ejecutada |
|---|---|
| Gol, exclusión, expulsión, falta, penal convertido/no convertido y tiempo muerto: registrar, corregir y anular conservando auditoría. | `IncidentTests.Cada_incidencia_se_registra_corrige_y_anula_con_auditoria` (7 casos); `IncidentOperationsTests.Cada_tipo_persiste_revisiones_rechaza_duplicados_y_se_recupera_sin_repetir_avisos` (7 casos). |
| Equipo/jugador válidos; tiempo muerto sin jugador; detalles compatibles y resultado explícito del penal. | `Las_incidencias_de_jugador_exigen_nomina_del_equipo_incluso_al_corregir`, `Se_rechazan_detalles_ausentes_o_incompatibles_con_el_tipo`; pruebas del tiempo muerto en dominio y ViewModel. |
| Un penal convertido aporta exactamente uno al marcador y a goleadores; corregir resultado/equipo/jugador y anular recalculan. | `El_penal_convertido_suma_un_solo_gol_y_sus_correcciones_actualizan_resultado_y_goleador`, `Cambiar_resultado_del_penal_y_detalle_de_exclusion_conserva_el_original`, `Corregir_resultado_del_penal_actualiza_ambas_vistas_y_conserva_historial`. |
| Nada se publica antes del commit; idempotencia también al corregir/anular; fallo revierte todo. | Suscriptor que lee el comando confirmado desde otra conexión; reenvío de los tres comandos; `Una_escritura_fallida_no_publica_ni_deja_datos_y_el_reintento_es_seguro` (4 casos), `Un_fallo_dentro_de_la_transaccion_revierte_version_incidencia_y_comando`. |
| Contexto sin señal y recuperación de todos los tipos con revisiones. | `Cada_tipo_persiste_revisiones…` guarda calidad no disponible y tiempos nulos, reconstruye con objetos nuevos. |
| No se aplican sanciones ni un límite de tiempos muertos no aprobado. | `Acumular_faltas_y_registrar_expulsion_no_aplica_sanciones_automaticas`, `El_tiempo_muerto_no_admite_jugador_y_no_se_bloquea_por_un_limite_no_aprobado`. |
| Respaldo real antes de 0002 y conservación de la versión anterior. | `IncidentMigrationTests.Migrar_de_uno_a_dos_respalda_antes_y_conserva_partidos_goles_y_revisiones`: base creada con SQL 0001, dos partidos y gol corregido; respaldo íntegro aún v1 sin columnas nuevas y con revisiones originales; base v2 con mismo resultado. |
| No migrar si falla el respaldo; restricciones de solo inserción conservadas. | `Si_no_se_puede_crear_el_respaldo_no_se_aplica_la_migracion`, `Las_restricciones_nuevas_y_la_auditoria_siguen_protegidas_en_sqlite`, pruebas originales de disparadores. |
| Mesa: selección, motivo obligatorio, error sin modificar resultado, ambas vistas actualizadas y doble envío bloqueado entre equipos. | `MesaIncidentTests` (8 casos), sobre SQLite temporal; compuertas de tareas sin esperas reales. |

El recorrido de interfaz se ejecuta con `powershell -NoProfile -ExecutionPolicy Bypass -File
.\scripts\verificar-r1-ui.ps1`: crea una raíz nueva dentro de `artifacts`, opera por `AutomationId`,
no envía teclas y cierra el proceso al terminar. Su evidencia visual final se detalla en el reporte
[hito-a-r1.md](hito-a-r1.md).

**Ejecutado en pantalla:** Release desde `bin`, Windows 11, una pantalla 1920×1080 al 125 %;
datos ficticios nuevos bajo `artifacts/verificacion-r1-8d9b792ddc8f4284955fb1a4e871f160/datos`.
Se registraron las seis opciones nuevas, se corrigieron y anularon con motivo. Se comprobó el
penal convertido/no convertido y el resultado final 1–0 en mesa y público; sin señal, `--:--`/`--`
y falta registrada «SIN reloj». El tiempo muerto se corrigió de equipo sin jugador. Se comprobó
que la acción de visita sin jugador estaba deshabilitada. Se guardaron capturas de mesa,
corrección y público y se cerró el proceso. El recorrido exige controles visibles o accesibles
mediante el desplazamiento del panel; historial y corrección se alternan dentro de la mesa.

**Hallazgo H2 (corregido):** las nuevas opciones y el formulario simultáneo agotaban el espacio
central al 125 %. Se ajustó el alto inicial de la ventana a 800 DIP y se alternan historial y
corrección en pestañas locales con desplazamiento. El recorrido completo pasó tras el ajuste.

**Sin verificar en R1:** pulsación física de F5/F8, F6/F9 y Alt+letra; segundo monitor y pantalla
LED; otras resoluciones/escalas; consola Colorado; equipo limpio/offline; instalador; latencias.
El simulador y las pruebas de ViewModels no sustituyen esas verificaciones.

### 1.5 R2 — eventos, equipos y programación (2026-11-18)

**Automatizada — 151 pruebas, 0 fallos** (dominio 54, infraestructura 77, ViewModels 20).
Nuevas: 15 en `CatalogOperationsTests` y 12 en `CatalogViewModelTests`.

| Comprobación | Prueba |
|---|---|
| Dos equipos distintos guardados y un encuentro programado en el evento correcto. | `Se_guardan_dos_equipos_distintos_y_se_programa_un_encuentro_en_el_evento_correcto`; desde la interfaz: `Desde_la_interfaz_se_crean_evento_y_dos_equipos_y_se_programa_el_partido`. |
| Nómina del evento y colores por partido congelados; el catálogo no cambia. | `Programar_congela_la_nomina_del_evento_y_los_colores_elegidos_para_el_partido`. |
| Editar el catálogo no altera partidos iniciados ni ya programados. | `Editar_el_catalogo_no_altera_partidos_iniciados_ni_ya_programados`. |
| Mismo equipo en ambos lados, evento o equipo inexistente, descripción vacía, color inválido. | `Se_rechaza_el_mismo_equipo…`, `Se_rechaza_programar_sin_evento_o_equipo_existente` (2 casos), `Programar_con_descripcion_vacia_o_color_invalido_se_rechaza`. |
| Código de equipo y nombre de evento únicos; datos inválidos con mensaje en español. | `El_codigo_de_equipo_es_unico…`, `El_nombre_de_evento_no_se_repite`, `Datos_invalidos_del_equipo…`. |
| Idempotencia: reenviar altas y programación no duplica; doble clic mientras se guarda; reintento tras fallo de disco. | `Reenviar_el_mismo_alta_no_duplica…`, `Mientras_se_guarda_un_segundo_envio…`, `Si_falla_el_guardado_el_reintento_usa_el_mismo_partido…`. |
| Programar no publica ni cambia el partido abierto. | `Programar_no_publica_ni_cambia_el_partido_abierto_en_la_mesa`. |
| Perfil y preset: evento nuevo con preset DEMO identificado; marca demo según perfil. | `Un_evento_nuevo_usa_el_preset_demo_identificado_y_respeta_el_perfil`. |
| Fecha local `dd/mm/aaaa hh:mm` a UTC; formatos inválidos rechazados. | `El_formato_de_fecha_es_el_local…` (5 casos), `Una_fecha_mal_escrita_no_programa_y_se_explica`. |

**Visual en Windows.** Recorrido por automatización de interfaz (sin teclas), con datos temporales,
sobre el `bin` Release y luego sobre el **paquete publicado regenerado**, sin el runtime local en el
entorno. En ambos: crear evento (botón deshabilitado sin nombre); crear dos equipos; código `DEL`
repetido rechazado con mensaje; nómina de 13 jugadores al elegir evento y equipo; colores y gorros
propuestos desde el catálogo al elegir local y visita; botón deshabilitado sin descripción; fecha
`2027-03-14` rechazada con explicación; partido programado y visible en la lista con
«14/03/2027 15:30»; abierto en la mesa e iniciado; cierre limpio (código 0). En la base: evento
correcto, gorro de visita «Negro» solo en el partido (el catálogo conserva «Gris») y hora guardada
como `2027-03-14T20:30Z` (Lima, UTC−5).

**Sin verificar en R2:** pulsación física de teclas; otras resoluciones y escalas; la pestaña con
nombres de equipo muy largos (la columna recorta el nombre).

**Nota de entorno:** el ejecutable de `bin` es dependiente del runtime y en este equipo solo
arranca con `DOTNET_ROOT` apuntando a `.tools\dotnet` (`. .\scripts\entorno.ps1`); sin eso Windows
pide instalar .NET. El paquete publicado es self-contained y no lo necesita.

### 1.6 R3 — nóminas: edición manual e importación CSV/XLSX (2026-11-18)

**Automatizada — 180 pruebas, 0 fallos** (dominio 54, infraestructura 99, ViewModels 27).
Nuevas: 22 en `RosterImportTests` (SQLite temporal y archivos reales en la carpeta temporal) y 7 en
`RosterViewModelTests`.

| Criterio de RF02 | Prueba |
|---|---|
| Errores con fila y columna. | `Los_errores_indican_fila_y_columna_y_bloquean_la_importacion` (vacío, «tres», 100, código vacío, nombre de más de 80); en la interfaz: `Un_archivo_con_errores_muestra_fila_y_columna_y_no_deja_importar`. |
| Requeridos (datos y encabezado). | La anterior y `Falta_una_columna_obligatoria_en_el_encabezado`. |
| Gorro duplicado en el mismo equipo; el mismo gorro en equipos distintos es válido. | `El_gorro_repetido_en_el_mismo_equipo_es_error_y_en_equipos_distintos_es_valido`. |
| Cancelar no escribe. | `Cancelar_la_vista_previa_no_escribe_nada` (compara equipos, jugadores e inscripciones); en la interfaz: `Cancelar_la_vista_previa_no_guarda_nada_y_lo_dice`. |
| Confirmar es transaccional. | `Si_la_nomina_cambia_antes_de_confirmar_no_se_importa_nada`: el equipo nuevo y DEL ya se habían escrito dentro de la transacción cuando se detecta el cambio en TIB; la base queda idéntica. |
| Reimportar no duplica. | `Reimportar_la_misma_plantilla_no_duplica_jugadores`, `La_importacion_no_quita_a_los_jugadores_que_no_estan_en_el_archivo`. |
| Cambios ambiguos exigen resolución explícita; la identidad no se deduce por el nombre. | `Un_gorro_ocupado_por_otro_nombre_exige_una_decision_explicita`, `Corregir_el_nombre_conserva_al_jugador_y_reemplazar_inscribe_a_otro`, `El_mismo_nombre_con_otro_gorro_no_se_une_por_el_nombre_sin_decision`, `Cambiar_de_gorro_conserva_al_jugador_y_un_intercambio_se_resuelve_moviendo_a_ambos`, `Dos_decisiones_sobre_el_mismo_jugador_se_rechazan`; en la interfaz: `Una_fila_ambigua_impide_importar_hasta_que_el_operador_decide`. |
| Límite de filas y formato validados antes de importar. | `El_limite_de_filas_se_valida_antes_de_importar`, `Se_rechazan_los_libros_con_macros_el_formato_xls_y_otras_extensiones` (también un XLSX con tipo de contenido de macros), `Un_XLSX_danado_o_un_CSV_que_no_es_UTF8_se_informan_sin_importar`. |
| CSV y XLSX. | `La_plantilla_CSV_se_importa_en_el_evento_elegido_al_confirmar`, `La_plantilla_XLSX_de_muestra_se_importa_como_la_CSV` (libro generado con openpyxl), `El_XLSX_se_lee_igual_que_el_CSV…` (textos compartidos con tramos, en línea, números, guía fonética), `El_CSV_admite_punto_y_coma_comillas…`, `Unas_comillas_sin_cerrar_se_informan_con_la_fila`. |
| Equipos nuevos y partidos ya programados. | `Un_equipo_que_no_esta_en_el_catalogo_se_crea_al_confirmar`, `Los_partidos_ya_programados_conservan_la_nomina_con_la_que_se_programaron`. |
| Edición manual. | `Desde_la_interfaz_se_agrega_corrige_y_quita_un_jugador_de_la_nomina` (incluye gorro repetido rechazado), `Quitar_pide_confirmacion_y_si_se_rechaza_no_cambia_nada`. |

La lectura del archivo y la clasificación corren en `Task.Run` dentro de
`CatalogOperations.PreviewRosterImportAsync`, fuera del hilo de interfaz (comprobado por código, no
medido).

**Visual en Windows.** `scripts\verificar-r3-ui.ps1`: recorrido por automatización de interfaz con
datos temporales, sobre el `bin` Release y después sobre el **paquete publicado regenerado**. El
diálogo «Abrir» de Windows se maneja con mensajes dirigidos a sus controles (`WM_SETTEXT` y
`BM_CLICK`), sin teclado. En ambos: evento nuevo; CSV con dos errores → vista previa con
«Fila 2, columna jugador_nombre (C)» y «Fila 3, columna numero_gorro (D)», *Importar*
deshabilitado, cancelar; plantilla XLSX → «6 nuevos», importada, DEL con 3 jugadores; CSV con
conflicto → «Falta decidir la fila 2» e *Importar* deshabilitado hasta elegir «Mismo jugador:
corregir su nombre»; reimportar la plantilla → el nombre corregido vuelve a pedir decisión (no se
une por el nombre) y se cancela; alta manual con el gorro libre propuesto (4). Capturas:
[vista previa con decisión](evidencia/nomina-vista-previa-decision.png),
[vista previa con errores](evidencia/nomina-vista-previa-errores.png),
[pestaña con la nómina](evidencia/mesa-nomina.png).

**Sin verificar en R3:** un XLSX guardado por Microsoft Excel (no hay Excel en este equipo; la
muestra se generó con openpyxl y las pruebas usan XML escrito a mano); un CSV guardado por Excel con
configuración regional de punto y coma (cubierto por prueba de texto, no con Excel); archivos en
red o en uso por otro programa; pulsación física de teclas.

### 1.7 Estado de conexiones y colores de equipo (2026-11-18, pedido del operador)

**Automatizada — 199 pruebas, 0 fallos** (dominio 61, infraestructura 102, ViewModels 36). Nuevas:
7 en `ConnectionStatusViewModelTests`, 2 de colores en `RosterViewModelTests`, 7 casos en
`CatalogTests` (dominio) y 3 en `DataLocationTests`.

| Comprobación | Prueba |
|---|---|
| Todo en orden con simulador, dos pantallas y marcador a pantalla completa; la consola en gris en demo. | `Con_simulador_marcador_a_pantalla_completa_y_dos_pantallas_todo_esta_en_orden`. |
| Pérdida de señal: contingencia con hora; ambos relojes sin señal; al volver, el historial lo registra. | `Perder_la_señal_es_contingencia_con_hora_y_al_volver_queda_en_el_historial`. |
| Consola pendiente de validación: «NO DISPONIBLE» con el motivo; nunca «Conectada» sin datos. | `Con_la_consola_pendiente_de_validacion…`, `La_consola_solo_figura_conectada_cuando_llegan_datos_vigentes_de_ella`. |
| Un reloj sin lectura con señal vigente es aviso. | `Si_falta_un_reloj_con_señal_vigente_queda_en_aviso`. |
| Marcador cerrado o en ventana, desconexión de la pantalla del público. | `Marcador_cerrado_en_ventana_o_con_una_sola_pantalla…`. |
| Poco espacio en disco y base no encontrada; comprobación manual. | `El_almacenamiento_avisa_con_poco_espacio_y_es_contingencia_sin_la_base`. |
| Color por paleta, por selector (cancelar no cambia), escrito sin «#»; texto inválido rechazado con ejemplo; color de cada equipo del partido. | `El_color_del_equipo_se_elige_con_la_paleta…`, `El_color_de_cada_equipo_del_partido_se_elige_con_el_selector`, `CatalogTests`. |

**Visual en Windows.** `scripts\verificar-conexiones-ui.ps1` sobre el `bin` Release y sobre el
paquete publicado: con el marcador abierto (en ventana, una sola pantalla) la barra inferior indica
«Conexiones: 2 avisos» y el panel lo detalla; *Simular pérdida de señal* → «CONTINGENCIA: Señal de
tiempo (PERDIDA desde las hh:mm:ss) · Reloj de partido (Sin señal) · Reloj de posesión (Sin señal)»
en rojo y «3 contingencias» en la barra; *Reconectar* → vuelve a avisos y el historial muestra la
secuencia con hora. En equipos: un clic en «Rojo» pone `#C62828`; «Más colores…» abre el selector
de Windows y cancelarlo no cambia el color. Capturas:
[contingencia](evidencia/diagnostico-conexiones-contingencia.png),
[normal](evidencia/diagnostico-conexiones-normal.png),
[colores](evidencia/catalogo-colores-equipo.png).

**Carpeta de datos en otro disco.** `DataLocationTests` (3): la variable `FDNDA_WATERPOLO_DATOS`
lleva demo y producción a la carpeta indicada; `--datos` tiene prioridad; sin ninguna se usa el
perfil del usuario. En este equipo se fijó la variable de usuario en `G:\FDNDA\Waterpolo`, se copió
la base demo (idéntica por SHA-256, `integrity_check` ok) y el paquete publicado abrió esa carpeta:
el diagnóstico muestra «G:\FDNDA\Waterpolo\demo (elegida por: variable de entorno …)» y solo se
escribió en G:. El botón **Volver a detectar pantallas** (antes «Actualizar pantallas») relee las
pantallas y comprueba el estado de conexiones.

**Sin verificar:** el panel con la consola Colorado real (hito B) y con un segundo monitor que se
desconecta (hito C); elegir un color en el selector de Windows y aceptarlo con el ratón (se probó
abrir y cancelar; aceptar está cubierto por prueba con selector simulado).
### 1.8 R4 — reglas por evento (2026-11-18)

**Automatizada — 229 pruebas, 0 fallos** (dominio 71, infraestructura 106, ViewModels 52). Nuevas:
`EventRulesTests` (10 casos, dominio), `RulesOperationsTests` (4, SQLite temporal) y
`RulesEditorViewModelTests` (16 casos, ViewModels).

| Comprobación | Prueba |
|---|---|
| Editar las reglas del evento no altera el partido en juego; el partido que se inicia después las toma. | `Editar_las_reglas_del_evento_no_altera_el_partido_en_juego_y_las_toma_el_que_se_inicia_despues`. |
| Un partido cerrado, reabierto y leído tras reiniciar conserva sus reglas. | `Un_partido_cerrado_y_reabierto_conserva_sus_reglas_tras_editar_el_evento`. |
| Preset DEMO identificado; valores distintos quedan «sin aprobación técnica»; volver a los valores DEMO restituye el preset; repetir no escribe. | `Los_valores_del_demo_se_guardan_como_el_preset_demo_identificado`, `Valores_distintos_del_demo_quedan_como_reglas_personalizadas_sin_aprobacion_tecnica`, `Volver_a_los_valores_demo_restituye_el_preset_demo_y_repetir_no_escribe`. |
| Valores fuera de rango rechazados con el rango en el mensaje; el evento conserva sus reglas. | `Un_valor_fuera_de_rango_se_rechaza_explicando_el_rango` (8 casos), `Un_valor_fuera_de_rango_no_se_guarda_y_el_evento_conserva_sus_reglas`. |
| Duraciones en `m:ss` o minutos; texto mal escrito no se guarda; el error se borra al corregir. | `La_duracion_se_escribe_en_minutos_o_como_minutos_y_segundos` (4), `Una_duracion_mal_escrita_no_se_guarda_y_lo_explica` (6), `Al_corregir_un_valor_se_borra_el_error_anterior`. |
| Edición desde la interfaz; el diálogo cuenta los partidos al abrirse; cancelar no guarda; sin evento no se abre. | `Desde_la_interfaz_se_editan_las_reglas_del_evento_en_minutos_y_segundos`, `El_editor_cuenta_los_partidos_al_abrirse_aunque_la_lista_se_haya_cargado_antes`, `Cargar_DEMO_restituye_los_valores_ilustrativos_y_el_preset_de_competencia_queda_pendiente`, `Sin_evento_elegido_no_se_pueden_editar_reglas`. |
| Renombrar el evento conserva sus reglas; evento inexistente rechazado. | `Renombrar_el_evento_conserva_sus_reglas_y_un_evento_inexistente_se_rechaza`. |

**Visual en Windows.** `scripts\verificar-r4-ui.ps1` sobre el `bin` Release y sobre el paquete
publicado, con datos temporales y sin enviar teclas: se inicia el partido demo 1 (reglas DEMO
4 × 8:00); en *Reglas del evento…* el diálogo muestra las reglas actuales, «El partido programado de
este evento tomará estas reglas al iniciarse. El partido ya iniciado o cerrado conserva las reglas
con las que empezó.» y el botón de competencia deshabilitado; con 0 periodos rechaza «La cantidad de
periodos debe estar entre 1 y 20.»; con 4 × 7:30, posesión 28 s y tanda guarda «Personalizadas del
evento (sin aprobación técnica)» y la tarjeta del evento lo resume. El partido 1 en juego sigue con
DEMO 4 × 8:00 también tras finalizarlo; el partido 2 iniciado después muestra «Personalizadas …
4 × 7:30 · posesión 28 s» y *Cargar periodo nuevo* pone el simulador en 7:30 / 28. Capturas:
[diálogo](evidencia/reglas-evento-dialogo.png), [partido nuevo](evidencia/reglas-partido-nuevo.png).

**Hallazgos corregidos en el recorrido:** el diálogo contaba los partidos con la lista cargada al
arrancar (decía «2 programados» con uno ya en juego); ahora los relee al abrirse. El mensaje de
error seguía visible después de corregir el valor; ahora se borra al cambiar un campo.

**Sin verificar:** preset de competencia (sin valores aprobados); manejo del diálogo con teclado
físico (Alt+letra, Intro/Esc) y a otras escalas de pantalla.

### 1.9 R5 — respaldo y restauración (2026-11-23)

**Automatizada — 243 pruebas, 0 fallos** (dominio 71, infraestructura 114, ViewModels 58). Nuevas:
`BackupRestoreTests` (8, SQLite temporal) y `BackupsViewModelTests` (6).

| Comprobación | Prueba |
|---|---|
| Respaldar y restaurar en **otra carpeta de datos** conserva partidos, incidencias, revisiones, versión, resultado y reglas; la base anterior queda guardada. | `Respaldar_y_restaurar_en_otra_carpeta_conserva_partidos_incidencias_revisiones_y_reglas`. |
| No se restaura con un partido en juego; no se crea ni se toca nada. | `No_se_restaura_con_un_partido_en_juego_y_no_se_toca_la_base`, `Con_un_partido_en_juego_no_se_restaura_ni_se_pregunta`. |
| Archivo dañado, base ajena, archivo inexistente o la propia base en uso: rechazados. | `Un_archivo_danado_o_ajeno_no_se_restaura`. |
| Versión de esquema más nueva: rechazada; versión anterior: se restaura y se migra con respaldo previo. | `Un_respaldo_de_una_version_mas_nueva_no_se_restaura`, `Un_respaldo_de_esquema_anterior_se_restaura_y_se_migra`. |
| Si algo falla después de copiar, se vuelve a la base anterior, íntegra. | `Si_falla_despues_de_copiar_se_vuelve_a_la_base_anterior` (fallo inyectado en la migración). |
| Producción no acepta datos demo. | `Produccion_no_restaura_un_respaldo_con_datos_de_demostracion`. |
| Lista ordenada con motivo, fecha y tamaño; dos respaldos en el mismo segundo no se pisan. | `Los_respaldos_se_listan_del_mas_reciente_al_mas_antiguo_con_su_motivo`. |
| Respaldo automático al iniciar y finalizar; desactivable; si falla el previo se pregunta y «No» no inicia. | `Iniciar_y_finalizar_desde_la_mesa_crean_respaldos_automaticos`, `Con_los_respaldos_automaticos_desactivados_no_se_crea_ninguno`, `Si_el_respaldo_previo_falla_se_pregunta_y_al_decir_no_el_partido_no_se_inicia`. |
| Restaurar desde la pestaña recarga mesa y catálogo, aunque el partido restaurado tenga una versión menor que la que estaba en pantalla; cancelar no cambia nada; elegir un archivo de otra instalación. | `Restaurar_desde_la_pestaña_recarga_la_mesa_y_el_catalogo`, `Cancelar_la_confirmacion_no_restaura_y_un_respaldo_de_otra_instalacion_se_elige_por_archivo`. |

**Visual en Windows.** `scripts\verificar-r5-ui.ps1` sobre el `bin` Release y sobre el paquete
publicado, con dos carpetas de datos temporales y sin enviar teclas. Instalación A: partido 1
iniciado («Respaldo previo creado»), gol local, finalizado («Respaldo de cierre creado»); respaldo
manual; con el partido 2 en juego, *Restaurar el seleccionado…* responde «No se puede restaurar …:
Hay un partido en juego…»; tras finalizarlo se restaura el respaldo previo al partido 1 y la mesa
vuelve a PROGRAMADO 0-0 con la base anterior guardada como «antes-de-restaurar-…». Instalación B
(otra carpeta, sin respaldos): *Restaurar desde otro archivo…* con el respaldo de cierre de A → la
mesa muestra el partido 1 FINALIZADO 1-0 con su gol. Capturas:
[lista](evidencia/respaldos-lista.png), [rechazo en juego](evidencia/respaldos-rechazo-en-juego.png),
[otra carpeta](evidencia/respaldos-restaurado-otra-carpeta.png).

**Sin verificar:** restaurar en otra computadora (se probó otra carpeta de datos en el mismo equipo);
imágenes en el respaldo (aún no existen, R7); restaurar un respaldo en una memoria USB de solo
lectura; con datos restaurados sin ningún partido, el marcador público conserva el último partido
mostrado hasta abrir otro.

### 1.10 R6 — resultados y exportación (2026-11-23)

**Automatizada — 264 pruebas, 0 fallos** (dominio 71, infraestructura 131, ViewModels 62). Nuevas:
`ReportTests` (17 casos, SQLite temporal) y `ResultsViewModelTests` (4).

| Comprobación | Prueba |
|---|---|
| Tras corregir un goleador y anular un gol, el resumen coincide con el marcador: resultado, goleadores, exclusiones, expulsiones, faltas, penales y tiempos muertos; la anulada aparece marcada. | `Los_totales_del_resumen_coinciden_con_el_marcador_tras_corregir_y_anular`. |
| Resumen del evento: solo suma partidos iniciados; no mezcla otro evento; goleadores agrupados por jugador a través de partidos; goles a favor y en contra. | `El_resumen_del_evento_suma_solo_partidos_iniciados_y_agrupa_goleadores_por_jugador`. |
| CSV: fórmulas neutralizadas (`=`, `+`, `-`, `@`), comillas y comas; UTF-8 con BOM y CRLF; un nombre de equipo malicioso no queda como fórmula. | `Un_campo_CSV_neutraliza_formulas_y_respeta_comillas` (8 casos), `El_CSV_exportado_es_UTF8_con_BOM_y_ningun_texto_del_catalogo_queda_como_formula`. |
| PDF: cabecera, tabla xref (cada desplazamiento apunta a su objeto), longitudes de los flujos, número de páginas, acentos en WinAnsi; varias páginas con encabezado repetido y texto cortado con «…». | `El_PDF_exportado_tiene_estructura_valida_y_el_texto_del_resumen`, `Un_resumen_largo_ocupa_varias_paginas_y_corta_lo_que_no_cabe`. |
| Destino bloqueado (abierto en otro programa): no se informa éxito, el archivo anterior queda y no quedan temporales. | `Si_el_destino_esta_bloqueado_no_se_reporta_exito_y_el_archivo_anterior_queda`. |
| Nombre sugerido sin acentos ni caracteres reservados. | `El_nombre_sugerido_no_lleva_acentos_ni_caracteres_reservados` (3). |
| Pestaña: resultados, goleadores y totales del partido elegido; exportar evento y partido en PDF y CSV; cancelar no exporta; abrir la pestaña desde la mesa actualiza. | `ResultsViewModelTests` (4). |

**Visual en Windows.** `scripts\verificar-r6-ui.ps1` sobre el `bin` Release y sobre el paquete
publicado, con datos temporales y sin enviar teclas: partido 1 terminado 2-1; la pestaña muestra
«2 – 1», los tres goleadores y los totales del partido; los cuatro archivos se exportan por el
diálogo «Guardar como» de Windows. **Fuera del programa:** PyMuPDF abre ambos PDF (1 página cada
uno), lee el título y el texto con acentos («Demostración», «Pacífico», «Página 1 de 1») y los
convierte en imagen; los CSV se leen como UTF-8. Capturas: [pestaña](evidencia/resultados-pestana.png),
[PDF del evento](evidencia/resultados-pdf-evento.png), [PDF del partido](evidencia/resultados-pdf-partido.png).

**Hallazgos corregidos en el recorrido:** la fecha y algunos encabezados («Exclusiones», «Faltas
personales») se cortaban en el PDF del evento; se reajustaron los anchos.

**Sin verificar:** abrir los archivos en Microsoft Excel y en un lector de PDF de escritorio (Edge,
Acrobat) con una persona; impresión en papel; nombres muy largos (se cortan con «…» en el PDF y van
completos en el CSV); la tanda (R8).

### 1.11 R7 — imágenes e identidad (2026-11-23)

**Automatizada — 296 pruebas, 0 fallos** (dominio 86, infraestructura 140, ViewModels 70). Nuevas:
`IdentityTests` (15 casos, dominio), `IdentityOperationsTests` (9, SQLite temporal) e
`IdentityViewModelTests` (8, ViewModels sin ventanas). Se ajustaron dos pruebas que suponían que el
esquema más nuevo era el v2 (`Migrar_de_uno_a_dos…`, `Un_respaldo_de_esquema_anterior…`): ahora usan
la última versión conocida y comprueban que la migración 0003 agrega la bandera y los auspiciadores.

| Comprobación | Prueba |
|---|---|
| PNG y JPEG se reconocen por su contenido con sus dimensiones; GIF, BMP, WebP, SVG y texto se rechazan con un mensaje que nombra el formato; más de 2 MB, menos de 16 px o más de 4096 px por lado se rechazan; un archivo truncado se informa como dañado. | `IdentityTests`: `Un_PNG_se_reconoce…`, `Un_JPEG_se_reconoce_aunque_el_archivo_tenga_otra_extension`, `Otros_formatos_se_rechazan…` (5), `Una_imagen_de_mas_de_2_MB…`, `Las_dimensiones_fuera_de_limite…` (4), `Un_PNG_o_JPEG_truncado…`. |
| La plantilla exige título, valida colores, admite hasta 8 auspiciadores y exige que cada imagen sea del tipo que ocupa. | `La_plantilla_exige_titulo_valida_colores_y_limita_auspiciadores`, `Cada_imagen_de_la_plantilla_debe_ser_del_tipo_que_ocupa`. |
| Escudo y bandera se guardan, repetir no escribe, reemplazar o quitar borra la imagen que nadie usa; tipo equivocado o equipo inexistente no escriben. | `Escudo_y_bandera_se_guardan_y_reemplazar_borra…`, `Una_imagen_de_otro_tipo_o_un_equipo_inexistente…`. |
| La plantilla del evento se guarda con auspiciadores en orden, se reordena y se quita; una plantilla inválida no escribe. | `La_plantilla_del_evento_se_guarda_con_auspiciadores_en_orden…`, `Una_plantilla_invalida_se_rechaza_con_el_motivo`. |
| Al abrir un partido se publica su identidad; cambiar escudo, bandera o plantilla la vuelve a publicar; una incidencia no la relee. | `Al_abrir_un_partido_se_publica_su_identidad_y_cada_cambio_la_actualiza`. |
| **Las imágenes viajan en el respaldo** y se restauran en otra carpeta de datos con la misma huella SHA-256. | `Las_imagenes_viajan_en_el_respaldo_y_se_restauran_identicas`. |
| Cargar un archivo valida antes de guardar y el error nombra el archivo (texto, más de 2 MB, inexistente). | `Al_cargar_un_archivo_se_valida_antes_de_guardar…`. |
| La plantilla de ejemplo del Panamericano carga recursos genéricos válidos, marcados como ejemplo, y se puede guardar; la demo trae escudos genéricos para sus dos clubes ficticios. | `La_plantilla_de_ejemplo_del_Panamericano…`, `La_demo_trae_escudos_genericos…`. |
| Exclusión y tiempo muerto se anuncian una vez; corregir y anular no repiten; reabrir el marcador no repite. | `La_exclusion_y_el_tiempo_muerto_se_anuncian_una_vez…`. |
| Cada tipo de aviso se desactiva por separado y el interruptor general apaga todos; el penal convertido se anuncia como gol; la falta y el penal fallado no se anuncian. | `Cada_tipo_de_aviso_se_desactiva_por_separado…`, `El_penal_convertido_se_anuncia_como_gol…`. |
| Marcador y mesa muestran la identidad del partido abierto y se actualizan al cambiarla; la identidad de otro partido no se aplica. | `El_marcador_y_la_mesa_muestran_la_identidad…`, `La_identidad_de_otro_partido_no_se_aplica…`. |
| Escudo y bandera desde la pestaña; un archivo inválido no cambia nada; editor de identidad con plantilla de ejemplo, auspiciador agregado y reordenado, guardado al confirmar; plantilla inválida no se guarda; cancelar no deja rastro. | `El_escudo_del_equipo_se_carga_desde_un_archivo…`, `El_editor_carga_la_plantilla_de_ejemplo…`, `El_editor_no_guarda_una_plantilla_invalida…`. |

**Visual en Windows.** `scripts\verificar-r7-ui.ps1` sobre el `bin` Release y sobre el paquete
publicado `artifacts\publish\win-x64`, con datos temporales y sin enviar teclas (el diálogo «Abrir»
recibe mensajes dirigidos): TIB muestra su escudo demo; se carga `bandera-ficticia-1.png` → «Bandera
de TIB guardada: … 360×240 px»; un `.png` que es texto → «No se cargó «no-es-imagen.png»: El archivo no
es una imagen reconocible…» y el escudo no cambia; *Identidad y plantilla…* → *Usar plantilla de
ejemplo* → se agrega un auspiciador → «Auspiciadores (4 de 8)» → guardar. La cabecera de la mesa
muestra los escudos y la bandera; el marcador muestra título, subtítulo, ambos logos, escudos,
bandera y 4 auspiciadores. Con el partido iniciado: exclusión → aviso rojo «EXCLUSIÓN · Delfines del
Pacífico · 5 Tomás Brisa», cuyo rectángulo en pantalla no se superpone con los de los dos relojes;
tiempo muerto → aviso azul; con «exclusión» desmarcado en la mesa, una exclusión nueva no se anuncia;
cerrar y reabrir el marcador no repite avisos. Capturas: [marcador previa](evidencia/identidad-marcador-previa.png),
[aviso de exclusión](evidencia/identidad-aviso-exclusion.png), [aviso de tiempo muerto](evidencia/identidad-aviso-tiempo-muerto.png),
[editor de identidad](evidencia/identidad-editor.png), [escudo y bandera del equipo](evidencia/identidad-equipo.png).

**Hallazgos corregidos en el recorrido:** el título largo de la plantilla se cortaba con «…»; ahora
se reduce para entrar completo. Escudo y bandera se achicaron para dejar más ancho al nombre del
equipo. El selector de archivos y las confirmaciones abiertos desde el editor de identidad toman como
dueña la ventana activa (antes, la mesa).

**Sin verificar:** marcador a pantalla completa en un segundo monitor y en la pantalla LED (legibilidad
de logos y auspiciadores a distancia); imágenes oficiales reales (no se entregaron); JPEG progresivos o
con perfiles de color poco comunes guardados por otros programas (los de prueba son sintéticos o PNG);
nombres de equipo muy largos junto a escudo y bandera (se reducen, sin cortarse).

### 1.12 R8 — prórroga y tanda de penales (2026-11-24)

**Automatizada — 333 pruebas, 0 fallos** (dominio 111, infraestructura 147, ViewModels 75). Nuevas:
`OvertimeRulesTests`, `OvertimeTests` y `ShootoutTests` (25 casos, dominio),
`OvertimeShootoutOperationsTests` (7, SQLite temporal) y `OvertimeShootoutViewModelTests` (5,
ViewModels sin ventanas). Se cambió el fixture de base v1 (`CreateVersionOneAsync`): genera los datos
demo en una base aparte con el esquema actual y copia a la base v1 solo las columnas que existían en
v1, porque el sembrador ya escribe columnas de migraciones posteriores.

| Comprobación | Prueba |
|---|---|
| El preset DEMO no tiene prórroga ni tanda; con prórroga las reglas quedan «sin aprobación técnica»; sin prórroga, su duración no cambia el preset; valores fuera de rango se rechazan. | `OvertimeRulesTests` (7 casos). |
| La prórroga empieza solo con empate al terminar el reglamentario; sus periodos se juegan completos aunque se desempate; no se pasa de la última; sus goles suman y llevan su periodo. | `OvertimeTests` (5). |
| La tanda exige tanda en las reglas, todos los periodos jugados (también la prórroga) y empate; se puede iniciar desde el último periodo o su descanso. | `La_tanda_se_inicia_con_empate…`, `…sin_descanso`, `Sin_tanda_en_las_reglas…`, `Sin_empate_no_hay_tanda`, `Con_prorroga_configurada_la_tanda_espera…`. |
| **Los lanzamientos no suman al resultado reglamentario, a los goleadores ni a los totales**; exigen jugador del equipo y resultado; fuera de la tanda no se registran y durante ella no hay goles ni penales de juego; se corrigen y anulan con revisiones. | `Los_lanzamientos_no_suman…`, `El_lanzamiento_exige_jugador…`, `Fuera_de_la_tanda_no_se_registran…`, `Un_lanzamiento_se_corrige_y_se_anula…`. |
| No se finaliza con la tanda empatada; reabrir un partido cerrado en la tanda vuelve a la tanda; con tanda no se acepta un cambio que desempate el reglamentario (sí cambiar el goleador dentro del equipo); la tanda se anula con motivo solo sin lanzamientos vigentes y entonces el reglamentario se puede corregir. | `No_se_finaliza_con_la_tanda_empatada`, `Al_reabrir_…vuelve_a_la_tanda`, `Con_tanda_no_se_acepta_un_cambio…`, `La_tanda_se_anula_con_motivo…`. |
| Reglas de prórroga guardadas en el evento y copiadas al partido; partido con prórroga y tanda recuperado tras reiniciar con la tanda separada; mismo comando dos veces no duplica; tanda anulada en el historial con su motivo. | `OvertimeShootoutOperationsTests`: `Las_reglas_de_prorroga_se_guardan…`, `Partido_con_prorroga_y_tanda_se_recupera…`, `Iniciar_la_tanda_dos_veces…`, `La_tanda_anulada_queda_en_el_historial…`. |
| La base rechaza un lanzamiento sin resultado y no deja borrarlo. | `La_base_rechaza_un_lanzamiento_sin_resultado…`. |
| **Resumen:** reglamentario 2–2 con «(pr.)», tanda 0–1 aparte, lanzamientos fuera de las incidencias de juego, periodos de prórroga como «Pr. 1», goles del evento sin la tanda; CSV del partido con la sección «Tanda de penales» y CSV del evento con la columna «Tanda». | `El_resumen_separa_la_tanda_del_resultado…`. |
| **Migración 0004** sobre una base v1 con un partido en juego: mismas filas en partidos, incidencias, revisiones y cambios de estado; `foreign_key_check` vacío; índice y cinco disparadores recreados; ninguna tabla `_new` sobrante; las incidencias siguen sin poder modificarse; el partido sigue operable. | `Migrar_a_la_version_actual_reconstruye_las_tablas…`. |
| Mesa: «Iniciar prórroga» solo si queda prórroga; «Iniciar tanda» deshabilitado con el motivo; en la tanda F6/F9 registran lanzamientos y F5/F8 quedan deshabilitados; los lanzamientos no disparan avisos; el marcador muestra la tanda aparte con sus marcas; al finalizar vuelven las acciones de juego. | `La_mesa_pasa_por_prorroga_y_tanda…`, `Sin_tanda_en_las_reglas_el_boton…`, `La_tanda_anulada_desde_la_mesa…`. |
| Editor de reglas: lee y valida la prórroga. Resultados: columna de la tanda. | `El_editor_de_reglas_lee_y_valida_la_prorroga`, `Los_resultados_muestran_la_tanda_en_su_columna`. |

**Visual en Windows.** `scripts\verificar-r8-ui.ps1` sobre el `bin` Release y sobre el paquete
publicado `artifacts\publish\win-x64`, con datos temporales y sin enviar teclas (los cuadros de
confirmación reciben un mensaje dirigido a «Sí»): en *Reglas del evento…* «dos» periodos de prórroga
→ «Revise: Escriba los periodos de prórroga como número entero…»; 1 × 3:00 y tanda → «Personalizadas
del evento (sin aprobación técnica)». 1–1 tras el cuarto periodo: el botón dice «Iniciar prórroga» e
«Iniciar tanda» está deshabilitado con la ayuda «La tanda se inicia después de jugar los periodos
reglamentarios y la prórroga.». Prórroga → mesa y marcador «PRÓRROGA 1»; 2–2; descanso → «Iniciar
tanda» habilitado → confirmación → mesa «TANDA DE PENALES», marcador «PENALES», acción
preseleccionada «Tanda: convertido», GOL local y visita deshabilitados. Cuatro lanzamientos → «Penales:
DEL 2 – 1 TIB»; el marcador sigue 2–2 y muestra «PENALES 2» y «PENALES 1» con sus círculos, sin
superponerse con los relojes (rectángulos en pantalla) y sin aviso animado. Finalizar → «RESULTADO
FINAL» con la tanda visible. *Resultados y respaldos* → «Delfines del Pacífico 2 – 2 (pr.) Club
Deportivo Tiburones del Norte · penales 2 – 1 · Finalizado». Capturas: [marcador en la tanda](evidencia/tanda-marcador.png),
[marcador final](evidencia/tanda-marcador-final.png), [marcador en prórroga](evidencia/prorroga-marcador.png),
[mesa en la tanda](evidencia/tanda-mesa.png), [editor de reglas](evidencia/tanda-reglas.png),
[resultados](evidencia/tanda-resultados.png).

**Hallazgos corregidos en el recorrido y las pruebas:** al entrar en la tanda quedaba preseleccionada
«Exclusión» en lugar de «Tanda: convertido» (orden de la lista); el botón de siguiente periodo decía
«Siguiente prórroga» cuando ya no quedaba ninguna; la columna «Per.» recortaba «Tanda»; guardar las
reglas del evento descartaba los valores de prórroga (`UpdateTournamentRulesAsync` los normalizaba sin
ellos). La primera captura del marcador en la tanda mostraba el aviso del último gol de la prórroga
(dura 4 s y el recorrido automatizado es más rápido): el recorrido ahora lo deja terminar y comprueba
que ningún lanzamiento cambia el aviso.

**Sin verificar:** la tanda a pantalla completa en un segundo monitor y en la pantalla LED (tamaño y
legibilidad de «PENALES» y los círculos a distancia); una tanda larga con muerte súbita (más de 10
lanzamientos por equipo muestran los últimos 10; probado solo en el ViewModel por construcción, no en
pantalla); el PDF con la sección de la tanda abierto en un lector de escritorio (el CSV se comprobó en
prueba; el PDF usa el mismo documento y el mismo escritor validado en R6).

### 1.13 R9 — reproducción de cronometraje (2026-11-24)

**Automatizada — 361 pruebas, 0 fallos** (dominio 111, infraestructura 171, ViewModels 79). Nuevas:
`ReplayTimingTests` (24 casos, con un reloj controlable y SQLite temporal) y `ReplayViewModelTests`
(4, ViewModels sin ventanas).

| Comprobación | Prueba |
|---|---|
| La grabación de ejemplo se lee completa: 38 s, sesiones 1 y 2, hueco de 20 a 24 s, posesión apagada desde 30 s. | `La_grabacion_de_ejemplo_se_lee_completa`. |
| Un archivo inválido se rechaza entero con la línea y el motivo: vacío, sin formato o encabezado, columnas de más o de menos, tiempo o sesión que retroceden, relojes negativos o de más de 2 h, sin ningún reloj, marcha, periodo o precisión inválidos, «fin» anterior o datos después de «fin». | `Un_archivo_invalido_se_rechaza_con_la_linea_y_el_motivo` (15 casos). |
| **Determinista:** dos reproducciones de la misma grabación, consumidas en pasos que no coinciden con las muestras, entregan los mismos valores con la misma marca de tiempo relativa. | `Dos_reproducciones_de_la_misma_grabacion_entregan_lo_mismo…`. |
| El hueco es pérdida de señal (relojes sin valor); la sesión nueva vuelve sin heredar valores; cada reloj conserva su vigencia por separado; al terminar, la señal figura perdida. | `La_senal_se_pierde_en_el_hueco_y_vuelve_con_una_sesion_nueva…`. |
| El avance se avisa una vez por segundo también sin muestras y al terminar; reiniciar vuelve al comienzo con sesiones nuevas. | `El_avance_se_avisa_una_vez_por_segundo…`, `Reiniciar_vuelve_al_comienzo…`. |
| Archivo inválido, ausente o que no es UTF-8: la fuente queda «no disponible» con el motivo, sin relojes y con incidencias sin hora de juego. | `Un_archivo_invalido_deja_la_fuente_no_disponible…`, `Un_archivo_ausente_o_que_no_es_UTF8…` (2). |
| Una incidencia registrada durante la reproducción guarda el reloj con origen `Replay` y la base lo acepta. | `Una_incidencia_registrada_durante_la_reproduccion…`. |
| `--reproduccion` solo con el perfil demo; la mesa muestra el avance, el fin y reinicia; sin grabación cargada lo dice y no aparece el simulador; el marcador muestra «REPRODUCCIÓN»; el diagnóstico deja la consola en gris («no se usa»). | `ReplayViewModelTests` (4). |

**Visual en Windows.** `scripts\verificar-r9-ui.ps1` sobre el `bin` Release y sobre el paquete
publicado, con datos temporales y la grabación sintética de `samples\cronometraje`, en tiempo real:
la mesa y el marcador muestran «REPRODUCCIÓN», el simulador no aparece y el panel indica «0:03 de
0:38»; un gol queda registrado con reloj 7:59. En el hueco de la grabación la mesa pasa a «SIN SEÑAL
DE TIEMPO» y el marcador a `--:--` / `--` con «SEÑAL DE TIEMPO NO DISPONIBLE»; con la sesión 2 vuelve
a 7:42 / 18. Al terminar, el panel dice «reproducción terminada (0:38)» y los relojes quedan sin
señal; *Reiniciar reproducción* vuelve a 8:00 / 30. Con un archivo roto: «FUENTE DE TIEMPO NO
DISPONIBLE» y el aviso «No se pudo cargar la reproducción «grabacion-rota.csv». Línea 2: …», sin
simulador. Con `--perfil produccion --reproduccion …`: mensaje «La reproducción de cronometraje solo
se usa con el perfil demo.» y el programa se cierra sin crear la carpeta de datos. Capturas:
[marcador](evidencia/reproduccion-marcador.png), [corte de señal](evidencia/reproduccion-sin-senal.png),
[mesa al terminar](evidencia/reproduccion-mesa-terminada.png), [archivo inválido](evidencia/reproduccion-archivo-invalido.png).

**Hallazgo corregido en el recorrido:** el panel nunca llegaba a «reproducción terminada»: se
actualizaba solo cuando cambiaba la vista del reloj, que tras la pérdida final ya no cambia. Ahora
la fuente avisa su avance una vez por segundo (prueba nueva para ese caso).

**Sin verificar / no cubierto:** la reproducción de **capturas reales** del Colorado (no hay acceso
ni protocolo; RF11 sigue bloqueado) y la conversión de esas capturas al formato normalizado.

### 1.14 Hito B, preparación — decodificador de tablero y herramienta de captura (2026-11-24)

**Automatizada — 391 pruebas, 0 fallos** (dominio 111, infraestructura 201, ViewModels 79). Nueva:
`CtsScoreboardTests` (30 casos). **Todos los bytes son sintéticos**, armados según el formato que
describen terceros (colorado-system6.md §6): fijan la implementación, **no validan el protocolo**.

| Comprobación | Prueba |
|---|---|
| Bytes de dirección y de dato según el formato descrito (canal 1 = 0xBC, formato = 0xBD, dígito invertido, 15 = en blanco). | `Los_bytes_de_direccion_y_de_dato_siguen_el_formato_descrito`. |
| Un canal se entrega completo al llegar otro, con dígitos en blanco y su formato; tramas cortadas en cualquier byte o varias juntas dan lo mismo; datos sin canal se descartan y se cuentan; un formato sin lectura se recuerda; reiniciar lo olvida. | `Un_canal_se_entrega_completo…`, `Tramas_cortadas_en_cualquier_punto_o_juntas…`, `Los_datos_sin_canal_se_descartan…`, `Un_formato_sin_su_lectura…`. |
| Partido m:ss y último minuto ss.d; posesión con una marca y décimas; periodo; canales configurables. | `El_reloj_de_partido_se_lee…` (5), `La_posesion_se_lee…` (3), `El_periodo_acompana…`. |
| **Ante ambigüedad no hay valor**: sin formato, sin marcas, segundos ≥ 60, minutos de más de dos cifras, marca en blanco, posesión con dos marcas o mayor que 120 s. | `Una_lectura_ambigua_o_fuera_de_rango_del_partido…` (5), `Una_posesion_ambigua…` (3). |
| Captura inválida rechazada con la línea; captura convertida en grabación normalizada que se reescribe y relee igual, con la reconexión como sesión 2, y se reproduce; canales equivocados lo dicen; el análisis muestra los canales y sus dígitos. | `Una_captura_invalida…` (6), `Una_captura_se_convierte…`, `Una_captura_sin_los_canales_supuestos…`. |

**Herramienta, ejecutada aquí sin consola.** `samples\cronometraje\captura-sintetica-cts.txt` se generó
con un codificador **independiente en Python** (cruce con el de C#). `analizar` muestra los canales 1,
3, 13 y 14 con 8:00 → 7:59… y la posesión reiniciada a 30 a los 6 s; `convertir` produce 90 lecturas,
incluido el último minuto con décimas (45.0 → 43.0) tras la reconexión. `puertos` lista COM3 y COM4
de este equipo, que **no se abrieron**. `capturar` con un puerto inexistente reintenta cada segundo
sin fallar y deja una captura vacía.

**No verificado:** todo lo que dependa de la consola real — que el jack entregue este formato, los
canales de la FDNDA, la interpretación de las marcas, el nivel eléctrico y el cable en Y sin afectar
al tablero (P14–P17 siguen pendientes).

### 1.15 Hito B — lectura de COM PORT 2 de la consola (2026-11-24)

**Captura real ejecutada** con la System 6 prestada por la FDNDA (sin tablero), adaptador USB a RS-232
en COM PORT 2, herramienta de captura en solo lectura. Formato y casos en
[colorado-system6.md §7](colorado-system6.md); capturas en `docs/evidencia/capturas/`.

**Automatizada — 430 pruebas, 0 fallos** (dominio 111, infraestructura 232, ViewModels 87). Nuevas:
`CtswpDecoderTests` (24), `ColoradoSourceTests` (7), `StartupOptionsTests` (8); se ajustó
`Colorado_sin_puerto_configurado…` (antes «pendiente»).

| Comprobación | Prueba |
|---|---|
| Trama en reposo → partido, posesión, periodo; segundos enteros; sin estado de marcha. Formatos `m:ss`, `mm:ss`, `:ss`; posesión en blanco. | `Una_trama_en_reposo…`, `El_tiempo_de_partido_admite…` (4), `Posesion_en_blanco…`. |
| **P16 (parte automatizada)**: bytes sueltos descartados y contados; cortada byte a byte = entera; trama cortada por otra; Reset; encabezado, longitud, segundos, minutos, posesión, periodo o cierre inválidos → sin lectura y contada; flujo sin fin acotado. | `Los_bytes_sueltos…`, `La_misma_secuencia_cortada…`, `Una_trama_cortada_por_el_inicio…`, `Reset_descarta…`, `Una_trama_invalida…` (8), `Un_flujo_sin_fin…`. |
| **Capturas reales**: 323 tramas de la guiada sin descartes (6:00 → 5:59, Reset → 28, «18» → 18); último minuto `:34` y cambio de periodo; cable desenchufado y consola apagada: 302 válidas, 0 inválidas, 44 bytes sueltos, partido nuevo tras el apagado; conversión con una sesión por conexión. | `La_captura_real_guiada…`, `La_captura_real_trae_el_ultimo_minuto…`, `La_captura_real_con_cable_desenchufado…`, `La_captura_real_se_convierte…`, `El_formato_de_la_captura…`. |
| Fuente Colorado con transporte de prueba: muestras de la fuente Colorado con sesión y secuencia; inválidas no publicadas y contadas; pérdida del puerto → sesión nueva sin restos; puerto que no abre → reintentos crecientes con tope de 5 s, sin declararse no disponible; un fallo repetido se anota una vez; sin puerto → «no disponible» con el motivo. | `ColoradoSourceTests` (7), `Colorado_sin_puerto_configurado…`. |
| `--puerto COMx` solo en producción, normalizado y validado. | `StartupOptionsTests` (8). |

**Programa ejecutado aquí sin la consola** (`--perfil produccion --puerto COM5`, adaptador
desenchufado): arranca, la mesa muestra `--:--`, `--` y «Esperando datos de la fuente de tiempo», el
registro anota una vez «COM5 … no disponible» y sigue reintentando. Herramienta: `analizar` y
`convertir` reconocen las capturas CTSWP; `samples/cronometraje/consola-real-s6-guiada.csv` (323
lecturas) se generó de la captura real. Intervalo entre tramas medido: mediana 1,0 s, máximo 1,07 s
sin cortes; el umbral de vigencia provisional (1,5 s) queda por encima.

**No verificado:** el programa leyendo la consola **en vivo** por `SerialColoradoTransport` (P14, P15,
P17 y la parte con equipo real de P16), la latencia de extremo a extremo y el número de serie.

### 1.16 Hito B — prueba en vivo con la consola (2026-11-24)

`artifacts\publish\win-x64\FDNDA.Waterpolo.exe --perfil produccion --puerto COM5 --datos G:\prueba-consola`,
adaptador en COM PORT 2. El registro muestra «COM5 … abierto» y la señal en vivo 0,06 s después; la
mesa mostró 6:00 y 28 como la consola. El operador recorrió inicio y pausa (30 s sin aviso de pérdida),
Reset y «18», desenchufe en juego y en pausa (dos reconexiones, 0 tramas descartadas en 267) y
*Period +1*: **todo coincidió** según el operador.

**Observaciones.** (1) **En el descanso entre periodos la consola no emite**: tras *Period +1* la mesa
pasa a «sin señal» hasta que empieza el periodo siguiente (36 s y 18 s en vivo; hasta 117 s en las
capturas). **Resuelto por decisión del usuario:** en el descanso de la mesa se muestra «Descanso», sin alarma ni
contingencia (`SignalState.Intermission`; pruebas `En_el_descanso_de_la_mesa…` del tracker, de la
fuente Colorado con la mesa real y de la mesa y el diagnóstico en `ColoradoDeskTests`). La mesa avisa
si el periodo de la consola no coincide con el suyo (`La_mesa_avisa_si_el_periodo…`). **Verificado en
vivo** (22:43–22:47 UTC, partidos de ejemplo ficticios en la carpeta de prueba): con *Iniciar descanso* el
registro pasa a `Intermission` en el mismo instante del cambio en la mesa (BreakStarted 22:44:09.757 →
Intermission 22:44:09.768) y vuelve a `Live` al empezar la consola el periodo siguiente; con la consola
en otro periodo, la mesa mostró el aviso naranja y desapareció al ajustar la mesa (según el operador).
Corregido además un aviso falso «dejó de entregar datos» al cerrar el programa
(`Al_cerrar_el_programa_no_se_anota_una_perdida_de_datos`). **440 pruebas, 0 fallos.** (2) La mesa no muestra el
periodo de la consola: el periodo oficial es el del partido abierto en la mesa (*Siguiente periodo*); en
esta prueba no había partido abierto.

**No verificado:** latencia medida, P16 con equipo, un partido completo con incidencias.

### 1.17 Rediseño del marcador del público «A · Transmisión» (2026-11-25)

Diseño elegido y aprobado por el usuario en maquetas (tres direcciones; avisos «toma del lado»). Spec:
`docs/superpowers/specs/2026-11-25-marcador-publico-design.md`; plan:
`docs/superpowers/plans/2026-11-25-marcador-publico.md`. Solo cambian la ventana del marcador, su
ViewModel y ayudas de presentación (`ColorTools`, `ScoreboardTexts`, conversores); dominio, base, reglas
y lectura de la consola no cambian.

| Criterio | Evidencia automatizada (`MarcadorDisenoTests`, sin ventanas) |
|---|---|
| Colores: texto negro sobre colores claros y blanco sobre oscuros; oscurecer y opacidad; un color inválido no falla. | `Los_colores_claros_llevan_texto_negro…` (5 casos), `Oscurecer_y_dar_opacidad…`. |
| Ordinales del público («1.er», «2.º», «3.er», «4.º», «11.º»). | `Los_ordinales_son_los_del_publico` (5 casos). |
| Previa («VS», sin relojes), juego («1.er PERIODO», puntos), descanso («FIN DEL 1.er PERIODO», «DESCANSO», sin relojes), final con el perdedor atenuado. | `El_marcador_pasa_por_previa_juego_descanso_y_final…`, `Un_final_empatado_no_atenua_a_nadie`. |
| Tanda sin relojes, con «Reglamentario 0 – 0»; el final atenúa al que pierde la tanda y anota «Penales 0 – 1». | `En_la_tanda_no_hay_relojes…`; ajuste aprobado en `OvertimeShootoutViewModelTests` (en la tanda `ShowClocks` es falso). |
| Prórroga sin puntos de periodo; «FIN DE LA PRÓRROGA 1»; final «Tras prórroga». | `En_la_prorroga_no_hay_puntos_de_periodo…`. |
| Anillo de posesión = lectura ÷ posesión de las reglas (18 s de 30 → 0,6; tope 1); sin partido o sin señal, 0, relojes atenuados y `--:--` / `--`. | `El_anillo_de_posesion_es_la_fraccion…`. |
| Avisos: lado, colores, titular, gorro y jugador; penal convertido como «¡GOL DE PENAL!»; expulsión distinta de la exclusión; tiempo muerto sin jugador; solo los avisos nuevos suben `BannerGeneration` (corregir, anular o un tipo desactivado no). | `El_gol_sale_del_lado…`, `La_exclusion_y_la_expulsion…`, `El_tiempo_muerto_sale_en_azul…`, `Cada_aviso_nuevo_sube_la_generacion…`; las pruebas de R7 siguen pasando sin cambios. |

**Recorrido en el ejecutable** (`scriptserificar-marcador-diseno-ui.ps1`, perfil demo con datos
aislados bajo `artifactserificacion-diseno-…`, marcador en modo ventana 1280×720, capturas con
PrintWindow): previa, en juego con el simulador, gol local (aviso, número que late), exclusión, expulsión,
tiempo muerto, gol visitante, pérdida de señal (`--:--`, `--`, aviso rojo bajo el anillo), descanso y
resultado final 2–1. En los cuatro avisos el rectángulo en pantalla del aviso **no se superpone** con el
reloj, la posesión ni la línea de periodo. **460 pruebas, 0 fallos.**

**Hallazgos corregidos en el recorrido:** el `--:--` sin señal se veía como bloques (el peso fijo del texto
impedía aplicar el peso fino; pasó al estilo) y el resultado rozaba el nombre del equipo (más margen).

**No verificado:** pantalla completa en el segundo monitor y en la pantalla LED, y otras proporciones de
pantalla (el formato del LED del evento aún no se conoce; hito C). La tipografía Bahnschrift viene con
Windows 10/11; en un equipo sin ella WPF usa Segoe UI.

### 1.18 R10 — reprogramar, actualizar nómina y anular; menores del hito A (2026-11-25)

**Automatizada — 488 pruebas, 0 fallos** (dominio 121, infraestructura 249, ViewModels 118). Nuevas: 10 en
`RescheduleTests`, 10 en `RescheduleOperationsTests`, 7 en `RescheduleViewModelTests` y 1 en `RosterImportTests`.

| Comprobación | Prueba |
|---|---|
| Reprogramar cambia descripción, fecha (o la deja vacía), colores y gorros; equipos intactos; queda en el historial; sin cambios o con datos inválidos no deja rastro. | `Reprogramar_cambia_descripcion_fecha…`, `Reprogramar_puede_dejar_el_partido_sin_fecha`, `Reprogramar_sin_cambios_se_rechaza…`, `Reprogramar_con_datos_invalidos_no_cambia_nada`. |
| Persistencia, reinicio e idempotencia; solo se publica si es el partido abierto, sin animaciones. | `Reprogramar_guarda_descripcion…_y_sobrevive_al_reinicio`, `Reprogramar_con_el_mismo_comando_no_repite_el_cambio`, `Reprogramar_el_partido_abierto_lo_publica_sin_animar_y_otro_no_cambia_la_mesa`. |
| Actualizar nómina copia la vigente del evento (alta, gorro cambiado, baja); igual a la actual se rechaza; un gorro repetido conserva la anterior; tras actualizar se registran goles de los nuevos. | `Actualizar_la_nomina_reemplaza…`, `…igual_a_la_del_partido_se_rechaza`, `…con_gorro_repetido…`, `Actualizar_la_nomina_copia_la_vigente_del_evento…`, `Tras_actualizar_la_nomina_se_registran_goles…`. |
| Anular exige motivo, no borra, no se puede iniciar ni modificar; no cuenta en el evento ni en resultados; el resumen lo lista como «Anulado». | `Anular_exige_motivo…`, `Un_partido_anulado_no_se_inicia…`, `Anular_conserva_el_partido_con_motivo…`, `El_partido_anulado_aparece_como_tal_en_el_resumen…`. |
| Un partido iniciado rechaza las tres acciones (dominio y casos de uso); la base impide borrar su nómina y estados desconocidos. | `Un_partido_iniciado_no_se_reprograma…` (dominio e infraestructura), `La_base_impide_cambiar_el_estado…`. |
| Migración 0005 sobre una base v1 con un partido en juego. | `Migrar_una_base_antigua_permite_anular_el_programado…`; la prueba v1→actual de R8 sigue pasando. |
| Interfaz: cargar en el formulario y guardar sin crear otro; cancelar; el partido abierto se actualiza en la mesa; anular pide motivo (cancelar el diálogo no hace nada); actualizar nómina pide confirmación; acciones deshabilitadas en iniciados y anulados. | `RescheduleViewModelTests` (7 casos). |
| *Actualizar* conserva colores y gorros escritos; cambiar de equipo sí propone los del catálogo. | `Actualizar_conserva_los_colores_y_gorros_escritos_en_el_formulario`. |
| XLSX guardado por Microsoft Excel 16 (no por una librería) se lee igual que la CSV. | `Un_XLSX_guardado_por_Microsoft_Excel_se_importa_como_la_CSV` con `samples/plantilla-nomina-excel.xlsx`. |

**Visual en Windows** (`scripts\verificar-r10-ui.ps1`, `bin` Release y paquete publicado regenerado, datos temporales, sin teclas): reprogramar
el partido demo 2 con los combos de evento y equipos deshabilitados; la lista muestra la nueva descripción y
«20/03/2027 18:00» y el formulario vuelve a «Programar partido»; *Actualizar nómina* sin cambios en el evento
muestra «La nómina del partido ya coincide con la del evento.»; anular con motivo deja «Anulado» y deshabilita
las tres acciones; tras *Actualizar* siguen «#123456» y el gorro escrito a mano; un equipo de 80 caracteres se ve
recortado con «…» en la lista. Revisadas las capturas.

**No verificado:** pulsación física de teclas y otras resoluciones; la ayuda emergente del nombre largo se
comprobó en el XAML y las pruebas, no con el ratón.

### 1.19 Avisos de falta, fin de tiempo muerto y periodo; faltas por jugador (2026-11-25)

Pedido del usuario: animaciones de expulsión (ya existían), fin del tiempo muerto, faltas, etc., y la
cantidad de faltas de cada jugador en pantalla. Decisiones: faltas = personales + exclusiones + expulsiones
vigentes (solo se cuentan; no hay sanción automática); gorros con tres puntos bajo el resultado; el fin del
tiempo muerto se toma del reloj de tiempo muerto de la consola (bytes 21–24 de `CTSWP1.1c`); avisos extra
de falta personal, inicio de periodo, descanso, tanda y final. Spec:
`docs/superpowers/specs/2026-11-25-avisos-y-faltas-design.md`.

**Automatizada — 514 pruebas, 0 fallos** (dominio 122, infraestructura 262, ViewModels 130). Nuevas: 1 en
`IncidentTests`, 13 en `TiempoMuertoTests` y 12 en `AvisosFaltasTests`; 2 pruebas de R7 ajustadas al pedido
(la exclusión agrega «1.ª FALTA» y la falta personal ahora se anuncia; el penal fallado sigue sin anunciarse).

| Comprobación | Prueba |
|---|---|
| Faltas por jugador: personales + exclusiones + expulsiones; el lanzador de un penal y el goleador no suman; corregir el jugador las mueve y anular las quita. | `Las_faltas_de_cada_jugador_suman_personales_exclusiones_y_expulsiones_vigentes`. |
| Aviso de falta personal en ámbar con gorro y «1.ª FALTA»; la exclusión siguiente del mismo jugador dice «2.ª FALTA»; el gol no lleva línea. | `La_falta_personal_sale_en_ambar…`, `La_linea_de_faltas_usa_el_ordinal_femenino`. |
| Gorros con faltas bajo el resultado, por gorro; ocultos en la previa; el de la falta nueva late; anular la quita; más de tres: tres puntos y el número. | `Los_gorros_con_faltas_se_ven_bajo_el_resultado_y_el_nuevo_late`, `Con_mas_de_tres_faltas…`, `Sin_el_aviso_de_falta_no_hay_animacion_pero_el_gorro_suma`. |
| Anuncios de inicio, descanso, periodo siguiente y final; abrir el partido, un gol o reabrir no anuncian; casillas desactivadas. | `Inicio_descanso_periodo_y_final_se_anuncian_y_reabrir_no`, `Sin_el_anuncio_de_periodos_no_se_anuncia_nada`. |
| Trama: reloj de tiempo muerto `1:00`, ` :59`, ` :00`, en blanco; un valor inválido descarta la trama. | `La_trama_entrega_el_reloj_de_tiempo_muerto` (3 casos), `Sin_tiempo_muerto_el_campo_va_en_blanco…`. |
| **Capturas reales de la consola** (`s6-com2-03`, `s6-com2-04`): el tiempo muerto empieza en 1:00 y su fin («Clear Timeout») se cuenta una sola vez. | `En_las_capturas_reales_hay_un_tiempo_muerto…` (2 capturas). |
| Fin detectado al llegar a 0 o al borrarse; no otra vez de 0 a blanco; no por pérdida de señal ni reconexión; sin señal no se muestra la cuenta. | `El_fin_se_cuenta_al_llegar_a_cero…`, `Una_perdida_de_senal_o_una_reconexion_no_cuentan_como_fin`. |
| Simulador: el tiempo muerto detiene el partido, corre aparte, queda en 0 y se termina a mano. Formato `1:00` / `59`. | `El_simulador_corre_el_tiempo_muerto_aparte…`, `Formato_del_reloj_de_tiempo_muerto` (4 casos). |
| Marcador: anillo azul con la cuenta y fracción sobre el mayor valor leído; al terminar, aviso «FIN DEL TIEMPO MUERTO» del equipo del último tiempo muerto registrado en el periodo; sin registro en el periodo no hay aviso; al abrir la pantalla o con la casilla apagada no anima. | `Durante_el_tiempo_muerto_el_anillo_muestra_su_cuenta…`, `Sin_tiempo_muerto_registrado_en_el_periodo…`, `Al_abrir_la_pantalla_no_se_anuncian…`. |

**Recorrido en el ejecutable** (`scripts\verificar-avisos-faltas-ui.ps1`, `bin` Release, perfil demo con
datos aislados bajo `artifacts\verificacion-avisos-…`, marcador en ventana 1280×720, capturas con
PrintWindow, sin teclas): anuncio de inicio con el brillo que cruza; falta personal «1.ª FALTA» y exclusión
del mismo gorro «2.ª FALTA»; gorros en pantalla «Gorro 2, Luis Marea: 1 falta | Gorro 3, Diego Coral: 2
faltas» y en la visita gorros 5 y 7; tiempo muerto del simulador con el anillo azul en «57» y el reloj de
partido detenido en 7:22; «FIN DEL TIEMPO MUERTO · Club Deportivo Tiburones del Norte» del lado de la visita;
«DESCANSO» apareciendo con desenfoque; final 1–0. Los avisos de falta y de fin de tiempo muerto **no se
superponen** con el reloj, la posesión ni el periodo (rectángulos en pantalla). Revisadas las capturas.

**Hallazgo corregido en el recorrido:** los gorros con faltas se veían chicos para una pantalla lejana
(círculo de 52 px del lienzo); pasaron a 78 px con puntos de 17 px.

**No verificado:** el latido del gorro con la falta nueva y el del número del ganador al final no quedaron
en ninguna captura (solo se comprobó que se programan); el fin del tiempo muerto con la consola en vivo
(solo con sus capturas grabadas); pantalla completa, segundo monitor y LED (hito C). Si la consola dejara
el campo en blanco un instante durante un tiempo muerto, se contaría un fin falso: no se vio en las capturas.

## 2. Criterios del piloto

| N.º | Criterio | Pasos | Resultado esperado | Nivel | Evidencia | Estado |
|---|---|---|---|---|---|---|
| P01 | Instalación limpia offline. | En una laptop Windows sin SDK ni internet: instalar `FDNDA-Waterpolo-Setup.exe`, abrir por doble clic. | Abre el demo; acceso directo creado; datos en `%LocalAppData%`. | Equipo real | Acta de instalación | Pendiente (hito C) |
| P02 | Importación con errores. | Importar un CSV con una fila sin nombre y un gorro repetido en el mismo equipo. | Vista previa con fila y columna de cada error; no se escribe nada. | Automatizada + visual | §1.6 | **Verificado** |
| P03 | Cancelación de importación. | Abrir la vista previa de un archivo válido y cancelar. | La base no cambia. | Automatizada + visual | §1.6 | **Verificado** |
| P04 | Reimportación. | Importar dos veces la misma plantilla. | No hay duplicados. | Automatizada + visual | §1.6 | **Verificado** con CSV y XLSX de muestra; falta un XLSX guardado por Excel. |
| P05 | Partido con cambios de periodo. | Iniciar, descanso, siguiente periodo… hasta el cuarto; finalizar. | Etapas correctas en mesa y público; no se pasa del último periodo. | Automatizada + visual | §1.1, §1.2 y §1.12 | **Verificado**, con prórroga configurable (§1.12) |
| P06 | Gol y corrección. | Registrar gol; corregir goleador con motivo; anular con motivo. | Estadística corregida sin duplicar; anular resta uno; historial íntegro. | Automatizada + visual | §1.1, §1.2 y §1.4 (extensión a penales). | **Verificado** en el alcance de juego. |
| P07 | Exclusión y tiempo muerto. | Registrar, corregir y anular cada uno. | Quedan en historial y estadísticas. | Automatizada + visual | §1.4; reporte R1. | **Verificado:** registro y revisiones (R1), totales en el resumen (R6) y avisos en el marcador (R7). |
| P08 | Animaciones. | Registrar gol, exclusión y tiempo muerto; corregir; reabrir la pantalla; desactivar animaciones. | Una vez por acción nueva; sin repetición; no tapan relojes ni bloquean la mesa. | Automatizada + visual | §1.2 (gol), §1.11 y §1.17 | **Verificado en modo ventana:** gol, exclusión y tiempo muerto una vez; corregir, anular y reabrir no repiten; desactivables por tipo; no tapan los relojes. Desde el 2026-11-25 son avisos animados «toma del lado» (§1.17), también sin tapar los relojes. Falta la pantalla LED (hito C/D). |
| P09 | Resultado y tanda. | Partido empatado, tanda de penales. | La tanda se muestra y exporta separada del reglamentario. | Automatizada + visual | §1.12 | **Verificado** con reglas de prueba (prórroga y tanda pendientes de aprobación técnica); falta verlo en la pantalla LED. |
| P10 | Exportación PDF y CSV. | Exportar resumen del partido y del evento; abrir en un lector de PDF y en una hoja de cálculo. | Totales iguales al marcador; texto con `=`, `+`, `-`, `@` no se interpreta como fórmula. | Automatizada + visual | §1.10 | **Parcial:** exportación, totales y neutralización verificados; PDF abiertos con PyMuPDF. Falta abrirlos en Excel y en un lector de escritorio con una persona. La tanda sale aparte desde R8 (§1.12). |
| P11 | Dos pantallas. | Con segundo monitor: elegir pantalla, pantalla completa; desconectar el monitor. | Público solo ve el marcador; la mesa sigue operable; al desconectar pasa a ventana con aviso. | Equipo real | — | **Parcial**: modo ventana verificado; segundo monitor pendiente (hito C) |
| P12 | Corte de la aplicación y recuperación. | Registrar goles, terminar el proceso por la fuerza, reabrir. | Mismo resultado, periodo e incidencias. | Automatizada + visual | §1.1 y §1.2 | **Verificado** |
| P13 | Respaldo y restauración. | Respaldar, instalar en otro equipo, restaurar. | Se conservan partidos, incidencias, imágenes y configuración; no se restaura en pleno partido. | Automatizada + visual + equipo real | §1.9; `El_respaldo_es_una_copia_integra…`; `IncidentMigrationTests` (§1.4). | **Parcial**: respaldo, restauración en otra carpeta de datos, rechazo en pleno partido, vuelta atrás e imágenes (§1.11) verificados; falta otro equipo (hito C). |
| P14 | Colorado: inicio y pausa. | Iniciar y detener el reloj en la consola. | Mesa y público lo reflejan; una pausa no se muestra como pérdida de señal. | Equipo real | — | **Verificado con equipo real** (2026-11-24, consola de la FDNDA por COM PORT 2; operador y registro): la mesa baja con la consola y una pausa de 30 s no se muestra como pérdida. Latencia no medida. |
| P15 | Colorado: reinicio del reloj y reset de posesión. | Cargar un periodo; reset completo y alterno. | Ambos relojes coinciden con la consola. | Equipo real | — | **Verificado con equipo real** (2026-11-24): Reset → 28 y tecla «18» → 18 coinciden con la consola. Carga de periodo: ver nota de §1.16. |
| P16 | Colorado: dato inválido. | Reproducir tramas fragmentadas, concatenadas, inválidas y fuera de rango. | Solo se publican muestras válidas; el resto se descarta y se cuenta en el diagnóstico. | Automatizada + equipo real | — | **Parte automatizada ejecutada** (§1.15, con capturas reales). Falta con la consola en vivo y mostrar los contadores en el diagnóstico (hoy van al registro). |
| P17 | Colorado: pérdida y reconexión. | Desconectar el cable en juego y en pausa; reconectar. | Aviso y relojes sin mostrarse como vigentes; al volver, datos nuevos válidos. | Equipo real | — | **Verificado con equipo real** (2026-11-24): cable USB desenchufado en juego y en pausa; la mesa pasó a «sin señal» ~1 s después de cerrarse el puerto y volvió sola al reconectar (reconexiones 1 y 2 en el registro). La reconexión puede tardar hasta 5 s por la espera entre intentos. |
| P18 | Operación sin internet. | Wi-Fi apagado durante un partido completo. | Todo funciona; no hay intentos de conexión. | Equipo real | — | Pendiente (hito C). Por diseño no hay código de red. |
| P20 | Estado de conexiones. | Durante un partido: cortar la señal, desconectar la pantalla del público, cerrar el marcador; volver a conectar. | El panel y la barra inferior pasan a rojo o ámbar con el motivo y la hora; al volver, verde; el historial conserva la secuencia. La consola solo figura «Conectada» con datos válidos. | Automatizada + visual + equipo real | §1.7 | **Parcial:** verificado con el simulador y una pantalla; consola real (hito B) y segundo monitor (hito C) pendientes. |
| P21 | Reglas por evento. | Con un partido en juego, cambiar duración, posesión y tanda del evento; finalizar e iniciar otro partido. | El partido en juego conserva sus reglas; el nuevo toma las editadas; las reglas escritas a mano quedan marcadas «sin aprobación técnica». | Automatizada + visual | §1.8 | **Verificado** (preset de competencia pendiente de aprobación técnica) |
| P22 | Reproducción de cronometraje. | Abrir la demo con una grabación normalizada; dejarla correr con su corte de señal; reiniciarla; probar un archivo roto y el perfil de producción. | «REPRODUCCIÓN» en ambas ventanas; el corte se ve como pérdida de señal y la reconexión como sesión nueva; dos reproducciones iguales entregan lo mismo; un archivo roto deja la fuente no disponible; producción lo rechaza. | Automatizada + visual | §1.13 | **Verificado** con una grabación sintética. Falta reproducir capturas reales de la consola (hito B). |
| P19 | Eventos, equipos y programación. | Crear un evento y dos equipos; programar un partido con colores y gorros propios; editar el catálogo. | El partido aparece en el evento correcto; editar el catálogo no cambia partidos ya programados ni iniciados. | Automatizada + visual | §1.5, §1.18 | **Verificado**, con escudos, banderas y auspiciadores (§1.11) y, desde R10, reprogramar, actualizar la nómina y anular un partido no iniciado (§1.18). |

## 3. Bloqueos de aceptación abiertos

1. **RF11:** no hay ruta de datos validada hacia la consola. Si no se puede detectar de forma fiable
   la pérdida de señal con las señales que existan, se registra aquí como bloqueo definitivo.
2. **Reglas de competencia** sin aprobar: el preset disponible es de demostración; las reglas
   escritas a mano se guardan marcadas «sin aprobación técnica».
3. **Pantalla LED:** resolución y conexión por confirmar con el equipo real.
