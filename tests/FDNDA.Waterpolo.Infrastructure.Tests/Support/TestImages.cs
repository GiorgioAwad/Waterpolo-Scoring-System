using System.Buffers.Binary;

namespace FDNDA.Waterpolo.Infrastructure.Tests.Support;

/// <summary>
/// Cabeceras sintéticas de PNG y JPEG con el tamaño pedido, suficientes para la validación del
/// dominio (que no decodifica píxeles). Los datos de relleno distinguen una imagen de otra.
/// </summary>
internal static class TestImages
{
    public static byte[] Png(int width, int height, int padding = 64, byte fill = 0x11)
    {
        var bytes = new byte[33 + padding];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), (uint)height);
        bytes[24] = 8; // profundidad
        bytes[25] = 6; // RGBA
        bytes.AsSpan(33).Fill(fill);
        return bytes;
    }

    public static byte[] Jpeg(int width, int height)
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };
        bytes.AddRange([0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1, 0, 0]);
        bytes.AddRange([0xFF, 0xDB, 0x00, 0x04, 0x00, 0x00]); // tabla de cuantización mínima
        bytes.AddRange([0xFF, 0xC0, 0x00, 0x11, 0x08, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 0x03]);
        bytes.AddRange(new byte[9]);
        bytes.AddRange([0xFF, 0xD9]);
        return [.. bytes];
    }
}
