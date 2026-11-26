using FDNDA.Waterpolo.Application.Abstractions;
using FDNDA.Waterpolo.Infrastructure.Storage;

namespace FDNDA.Waterpolo.Infrastructure.Tests;

/// <summary>Elección de la carpeta de datos: opción de arranque, variable de entorno o perfil del usuario.</summary>
public class DataLocationTests
{
    [Fact]
    public void La_variable_de_entorno_lleva_los_datos_a_otro_disco_separando_demo_y_produccion()
    {
        static string? Variable(string name) => name == AppPaths.DataRootVariable ? @" ""G:\FDNDA\Waterpolo"" " : null;

        var demo = AppPaths.ForProfile(AppProfile.Demo, null, Variable);
        var production = AppPaths.ForProfile(AppProfile.Produccion, null, Variable);

        Assert.Equal(@"G:\FDNDA\Waterpolo\demo", demo.DataDirectory);
        Assert.Equal(@"G:\FDNDA\Waterpolo\produccion\waterpolo.db", production.DatabasePath);
        Assert.Equal(DataRootOrigin.EnvironmentVariable, demo.Origin);
        Assert.Equal("variable de entorno FDNDA_WATERPOLO_DATOS", demo.OriginText);
    }

    [Fact]
    public void La_opcion_datos_tiene_prioridad_sobre_la_variable()
    {
        var paths = AppPaths.ForProfile(AppProfile.Demo, @"D:\pruebas", _ => @"G:\FDNDA\Waterpolo");

        Assert.Equal(@"D:\pruebas\demo", paths.DataDirectory);
        Assert.Equal(DataRootOrigin.StartupOption, paths.Origin);
    }

    [Fact]
    public void Sin_opcion_ni_variable_se_usa_el_perfil_del_usuario()
    {
        var paths = AppPaths.ForProfile(AppProfile.Demo, null, _ => "  ");

        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FDNDA", "Waterpolo", "demo");
        Assert.Equal(expected, paths.DataDirectory);
        Assert.Equal(DataRootOrigin.UserProfile, paths.Origin);
    }
}
