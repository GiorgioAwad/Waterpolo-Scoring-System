namespace FDNDA.Waterpolo.Infrastructure.Storage;

/// <summary>
/// Impide dos instancias operadoras sobre los mismos datos. Mantiene abierto en exclusiva
/// un archivo de bloqueo dentro de la carpeta de datos; el sistema lo libera aunque el
/// proceso termine de forma abrupta.
/// </summary>
public sealed class InstanceLock : IDisposable
{
    private readonly FileStream _stream;

    private InstanceLock(FileStream stream)
    {
        _stream = stream;
    }

    /// <summary>Devuelve el bloqueo, o nulo si otra instancia ya usa esos datos.</summary>
    public static InstanceLock? TryAcquire(string lockFilePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(lockFilePath))!);
        try
        {
            var stream = new FileStream(
                lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: 1, FileOptions.DeleteOnClose);
            return new InstanceLock(stream);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Dispose() => _stream.Dispose();
}
