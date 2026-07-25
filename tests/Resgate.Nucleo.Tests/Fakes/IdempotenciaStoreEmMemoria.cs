using System.Collections.Concurrent;
using Resgate.Nucleo;

namespace Resgate.Nucleo.Tests.Fakes;

public sealed class IdempotenciaStoreEmMemoria : IIdempotenciaStore
{
    private readonly ConcurrentDictionary<string, RegistroIdempotencia> _registros = new();

    public Task<RegistroIdempotencia?> ObterAsync(string idempotencyKey, CancellationToken cancellationToken) =>
        Task.FromResult(_registros.TryGetValue(idempotencyKey, out var registro) ? registro : null);

    public Task GravarAsync(RegistroIdempotencia registro, CancellationToken cancellationToken)
    {
        _registros[registro.IdempotencyKey] = registro;
        return Task.CompletedTask;
    }
}
