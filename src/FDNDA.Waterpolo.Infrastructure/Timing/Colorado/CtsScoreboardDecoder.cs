namespace FDNDA.Waterpolo.Infrastructure.Timing.Colorado;

// Decodificador del flujo de datos de tablero de Colorado Time Systems («Scoreboard Out»).
//
// ESTADO: HIPÓTESIS SIN VALIDAR EN HARDWARE. El formato NO viene de un documento de Colorado: es el
// que describen, de forma coincidente, proyectos de terceros que lo obtuvieron observando el enlace
// (ver docs/colorado-system6.md §6): scorebox-consoles (MIT), coloradoScoreboard (MIT), vsCTS,
// CTS_Scoreboard y el artículo de Marco Walther (2015). Esta es una implementación propia a partir de
// esas descripciones; no copia su código.
//
// Formato descrito por esas fuentes (9600 baudios, 8 bits, paridad par, 1 bit de parada; emisión
// continua; el tablero nunca responde):
//   - Byte de dirección (bit 7 = 1): canal = ((b >> 1) & 0x1F) ^ 0x1F. Bit 0 = 0: los bytes que siguen
//     son dígitos («lectura»); bit 0 = 1: son códigos de formato del mismo canal.
//   - Byte de dato (bit 7 = 0): nibble alto = posición del dígito (0–7); nibble bajo invertido
//     (XOR 0x0F) = valor. En una lectura, un valor mayor que 9 es un dígito apagado (en blanco).
// Un canal se da por completo cuando llega la lectura de otro canal (como hace scorebox-consoles).

/// <summary>Contenido de un canal del tablero (un módulo de 8 dígitos) recibido completo.</summary>
/// <param name="Channel">Número de canal decodificado (0–31).</param>
/// <param name="Digits">Ocho posiciones: 0–9, o nulo si el dígito está en blanco o no llegó.</param>
/// <param name="Formats">Ocho códigos de formato (0–15) o nulo; los últimos conocidos del canal.</param>
/// <param name="FormatsKnown">Falso si nunca llegó un código de formato para este canal.</param>
public sealed record CtsModuleUpdate(int Channel, IReadOnlyList<int?> Digits, IReadOnlyList<int?> Formats, bool FormatsKnown);

/// <summary>Decodificador incremental: tolera tramas cortadas entre lecturas y varias tramas juntas.</summary>
public sealed class CtsScoreboardDecoder
{
    public const int Positions = 8;
    public const int Channels = 32;

    private readonly int?[][] _knownFormats = NewTable();
    private readonly bool[] _formatsKnown = new bool[Channels];

    private Pending? _pending;
    private Target _target = Target.None;
    private int _targetChannel;

    private enum Target
    {
        None,
        Readout,
        Format,
    }

    /// <summary>Bytes recibidos en total.</summary>
    public long BytesRead { get; private set; }

    /// <summary>Bytes de dato descartados por no tener un canal al que pertenecer.</summary>
    public long DiscardedBytes { get; private set; }

    /// <summary>Canales completos entregados.</summary>
    public long Updates { get; private set; }

    /// <summary>Byte de dirección para un canal (lo usan las pruebas y la herramienta de captura).</summary>
    public static byte AddressByte(int channel, bool format) =>
        (byte)(0x80 | (((channel ^ 0x1F) & 0x1F) << 1) | (format ? 1 : 0));

    /// <summary>Byte de dato para una posición y un valor (0–15; 15 = en blanco en una lectura).</summary>
    public static byte DataByte(int position, int value) =>
        (byte)(((position & 0x07) << 4) | ((value ^ 0x0F) & 0x0F));

    public IReadOnlyList<CtsModuleUpdate> Feed(ReadOnlySpan<byte> bytes)
    {
        var completed = new List<CtsModuleUpdate>();
        foreach (var b in bytes)
        {
            BytesRead++;
            if (b >= 0x80)
            {
                var channel = ((b >> 1) & 0x1F) ^ 0x1F;
                var isFormat = (b & 1) == 1;
                if (!isFormat && _pending is { } current && current.Channel != channel)
                {
                    completed.Add(Complete(current));
                    _pending = null;
                }

                if (!isFormat)
                {
                    _pending ??= new Pending(channel);
                }

                _target = isFormat ? Target.Format : Target.Readout;
                _targetChannel = channel;
                continue;
            }

            var position = (b >> 4) & 0x07;
            var value = (b & 0x0F) ^ 0x0F;
            switch (_target)
            {
                case Target.Readout:
                    _pending!.Digits[position] = value <= 9 ? value : null;
                    break;
                case Target.Format:
                    _knownFormats[_targetChannel][position] = value;
                    _formatsKnown[_targetChannel] = true;
                    break;
                default:
                    DiscardedBytes++; // dato sin canal (p. ej. al conectarse a mitad de una trama)
                    break;
            }
        }

        return completed;
    }

    /// <summary>Entrega el canal en curso (al cerrar una captura).</summary>
    public IReadOnlyList<CtsModuleUpdate> Flush()
    {
        if (_pending is not { } current)
        {
            return [];
        }

        _pending = null;
        _target = Target.None;
        return [Complete(current)];
    }

    /// <summary>Descarta todo el estado (al reconectar): no se mezclan datos de dos conexiones.</summary>
    public void Reset()
    {
        _pending = null;
        _target = Target.None;
        for (var i = 0; i < Channels; i++)
        {
            Array.Fill(_knownFormats[i], null);
            _formatsKnown[i] = false;
        }
    }

    private CtsModuleUpdate Complete(Pending pending)
    {
        Updates++;
        return new CtsModuleUpdate(
            pending.Channel,
            [.. pending.Digits],
            [.. _knownFormats[pending.Channel]],
            _formatsKnown[pending.Channel]);
    }

    private static int?[][] NewTable()
    {
        var table = new int?[Channels][];
        for (var i = 0; i < Channels; i++)
        {
            table[i] = new int?[Positions];
        }

        return table;
    }

    private sealed class Pending(int channel)
    {
        public int Channel { get; } = channel;

        public int?[] Digits { get; } = new int?[Positions];
    }
}
