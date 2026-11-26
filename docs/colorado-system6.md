# Colorado Time Systems System 6 — diseño de la lectura y verificación

**Novedad 2026-11-24 (§7): ruta de datos observada con la consola de la FDNDA.** Por **COM PORT 2**
(DB9), a 115200 baudios 8-N-1, la consola emite tramas ASCII `CTSWP1.1c` con tiempo de partido,
posesión, periodo, goles, tiempos muertos y exclusiones. Es evidencia **propia**, capturada en solo
lectura; no es documentación oficial. **Desde el 2026-11-24 el programa la lee** con
`--perfil produccion --puerto COMx` (`CtswpFrameDecoder`, `SerialColoradoTransport`,
`ColoradoSystem6TimingSource`); falta verificarlo en vivo con la consola (P14–P17).

**Estado anterior (se conserva como registro):** PENDIENTE DE VALIDACIÓN EN HARDWARE. No existía
evidencia **propia ni oficial** de
una ruta de datos que permita **recibir** el tiempo de partido y el reloj de posesión. Desde el
2026-11-24 sí hay una **descripción de terceros** de la salida de tablero, coincidente en cinco fuentes,
y un decodificador y una herramienta de captura preparados con ella (§6); falta confirmarla con la
consola de la FDNDA. El adaptador real
(`ColoradoSystem6TimingSource`) no abre ningún puerto, no envía nada a la consola y devuelve un
resultado explícito de «no disponible». El simulador permite desarrollar y demostrar, pero **no
valida RF11**.

## 1. Qué dicen las fuentes oficiales

Consultadas el 2026-11-11. Solo se listan hechos presentes en las fuentes.

### Ficha del producto — <https://coloradotime.com/products/system-6-water-polo>

- El producto figura como **descontinuado**.
- Software: «WaterPolo.1.130 – 12 November 2024». «This update requires Firmware v1.36 or greater.»
- «This update provides functionality for new (internal) Sys6 hardware. It is only required for
  serial numbers greater than or equal to 6875. **Due to hardware limitations, COM2 is no longer
  supported.**» La ficha no dice para qué servía COM2 ni afirma que funcione en series anteriores.
- La consola muestra tiempos a la centésima, tiempo de partido, tiempo de posesión, exclusiones,
  faltas, resultado, tiempos muertos y hora del día. El operador controla desde el teclado los
  relojes de posesión, el tiempo de partido, tres tiempos de exclusión por equipo, el resultado y la bocina.
- No describe puertos, protocolo, velocidad ni formato de datos para un PC.

### Manual F872, Rev. 202006 — «System 6 Water Polo Software User Guide» (53 páginas)

- **Conexiones documentadas** (págs. 6–8): alimentación; un **divisor en Y de datos de tablero**
  (CTS 015-383) en el conector «scoreboard» de la consola, con una rama al tablero y otra a los
  relojes de posesión, encadenados entre sí con cables de datos; la **caja de interfaz de
  waterpolo** (WP-RS2R) con pulsadores e interruptor, en cualquiera de los cuatro conectores negros
  pequeños del panel trasero; bocina; impresora USB o paralela.
- **Relojes** (pág. 11): el reloj de posesión arranca con el de partido y «only stops when the game
  time is stopped or when it counts down to zero». Se reinicia al tiempo completo o a un tiempo
  alterno, desde el teclado o desde los pulsadores de la caja de interfaz.
- **Precisión** (pág. 11): la consola trabaja internamente al milisegundo; la precisión que envía al
  tablero es configurable y «only shows during STOP TIME or when you are in the final minute of a period».
- **Datos al tablero** (págs. 23–28): la consola envía «channels of information» a módulos del
  tablero, reasignables desde «Define Module». Hay ajustes que cambian lo que sale por esa vía:
  mostrar o no décimas en marcha, cuenta ascendente o descendente, tiempo de periodo o de partido,
  qué muestra el reloj de posesión cuando queda menos tiempo de partido que de posesión
  (tiempo de partido, posesión completa o **en blanco**), y «Scoreboard Blank», que apaga el tablero.
- **Redondeo** (pág. 27): con las décimas en marcha activadas los tiempos son exactos; desactivadas,
  «all times will be rounded up to the nearest second». El simulador y el formato actual del
  programa (que trunca el reloj de partido) son provisionales y deben alinearse con lo que se
  observe en la consola.
- **No documenta** ninguna salida de datos para PC, software de terceros o gráficos de video: ni
  puerto serie, ni velocidad, ni estructura de mensajes, ni handshake. Tampoco dice si la salida al
  tablero es continua o por cambios, ni qué ocurre al desconectar un cable. Las figuras del panel
  trasero y de configuración de módulos son imágenes y no se pudieron leer como texto.

### Conclusión

Las fuentes consultadas **no alcanzan** para implementar la lectura. La única ruta documentada que
transporta ambos relojes es la salida de datos hacia el tablero y los relojes de posesión, pero su
interfaz eléctrica y su protocolo no están descritos. No se presupone RS-232, COM2, USB, velocidad,
mensajes, handshakes ni acceso simultáneo a ambos relojes.

## 2. Preguntas que deben resolverse con la consola y con Colorado Time Systems

1. ¿Qué salida de la consola de la FDNDA entrega el tiempo de partido y el de posesión a un equipo
   externo, y con qué documentación oficial?
2. ¿Esa salida es de **solo emisión** (basta escuchar) o exige que el receptor envíe comandos o
   responda? **Si exige enviar, no se implementa bajo el supuesto de solo lectura: se registra la
   limitación y se escala a la FDNDA.**
3. ¿Se puede derivar la señal sin afectar al tablero ni a los relojes de posesión oficiales
   (divisor, puerto adicional, adaptador)? ¿Qué cable y qué adaptador hacen falta?
4. ¿La documentación del protocolo es pública, requiere solicitud a soporte o una licencia de terceros?
5. ¿Los datos se emiten de forma continua o solo cuando cambian? De esto depende cómo distinguir un
   reloj detenido de una conexión perdida.
6. ¿Ambos relojes llegan en la misma trama o por separado? ¿Con qué precisión en cada fase del periodo?
7. ¿Los ajustes del tablero (cuenta ascendente, tiempo de partido/periodo, posesión en blanco,
   tablero apagado) alteran lo que recibiría el programa?

Contacto oficial que figura en el manual: `support@coloradotime.com`.

## 3. Checklist del equipo (completar antes de implementar la conexión)

| Dato | Valor | Evidencia (foto, pantalla, documento) |
|---|---|---|
| Modelo exacto de la consola | System 6 con plantilla de teclado de waterpolo (rótulo 012-415B). Prestada por la FDNDA el 2026-11-24, sin tablero. | `evidencia/consola-s6-frente.png`, `consola-s6-teclado.png` |
| Número de serie (¿menor o ≥ 6875?) | | |
| Versión de firmware (requisito: v1.36 o superior para WaterPolo 1.130) | | |
| Versión del módulo de waterpolo (softkey de versión) | | |
| Conectores del panel trasero (foto rotulada) | COM PORT 1 y COM PORT 2 (DB9), PRINTER PORT, CTS EXPANSION PORTS (2 USB), ETHERNET, MONITOR, KEYBOARD, MOUSE, NEAR END y FAR END (dos cada uno), cuatro conectores pequeños con traba (probablemente los de la caja WP-RS2R, manual pág. 6–8), POWER (el plug azul es la **alimentación**) y JUDGES I/O (redondo, multipin). **En el panel trasero no hay ningún jack rotulado «Scoreboard»**: falta ubicar la salida de tablero (costados, parte inferior o el cable que usa la FDNDA). | `evidencia/consola-s6-panel-trasero-1.png` a `-4.png` |
| Salidas habilitadas y en uso durante un partido | | |
| ¿Hay divisor en Y 015-383? ¿Qué cuelga de cada rama? | | |
| ¿Hay caja de interfaz WP-RS2R? | | |
| Tablero y relojes de posesión conectados (modelo) | | |
| Configuración de «Scoreboard Time Display» durante los partidos | | |
| Protocolo y documentación disponible (origen, licencia) | | |
| Cables y adaptadores necesarios hacia la laptop | | |
| Puerto que verá Windows (tipo, controlador) | | |
| Responsable de la FDNDA que autoriza la prueba | | |

## 4. Reglas de la prueba técnica

- La prueba se coordina **desde el inicio** del hito B y se hace con una consola **fuera de
  competencia**. No se envían comandos ni se hacen pruebas activas en una consola en uso.
- Primero se observa; solo se conecta lo que el fabricante o la documentación indiquen como salida.
- Toda captura es acotada, queda autorizada por la FDNDA y no contiene datos personales.

### Muestra a capturar, anotando el valor visible en la consola en cada momento

| Escenario | Qué se anota |
|---|---|
| Reposo tras encender, sin partido | ¿Emite algo? |
| Reloj detenido durante una pausa prolongada (≥ 2 min) | ¿Sigue emitiendo? ¿Cada cuánto? |
| Inicio del reloj | Latencia entre la consola y la llegada del dato |
| Último minuto y paso por cero | Precisión y formato |
| Fin de periodo y carga del siguiente | Valores y periodo informado |
| Reset de posesión (completo y alterno) | Cambio del reloj de posesión; ¿llega junto al de partido? |
| Reloj de posesión apagado o en blanco | Qué llega cuando no hay posesión que mostrar |
| Tiempo muerto y exclusión | ¿Aparecen en la misma salida? |
| Desconexión del cable y reconexión | Qué se recibe antes, durante y después |
| Apagado y encendido de la consola | Reinicio de la emisión |

### Resultados que la prueba debe dejar

- Protocolo confirmado y documentado (o la limitación que lo impide).
- Criterio fiable para distinguir **reloj detenido** de **conexión perdida**. Si no existe, queda
  registrado como **bloqueo de aceptación** de RF11.
- Umbral de vigencia y frecuencia de visualización fijados con medidas reales (hoy: 1,5 s y 250 ms,
  provisionales).
- Latencia medida de cada reloj por separado. No se declara ningún SLA antes del ensayo.
- Capturas convertidas en fixtures para `ReplayTimingSource` y para las pruebas del decodificador.

## 5. Diseño en el código

```
ITimingSource (Application)            ← puerto: inicio, detención y flujo cancelable de TimingSnapshot
 ├─ SimulatedTimingSource              ← hecho; etiqueta «DEMO / SIMULACIÓN»
 ├─ ReplayTimingSource                 ← hecho (R9); etiqueta «REPRODUCCIÓN»; formato normalizado propio
 └─ ColoradoSystem6TimingSource        ← hecho (2026-11-24); sin --puerto, «no disponible»
      ├─ IColoradoTransport            ← SerialColoradoTransport: COM PORT 2, 115200 8-N-1, solo recepción
      └─ CtswpFrameDecoder             ← tramas CTSWP1.1c observadas (§7)
 (herramienta de captura: además CtsWaterPoloInterpreter/CtsScoreboardDecoder, formato de terceros, §6)
```

- `TimingSnapshot` lleva fuente, sesión, tiempos restantes de partido y posesión **por separado**
  (milisegundos enteros, opcionales), precisión, estado de marcha si hay evidencia, periodo si está
  disponible, secuencia interna, marca monotónica de recepción, hora UTC y calidad.
- `TimingStateTracker` mantiene la vigencia de cada reloj por separado, descarta lecturas inválidas,
  repetidas, antiguas, fuera de rango o de otra fuente, y al cambiar de sesión no hereda valores.
- `TimingMonitor` consume la fuente fuera del hilo de UI, reevalúa la vigencia aunque no lleguen
  datos y libera sus recursos al cerrar.
- Sin señal vigente se muestra `--:--` y `--` con aviso. No se extrapola. El tanteo sigue
  registrándose con contexto de reloj «no disponible».
- En el perfil de producción la fuente configurada es el Colorado. El programa **no** cambia por su
  cuenta a demo ni a un reloj manual.

### Al implementar (hito B)

1. Transporte de solo lectura según la ruta validada; reconexión con espera creciente; sin escribir
   nunca en el puerto. Si depende de Windows o de hardware, aislarlo en su propio proyecto.
2. Decodificador incremental con pruebas de tramas válidas, fragmentadas, concatenadas, inválidas y
   fuera de rango, según el protocolo confirmado.
3. Sesión nueva en cada reconexión; publicar solo muestras válidas.
4. Registro de diagnóstico local y acotado (estados de conexión y contadores, nunca nóminas).
5. Convertir las capturas autorizadas al formato normalizado de `ReplayTimingSource` (R9,
   `Infrastructure/Timing/TimingRecording.cs`) para ensayar sin la consola. El formato y la
   reproducción ya existen; falta el conversor, que depende del decodificador.

## 6. Lo que describen proyectos de terceros (consultado el 2026-11-24)

Ninguna de estas fuentes es de Colorado Time Systems y **ninguna se probó todavía con la consola de
la FDNDA**. Coinciden entre sí en el formato básico, lo que lo hace una hipótesis fuerte, no un hecho.

| Fuente | Licencia | Qué aporta |
|---|---|---|
| [scorebox-consoles](https://github.com/FlantasticDan/scorebox-consoles) (Python) | MIT | Único que declara **System 6 con waterpolo**: tiempo de partido (canal 1), posesión (canal 3), periodo (canal 14), goles (canal 13). Conexión al jack «Scoreboard Out» y parámetros serie. |
| [coloradoScoreboard](https://github.com/fabriziobertocci/coloradoScoreboard) (Node.js) | MIT | Natación. Mismo formato de bytes; cable en Y para no desconectar el tablero ([issue 10](https://github.com/fabriziobertocci/coloradoScoreboard/issues/10)). |
| [vsCTS](https://github.com/hwbrill/vsCTS) | sin licencia visible | Natación. Describe el enlace: 9600 baudios, continuo, sin delimitadores, el tablero nunca responde. |
| [CTS_Scoreboard](https://github.com/STU940652/CTS_Scoreboard) | sin licencia visible | Natación. Mismo decodificado de canal y dígitos. |
| [Marco Walther, 2015](https://marcoscorner.walther-family.org/2015/07/colorado-timing-console-scoreboard-protocol/) | artículo | System 5/6, RS-232, 9600 baudios 8-E-1; tabla de canales de natación en los comentarios. |

**Formato descrito.** 9600 baudios (2400 en modo lento, configurable en la consola), 8 bits,
paridad par, 1 bit de parada. La consola emite continuamente; el tablero nunca responde (basta
escuchar). Un byte con el bit 7 en 1 es una dirección: canal = ((b >> 1) & 0x1F) ^ 0x1F; bit 0 en 0 =
siguen dígitos, en 1 = siguen códigos de formato del mismo canal. Un byte con el bit 7 en 0 es un
dato: nibble alto = posición (0–7) y nibble bajo invertido (XOR 0x0F) = valor; en una lectura, un valor
mayor que 9 es un dígito apagado.

**Conexión descrita.** Jack de 1/4" mono «Scoreboard Out» de la consola; con un cable en Y (p. ej.
Hosa YPP-111) una rama sigue al tablero y la otra va a un DB9: punta al pin 2 (RX), malla al pin 5
(GND); adaptador USB a RS-232. Solo recepción: la laptop no tiene nada conectado al pin de transmisión.

**Interpretación de waterpolo (solo scorebox-consoles, la más débil).** El código de formato 2 marca
la puntuación. Partido: con dos marcas, minutos = dígitos antes de la primera y segundos = los marcados
(m:ss); con una, segundos = dígitos hasta la marca y décimas el siguiente (ss.d, último minuto).
Posesión: una sola marca; segundos hasta ella. Periodo: segundo dígito del canal 14.

**Cautelas.** (1) El manual F872 dice que los canales de cada módulo se reasignan en «Define Module»:
los de la consola de la FDNDA pueden ser otros. (2) La interpretación de las marcas es heurística y de
una sola fuente. (3) Los ajustes de «Scoreboard Time Display» (décimas en marcha, cuenta ascendente,
posesión en blanco, tablero apagado) cambian lo que sale. (4) El estado de marcha no viaja en el flujo:
el programa no lo deduce. (5) Gen7 usa otra salida (RS-485, protocolo WA-2): no aplica a la System 6.
(6) La ficha dice que en series ≥ 6875 no hay COM2; la salida de tablero es otra y no se ve afectada
según estas fuentes, pero hay que confirmarlo.

**Qué se implementó (sin activarlo en producción).** Implementación propia, sin copiar código:
`Infrastructure/Timing/Colorado/CtsScoreboardDecoder.cs` (formato básico),
`CtsWaterPoloInterpreter.cs` (`IColoradoFrameDecoder`; canales configurables; ante cualquier
ambigüedad no entrega valor) y `CtsCapture.cs` (formato de captura, análisis por canal y conversión
a la grabación normalizada de R9). Herramienta aparte `tools/FDNDA.Waterpolo.Captura` (comandos
`puertos`, `capturar`, `analizar`, `convertir`), que **solo lee** el puerto: nunca escribe, DTR y RTS
apagados, sin control de flujo. `ColoradoSystem6TimingSource` sigue devolviendo «no disponible».

### Procedimiento para el día de la prueba (con autorización y consola fuera de competencia)

Versión paso a paso para imprimir, con la consola prestada sin tablero (incluye la WTTC inalámbrica):
[checklist-prueba-consola-2026-11-25.md](checklist-prueba-consola-2026-11-25.md).

1. Completar el checklist de §3 (serie, firmware, fotos del panel, «Define Module», «Scoreboard Time Display»).
2. Con la consola apagada, poner el cable en Y en «Scoreboard Out»; comprobar que el tablero sigue
   funcionando igual al encenderla.
3. `FDNDA.Waterpolo.Captura.exe puertos` y, con el puerto del adaptador,
   `capturar --puerto COMx --salida prueba-01.txt --nota "quién autorizó, qué se hizo"`. Recorrer la
   tabla de §4 anotando la hora y lo que muestra la consola.
4. `analizar --entrada prueba-01.txt`: confirmar qué canal cambia como el tiempo de partido y cuál
   como la posesión; si no son 1 y 3, repetir con `--canal-partido` y `--canal-posesion`.
5. `convertir` y reproducir con `--reproduccion` en la mesa; comparar con las anotaciones.
6. Recién con eso: transporte serie de solo lectura en el programa, umbrales medidos y P14–P17.

## 7. Salida serie observada en COM PORT 2 (evidencia propia, 2026-11-24)

Consola prestada por la FDNDA, sin tablero: System 6 de waterpolo, software 1.127, firmware 1.44.
Adaptador USB a RS-232 Prolific PL2303GT conectado a **COM PORT 2** de la consola. Captura de **solo
lectura** con `tools/FDNDA.Waterpolo.Captura` (DTR y RTS apagados, sin escribir). Archivos en
[evidencia/capturas/](evidencia/capturas/); fotos de la consola en `evidencia/consola-s6-*.png`.

**Hallazgos de hardware.** El panel trasero no tiene ningún jack «Scoreboard»: el plug azul es la
alimentación (POWER) y el conector redondo es JUDGES I/O. A 9600 8-E-1 (lo que dicen los terceros para la
salida de tablero) llegaban ráfagas con errores; un barrido de velocidades dio **115200 baudios, 8 bits,
sin paridad, 1 bit de parada, 0 errores**. COM PORT 2 funciona en esta consola (la ficha dice que en las
series ≥ 6875 no; falta leer el número de serie). COM PORT 1 no se probó.

**Emisión.** Con el reloj detenido, la misma trama **una vez por segundo** (latido). Con el reloj en
marcha, una trama por cada cambio de cualquiera de los relojes (partido, posesión, exclusiones). Las
pulsaciones (goles, resets, tiempos muertos) llegan en la trama siguiente, en menos de un segundo. El
latido permite distinguir **reloj detenido** (siguen llegando tramas) de **conexión perdida** (no llegan).

**Descanso entre periodos: la consola no emite nada.** Tras *Period +1* la consola queda en descanso y
deja de enviar tramas hasta que empieza el periodo siguiente (llega directamente con 6:00 y el periodo
nuevo). En `s6-com2-03-guiada.txt` hay silencios completos de 56,9 s, 18,0 s y 117,0 s, sin ningún byte;
en la prueba en vivo, pérdidas de 36 s y 18 s. Por los datos, **un descanso no se distingue de un cable
desconectado**. (Se pasó por alto en el primer análisis, que trató como cortes los silencios de más de 5 s.)
**Decisión del usuario (2026-11-24):** manda la mesa. Mientras el partido abierto está en descanso
(*Iniciar descanso*), el silencio se muestra como «Descanso: la consola no envía tiempos», sin alarma
ni contingencia y sin tiempos; si al iniciar el periodo siguiente en la mesa la consola sigue callada,
es pérdida de señal. Además, la mesa avisa (sin cambiar nada) si el periodo de la consola no coincide
con el suyo.

**Trama.** `STX (0x02)` + 97 bytes + `EOT (0x04)`. Posiciones contadas desde 0 tras el STX:

| Bytes | Contenido | Ejemplo | Confirmado con |
|---|---|---|---|
| 0–8 | Identificador `CTSWP1.1c` | `CTSWP1.1c` | todas las tramas |
| 9 | `NUL` (0x00) | | todas las tramas |
| 10–14 | Tiempo de partido, `m:ss` o `:ss` en el último minuto, alineado a la derecha | ` 6:00`, `  :34` | pantalla `:33.99` → `:34` |
| 15–17 | Espacios | | |
| 18–19 | Posesión, dos dígitos | `28`, `07`, `00` | pantalla `06.99` → `07`; Reset → 28; tecla «18» → 18 |
| 20 | Periodo | `1`…`4` | pantalla «Period: 4» |
| 21–24 | Reloj de tiempo muerto (`m:ss` / `:ss`), en blanco sin tiempo muerto | `1:00`, ` :59` | Start Timeout / Clear Timeout |
| 25–26 | **Goles de White**, 2 caracteres alineados a la derecha | ` 3`, `11` | pantalla «Score»; Score +1 hasta 11 |
| 27 | **Tiempos muertos usados por White** | `0` | pantalla «Timeouts Used» |
| 28–41 | Dígitos sin identificar de White (los de 32 y 39 cambiaron, ver abajo) | `00001000000100` | — |
| 42–43 | Espacios | | |
| 44–47, 50–53, 56–59 | Exclusiones A, B, C de White: 2 dígitos de segundos + gorro en 2 caracteres; en blanco si no hay | `07 5`, `0812` | pantalla: gorro 5 :07, gorro 12 :08 |
| 60–61 | **Goles de Dark** (en el teclado, «Blue»), 2 caracteres alineados a la derecha | ` 0` | pantalla «Score» |
| 62 | **Tiempos muertos usados por Dark** | `1`, `2` | pantalla «Timeouts Used»; segundo tiempo muerto → 2 |
| 63–76 | Dígitos sin identificar de Dark (67 y 74 cambiaron) | `00001000000100` | — |
| 79–82, 85–88, 91–94 | Exclusiones A, B, C de Dark | `08 7` | pantalla: gorro 7 :08 |
| 95–96 | `FF FF` | | todas las tramas |

**Goles y tiempos muertos.** Los goles ocupan 2 caracteres alineados a la derecha (con 11 goles la
trama trae `11` en 25–26; con menos de 10, un espacio delante) y suben y bajan con Score +1/−1. Los
tiempos muertos usados van en el carácter siguiente (Dark llegó a 2). Los dígitos 32/39 (White) y 67/74
(Dark) pasaron a 1 en ambos equipos a la vez, al cambiar de periodo y al cargar exclusiones:
**significado desconocido**, no se usan.

**Precisión.** Solo segundos enteros, también en el último minuto y en la posesión, aunque la pantalla de
la consola muestre centésimas; los valores parecen redondeados hacia arriba (`:33.99` → `:34`). Falta
probar si algún ajuste de la consola agrega décimas. Al llegar la posesión a 0 se vio un parpadeo breve
(27 → 28 → 00 → 28) antes de quedar en 28.

**Desconexión y apagado** (`s6-com2-04-casos-limite.txt`). Al desenchufar el USB del adaptador ~25 s,
Windows quita el COM y la herramienta lo reabrió sola en cuanto volvió; la primera trama llegó ~1 s
después. Al apagar la consola dejan de llegar tramas; durante el arranque solo llegaron 3 bytes `0x00`
(y 2 errores de línea), y las tramas volvieron ~68 s después, al entrar al modo de waterpolo. **Tras el
apagado la consola empezó un partido nuevo** (6:00, posesión 28, periodo 1, todo en cero): no conserva
el estado. Una trama puede quedar cortada al apagar: el decodificador debe resincronizar con el STX.

**Implementado (2026-11-24).** `Timing/Colorado/CtswpFrameDecoder.cs` (trama completa o nada; cuenta
descartes), `Timing/Colorado/SerialColoradoTransport.cs` (115200 8-N-1, DTR/RTS apagados, nunca
escribe) y `Timing/ColoradoSystem6TimingSource.cs` (sesión nueva por conexión; reintentos de 0,5 s a
5 s; un mismo fallo se anota una vez). Se activa con `--perfil produccion --puerto COMx`. Intervalo
medido entre tramas: mediana 1,0 s, máximo 1,07 s (umbral de vigencia: 1,5 s).

**Pendiente.** Verificación en vivo (P14–P17), mostrar en la mesa el motivo concreto (p. ej. «no se
encuentra COM5») y los contadores en el diagnóstico, número de serie, COM PORT 1, ajuste de décimas, y luego:
decodificador con pruebas basadas en estas capturas, transporte serie de solo lectura dentro del programa
y criterios P14–P17 de aceptación.
