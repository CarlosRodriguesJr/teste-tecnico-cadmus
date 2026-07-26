CREATE TABLE clientes (
    id              UUID PRIMARY KEY,
    nome            TEXT NOT NULL,
    criado_em       TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE pontos_ledger (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    cliente_id          UUID NOT NULL REFERENCES clientes(id),
    tipo                TEXT NOT NULL CHECK (tipo IN ('ACUMULO', 'RESGATE', 'ESTORNO')),
    valor               BIGINT NOT NULL CHECK (valor > 0),
    origem              TEXT NOT NULL,
    referencia_externa  TEXT,
    idempotency_key     TEXT,
    criado_em           TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX ux_pontos_ledger_idempotency_key
    ON pontos_ledger (idempotency_key)
    WHERE idempotency_key IS NOT NULL;

CREATE INDEX ix_pontos_ledger_cliente_id_criado_em
    ON pontos_ledger (cliente_id, criado_em DESC);

CREATE TABLE pontos_saldo (
    cliente_id      UUID PRIMARY KEY REFERENCES clientes(id),
    saldo           BIGINT NOT NULL DEFAULT 0 CHECK (saldo >= 0),
    atualizado_em   TIMESTAMPTZ NOT NULL DEFAULT now()
);
