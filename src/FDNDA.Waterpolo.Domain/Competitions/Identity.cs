using System.Buffers.Binary;
using System.Security.Cryptography;
using FDNDA.Waterpolo.Domain.Common;

namespace FDNDA.Waterpolo.Domain.Competitions;

// Identidad visual: escudos, banderas, logos y auspiciadores. Las imágenes viven dentro de la
// base (tabla asset) para que el respaldo sea un único archivo coherente. El dominio solo ve
// bytes: no abre archivos ni decodifica píxeles.

/// <summary>Uso de una imagen. En la base se guarda con su nombre en español.</summary>
public enum AssetKind
{
    Crest = 1,
    Flag = 2,
    Sponsor = 3,
    EventLogo = 4,
    Other = 5,
}

/// <summary>
/// Límites de las imágenes. Provisionales hasta validar en la pantalla LED real: alcanzan para
/// un logo nítido a 1920×1080 sin cargar la base ni la memoria del marcador.
/// </summary>
public static class ImageRules
{
    /// <summary>Peso máximo por imagen (2 MB). La tabla <c>asset</c> impone el mismo límite.</summary>
    public const int MaxBytes = 2 * 1024 * 1024;

    public const int MinSidePixels = 16;

    public const int MaxSidePixels = 4096;

    public const int MaxSponsors = 8;

    public const string AcceptedFormatsText = "PNG o JPEG";

    public static string KindText(AssetKind kind) => kind switch
    {
        AssetKind.Crest => "escudo",
        AssetKind.Flag => "bandera",
        AssetKind.Sponsor => "auspiciador",
        AssetKind.EventLogo => "logo",
        _ => "imagen",
    };
}

/// <summary>Formato y tamaño leídos de la cabecera de la imagen.</summary>
public sealed record ImageInfo(string MediaType, int Width, int Height);

/// <summary>
/// Reconoce PNG y JPEG por su contenido (no por la extensión) y lee sus dimensiones de la
/// cabecera. Cualquier otro formato se rechaza con un mensaje para el operador.
/// </summary>
public static class ImageInspector
{
    public const string Png = "image/png";

    public const string Jpeg = "image/jpeg";

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static ImageInfo Inspect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            throw new DomainException("imagen.vacia", "El archivo está vacío.");
        }

        if (bytes.Length > ImageRules.MaxBytes)
        {
            throw new DomainException(
                "imagen.pesada",
                $"La imagen pesa {SizeText(bytes.Length)}; el máximo es {SizeText(ImageRules.MaxBytes)}. Redúzcala o expórtela de nuevo.");
        }

        var info = bytes.StartsWith(PngSignature) ? ReadPng(bytes)
            : bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF ? ReadJpeg(bytes)
            : throw new DomainException("imagen.formato", $"{DescribeOtherFormat(bytes)} Use una imagen {ImageRules.AcceptedFormatsText}.");

        if (info.Width < ImageRules.MinSidePixels || info.Height < ImageRules.MinSidePixels)
        {
            throw new DomainException(
                "imagen.pequena",
                $"La imagen mide {info.Width}×{info.Height} px; cada lado debe tener al menos {ImageRules.MinSidePixels} px.");
        }

        if (info.Width > ImageRules.MaxSidePixels || info.Height > ImageRules.MaxSidePixels)
        {
            throw new DomainException(
                "imagen.grande",
                $"La imagen mide {info.Width}×{info.Height} px; el máximo es {ImageRules.MaxSidePixels} px por lado.");
        }

        return info;
    }

    /// <summary>Peso legible, p. ej. «35 KB» o «1,8 MB».</summary>
    public static string SizeText(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} bytes",
        < 1024 * 1024 => $"{Math.Ceiling(bytes / 1024.0):0} KB",
        _ => (bytes / (1024.0 * 1024.0)).ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("es-PE")) + " MB",
    };

    private static ImageInfo ReadPng(ReadOnlySpan<byte> bytes)
    {
        // Firma (8) + longitud (4) + «IHDR» (4) + ancho (4) + alto (4).
        if (bytes.Length < 24 || !bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            throw Damaged("PNG");
        }

        var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(20, 4));
        return new ImageInfo(Png, Clamp(width), Clamp(height));
    }

    private static ImageInfo ReadJpeg(ReadOnlySpan<byte> bytes)
    {
        var offset = 2;
        while (offset + 4 <= bytes.Length)
        {
            if (bytes[offset] != 0xFF)
            {
                throw Damaged("JPEG");
            }

            var marker = bytes[offset + 1];
            if (marker == 0xFF)
            {
                offset++; // relleno entre segmentos
                continue;
            }

            if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7))
            {
                offset += 2; // marcadores sin longitud
                continue;
            }

            if (marker is 0xD9 or 0xDA)
            {
                break; // fin de imagen o datos comprimidos antes del tamaño
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 2, 2));
            if (length < 2 || offset + 2 + length > bytes.Length)
            {
                throw Damaged("JPEG");
            }

            // SOF0–SOF15 llevan el tamaño; C4 (Huffman), C8 y CC (aritmética) no son SOF.
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                if (length < 7)
                {
                    throw Damaged("JPEG");
                }

                var height = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 5, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 7, 2));
                return new ImageInfo(Jpeg, width, height);
            }

            offset += 2 + length;
        }

        throw Damaged("JPEG");
    }

    private static int Clamp(uint value) => value > int.MaxValue ? int.MaxValue : (int)value;

    private static DomainException Damaged(string format) =>
        new("imagen.danada", $"El archivo parece {format} pero está dañado o incompleto. Expórtelo de nuevo.");

    private static string DescribeOtherFormat(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith("GIF8"u8))
        {
            return "Las imágenes GIF no se admiten.";
        }

        if (bytes.StartsWith("BM"u8))
        {
            return "Las imágenes BMP no se admiten.";
        }

        if (bytes.Length >= 12 && bytes.StartsWith("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return "Las imágenes WebP no se admiten.";
        }

        var head = bytes[..Math.Min(bytes.Length, 256)];
        if (head.IndexOf("<svg"u8) >= 0 || head.IndexOf("<?xml"u8) >= 0)
        {
            return "Las imágenes SVG no se admiten.";
        }

        return "El archivo no es una imagen reconocible.";
    }
}

/// <summary>
/// Imagen validada lista para guardar: formato, peso y dimensiones comprobados al construirla.
/// Las imágenes no se editan: reemplazar un escudo guarda una imagen nueva.
/// </summary>
public sealed class ImageAsset
{
    public ImageAsset(Guid id, AssetKind kind, string fileName, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Id = Guard.NotEmpty(id, "imagen");
        if (!Enum.IsDefined(kind))
        {
            throw new DomainException("imagen.tipo", "Tipo de imagen desconocido.");
        }

        Kind = kind;
        FileName = Guard.Text(fileName, "imagen.sin_nombre", "Falta el nombre del archivo de la imagen.", 200);
        var info = ImageInspector.Inspect(content);
        MediaType = info.MediaType;
        Width = info.Width;
        Height = info.Height;
        Content = content;
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(content));
    }

    public Guid Id { get; }

    public AssetKind Kind { get; }

    public string FileName { get; }

    public string MediaType { get; }

    public int Width { get; }

    public int Height { get; }

    public byte[] Content { get; }

    public int ByteLength => Content.Length;

    /// <summary>Huella SHA-256 en hexadecimal; permite comprobar que el respaldo trae la misma imagen.</summary>
    public string Sha256 { get; }

    /// <summary>Descripción para el operador, p. ej. «escudo.png · 240×240 px · 35 KB».</summary>
    public string Description => $"{FileName} · {Width}×{Height} px · {ImageInspector.SizeText(ByteLength)}";
}

/// <summary>Auspiciador de un evento: nombre (texto alternativo) e imagen.</summary>
public sealed record SponsorAsset
{
    public SponsorAsset(string name, ImageAsset image)
    {
        Name = Guard.Text(name, "auspiciador.sin_nombre", "Indique el nombre de cada auspiciador.", 60);
        Image = image ?? throw new ArgumentNullException(nameof(image));
        if (image.Kind != AssetKind.Sponsor)
        {
            throw new DomainException("auspiciador.tipo", "La imagen del auspiciador debe cargarse como auspiciador.");
        }
    }

    public string Name { get; }

    public ImageAsset Image { get; }
}

/// <summary>Escudo y bandera de un equipo o selección; ambos opcionales.</summary>
public sealed record TeamImages(ImageAsset? Crest, ImageAsset? Flag)
{
    public static TeamImages None { get; } = new(null, null);
}

/// <summary>
/// Plantilla visual del marcador para un evento: título y subtítulo, colores de cabecera y
/// acento, logo del evento, logo de la organización (identidad FDNDA) y auspiciadores en orden.
/// Es presentación: no forma parte del registro de ningún partido.
/// </summary>
public sealed record EventBranding
{
    public EventBranding(
        Guid tournamentId,
        string displayTitle,
        string? subtitle,
        string? primaryColorHex,
        string? accentColorHex,
        ImageAsset? logo,
        ImageAsset? organizerLogo,
        IReadOnlyList<SponsorAsset>? sponsors)
    {
        TournamentId = Guard.NotEmpty(tournamentId, "evento");
        DisplayTitle = Guard.Text(displayTitle, "identidad.sin_titulo", "Indique el título que verá el público.", 120);
        Subtitle = string.IsNullOrWhiteSpace(subtitle)
            ? null
            : Guard.Text(subtitle, "identidad.subtitulo", "Subtítulo no válido.", 120);
        PrimaryColorHex = string.IsNullOrWhiteSpace(primaryColorHex) ? null : Guard.ColorHex(primaryColorHex);
        AccentColorHex = string.IsNullOrWhiteSpace(accentColorHex) ? null : Guard.ColorHex(accentColorHex);
        Logo = RequireKind(logo, AssetKind.EventLogo, "logo del evento");
        OrganizerLogo = RequireKind(organizerLogo, AssetKind.EventLogo, "logo de la organización");
        Sponsors = sponsors?.ToList() ?? [];
        if (Sponsors.Count > ImageRules.MaxSponsors)
        {
            throw new DomainException(
                "identidad.auspiciadores",
                $"Se admiten hasta {ImageRules.MaxSponsors} auspiciadores por evento; hay {Sponsors.Count}.");
        }
    }

    public Guid TournamentId { get; }

    public string DisplayTitle { get; }

    public string? Subtitle { get; }

    /// <summary>Fondo de la cabecera del marcador, <c>#RRGGBB</c>; nulo = predeterminado.</summary>
    public string? PrimaryColorHex { get; }

    /// <summary>Color de la etapa y los detalles, <c>#RRGGBB</c>; nulo = predeterminado.</summary>
    public string? AccentColorHex { get; }

    public ImageAsset? Logo { get; }

    public ImageAsset? OrganizerLogo { get; }

    public IReadOnlyList<SponsorAsset> Sponsors { get; }

    /// <summary>Todas las imágenes que usa la plantilla.</summary>
    public IEnumerable<ImageAsset> Images =>
        new[] { Logo, OrganizerLogo }.OfType<ImageAsset>().Concat(Sponsors.Select(s => s.Image));

    private static ImageAsset? RequireKind(ImageAsset? image, AssetKind kind, string what) =>
        image is null || image.Kind == kind
            ? image
            : throw new DomainException("identidad.tipo_imagen", $"La imagen del {what} no es del tipo esperado.");
}
