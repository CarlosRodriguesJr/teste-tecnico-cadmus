namespace Resgate.Nucleo.Excecoes;

public sealed class ClienteNaoEncontradoException : Exception
{
    public Guid ClienteId { get; }

    public ClienteNaoEncontradoException(Guid clienteId)
        : base($"Cliente {clienteId} não encontrado.")
    {
        ClienteId = clienteId;
    }
}
