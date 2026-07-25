namespace Resgate.Nucleo.Modelos;

public sealed record ResgateResultado(
    Guid ResgateId,
    Guid ClienteId,
    long ValorDebitado,
    long SaldoAtual,
    StatusResgate Status,
    DateTimeOffset CriadoEm
);
