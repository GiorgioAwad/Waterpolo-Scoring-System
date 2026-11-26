# Reporte de sesión — Hito B, lectura de la consola (2026-11-24)

**Qué se hizo:** con la System 6 que prestó la FDNDA (sin tablero) se encontró una ruta de datos
distinta de la que describían terceros: **COM PORT 2**, 115200 baudios 8-N-1, tramas ASCII
`CTSWP1.1c` con tiempo de partido, posesión, periodo, goles, tiempos muertos y exclusiones. Se mapearon
los campos con capturas guiadas y se implementó la lectura en el programa. **RF11 queda implementado,
pendiente de verificación en vivo** con la consola (P14–P17). No hubo commits ni push.

## Cómo se llegó

1. El panel trasero no tiene jack «Scoreboard»: el plug azul es la alimentación y el conector redondo
   es JUDGES I/O (fotos en `docs/evidencia/consola-s6-*.png`).
2. Con el adaptador USB a RS-232 en COM PORT 2, a 9600 8-E-1 llegaban ráfagas con errores; un barrido
   de velocidades (siempre solo lectura) dio 115200 8-N-1 sin errores.
3. Cuatro capturas guiadas con el operador (`docs/evidencia/capturas/`), comparadas con la pantalla
   de la consola: relojes, resets (28 y 18), goles hasta 11, tiempos muertos, exclusiones con gorro,
   periodos, último minuto, cable desenchufado y consola apagada. Detalle en
   [colorado-system6.md §7](colorado-system6.md).

## Hallazgos que importan al programa

- La consola manda **segundos enteros** (redondeados hacia arriba), también en el último minuto.
- Con el reloj detenido repite la trama **cada segundo**: el programa distingue pausa (siguen llegando)
  de pérdida (dejan de llegar). Intervalo medido: mediana 1,0 s, máximo 1,07 s; umbral de vigencia 1,5 s.
- Al apagarse, la consola **empieza un partido nuevo**: no conserva el estado. El marcador oficial sigue
  siendo el de la mesa; de la consola solo se toman relojes y periodo.
- Al desenchufar el adaptador, Windows quita el COM; al volver, se reabre y la primera trama llega en ~1 s.

## Archivos

| Área | Creados o modificados |
|---|---|
| Infraestructura | Nuevos `Timing/Colorado/CtswpFrameDecoder.cs` y `SerialColoradoTransport.cs`; `Timing/ColoradoSystem6TimingSource.cs` reescrito (transporte, sesiones, reintentos 0,5–5 s, contadores, registro sin repeticiones); `CtsCapture.cs` (`IsCtswp`, `FromCtswp`); `System.IO.Ports` en el proyecto. |
| Escritorio | `StartupOptions.cs` (`--puerto COMx`, solo producción, validado); `App.xaml.cs` crea la fuente con `SerialColoradoTransport`. |
| Herramienta | `tools/FDNDA.Waterpolo.Captura`: por defecto 115200 8-N-1; `analizar`/`convertir` reconocen CTSWP; muestra en vivo las tramas válidas. |
| Pruebas | Nuevos `CtswpDecoderTests` (24, con capturas reales), `ColoradoSourceTests` (7), `StartupOptionsTests` (8); ajustada la prueba de Colorado sin puerto. |
| Muestras y evidencia | `samples/cronometraje/consola-real-s6-guiada.csv`; `docs/evidencia/capturas/` (6 capturas) y fotos de la consola. |
| Documentación | `colorado-system6.md` (§3, §5, §7), `requisitos.md` (RF11), `aceptacion.md` (§1.15, P16), `arquitectura.md` (D42), `plan-entrega.md` (B4), `README.md`, `samples/README.md`, `checklist-prueba-consola-2026-11-25.md`, `Prompt-Codex-Hito-A.md` y este reporte. |

## Evidencia ejecutada

- Base: **391/391**. Final: **430/430** (dominio 111, infraestructura 232, ViewModels 87), compilación
  Release sin advertencias.
- Programa con `--perfil produccion --puerto COM5` y el adaptador desenchufado: arranca, muestra
  `--:--` / `--` con «Esperando datos de la fuente de tiempo» y reintenta; el registro anota el fallo
  una vez.

## Prueba en vivo y descanso (misma fecha, más tarde)

Con el paquete publicado y `--puerto COM5`, el operador verificó P14, P15 y P17 (aceptación §1.16).
Apareció un problema: **en el descanso entre periodos la consola no emite**, y la mesa mostraba «sin
señal». Por decisión del usuario, con la mesa en descanso ahora se muestra «Descanso» sin alarma, y la
mesa avisa si el periodo de la consola no coincide con el suyo (D43); verificado en vivo con dos
partidos de ejemplo. Se corrigió un aviso falso en el registro al cerrar. **440/440** pruebas.

## Qué falta

1. **Prueba en vivo** con la consola: `FDNDA.Waterpolo.exe --perfil produccion --puerto COMx --datos <carpeta de prueba>`;
   recorrer P14 (inicio/pausa), P15 (resets), P16 (con equipo) y P17 (desenchufar y reconectar en juego y
   en pausa), y medir la latencia entre la pantalla de la consola y la mesa.
2. Mostrar en la mesa y en *Estado de conexiones* el motivo concreto (p. ej. «no se encuentra COM5») y
   los contadores de tramas; hoy van al registro.
3. Número de serie de la consola; COM PORT 1 sin probar; ver si algún ajuste agrega décimas.
4. Elegir el puerto sin línea de comandos (instalador o ajuste), en el hito C.
