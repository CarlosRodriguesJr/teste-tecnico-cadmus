namespace Resgate.Nucleo.Excecoes;

public sealed class SaldoInsuficienteException(Guid clienteId, long saldoAtual, long valorSolicitado)
    : Exception($"Cliente {clienteId} possui saldo insuficiente: saldo={saldoAtual}, solicitado={valorSolicitado}.")
{
    public Guid ClienteId { get; } = clienteId;
    public long SaldoAtual { get; } = saldoAtual;
    public long ValorSolicitado { get; } = valorSolicitado;
}
