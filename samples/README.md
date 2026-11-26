# Muestras

- `plantilla-nomina.csv`: plantilla de nóminas (CSV UTF-8) con las columnas
  `equipo_codigo,equipo_nombre,jugador_nombre,numero_gorro`. Los nombres son ficticios.
- `plantilla-nomina.xlsx`: el mismo contenido como libro de Excel (generado con openpyxl, sin
  macros), con el gorro como número.
- `plantilla-nomina-excel.xlsx`: el mismo contenido **guardado por Microsoft Excel 16** (cabecera en
  negrita, columnas ajustadas, sin información personal). Comprueba que el lector acepta lo que produce
  Excel y no solo una librería (`Un_XLSX_guardado_por_Microsoft_Excel_se_importa_como_la_CSV`).

Ambas se importan desde *Eventos y nóminas → Importar nómina…* y sirven de fixture a las pruebas
de importación (`RosterImportTests`). Excel puede guardar el CSV con punto y coma según la
configuración regional; el programa lo acepta. Use «CSV UTF-8» al guardar para no perder tildes.

## Identidad de ejemplo (`identidad-ejemplo/`)

Imágenes PNG **originales y genéricas** creadas para el proyecto (formas geométricas y texto); no son
marcas oficiales de la FDNDA, del Panamericano ni de ningún auspiciador real:

- `logo-evento-ejemplo.png`, `logo-organizacion-ejemplo.png` y `auspiciador-a/b/c-ejemplo.png`: la
  plantilla de ejemplo del Panamericano Sub-17 2027 (*Identidad y plantilla… → Usar plantilla de ejemplo*).
- `escudo-delfines-ejemplo.png`, `escudo-tiburones-ejemplo.png`: escudos de los dos clubes ficticios
  de la demo (se cargan al crear una base demo nueva).
- `bandera-ficticia-1.png`, `bandera-ficticia-2.png`: banderas que no corresponden a ningún país, para
  probar *Cargar…* junto a **Bandera**.

El programa las lleva embebidas (proyecto Infrastructure), así que la plantilla de ejemplo funciona
también en el paquete publicado. Para la identidad real, reemplácelas desde la interfaz por las que
entregue la FDNDA o el organizador.

## Cronometraje (`cronometraje/`)

- `consola-real-s6-guiada.csv`: grabación normalizada convertida de una captura **real** de COM PORT 2
  de la System 6 de la FDNDA (2026-11-24, `docs/evidencia/capturas/s6-com2-02-guiada.txt`): 3 min en
  reposo con 6:00, reloj en marcha, Reset de posesión (28) y alterno (18). Segundos enteros. Para
  ensayar con `--perfil demo --reproduccion`.
- `captura-sintetica-cts.txt`: captura **sintética** de la salida de tablero en el formato que
  describen terceros (colorado-system6.md §6), generada con un codificador en Python independiente del
  programa, para probar `tools/FDNDA.Waterpolo.Captura` (`analizar`, `convertir`) sin consola. **No
  viene de ninguna consola.**
- `guion-sintetico.csv`: grabación **sintética** en el formato normalizado del programa (R9), para
  ensayar con `--reproduccion` (o `.\scripts\demo.ps1 -Reproduccion …`). Guion de 40 s: reposo,
  juego, reset de posesión, pausa, corte de señal de 4 s, reconexión con sesión nueva, reloj de
  posesión apagado y silencio final. **No es una captura del Colorado System 6 ni su protocolo**:
  los valores se generaron a mano. Las capturas autorizadas de la consola se convertirán a este
  formato cuando exista el decodificador (hito B).

Formato (detalle en `src/FDNDA.Waterpolo.Infrastructure/Timing/TimingRecording.cs`): líneas vacías y
las que empiezan con `#` se ignoran; luego `formato,fdnda-cronometraje-normalizado,1`, el encabezado
`t_ms,sesion,partido_ms,posesion_ms,en_marcha,periodo,precision` y una muestra por línea.
`t_ms` es el instante desde el comienzo (no decreciente); `sesion` el número de conexión (1, 2…,
no decreciente); los relojes son milisegundos restantes y vacíos si la muestra no los trae;
`en_marcha` es 1, 0 o vacío; `periodo` vacío o 1–20; `precision` es `segundos`, `decimas`,
`centesimas`, `milisegundos` o `desconocida`. Un hueco sin filas es un corte de señal. Una línea
final `fin,<t_ms>` alarga el silencio final.
