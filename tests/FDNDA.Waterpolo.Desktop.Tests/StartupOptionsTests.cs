using FDNDA.Waterpolo.Application.Abstractions;
using FDNDA.Waterpolo.Desktop;

namespace FDNDA.Waterpolo.Desktop.Tests;

/// <summary>Hito B: el puerto de la consola Colorado se elige al arrancar, solo en producción.</summary>
public class StartupOptionsTests
{
    [Theory]
    [InlineData("COM5", "COM5")]
    [InlineData("com12", "COM12")]
    public void El_puerto_de_la_consola_se_elige_con_la_opcion_puerto(string value, string expected)
    {
        var options = StartupOptions.Parse(["--perfil", "produccion", "--puerto", value]);

        Assert.Equal((AppProfile.Produccion, expected), (options.Profile, options.ConsolePort));
    }

    [Fact]
    public void Sin_la_opcion_puerto_no_hay_puerto_configurado()
    {
        Assert.Null(StartupOptions.Parse(["--perfil", "produccion"]).ConsolePort);
    }

    [Fact]
    public void El_puerto_de_la_consola_no_se_usa_en_el_perfil_demo()
    {
        var error = Assert.Throws<ArgumentException>(() => StartupOptions.Parse(["--perfil", "demo", "--puerto", "COM5"]));

        Assert.Equal("El puerto de la consola solo se usa con el perfil produccion.", error.Message);
    }

    [Theory]
    [InlineData("5")]
    [InlineData("COM")]
    [InlineData("/dev/ttyS0")]
    [InlineData("COM5; del")]
    public void Un_puerto_con_otro_formato_se_rechaza(string value)
    {
        var error = Assert.Throws<ArgumentException>(() => StartupOptions.Parse(["--perfil", "produccion", "--puerto", value]));

        Assert.Contains("COM", error.Message, StringComparison.Ordinal);
    }
}
