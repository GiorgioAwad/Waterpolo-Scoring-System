using FDNDA.Waterpolo.Domain.Common;

namespace FDNDA.Waterpolo.Domain.Competitions;

// Catálogo de competiciones y plantillas. No guarda DNI, fecha de nacimiento, correo
// ni otros datos personales que el marcador no necesita.

/// <summary>Evento o torneo con sus reglas configurables.</summary>
public sealed record Tournament
{
    public Tournament(Guid id, string name, bool isDemo, RulesetSnapshot rules)
    {
        Id = Guard.NotEmpty(id, "evento");
        Name = Guard.Text(name, "evento.sin_nombre", "El evento debe tener nombre.", 120);
        IsDemo = isDemo;
        Rules = rules ?? throw new ArgumentNullException(nameof(rules));
    }

    public Guid Id { get; }

    public string Name { get; }

    /// <summary>Marca los datos de demostración; viven en una base separada de producción.</summary>
    public bool IsDemo { get; }

    public RulesetSnapshot Rules { get; }
}

/// <summary>Equipo o selección.</summary>
public sealed record Team
{
    public Team(Guid id, string code, string name, string colorHex, string capColor)
    {
        Id = Guard.NotEmpty(id, "equipo");
        Code = Guard.Text(code, "equipo.sin_codigo", "El equipo debe tener código.", 10).ToUpperInvariant();
        Name = Guard.Text(name, "equipo.sin_nombre", "El equipo debe tener nombre.", 80);
        ColorHex = Guard.ColorHex(colorHex);
        CapColor = Guard.Text(capColor, "equipo.sin_gorro", "Indique el color de gorro del equipo.", 30);
    }

    public Guid Id { get; }

    public string Code { get; }

    public string Name { get; }

    /// <summary>Color de equipo para la interfaz, formato <c>#RRGGBB</c>.</summary>
    public string ColorHex { get; }

    /// <summary>Color de gorro (texto libre, p. ej. «Blanco»).</summary>
    public string CapColor { get; }
}

/// <summary>Jugador de un equipo. Solo nombre: su identidad nunca se deduce por el nombre.</summary>
public sealed record Player
{
    public Player(Guid id, Guid teamId, string fullName)
    {
        Id = Guard.NotEmpty(id, "jugador");
        TeamId = Guard.NotEmpty(teamId, "equipo");
        FullName = Guard.Text(fullName, "jugador.sin_nombre", "El jugador debe tener nombre.", 80);
    }

    public Guid Id { get; }

    public Guid TeamId { get; }

    public string FullName { get; }
}

/// <summary>Inscripción de un jugador en la nómina de un evento con su número de gorro.</summary>
public sealed record TournamentRosterEntry
{
    public TournamentRosterEntry(Guid id, Guid tournamentId, Guid teamId, Guid playerId, CapNumber cap)
    {
        Id = Guard.NotEmpty(id, "nómina");
        TournamentId = Guard.NotEmpty(tournamentId, "evento");
        TeamId = Guard.NotEmpty(teamId, "equipo");
        PlayerId = Guard.NotEmpty(playerId, "jugador");
        Cap = cap;
    }

    public Guid Id { get; }

    public Guid TournamentId { get; }

    public Guid TeamId { get; }

    public Guid PlayerId { get; }

    public CapNumber Cap { get; }
}

/// <summary>Perfil local de operador de mesa.</summary>
public sealed record OperatorProfile
{
    public OperatorProfile(Guid id, string displayName)
    {
        Id = Guard.NotEmpty(id, "operador");
        DisplayName = Guard.Text(displayName, "operador.sin_nombre", "Indique el nombre del operador.", 60);
    }

    public Guid Id { get; }

    public string DisplayName { get; }

    public OperatorRef ToRef() => new(Id, DisplayName);
}

internal static class Guard
{
    public static Guid NotEmpty(Guid id, string what)
    {
        if (id == Guid.Empty)
        {
            throw new DomainException("id.vacio", $"Falta el identificador de {what}.");
        }

        return id;
    }

    public static string Text(string? value, string code, string message, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException(code, message);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new DomainException("texto.demasiado_largo", $"El texto supera los {maxLength} caracteres permitidos.");
        }

        return trimmed;
    }

    /// <summary>Color <c>#RRGGBB</c>. Se acepta escrito sin «#» (p. ej. <c>0B63CE</c>).</summary>
    public static string ColorHex(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length == 6 && text[0] != '#')
        {
            text = "#" + text;
        }

        var valid = text.Length == 7 && text[0] == '#' && text.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;
        if (!valid)
        {
            throw new DomainException("color.invalido", "Elija un color de la paleta o escríbalo como #RRGGBB (por ejemplo #0B63CE).");
        }

        return text.ToUpperInvariant();
    }
}
