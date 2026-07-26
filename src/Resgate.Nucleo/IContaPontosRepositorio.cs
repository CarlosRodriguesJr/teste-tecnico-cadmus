namespace Resgate.Nucleo;

/// <summary>
/// Implementação real (Postgres) deve executar SELECT...FOR UPDATE na linha de saldo
/// dentro de uma única transação, garantindo atomicidade mesmo sob resgates concorrentes.
/// </summary>
public interface IContaPontosRepositorio
{
    /// <returns>Saldo após o débito.</returns>
    Task<long> DebitarComLockAsync(
        Guid clienteId,
        Guid resgateId,
        long valorPontos,
        string? referenciaOrigem,
        CancellationToken cancellationToken);
}
