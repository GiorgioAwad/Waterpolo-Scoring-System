using Microsoft.Data.Sqlite;

namespace FDNDA.Waterpolo.Infrastructure.Persistence;

/// <summary>
/// Respaldo consistente con la API de backup de SQLite (no copia el archivo activo ni
/// ignora el WAL) y verificación de integridad de la copia.
/// La restauración desde la aplicación pertenece al hito A (ver docs/plan-entrega.md).
/// </summary>
public sealed class SqliteBackupService(SqliteDatabase database)
{
    /// <summary>Crea un respaldo verificado. Falla si el destino ya existe o la copia no es íntegra.</summary>
    public async Task<string> CreateBackupAsync(string destinationPath, CancellationToken ct = default)
    {
        var fullPath = Path.GetFullPath(destinationPath);
        if (File.Exists(fullPath))
        {
            throw new IOException($"Ya existe un archivo en la ruta del respaldo: {fullPath}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        try
        {
            await using (var source = await database.OpenAsync(ct).ConfigureAwait(false))
            {
                var destinationString = new SqliteConnectionStringBuilder
                {
                    DataSource = fullPath,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    Pooling = false,
                }.ToString();

                await using var destination = new SqliteConnection(destinationString);
                await destination.OpenAsync(ct).ConfigureAwait(false);
                source.BackupDatabase(destination);
            }

            var problem = await CheckIntegrityAsync(fullPath, ct).ConfigureAwait(false);
            if (problem is not null)
            {
                throw new IOException($"El respaldo no superó la verificación de integridad: {problem}");
            }

            return fullPath;
        }
        catch
        {
            TryDelete(fullPath);
            throw;
        }
    }

    /// <summary>Devuelve nulo si el archivo es una base íntegra; si no, la descripción del problema.</summary>
    public static async Task<string?> CheckIntegrityAsync(string databasePath, CancellationToken ct = default)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString();

        try
        {
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            var result = Convert.ToString(await command.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
            return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase) ? null : result ?? "sin resultado";
        }
        catch (SqliteException ex)
        {
            return ex.Message;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // se conserva el error original
        }
        catch (UnauthorizedAccessException)
        {
            // se conserva el error original
        }
    }
}
