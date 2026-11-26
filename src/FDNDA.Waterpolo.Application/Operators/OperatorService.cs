using FDNDA.Waterpolo.Application.Abstractions;
using FDNDA.Waterpolo.Domain.Competitions;

namespace FDNDA.Waterpolo.Application.Operators;

/// <summary>
/// Identificación local del operador de mesa. Sirve para atribuir acciones; no equivale
/// a autenticación fuerte.
/// </summary>
public sealed class OperatorService(IOperatorRepository operators, TimeProvider time)
{
    public Task<IReadOnlyList<OperatorProfile>> ListAsync(CancellationToken ct = default) => operators.ListAsync(ct);

    /// <summary>Devuelve el perfil con ese nombre o lo crea si no existe.</summary>
    public async Task<OperatorProfile> SignInAsync(string displayName, CancellationToken ct = default)
    {
        var candidate = new OperatorProfile(Guid.NewGuid(), displayName);
        var existing = await operators.FindByNameAsync(candidate.DisplayName, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        await operators.AddAsync(candidate, time.GetUtcNow(), ct).ConfigureAwait(false);
        return candidate;
    }
}
