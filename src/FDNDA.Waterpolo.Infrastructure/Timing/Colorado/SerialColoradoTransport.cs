using System.IO.Ports;
using System.Runtime.CompilerServices;

namespace FDNDA.Waterpolo.Infrastructure.Timing.Colorado;

/// <summary>
/// Transporte serie de SOLO LECTURA hacia COM PORT 2 de la System 6, con los parámetros observados
/// (115200 baudios, 8 bits, sin paridad, 1 bit de parada; docs/colorado-system6.md §7). Nunca llama a
/// ninguna operación de escritura; DTR y RTS quedan apagados y sin control de flujo. Si Windows quita
/// el puerto (adaptador desenchufado), la recepción falla y la fuente reintenta con un transporte nuevo.
/// </summary>
/// <remarks>Depende del hardware: no tiene pruebas automáticas; se verifica con la consola.</remarks>
public sealed class SerialColoradoTransport(string portName) : IColoradoTransport
{
    public const int BaudRate = 115200;

    private SerialPort? _port;

    public string Description => $"{portName} ({BaudRate} 8-N-1, solo lectura)";

    public Task OpenAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var port = new SerialPort(portName, BaudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            DtrEnable = false,
            RtsEnable = false,
            ReadTimeout = 500,
        };

        try
        {
            port.Open();
        }
        catch
        {
            port.Dispose();
            throw;
        }

        _port = port;
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReceiveAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var port = _port ?? throw new InvalidOperationException("El puerto no está abierto.");
        var buffer = new byte[4096];
        while (!ct.IsCancellationRequested)
        {
            // Lectura bloqueante con tiempo de espera corto, fuera del hilo del llamador, para poder
            // cancelar sin escribir nada en el puerto.
            var read = await Task.Run(
                () =>
                {
                    try
                    {
                        return port.Read(buffer, 0, buffer.Length);
                    }
                    catch (TimeoutException)
                    {
                        return 0; // sin datos (consola apagada o fuera de waterpolo): no es un error
                    }
                },
                CancellationToken.None).ConfigureAwait(false);

            if (read > 0)
            {
                yield return buffer.AsSpan(0, read).ToArray();
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        _port?.Dispose();
        _port = null;
        return ValueTask.CompletedTask;
    }
}
