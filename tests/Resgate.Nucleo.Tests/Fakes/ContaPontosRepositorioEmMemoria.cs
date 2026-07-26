using Resgate.Nucleo.Excecoes;

namespace Resgate.Nucleo.Tests.Fakes;

public sealed class ContaPontosRepositorioEmMemoria : IContaPontosRepositorio
{
    private readonly Dictionary<Guid, long> _saldos;
    private readonly Dictionary<Guid, SemaphoreSlim> _locks = new();
    private readonly object _lock = new();

    public ContaPontosRepositorioEmMemoria(IDictionary<Guid, long> saldosIniciais)
    {
        _saldos = new Dictionary<Guid, long>(saldosIniciais);
    }

    public async Task<long> DebitarComLockAsync(Guid clienteId, Guid resgateId, long valorPontos, string? referenciaOrigem, CancellationToken cancellationToken)
    {
        // Garante um semáforo por cliente — simula o FOR UPDATE do Postgres
        if (!_locks.TryGetValue(clienteId, out var portao))
        {
            lock (_lock)
            {
                if (!_locks.TryGetValue(clienteId, out portao))
                {
                    portao = new SemaphoreSlim(1, 1);
                    _locks[clienteId] = portao;
                }
            }
        }

        await portao.WaitAsync(cancellationToken);
        try
        {
            if (!_saldos.TryGetValue(clienteId, out var saldoAtual))
                throw new ClienteNaoEncontradoException(clienteId);

            if (saldoAtual < valorPontos)
                throw new SaldoInsuficienteException(clienteId, saldoAtual, valorPontos);

            // TODO: registrar no ledger igual faria no banco real
            _saldos[clienteId] = saldoAtual - valorPontos;
            return _saldos[clienteId];
        }
        finally
        {
            portao.Release();
        }
    }

    public long ObterSaldo(Guid clienteId) => _saldos[clienteId];
}
