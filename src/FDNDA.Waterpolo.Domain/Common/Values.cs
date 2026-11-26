namespace FDNDA.Waterpolo.Domain.Common;

/// <summary>Lado del equipo dentro de un partido.</summary>
public enum TeamSide
{
    Home = 1,
    Away = 2,
}

/// <summary>
/// Número de gorro. El rango 1–99 es una decisión de diseño; el rango reglamentario
/// se confirma con el área técnica (ver docs/requisitos.md, pendientes).
/// </summary>
public readonly record struct CapNumber
{
    public const int Min = 1;
    public const int Max = 99;

    public CapNumber(int value)
    {
        if (value is < Min or > Max)
        {
            throw new DomainException("gorro.fuera_de_rango", $"El número de gorro debe estar entre {Min} y {Max}.");
        }

        Value = value;
    }

    public int Value { get; }

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Número de periodo (1 en adelante).</summary>
public readonly record struct PeriodNumber
{
    public const int Max = 20;

    public PeriodNumber(int value)
    {
        if (value is < 1 or > Max)
        {
            throw new DomainException("periodo.fuera_de_rango", $"El periodo debe estar entre 1 y {Max}.");
        }

        Value = value;
    }

    public int Value { get; }

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Operador identificado localmente. No equivale a autenticación fuerte.</summary>
public sealed record OperatorRef
{
    public OperatorRef(Guid id, string name)
    {
        if (id == Guid.Empty)
        {
            throw new DomainException("operador.sin_id", "El operador no tiene identificador.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("operador.sin_nombre", "Indique el nombre del operador.");
        }

        Id = id;
        Name = name.Trim();
    }

    public Guid Id { get; }

    public string Name { get; }
}
