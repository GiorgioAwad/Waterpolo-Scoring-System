using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;
using System.Text;
using FDNDA.Waterpolo.Infrastructure.Timing;
using FDNDA.Waterpolo.Infrastructure.Timing.Colorado;

// Herramienta de captura de la consola Colorado System 6 (hito B).
//
// SOLO LECTURA: abre el puerto serie para recibir y nunca llama a ninguna operación de escritura;
// DTR y RTS quedan apagados y sin control de flujo. Úsese únicamente con autorización de la FDNDA.
// Por defecto lee COM PORT 2 de la consola (115200 8-N-1, tramas CTSWP; docs/colorado-system6.md §7).

Console.OutputEncoding = Encoding.UTF8;
try
{
    return Run(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine("Use «ayuda» para ver las opciones.");
    return 2;
}
catch (TimingRecordingException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static int Run(string[] args)
{
    var command = args.Length == 0 ? "ayuda" : args[0].ToLowerInvariant();
    var options = Options.Parse(args.Skip(1).ToArray());
    switch (command)
    {
        case "puertos":
            var ports = SerialPort.GetPortNames().Order(StringComparer.OrdinalIgnoreCase).ToList();
            Console.WriteLine(ports.Count == 0 ? "No se encontraron puertos serie." : "Puertos serie: " + string.Join(", ", ports));
            return 0;
        case "capturar":
            return Capture(options);
        case "analizar":
            var capture = CtsCaptureFile.Load(options.Required("entrada"));
            Console.WriteLine(CtsCaptureConverter.IsCtswp(capture) ? CtswpReport(capture) : CtsCaptureAnalyzer.Report(capture, options.Channels()));
            return 0;
        case "convertir":
            var source = CtsCaptureFile.Load(options.Required("entrada"));
            var ctswp = CtsCaptureConverter.IsCtswp(source);
            var channels = options.Channels();
            var recording = ctswp ? CtsCaptureConverter.FromCtswp(source) : CtsCaptureConverter.ToRecording(source, channels);
            var output = options.Required("salida");
            string[] comments = ctswp
                ?
                [
                    $"Convertida de la captura «{source.Name}» (tramas CTSWP1.1c de COM PORT 2).",
                    "Formato observado con la consola de la FDNDA (docs/colorado-system6.md §7); segundos enteros.",
                ]
                :
                [
                    $"Convertida de la captura «{source.Name}» con canales partido {channels.GameClock}, posesión {channels.ShotClock}, periodo {channels.Period}.",
                    "Interpretación de dígitos: hipótesis de scorebox-consoles, pendiente de validación con la consola.",
                ];
            File.WriteAllText(output, TimingRecordingWriter.Write(recording, comments), new UTF8Encoding(false));
            Console.WriteLine($"{recording.Samples.Count} lecturas escritas en {output}. Reprodúzcala con --reproduccion.");
            return 0;
        default:
            Console.WriteLine("""
                FDNDA.Waterpolo.Captura — captura de SOLO LECTURA de la consola Colorado System 6.

                  puertos
                      Lista los puertos serie de este equipo.
                  capturar --puerto COM5 --salida captura.txt [--baudios 115200] [--paridad ninguna|par|impar]
                           [--segundos 600] [--nota "texto"]
                      Graba todo lo que llega (Ctrl+C para terminar). Muestra en vivo los relojes leídos.
                  analizar --entrada captura.txt [--canal-partido 1] [--canal-posesion 3] [--canal-periodo 14]
                      Resume la captura: tramas CTSWP (COM PORT 2) o canales del flujo de tablero.
                  convertir --entrada captura.txt --salida grabacion.csv [canales como en analizar]
                      Convierte la captura en una grabación normalizada para --reproduccion.

                Conexión observada (docs/colorado-system6.md §7): adaptador USB a RS-232 en COM PORT 2 de la
                consola, 115200 baudios, 8 bits, sin paridad. Nunca conecte nada a la consola sin autorización.
                """);
            return command is "ayuda" or "--help" or "-h" ? 0 : 2;
    }
}

static int Capture(Options options)
{
    var portName = options.Required("puerto");
    var output = options.Required("salida");
    var baud = options.Int("baudios", SerialColoradoTransport.BaudRate);
    var parity = options.Value("paridad") switch
    {
        null or "ninguna" => Parity.None,
        "par" => Parity.Even,
        "impar" => Parity.Odd,
        var other => throw new ArgumentException($"Paridad «{other}» no válida: use par, impar o ninguna."),
    };
    var limit = options.Value("segundos") is null ? (TimeSpan?)null : TimeSpan.FromSeconds(options.Int("segundos", 0));
    if (File.Exists(output))
    {
        throw new ArgumentException($"«{output}» ya existe: elija otro nombre para no sobrescribir una captura.");
    }

    using var stop = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        stop.Cancel();
    };
    if (limit is { } l)
    {
        stop.CancelAfter(l);
    }

    using var file = new StreamWriter(output, append: false, new UTF8Encoding(false)) { AutoFlush = true };
    file.Write(CtsCaptureFile.Header(
    [
        "Captura cruda de la salida de tablero (solo lectura).",
        $"Puerto {portName}, {baud} baudios, paridad {parity}, 8 bits, 1 bit de parada.",
        $"Inicio (UTC): {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}.",
        options.Value("nota") ?? "Sin nota.",
    ]));

    var clock = Stopwatch.StartNew();
    var interpreter = new CtsWaterPoloInterpreter();
    var ctswp = new CtswpFrameDecoder();
    var buffer = new byte[4096];
    var errors = 0;
    var opened = false;
    long lastReport = 0;
    int? game = null;
    int? shot = null;
    Console.WriteLine($"Capturando de {portName} en {output}. Ctrl+C para terminar.");

    while (!stop.IsCancellationRequested)
    {
        using var port = new SerialPort(portName, baud, parity, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            DtrEnable = false,
            RtsEnable = false,
            ReadTimeout = 500,
        };
        port.ErrorReceived += (_, _) => Interlocked.Increment(ref errors);
        try
        {
            port.Open();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.WriteLine($"No se pudo abrir {portName}: {ex.Message} Se reintenta en 1 s.");
            stop.Token.WaitHandle.WaitOne(1000);
            continue;
        }

        if (opened)
        {
            file.Write(CtsCaptureFile.ReconnectionLine(clock.Elapsed));
            interpreter.Reset();
            ctswp.Reset();
            Console.WriteLine("Puerto abierto de nuevo: se registra una reconexión.");
        }

        opened = true;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                int read;
                try
                {
                    read = port.Read(buffer, 0, buffer.Length);
                }
                catch (TimeoutException)
                {
                    read = 0;
                }

                if (read > 0)
                {
                    file.Write(CtsCaptureFile.Line(clock.Elapsed, buffer.AsSpan(0, read)));
                    foreach (var reading in interpreter.Feed(buffer.AsSpan(0, read)).Concat(ctswp.Feed(buffer.AsSpan(0, read))))
                    {
                        game = reading.GameClockRemainingMs ?? game;
                        shot = reading.ShotClockRemainingMs ?? shot;
                    }
                }

                if (clock.ElapsedMilliseconds - lastReport >= 2000)
                {
                    lastReport = clock.ElapsedMilliseconds;
                    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"{clock.Elapsed.TotalSeconds,6:0} s · {interpreter.Decoder.BytesRead} bytes · tramas CTSWP {ctswp.ValidFrames} (descartadas {ctswp.InvalidFrames}) · " +
                        $"errores de línea {errors} · partido {Format(game)} · posesión {Format(shot)}"));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Se perdió el puerto ({ex.Message}). Se reintenta en 1 s.");
            stop.Token.WaitHandle.WaitOne(1000);
        }
    }

    file.Write($"# Fin: {clock.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s, {interpreter.Decoder.BytesRead} bytes, " +
        $"{errors} errores de línea (paridad o trama) informados por el puerto.\n");
    Console.WriteLine($"Captura terminada: {output}. Revise con «analizar --entrada {output}».");
    return 0;
}

static string CtswpReport(CtsCapture capture)
{
    var decoder = new CtswpFrameDecoder();
    var report = new StringBuilder();
    report.AppendLine(string.Create(CultureInfo.InvariantCulture,
        $"Captura «{capture.Name}»: {capture.Duration.TotalSeconds:0.0} s, tramas CTSWP1.1c (COM PORT 2)."));
    (int?, int?, int?)? last = null;
    var changes = 0;
    foreach (var chunk in capture.Chunks)
    {
        if (chunk.IsReconnection)
        {
            decoder.Reset();
            report.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{chunk.At.TotalSeconds,8:0.0} s  (reconexión)"));
            continue;
        }

        foreach (var r in decoder.Feed(chunk.Bytes))
        {
            var now = (r.GameClockRemainingMs, r.ShotClockRemainingMs, r.Period);
            if (now != last && changes++ < 40)
            {
                report.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"{chunk.At.TotalSeconds,8:0.0} s  partido {Format(r.GameClockRemainingMs)}  posesión {Format(r.ShotClockRemainingMs)}  periodo {r.Period?.ToString(CultureInfo.InvariantCulture) ?? "--"}"));
            }

            last = now;
        }
    }

    if (changes > 40)
    {
        report.AppendLine(string.Create(CultureInfo.InvariantCulture, $"… y {changes - 40} cambios más."));
    }

    report.Append(string.Create(CultureInfo.InvariantCulture,
        $"Tramas válidas {decoder.ValidFrames}, descartadas {decoder.InvalidFrames}, bytes sueltos {decoder.DiscardedBytes}."));
    return report.ToString();
}

static string Format(int? ms) => ms is { } v ? string.Create(CultureInfo.InvariantCulture, $"{v / 60000}:{v / 1000 % 60:00}.{v / 100 % 10}") : "--";

internal sealed class Options
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public static Options Parse(string[] args)
    {
        var options = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
            {
                throw new ArgumentException($"Opción no reconocida o sin valor: «{args[i]}».");
            }

            options._values[args[i][2..]] = args[++i];
        }

        return options;
    }

    public string? Value(string name) => _values.TryGetValue(name, out var v) ? v : null;

    public string Required(string name) => Value(name) ?? throw new ArgumentException($"Falta --{name}.");

    public int Int(string name, int fallback) =>
        Value(name) is not { } text ? fallback
        : int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v
        : throw new ArgumentException($"--{name} debe ser un número entero.");

    public CtsWaterPoloChannels Channels() =>
        new(Int("canal-partido", 1), Int("canal-posesion", 3), Int("canal-periodo", 14));
}
