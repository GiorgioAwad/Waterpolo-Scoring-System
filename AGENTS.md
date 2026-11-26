# Instrucciones para agentes — Marcador de Waterpolo FDNDA

Aplicación de escritorio WPF (.NET 10, C#) para mesa de control y marcador de waterpolo.
Antes de cambiar algo, lee `README.md`, `docs/requisitos.md` y `docs/arquitectura.md`.

## Entorno

- Windows. El SDK de .NET 10 puede estar solo dentro del repo (`.tools\dotnet`); el `dotnet` global
  del equipo puede ser más antiguo y fallar con `global.json`. En cada sesión de PowerShell:
  `. .\scripts\entorno.ps1` (si falta el SDK: `.\scripts\instalar-sdk-local.ps1`).
- Compilar: `dotnet build FDNDA.Waterpolo.sln -c Release`
- Probar: `dotnet test FDNDA.Waterpolo.sln -c Release`
- Publicar: `.\scripts\publicar.ps1`
- Demo con datos aislados: `.\scripts\demo.ps1 -Datos <carpeta temporal> -Operador "Prueba"`
- El `FDNDA.Waterpolo.exe` de `bin` depende del runtime: en un equipo sin .NET 10 global solo arranca
  si `DOTNET_ROOT` apunta a `.tools\dotnet` (lo hace `entorno.ps1`); si no, Windows muestra «You must
  install or update .NET». El paquete de `artifacts\publish\win-x64` es self-contained y no lo necesita.

## Reglas de arquitectura

- Dependencias: `Application → Domain`; `Infrastructure → Application, Domain`; `Desktop → Application`.
  Solo `src/FDNDA.Waterpolo.Desktop/App.xaml.cs` puede nombrar tipos de Infrastructure.
- El dominio no conoce UI, SQLite, puertos ni archivos. Las reglas del partido viven en `Match`.
- Los ViewModels llaman casos de uso y observan snapshots inmutables. No escriben SQL ni recalculan el marcador.
- Todo cambio de partido pasa por `MatchOperations`: cargar → validar → dominio → commit → publicar.
  El snapshot se publica **solo después del commit**. Cada comando lleva un `CommandId` idempotente.
- Incidencias, revisiones e historial de estados son de **solo inserción**; se corrige con revisiones.
  El resultado se deriva; no se guarda.
- Cambios de esquema: nueva migración `NNNN_nombre.sql` en `Infrastructure/Persistence/Migrations`.
  Nunca edites una migración ya publicada.
- SQL siempre parametrizado. Repositorios concretos; sin repositorio genérico, ORM, API HTTP,
  contenedores ni bus de mensajes.
- En Desktop, `Application` choca con el espacio de nombres `FDNDA.Waterpolo.Application`: escribe
  `System.Windows.Application`. Los proyectos WPF no incluyen `System.IO` en los usings implícitos.
- `TreatWarningsAsErrors` está activo y las versiones de paquetes son fijas en `Directory.Packages.props`.

## Reglas del producto

- Interfaz, mensajes, documentación y nombres de pruebas en **español**. Fechas en UTC en la base; presentación `es-PE`.
- Sin red: nada de llamadas externas, telemetría, CDN ni licencias en línea.
- El programa **nunca** escribe en la consola Colorado. El adaptador real no simula éxito: sin
  evidencia de hardware devuelve «no disponible». No inventes puertos, protocolos ni tramas.
- Sin señal vigente se muestra `--:--` / `--`; no se extrapolan tiempos ni se guarda una hora de juego inventada.
- No automatices sanciones ni decisiones arbitrales sin reglas aprobadas. No declares homologación.
- Sin datos personales reales (ni DNI, fechas de nacimiento o correos). Los datos demo son ficticios.
- Las letras de acceso exigen Alt (`App.RequireAltForAccessKeys`): no lo quites.

## Forma de trabajar

- Cada requisito nuevo lleva pruebas ejecutables sin WPF (dominio o SQLite temporal con
  `TempStorage` y `TestDesk`). Para el tiempo, usa `ManualTimeProvider`; nada de esperas reales.
- Al terminar un cambio, actualiza el estado en `docs/requisitos.md` y `docs/aceptacion.md`.
  Distingue lo **ejecutado** de lo solo escrito; no marques como hecho lo que no verificaste.
- Para probar la interfaz, lanza el programa con `--datos <carpeta temporal>` para no tocar los
  datos del usuario. Los controles tienen `AutomationProperties.AutomationId`.
- No hagas `git push` ni publiques nada. Haz commits solo si te lo piden.
