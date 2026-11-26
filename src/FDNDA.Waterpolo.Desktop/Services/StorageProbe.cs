using System.IO;
using FDNDA.Waterpolo.Desktop.ViewModels;

namespace FDNDA.Waterpolo.Desktop.Services;

/// <summary>Comprueba que la base de datos siga en su sitio y cuánto espacio queda en su disco.</summary>
internal sealed class StorageProbe(string databasePath) : IStorageProbe
{
    public StorageStatus Check()
    {
        var root = Path.GetPathRoot(Path.GetFullPath(databasePath)) ?? string.Empty;
        long? free = null;
        try
        {
            var drive = new DriveInfo(root);
            if (drive.IsReady)
            {
                free = drive.AvailableFreeSpace;
            }
        }
        catch (ArgumentException)
        {
            // ruta de red u otra raíz sin unidad: se informa como desconocido
        }

        return new StorageStatus(File.Exists(databasePath), free, root.TrimEnd('\\'));
    }
}
