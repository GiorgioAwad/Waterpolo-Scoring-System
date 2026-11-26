namespace FDNDA.Waterpolo.Domain.Common;

/// <summary>
/// Violación de una regla del dominio. El mensaje está en español y puede mostrarse en la mesa.
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string code, string message) : base(message)
    {
        Code = code;
    }

    /// <summary>Código estable para pruebas y diagnóstico (p. ej. <c>jugador.ajeno_a_nomina</c>).</summary>
    public string Code { get; }
}
