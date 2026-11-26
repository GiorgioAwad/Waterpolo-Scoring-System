# Avisos nuevos y faltas por jugador en el marcador del público

**Fecha:** 2026-11-25 · **Estado:** aprobado por el usuario («hazlo») e **implementado** el mismo día (aceptación §1.19) · **Alcance:** RF07, RF08 y la
lectura del reloj de tiempo muerto de la consola (RF11). Continúa el diseño «A · Transmisión»
(`2026-11-25-marcador-publico-design.md`), que no cambia salvo lo indicado aquí.

## 1. Pedido

«Animaciones para las expulsiones, final de tiempo muerto, faltas, etc. En la pantalla debe verse la
cantidad de faltas que tiene cada jugador.» Exclusión, expulsión, gol y tiempo muerto (inicio) ya se
animaban; se agregan falta personal, fin de tiempo muerto y anuncios de periodo, y las faltas por
jugador en pantalla.

Decisiones del usuario (2026-11-25):

- Faltas de un jugador = **faltas personales + exclusiones + expulsiones** vigentes.
- Se muestran como **gorros bajo el resultado** (solo jugadores con faltas, con 3 puntos).
- El fin del tiempo muerto se toma del **reloj de tiempo muerto de la consola**.
- Avisos extra: **falta personal**, **fin de periodo** (descanso y final) e **inicio de periodo**.

## 2. Faltas por jugador

- `MatchStatistics.PlayerFouls(match)` (dominio): por jugador de la nómina, cuenta las incidencias no
  anuladas cuya última revisión es `PersonalFoul` o `Exclusion` (cualquier clase) y lo atribuye al
  jugador de esa revisión. Solo jugadores con al menos una falta, ordenados por equipo y gorro.
- El snapshot lleva `Fouls` (`PlayerFoulSnapshot(Side, RosterEntryId, Cap, PlayerName, Fouls)`). El
  marcador no recalcula.
- En el penal la mesa registra al lanzador: la falta que lo provocó solo cuenta si se registra aparte
  como «Falta personal».
- **No hay sanción automática.** Con 3 o más faltas se llenan los tres puntos; no se muestra «fuera» ni
  cambia el color (el límite no está aprobado como regla, requisitos §5 punto 2). Más de 3 faltas: los
  3 puntos llenos y el número junto al gorro.

### Presentación

Bajo la barra de color de cada equipo (y = ~600 del lienzo), una fila de chips: círculo blanco de 78 px
con el gorro (44 px, negro, Bold) y debajo tres puntos de 17 px (blanco lleno = falta, blanco al 22 % =
libre). Hasta 6 chips por fila; con más filas el conjunto se reduce para no pasar de 150 px de alto. Visibles en juego, descanso y final; ocultos en la previa y
en la tanda (los lanzamientos usan ese espacio). Cuando un gorro suma una falta, su chip late una vez
(escala 1 → 1,35 → 1 con resplandor ámbar, ~0,9 s), al terminar el aviso del lado.

## 3. Avisos

| Aviso | Disparo | Forma | Color |
|---|---|---|---|
| Falta personal (nuevo) | `IncidentRegistered` de `PersonalFoul` | «Toma del lado» del equipo: gorro en círculo blanco, «FALTA PERSONAL», línea «2.ª FALTA», bandera + jugador + equipo | Ámbar fijo `#F2994A → #5C2A06` |
| Exclusión / expulsión | como hoy | Se agrega la línea «n.ª FALTA» | sin cambio |
| Fin de tiempo muerto (nuevo) | fin detectado en el reloj de la consola (§4) | «Toma del lado» del equipo del último tiempo muerto registrado en el periodo: cronómetro, «FIN DEL / TIEMPO MUERTO», equipo | Azul `#2F80ED → #0B2A63` |
| Anuncio de periodo (nuevo) | `Started`, `StageChanged` (periodo, descanso, tanda), `Finished` | En el centro: la línea del periodo (con sus puntos) y el titular, si lo hay, aparecen con escala 1,8 → 1 y desenfoque, y un brillo blanco suave cruza toda la pantalla. Nada opaco sobre los relojes. En el final, el número del ganador late en dorado. | — |

- Ordinal femenino del público: «1.ª», «2.ª», «3.ª»… (`ScoreboardTexts.FoulOrdinal`).
- Casillas nuevas en la mesa: «falta» (`FoulAnimationsEnabled`) y «periodos»
  (`PeriodAnimationsEnabled`). El fin de tiempo muerto obedece a «tiempo muerto».
- Siguen sin animar: corregir, anular, reabrir, reprogramar, abrir el partido (`Loaded`) o la pantalla.
- El anuncio de periodo dura 2,2 s y usa su propia generación (`AnnouncementGeneration`); no reemplaza
  ni corta un aviso «toma del lado».

## 4. Reloj de tiempo muerto

- **Consola:** bytes 21–24 de la trama `CTSWP1.1c` (`m:ss` / `:ss`, en blanco sin tiempo muerto;
  colorado-system6.md §7). `ColoradoReading` y `TimingSnapshot` ganan `TimeoutClockRemainingMs`
  (opcional; nulo = sin tiempo muerto en curso o la fuente no lo informa). Un campo inválido invalida
  la trama, como los demás relojes.
- **Seguimiento (`TimingStateTracker`):** el reloj de tiempo muerto se guarda con la misma vigencia que
  los otros. `TimingView` gana `TimeoutClock` (`ClockReading`) y `TimeoutsEnded` (contador).
  **Fin detectado** cuando una muestra aceptada trae 0 o nada y la muestra aceptada anterior traía más
  de 0, ambas de la misma sesión y separadas por no más del umbral de vigencia. Una reconexión, una
  pérdida o pasar de 0 a blanco no cuentan.
- **Marcador:** mientras `TimeoutClock` es vigente y mayor que 0 el anillo pasa a azul `#2F80ED`, el
  número muestra la cuenta (`m:ss` si pasa de 59 s; si no, segundos) y la etiqueta dice
  «TIEMPO MUERTO». La fracción es la lectura ÷ el mayor valor leído en ese tiempo muerto (no se
  supone una duración). Al subir `TimeoutsEnded` (no al abrir la pantalla) se busca el último tiempo
  muerto vigente del periodo en curso; si existe, sale su aviso; si no, solo vuelve el anillo de
  posesión.
- **Simulador (demo):** `ISimulationControl.StartTimeout(int seconds)` y `ClearTimeout()`. Iniciar
  detiene el reloj de partido; el reloj de tiempo muerto corre aparte hasta 0 y queda en 0 hasta
  terminarlo. La mesa demo agrega «Iniciar tiempo muerto (60 s)» y «Terminar tiempo muerto». La
  reproducción de grabaciones no cambia.

## 5. Pruebas (sin WPF)

- Dominio: faltas por jugador con corrección de jugador, de tipo de exclusión y anulación.
- Decodificador: trama real con `1:00`, ` :59` y en blanco; campo inválido descarta la trama.
- Seguimiento: fin detectado (>0 → 0 y >0 → blanco); no por reconexión, pérdida ni 0 → blanco.
- Simulador: corre aparte, detiene el partido, queda en 0, `ClearTimeout` lo borra.
- ViewModel: aviso de falta con «2.ª FALTA»; línea de falta en exclusión; chips por equipo y ocultos
  en la previa; anuncio de periodo en inicio, descanso, final y no en reabrir; fin de tiempo muerto
  con y sin registro en la mesa, y no al abrir; casillas desactivadas.

## 6. Fuera de alcance

Tiempos muertos restantes por equipo, exclusiones activas con su cuenta (la consola las envía: queda
para otra entrega), sanción automática por acumulación de faltas, colores configurables.
