using Resgate.Nucleo;
using Resgate.Nucleo.Excecoes;
using Resgate.Nucleo.Modelos;
using Resgate.Nucleo.Tests.Fakes;
using Xunit;

namespace Resgate.Nucleo.Tests;

public class ResgateServiceTests
{
    [Fact]
    public async Task Resgate_ComSaldoSuficiente_DebitaEDevolveNovoSaldo()
    {
        var clienteId = Guid.NewGuid();
        var repositorio = new ContaPontosRepositorioEmMemoria(new Dictionary<Guid, long> { [clienteId] = 100 });
        var service = new ResgateService(repositorio, new IdempotenciaStoreEmMemoria());

        var resultado = await service.ResgatarAsync(new ResgateRequest(Guid.NewGuid().ToString(), clienteId, 30, "app", null));

        Assert.Equal(30, resultado.ValorDebitado);
        Assert.Equal(70, resultado.SaldoAtual);
        Assert.Equal(70, repositorio.ObterSaldo(clienteId));
    }

    [Fact]
    public async Task Resgate_ComSaldoInsuficiente_LancaExcecaoENaoAlteraSaldo()
    {
        var clienteId = Guid.NewGuid();
        var repositorio = new ContaPontosRepositorioEmMemoria(new Dictionary<Guid, long> { [clienteId] = 10 });
        var service = new ResgateService(repositorio, new IdempotenciaStoreEmMemoria());

        await Assert.ThrowsAsync<SaldoInsuficienteException>(() =>
            service.ResgatarAsync(new ResgateRequest(Guid.NewGuid().ToString(), clienteId, 50, "app", null)));

        Assert.Equal(10, repositorio.ObterSaldo(clienteId));
    }

    [Fact]
    public async Task Resgate_ReenvioComMesmaChaveEMesmoPayload_NaoDebitaDeNovo()
    {
        var clienteId = Guid.NewGuid();
        var repositorio = new ContaPontosRepositorioEmMemoria(new Dictionary<Guid, long> { [clienteId] = 100 });
        var service = new ResgateService(repositorio, new IdempotenciaStoreEmMemoria());
        var request = new ResgateRequest(Guid.NewGuid().ToString(), clienteId, 30, "app", "pedido-1");

        var primeiro = await service.ResgatarAsync(request);
        var segundo = await service.ResgatarAsync(request);

        Assert.Equal(primeiro.ResgateId, segundo.ResgateId);
        Assert.Equal(primeiro.SaldoAtual, segundo.SaldoAtual);
        Assert.Equal(70, repositorio.ObterSaldo(clienteId));
    }

    [Fact]
    public async Task Resgate_ReenvioComMesmaChaveEPayloadDiferente_LancaConflito()
    {
        var clienteId = Guid.NewGuid();
        var repositorio = new ContaPontosRepositorioEmMemoria(new Dictionary<Guid, long> { [clienteId] = 100 });
        var service = new ResgateService(repositorio, new IdempotenciaStoreEmMemoria());
        var chave = Guid.NewGuid().ToString();

        await service.ResgatarAsync(new ResgateRequest(chave, clienteId, 30, "app", "pedido-1"));

        await Assert.ThrowsAsync<IdempotenciaConflitanteException>(() =>
            service.ResgatarAsync(new ResgateRequest(chave, clienteId, 40, "app", "pedido-2")));
    }

    [Fact]
    public async Task Resgate_DuasRequisicoesConcorrentesMesmoCliente_SaldoNuncaFicaNegativo()
    {
        var clienteId = Guid.NewGuid();
        var repositorio = new ContaPontosRepositorioEmMemoria(new Dictionary<Guid, long> { [clienteId] = 100 });
        var service = new ResgateService(repositorio, new IdempotenciaStoreEmMemoria());

        async Task<Exception?> Tentar(long valor)
        {
            try
            {
                await service.ResgatarAsync(new ResgateRequest(Guid.NewGuid().ToString(), clienteId, valor, "app", null));
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        var resultados = await Task.WhenAll(Tentar(70), Tentar(70));

        Assert.Single(resultados, r => r is null);
        Assert.Single(resultados, r => r is SaldoInsuficienteException);
        Assert.Equal(30, repositorio.ObterSaldo(clienteId));
        Assert.True(repositorio.ObterSaldo(clienteId) >= 0);
    }
}
