using Resgate.Nucleo.Excecoes;

namespace Resgate.Nucleo;

/// <summary>
/// Abstrai a leitura do saldo materializado e a gravação do lançamento de resgate no ledger.
/// Uma implementação real (Postgres) deve executar <see cref="DebitarComLockAsync"/> dentro de uma
/// única transação com "SELECT ... FOR UPDATE" na linha de saldo do cliente, garantindo que a
/// verificação de saldo e o débito sejam atômicos mesmo sob resgates concorrentes.
/// </summary>
public interface IContaPontosRepositorio
{
    /// <summary>
    /// Trava a linha de saldo do cliente, confere se há saldo suficiente e, em caso positivo,
    /// debita o valor e registra o lançamento no ledger — tudo na mesma seção crítica.
    /// </summary>
    /// <exception cref="ClienteNaoEncontradoException">Cliente inexistente.</exception>
    /// <exception cref="SaldoInsuficienteException">Saldo do cliente é menor que o valor solicitado.</exception>
    /// <returns>O saldo do cliente após o débito.</returns>
    Task<long> DebitarComLockAsync(
        Guid clienteId,
        Guid resgateId,
        long valorPontos,
        string? referenciaOrigem,
        CancellationToken cancellationToken);
}
