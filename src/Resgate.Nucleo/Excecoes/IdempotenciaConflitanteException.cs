namespace Resgate.Nucleo.Excecoes;

public sealed class IdempotenciaConflitanteException(string idempotencyKey)
    : Exception($"A chave de idempotência '{idempotencyKey}' já foi usada com um payload diferente.")
{
    public string IdempotencyKey { get; } = idempotencyKey;
}
