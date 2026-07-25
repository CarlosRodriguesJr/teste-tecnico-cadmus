using Resgate.Nucleo;
using Resgate.Nucleo.Excecoes;

namespace Resgate.Nucleo.Tests.Fakes;

/// <summary>
/// Fake em memória de <see cref="IContaPontosRepositorio"/>. Usa um semáforo por cliente para
/// simular a seção crítica que, em produção, seria um "SELECT ... FOR UPDATE" dentro de uma
/// transação Postgres — suficiente para validar em teste que o saldo nunca fica negativo sob
/// resgates concorrentes.
/// </summary>
public sealed class ContaPontosRepositorioEmMemoria : IContaPontosRepositorio
{
    private readonly Dictionary<Guid, long> _saldos;
    private readonly Dictionary<Guid, SemaphoreSlim> _locks = new();
    private readonly object _locksGate = new();

    public ContaPontosRepositorioEmMemoria(IDictionary<Guid, long> saldosIniciais)
    {
        _saldos = new Dictionary<Guid, long>(saldosIniciais);
    }

    public async Task<long> DebitarComLockAsync(
        Guid clienteId,
        Guid resgateId,
        long valorPontos,
        string? referenciaOrigem,
        CancellationToken cancellationToken)
    {
        var portao = ObterLockDoCliente(clienteId);
        await portao.WaitAsync(cancellationToken);
        try
        {
            if (!_saldos.TryGetValue(clienteId, out var saldoAtual))
            {
                throw new ClienteNaoEncontradoException(clienteId);
            }

            if (saldoAtual < valorPontos)
            {
                throw new SaldoInsuficienteException(clienteId, saldoAtual, valorPontos);
            }

            var novoSaldo = saldoAtual - valorPontos;
            _saldos[clienteId] = novoSaldo;
            return novoSaldo;
        }
        finally
        {
            portao.Release();
        }
    }

    public long ObterSaldo(Guid clienteId) => _saldos[clienteId];

    private SemaphoreSlim ObterLockDoCliente(Guid clienteId)
    {
        lock (_locksGate)
        {
            if (!_locks.TryGetValue(clienteId, out var portao))
            {
                portao = new SemaphoreSlim(1, 1);
                _locks[clienteId] = portao;
            }

            return portao;
        }
    }
}
