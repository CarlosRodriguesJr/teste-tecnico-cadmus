using Resgate.Nucleo.Modelos;

namespace Resgate.Nucleo;

public sealed record RegistroIdempotencia(string IdempotencyKey, int RequisicaoHash, ResgateResultado Resultado);

public interface IIdempotenciaStore
{
    Task<RegistroIdempotencia?> ObterAsync(string idempotencyKey);
    Task GravarAsync(RegistroIdempotencia registro);
}
