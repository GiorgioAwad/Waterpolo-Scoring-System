using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Competitions;

namespace FDNDA.Waterpolo.Domain.Tests;

/// <summary>Reglas del catálogo que no dependen de almacenamiento.</summary>
public class CatalogTests
{
    [Theory]
    [InlineData("#0b63ce", "#0B63CE")]
    [InlineData("0B63CE", "#0B63CE")]
    [InlineData("  c62828 ", "#C62828")]
    public void El_color_del_equipo_se_normaliza_a_numeral_y_mayusculas(string written, string expected)
    {
        var team = new Team(Guid.NewGuid(), "AAA", "Equipo", written, "Blanco");

        Assert.Equal(expected, team.ColorHex);
    }

    [Theory]
    [InlineData("rojo")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("")]
    public void Un_color_que_no_es_RRGGBB_se_rechaza_con_un_ejemplo(string written)
    {
        var ex = Assert.Throws<DomainException>(() => new Team(Guid.NewGuid(), "AAA", "Equipo", written, "Blanco"));

        Assert.Equal("color.invalido", ex.Code);
        Assert.Contains("#0B63CE", ex.Message, StringComparison.Ordinal);
    }
}
