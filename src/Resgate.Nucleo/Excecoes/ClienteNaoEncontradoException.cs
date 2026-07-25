namespace Resgate.Nucleo.Excecoes;

public sealed class ClienteNaoEncontradoException(Guid clienteId)
    : Exception($"Cliente {clienteId} não encontrado.")
{
    public Guid ClienteId { get; } = clienteId;
}
