-- =========================================================================
-- Modelagem de pontuação: ledger (fonte da verdade) + saldo materializado.
-- Ver justificativa completa dos trade-offs em CarlosJunior_testetecnico.md (item 2).
-- =========================================================================

CREATE TABLE clientes (
    id              UUID PRIMARY KEY,
    nome            TEXT NOT NULL,
    criado_em       TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Ledger: todo acúmulo, resgate ou estorno gera uma linha imutável aqui.
-- É a fonte da verdade auditável; o saldo materializado é apenas uma projeção dela.
CREATE TABLE pontos_ledger (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    cliente_id          UUID NOT NULL REFERENCES clientes(id),
    tipo                TEXT NOT NULL CHECK (tipo IN ('ACUMULO', 'RESGATE', 'ESTORNO')),
    valor               BIGINT NOT NULL CHECK (valor > 0),
    origem              TEXT NOT NULL,            -- ex.: 'app', 'ecommerce', 'loja_fisica', 'campanha_pontos_triplo'
    referencia_externa  TEXT,                      -- id da compra/resgate no sistema de origem
    idempotency_key     TEXT,                      -- preenchido apenas em RESGATE (chave enviada pelo cliente)
    criado_em           TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Garante que o reenvio de uma requisição de resgate (retry do app mobile) nunca
-- gere um segundo lançamento no ledger, mesmo sob concorrência real no banco.
CREATE UNIQUE INDEX ux_pontos_ledger_idempotency_key
    ON pontos_ledger (idempotency_key)
    WHERE idempotency_key IS NOT NULL;

CREATE INDEX ix_pontos_ledger_cliente_id_criado_em
    ON pontos_ledger (cliente_id, criado_em DESC);

-- Saldo materializado: leitura O(1) para a consulta de saldo no app, sem
-- precisar somar o ledger inteiro a cada request. Atualizado na mesma
-- transação do INSERT no ledger.
CREATE TABLE pontos_saldo (
    cliente_id      UUID PRIMARY KEY REFERENCES clientes(id),
    saldo           BIGINT NOT NULL DEFAULT 0 CHECK (saldo >= 0),  -- defesa em profundidade: nunca negativo
    atualizado_em   TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- =========================================================================
-- Débito consistente sob concorrência (usado pelo núcleo em C# via
-- IContaPontosRepositorio.DebitarComLockAsync): a linha de pontos_saldo do
-- cliente é travada com SELECT ... FOR UPDATE dentro da transação do resgate,
-- serializando resgates concorrentes do MESMO cliente sem lockar a tabela inteira.
-- Exemplo do que a aplicação executa:
--
--   BEGIN;
--     SELECT saldo FROM pontos_saldo WHERE cliente_id = $1 FOR UPDATE;
--     -- aplicação confere saldo >= valor solicitado; se não, ROLLBACK e retorna 409
--     INSERT INTO pontos_ledger (cliente_id, tipo, valor, origem, referencia_externa, idempotency_key)
--       VALUES ($1, 'RESGATE', $2, $3, $4, $5);
--     UPDATE pontos_saldo SET saldo = saldo - $2, atualizado_em = now() WHERE cliente_id = $1;
--   COMMIT;
-- =========================================================================
