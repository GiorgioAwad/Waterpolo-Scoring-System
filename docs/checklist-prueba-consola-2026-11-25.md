# Checklist — prueba con la consola Colorado prestada (sin tablero)

**Objetivo:** grabar lo que emite la consola por su salida de tablero para confirmar el formato y
después conectar el programa. La herramienta de captura **nunca envía nada** a la consola: solo escucha.

**Situación:** la FDNDA presta la consola sola, sin tablero ni relojes de posesión. Por eso **no hace
falta cable en Y**: nuestro cable va directo a la salida de tablero y no hay nada oficial que proteger.
Según las descripciones de terceros, la consola emite de forma continua aunque no haya tablero
conectado (falta confirmarlo).

---

## 1. Lo que hace falta

- [ ] La **consola**, con su fuente de alimentación (y, si la hay, la caja de pulsadores).
- [ ] **Adaptador USB a RS-232** (DB9 macho), mejor con chip FTDI, con su controlador instalado.
- [ ] **Plug mono de 1/4"** (TS: punta y malla) con un tramo de cable de 2 hilos.
- [ ] **DB9 hembra con bornera de tornillos** (para no soldar) y un destornillador pequeño.
- [ ] Laptop con la carpeta `artifacts\publish\captura-win-x64` copiada (no necesita instalar .NET),
      una subcarpeta `capturas` y la muestra `samples\cronometraje\captura-sintetica-cts.txt`.

## 2. Armar el cable

```
Plug 1/4" mono                 DB9 HEMBRA (bornera)
  punta (tip)    ───────────►  pin 2   (RX: la laptop recibe)
  malla (sleeve) ───────────►  pin 5   (GND)
                               pin 3   ← NO conectar nada (es transmisión)
```

- [ ] Punta → pin 2, malla → pin 5. **Pin 3 libre.**
- [ ] Con multímetro (si hay): continuidad punta–pin 2 y malla–pin 5, y que no haya cortocircuito entre ellos.

## 3. Preparar la laptop

En PowerShell, dentro de la carpeta de la herramienta:

- [ ] Conectar el adaptador y ejecutar:
      ```powershell
      .\FDNDA.Waterpolo.Captura.exe puertos
      ```
      Anotar el COM del adaptador: **COM____** (desconectar y reconectar para confirmar que es ese).
- [ ] Ensayo con la muestra sintética (debe listar los canales 1, 3, 13 y 14):
      ```powershell
      .\FDNDA.Waterpolo.Captura.exe analizar --entrada captura-sintetica-cts.txt
      ```

## 4. Antes de conectar: fotos

Ya se sabe por las fotos (`evidencia/consola-s6-*.png`): es una **System 6** de waterpolo. El panel
trasero tiene COM PORT 1 y 2, impresora, USB, Ethernet, monitor, teclado, ratón, NEAR END, FAR END, cuatro
conectores pequeños con traba, **POWER (el plug azul es la alimentación)** y **JUDGES I/O** (redondo,
multipin). **Ahí no hay ningún jack «Scoreboard»**: hay que encontrar la salida de tablero.

- [ ] **Preguntar a la FDNDA: ¿dónde enchufan el cable del tablero y de los relojes de posesión?** Pedir que
      lo muestren y, si se puede, que presten ese cable (o el divisor en Y CTS 015-383).
- [ ] Fotos de los **dos costados** y de la **parte inferior** de la consola, buscando un jack de 1/4"
      rotulado «Scoreboard» (o parecido).
- [ ] Si aparece: ¿es de 1/4" (el de guitarra, 6,35 mm)? Sí / No.
- [ ] **No conectar nada a JUDGES I/O, COM PORT 1 ni 2, NEAR END ni FAR END**: ninguno está documentado como
      salida de tiempos.

- [ ] Frente de la consola y **panel trasero** con todos los conectores, de cerca y legibles.
- [ ] Etiqueta con modelo y **número de serie**; versión de firmware y del software de waterpolo.
- [ ] **System 6:** pantallas «Define Module» (canal de cada módulo) y «Scoreboard Time Display»
      (décimas en marcha, cuenta ascendente, qué muestra la posesión).
- [ ] ¿La salida «Scoreboard Out» es un **jack de 1/4"**? Sí / No. Si no lo es, **no forzar nada**: fotos y se consulta.

## 5. Captura

1. [ ] Con la consola **apagada**, conectar nuestro plug a «Scoreboard Out» y el DB9 al adaptador.
2. [ ] Encender la consola y entrar al modo de **waterpolo**.
3. [ ] Iniciar la grabación (cambiar `COM5` por el anotado en el paso 3):
       ```powershell
       .\FDNDA.Waterpolo.Captura.exe capturar --puerto COM5 --salida capturas\s6-01.txt --nota "System 6 prestada por la FDNDA, sin tablero"
       ```
       Cada 2 s la pantalla muestra `segundos · bytes · canales · errores de línea · partido · posesión`.
4. [ ] **Revisar en los primeros 10 segundos:**
   - **bytes sube y errores de línea en 0 o casi** → bien, seguir.
   - **bytes en 0** → revisar que el plug entre a fondo, la bornera (pin 2 y pin 5) y que la consola esté en waterpolo.
   - **bytes sube pero errores de línea también, mucho** → velocidad o paridad distintas: Ctrl+C y repetir con
     otro nombre de archivo y `--baudios 2400`; si sigue, `--paridad ninguna`.
5. [ ] Con la grabación en marcha, hacer esta secuencia y **anotar los segundos de la pantalla** en cada paso
       (hoja del punto 7):
   - [ ] Reposo, reloj detenido, **2 minutos** sin tocar nada.
   - [ ] Arrancar el reloj de partido ~30 s y detenerlo.
   - [ ] Reset de posesión **completo** (30) y **alterno** (20), si existe.
   - [ ] Dejar correr la posesión hasta **0**.
   - [ ] Poner el partido en el **último minuto** (p. ej. 0:45) y dejarlo correr hasta **0:00**.
   - [ ] Pasar al **siguiente periodo**.
   - [ ] Un **gol** de cada equipo, un **tiempo muerto** y una **exclusión**.
   - [ ] Desconectar el DB9 del adaptador 10 s y volver a conectarlo.
   - [ ] Apagar la consola 10 s y volver a encenderla.
6. [ ] Ctrl+C para terminar y revisar:
       ```powershell
       .\FDNDA.Waterpolo.Captura.exe analizar --entrada capturas\s6-01.txt
       ```
       **Éxito** = un canal cuyos dígitos bajan como el tiempo de partido (se espera el 1) y otro como la
       posesión (se espera el 3). Copiar o fotografiar la salida.
7. [ ] (Recomendado) Repetir con la opción «Scoreboard Time Display» cambiada (por ejemplo, décimas en marcha
       activadas y desactivadas), un archivo por cada ajuste: `s6-02-decimas.txt`, …

## Si la consola prestada es la WTTC (inalámbrica)

Según su manual (F1071, pág. 6) tiene **puertos de tablero cableados**, una entrada de pulsadores y **dos
USB-B** (uno de alimentación y otro para un computador «Display Link Plus»). Su formato no está documentado:

- [ ] Fotos del panel trasero y de *Menu → Hardware Options*.
- [ ] Si el puerto de tablero cableado es un jack de 1/4": conectar nuestro cable y capturar como en el punto 5
      (`wttc-cable-01.txt`).
- [ ] Probar también el USB-B libre con un cable USB A–B: ejecutar `puertos`. Si aparece un COM nuevo,
      capturar en él (`wttc-usb-01.txt`). Si no, en el **Administrador de dispositivos** fotografiar el
      dispositivo nuevo y su *Id. de hardware* (Propiedades → Detalles).

## Si algo sale mal

| Síntoma | Qué hacer |
|---|---|
| «No se pudo abrir COMx» | Confirmar el número con `puertos`; cerrar otro programa que use el puerto; reconectar el adaptador. |
| «ya existe» | Otro nombre de archivo (la herramienta no sobrescribe capturas). |
| bytes en 0 | Revisar el cable (pin 2 y pin 5), el modo de waterpolo y que la consola esté encendida. |
| Muchos errores de línea | `--baudios 2400` y luego `--paridad ninguna`, cada vez con un archivo nuevo. |

## Hoja de anotaciones

| Archivo | Segundos (pantalla) | Qué se hizo en la consola | Qué mostraba la consola |
|---|---|---|---|
| | | | |
| | | | |
| | | | |
| | | | |
| | | | |
| | | | |
| | | | |
| | | | |

## Al terminar — qué traer

- [ ] Toda la carpeta `capturas` (también los archivos que salieron mal).
- [ ] Las fotos, la hoja de anotaciones y la salida de `analizar`.

Con eso: conversión a grabación (`convertir`), ensayo con `--reproduccion` en la mesa y, si el formato se
confirma, la lectura dentro del programa (hito B). Procedimiento de fondo en
[colorado-system6.md §6](colorado-system6.md).
