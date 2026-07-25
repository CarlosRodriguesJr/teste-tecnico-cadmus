using Resgate.Nucleo.Excecoes;
using Resgate.Nucleo.Modelos;

namespace Resgate.Nucleo;

/// <summary>
/// Núcleo do caso de uso "resgatar pontos": valida a entrada, garante idempotência por
/// reenvio (retry do app mobile) e delega o débito consistente ao repositório, que é
/// responsável por travar a linha de saldo do cliente durante a operação.
/// </summary>
public sealed class ResgateService(IContaPontosRepositorio repositorio, IIdempotenciaStore idempotencia)
{
    public async Task<ResgateResultado> ResgatarAsync(ResgateRequest request, CancellationToken cancellationToken = default)
    {
        ValidarRequisicao(request);

        var payloadHash = CalcularHashPayload(request);
        var registroExistente = await idempotencia.ObterAsync(request.IdempotencyKey, cancellationToken);
        if (registroExistente is not null)
        {
            if (registroExistente.RequisicaoHash != payloadHash)
            {
                throw new IdempotenciaConflitanteException(request.IdempotencyKey);
            }

            return registroExistente.Resultado;
        }

        var resgateId = Guid.NewGuid();
        var saldoAtual = await repositorio.DebitarComLockAsync(
            request.ClienteId,
            resgateId,
            request.ValorPontos,
            request.ReferenciaOrigem,
            cancellationToken);

        var resultado = new ResgateResultado(
            resgateId,
            request.ClienteId,
            request.ValorPontos,
            saldoAtual,
            StatusResgate.Confirmado,
            DateTimeOffset.UtcNow);

        await idempotencia.GravarAsync(new RegistroIdempotencia(request.IdempotencyKey, payloadHash, resultado), cancellationToken);

        return resultado;
    }

    private static void ValidarRequisicao(ResgateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new ArgumentException("Idempotency-Key é obrigatória.", nameof(request));
        }

        if (request.ClienteId == Guid.Empty)
        {
            throw new ArgumentException("ClienteId é obrigatório.", nameof(request));
        }

        if (request.ValorPontos <= 0)
        {
            throw new ArgumentException("ValorPontos deve ser maior que zero.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Canal))
        {
            throw new ArgumentException("Canal é obrigatório.", nameof(request));
        }
    }

    private static int CalcularHashPayload(ResgateRequest request) =>
        HashCode.Combine(
            request.ClienteId,
            request.ValorPontos,
            StringComparer.Ordinal.GetHashCode(request.Canal),
            request.ReferenciaOrigem is null ? 0 : StringComparer.Ordinal.GetHashCode(request.ReferenciaOrigem));
}
