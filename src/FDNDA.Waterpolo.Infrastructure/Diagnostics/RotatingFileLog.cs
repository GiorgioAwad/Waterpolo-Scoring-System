using System.Globalization;
using System.Text;
using FDNDA.Waterpolo.Application.Abstractions;

namespace FDNDA.Waterpolo.Infrastructure.Diagnostics;

/// <summary>
/// Registro de diagnóstico local, acotado y rotado. No recibe nóminas ni datos personales:
/// quien lo usa solo escribe eventos técnicos. Un fallo al escribir el log nunca detiene la mesa.
/// </summary>
public sealed class RotatingFileLog : IDiagnosticLog
{
    private readonly string _directory;
    private readonly long _maxBytes;
    private readonly int _maxFiles;
    private readonly TimeProvider _time;
    private readonly object _sync = new();

    public RotatingFileLog(string directory, TimeProvider time, long maxBytes = 1024 * 1024, int maxFiles = 5)
    {
        _directory = directory;
        _time = time;
        _maxBytes = maxBytes;
        _maxFiles = Math.Max(1, maxFiles);
    }

    public string CurrentFilePath => Path.Combine(_directory, "waterpolo.log");

    public void Info(string message) => Write("INFO", message, null);

    public void Warn(string message) => Write("AVISO", message, null);

    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        var line = new StringBuilder()
            .Append(_time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture))
            .Append(' ').Append(level).Append(' ').Append(message);

        if (exception is not null)
        {
            line.Append(" | ").Append(exception.GetType().Name).Append(": ").Append(exception.Message);
        }

        line.AppendLine();

        lock (_sync)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                RotateIfNeeded();
                File.AppendAllText(CurrentFilePath, line.ToString(), Encoding.UTF8);
            }
            catch (IOException)
            {
                // el diagnóstico es auxiliar
            }
            catch (UnauthorizedAccessException)
            {
                // el diagnóstico es auxiliar
            }
        }
    }

    private void RotateIfNeeded()
    {
        var current = new FileInfo(CurrentFilePath);
        if (!current.Exists || current.Length < _maxBytes)
        {
            return;
        }

        // waterpolo.log → waterpolo.1.log → … ; el más antiguo se descarta.
        for (var index = _maxFiles - 1; index >= 1; index--)
        {
            var source = index == 1 ? CurrentFilePath : Path.Combine(_directory, $"waterpolo.{index - 1}.log");
            var target = Path.Combine(_directory, $"waterpolo.{index}.log");
            if (File.Exists(source))
            {
                File.Move(source, target, overwrite: true);
            }
        }

        if (_maxFiles == 1)
        {
            File.Delete(CurrentFilePath);
        }
    }
}
