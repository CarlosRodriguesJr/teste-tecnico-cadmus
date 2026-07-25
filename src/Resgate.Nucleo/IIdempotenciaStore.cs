using Resgate.Nucleo.Modelos;

namespace Resgate.Nucleo;

public sealed record RegistroIdempotencia(string IdempotencyKey, int RequisicaoHash, ResgateResultado Resultado);

/// <summary>
/// Guarda o resultado de um resgate por Idempotency-Key, permitindo que reenvios do app mobile
/// (após timeout/falha de rede) recebam o mesmo resultado sem debitar os pontos de novo.
/// </summary>
public interface IIdempotenciaStore
{
    Task<RegistroIdempotencia?> ObterAsync(string idempotencyKey, CancellationToken cancellationToken);

    Task GravarAsync(RegistroIdempotencia registro, CancellationToken cancellationToken);
}
