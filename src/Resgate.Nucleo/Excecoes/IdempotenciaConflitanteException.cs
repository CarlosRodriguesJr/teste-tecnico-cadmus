namespace Resgate.Nucleo.Excecoes;

/// <summary>
/// A mesma Idempotency-Key foi reenviada com um payload diferente do original.
/// Isso indica reuso indevido da chave, não um reenvio legítimo por falha de rede.
/// </summary>
public sealed class IdempotenciaConflitanteException(string idempotencyKey)
    : Exception($"A chave de idempotência '{idempotencyKey}' já foi usada com um payload diferente.")
{
    public string IdempotencyKey { get; } = idempotencyKey;
}
