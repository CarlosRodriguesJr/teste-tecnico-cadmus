namespace Resgate.Nucleo.Modelos;

public sealed record ResgateRequest(
    string IdempotencyKey,
    Guid ClienteId,
    long ValorPontos,
    string Canal,
    string? ReferenciaOrigem
);
