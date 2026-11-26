# Reporte de sesión — Hito B, preparación sin consola (2026-11-24)

**Qué se hizo:** se buscó software que ya lea la consola y se encontró una descripción de terceros
de su salida de tablero (cinco fuentes coincidentes; una, scorebox-consoles, con waterpolo en System
6). Con ella se preparó, **sin activarlo en el programa de competencia**, un decodificador probado con
bytes sintéticos y una herramienta aparte de captura de solo lectura para el día de la prueba.
**RF11 sigue bloqueado** hasta confirmar todo con la consola de la FDNDA. No hubo commits ni push.

## Hallazgo

Detalle, fuentes y cautelas en [colorado-system6.md §6](colorado-system6.md). En resumen: salida
«Scoreboard Out» (jack de 1/4"), 9600 baudios 8-E-1, emisión continua sin respuesta del tablero
(basta escuchar), bytes de dirección de canal y bytes de dígito con el valor invertido; en waterpolo,
según scorebox-consoles, partido = canal 1, posesión = canal 3, periodo = canal 14. Riesgos: los
canales se pueden reasignar en la consola, la interpretación de las marcas de formato es heurística
y de una sola fuente, y nada se probó todavía con hardware.

## Archivos

| Área | Creados o modificados |
|---|---|
| Infraestructura | Nuevos `Timing/Colorado/CtsScoreboardDecoder.cs`, `CtsWaterPoloInterpreter.cs`, `CtsCapture.cs`; `Timing/TimingRecording.cs` (`TimingRecordingWriter`). `ColoradoSystem6TimingSource` **sin cambios** (sigue «no disponible»). |
| Herramienta | Nuevo proyecto `tools/FDNDA.Waterpolo.Captura` (comandos `puertos`, `capturar`, `analizar`, `convertir`); en la solución, carpeta `tools`. Paquete `System.IO.Ports` 10.0.15 (MIT) solo para ella. |
| Muestras y scripts | Nuevo `samples/cronometraje/captura-sintetica-cts.txt` (codificador Python independiente); `samples/README.md`; `scripts/publicar.ps1` publica también la herramienta en `artifacts\publish\captura-win-x64`. |
| Pruebas | Nuevo `Infrastructure.Tests/CtsScoreboardTests.cs` (30 casos, bytes sintéticos). |
| Documentación | `docs/colorado-system6.md` (estado, §5, §6 con procedimiento), `docs/requisitos.md` (RF11), `docs/aceptacion.md` (§1.14), `docs/arquitectura.md` (D41, §7), `docs/plan-entrega.md` (B3, B4), `README.md`, `Directory.Packages.props`, `Prompt-Codex-Hito-A.md` y este reporte. |

## Evidencia ejecutada

- Base: **361/361**. Final: compilación Release **0 advertencias**; **391/391** (dominio 111,
  infraestructura 201, ViewModels 79). `.\scripts\publicar.ps1` regeneró el paquete y la herramienta.
- La herramienta publicada (self-contained) se ejecutó sin el SDK: `analizar` y `convertir` sobre la
  captura sintética leen 8:00 → 7:59…, el reset de posesión, la reconexión y el último minuto con
  décimas. `puertos` listó COM3 y COM4 de este equipo, **que no se abrieron**.

## Qué hace falta para validar (B1–B3)

1. Autorización de la FDNDA y la consola fuera de competencia, con el checklist de §3.
2. Cable en Y mono de 1/4" (p. ej. Hosa YPP-111), adaptador USB a RS-232 y un conector DB9 para
   soldar (punta al pin 2, malla al pin 5), según la descripción de terceros.
3. Seguir el procedimiento de colorado-system6.md §6. Si la captura confirma el formato y los canales,
   el paso siguiente es el transporte serie de solo lectura dentro del programa, los umbrales medidos
   y los criterios P14–P17. Si no lo confirma, se registra la limitación y se escala a Colorado.
