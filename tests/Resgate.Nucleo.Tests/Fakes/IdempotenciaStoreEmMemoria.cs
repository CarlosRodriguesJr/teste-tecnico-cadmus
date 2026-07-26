using Resgate.Nucleo;

namespace Resgate.Nucleo.Tests.Fakes;

public sealed class IdempotenciaStoreEmMemoria : IIdempotenciaStore
{
    private readonly Dictionary<string, RegistroIdempotencia> _registros = new();
    private readonly object _lock = new();

    public Task<RegistroIdempotencia?> ObterAsync(string idempotencyKey)
    {
        lock (_lock)
            return Task.FromResult(_registros.TryGetValue(idempotencyKey, out var r) ? r : null);
    }

    public Task GravarAsync(RegistroIdempotencia registro)
    {
        lock (_lock)
            _registros[registro.IdempotencyKey] = registro;
        return Task.CompletedTask;
    }
}
