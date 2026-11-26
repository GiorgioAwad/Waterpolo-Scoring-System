using System.Text;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Competitions;
using FDNDA.Waterpolo.Infrastructure.Tests.Support;

namespace FDNDA.Waterpolo.Domain.Tests;

/// <summary>R7 — validación de imágenes por contenido y plantilla del evento.</summary>
public class IdentityTests
{
    [Fact]
    public void Un_PNG_se_reconoce_por_su_contenido_con_sus_dimensiones()
    {
        var image = new ImageAsset(Guid.NewGuid(), AssetKind.Crest, "escudo.png", TestImages.Png(240, 300));

        Assert.Equal("image/png", image.MediaType);
        Assert.Equal((240, 300), (image.Width, image.Height));
        Assert.Equal(64, image.Sha256.Length);
        Assert.Contains("240×300 px", image.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Un_JPEG_se_reconoce_aunque_el_archivo_tenga_otra_extension()
    {
        var image = new ImageAsset(Guid.NewGuid(), AssetKind.Flag, "bandera.png", TestImages.Jpeg(360, 240));

        Assert.Equal("image/jpeg", image.MediaType);
        Assert.Equal((360, 240), (image.Width, image.Height));
    }

    [Theory]
    [InlineData("GIF89a......", "GIF")]
    [InlineData("BM..........", "BMP")]
    [InlineData("RIFF....WEBPVP8 ", "WebP")]
    [InlineData("<?xml version=\"1.0\"?><svg></svg>", "SVG")]
    [InlineData("Hola, esto es texto", "no es una imagen")]
    public void Otros_formatos_se_rechazan_con_un_mensaje_claro(string content, string expected)
    {
        var ex = Assert.Throws<DomainException>(() =>
            new ImageAsset(Guid.NewGuid(), AssetKind.Sponsor, "archivo.png", Encoding.ASCII.GetBytes(content)));

        Assert.Equal("imagen.formato", ex.Code);
        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
        Assert.Contains("PNG o JPEG", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Una_imagen_de_mas_de_2_MB_se_rechaza()
    {
        var ex = Assert.Throws<DomainException>(() =>
            new ImageAsset(Guid.NewGuid(), AssetKind.EventLogo, "logo.png", TestImages.Png(800, 400, padding: ImageRules.MaxBytes)));

        Assert.Equal("imagen.pesada", ex.Code);
        Assert.Contains("el máximo es 2.0 MB", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(15, 200, "imagen.pequena")]
    [InlineData(200, 15, "imagen.pequena")]
    [InlineData(4097, 300, "imagen.grande")]
    [InlineData(300, 5000, "imagen.grande")]
    public void Las_dimensiones_fuera_de_limite_se_rechazan(int width, int height, string code)
    {
        var ex = Assert.Throws<DomainException>(() =>
            new ImageAsset(Guid.NewGuid(), AssetKind.Crest, "escudo.png", TestImages.Png(width, height)));

        Assert.Equal(code, ex.Code);
        Assert.Contains($"{width}×{height}", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Un_PNG_o_JPEG_truncado_se_informa_como_danado()
    {
        var png = TestImages.Png(100, 100)[..20];
        var jpeg = TestImages.Jpeg(100, 100)[..12];

        Assert.Equal("imagen.danada", Assert.Throws<DomainException>(() => ImageInspector.Inspect(png)).Code);
        Assert.Equal("imagen.danada", Assert.Throws<DomainException>(() => ImageInspector.Inspect(jpeg)).Code);
        Assert.Equal("imagen.vacia", Assert.Throws<DomainException>(() => ImageInspector.Inspect([])).Code);
    }

    [Fact]
    public void La_plantilla_exige_titulo_valida_colores_y_limita_auspiciadores()
    {
        var tournamentId = Guid.NewGuid();
        var sponsors = Enumerable.Range(1, ImageRules.MaxSponsors + 1)
            .Select(i => new SponsorAsset($"Auspiciador {i}", Sponsor(i)))
            .ToList();

        Assert.Equal("identidad.sin_titulo", Assert.Throws<DomainException>(() =>
            new EventBranding(tournamentId, " ", null, null, null, null, null, null)).Code);
        Assert.Equal("color.invalido", Assert.Throws<DomainException>(() =>
            new EventBranding(tournamentId, "Copa", null, "azul", null, null, null, null)).Code);
        Assert.Equal("identidad.auspiciadores", Assert.Throws<DomainException>(() =>
            new EventBranding(tournamentId, "Copa", null, null, null, null, null, sponsors)).Code);

        var branding = new EventBranding(tournamentId, " Copa ", " ", "0b2a4a", "#ffc83d", null, null, sponsors[..3]);
        Assert.Equal("Copa", branding.DisplayTitle);
        Assert.Null(branding.Subtitle);
        Assert.Equal(("#0B2A4A", "#FFC83D"), (branding.PrimaryColorHex, branding.AccentColorHex));
        Assert.Equal(3, branding.Images.Count());
    }

    [Fact]
    public void Cada_imagen_de_la_plantilla_debe_ser_del_tipo_que_ocupa()
    {
        var crest = new ImageAsset(Guid.NewGuid(), AssetKind.Crest, "escudo.png", TestImages.Png(100, 100));

        Assert.Equal("identidad.tipo_imagen", Assert.Throws<DomainException>(() =>
            new EventBranding(Guid.NewGuid(), "Copa", null, null, null, crest, null, null)).Code);
        Assert.Equal("auspiciador.tipo", Assert.Throws<DomainException>(() => new SponsorAsset("Marca", crest)).Code);
        Assert.Equal("auspiciador.sin_nombre", Assert.Throws<DomainException>(() => new SponsorAsset(" ", Sponsor(1))).Code);
    }

    private static ImageAsset Sponsor(int n) =>
        new(Guid.NewGuid(), AssetKind.Sponsor, $"a{n}.png", TestImages.Png(300, 120, fill: (byte)n));
}
