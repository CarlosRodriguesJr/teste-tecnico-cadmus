# Teste Técnico — Desenvolvedor(a) Full Stack (.NET + Angular/React)

**Candidato:** Carlos Junior
**Cliente:** Grupo CRM
**Domínio:** Plataforma de Fidelidade — Varejo

Código de apoio deste teste (núcleo da API do item 3, testes e DDL do item 2) está neste mesmo repositório:
- [`sql/ddl_pontos.sql`](sql/ddl_pontos.sql)
- [`src/Resgate.Nucleo/`](src/Resgate.Nucleo/)
- [`tests/Resgate.Nucleo.Tests/`](tests/Resgate.Nucleo.Tests/)

Para rodar os testes: `dotnet test` na raiz do repositório (5 testes, todos passando).

---

## 1. Investigação do incidente

### Os três sintomas e suas hipóteses

**a) Latência na consulta de saldo**
- Saldo sendo calculado on-the-fly somando todo o histórico de lançamentos a cada leitura, e o volume 3x da campanha tornou isso caro.
- Falta de índice adequado para a consulta (ex.: por `cliente_id`), forçando *sequential scan*.
- Contenção de lock: leituras de saldo concorrendo com transações de escrita (resgates) que seguram lock na mesma linha/tabela por mais tempo do que deveriam.
- Pool de conexões esgotado pelo aumento de tráfego, fazendo requisições esperarem por uma conexão livre antes mesmo de executar a query.

**b) Resgates deixando saldo negativo**
- Condição de corrida clássica: dois resgates concorrentes do mesmo cliente leem o mesmo saldo antes de qualquer débito ser aplicado (padrão *read-then-write* sem transação/lock cobrindo as duas etapas), e ambos debitam com base num saldo que já estava desatualizado.
- Ausência de uma constraint de banco que impeça saldo negativo — nada barra o problema mesmo que a aplicação falhe.

**c) Pontos atrasados no extrato**
- Fila/job assíncrono de processamento de acúmulo saturado pelo volume 3x da campanha (backlog crescendo mais rápido do que é consumido).
- Job rodando em janela fixa (ex.: a cada N minutos) que não escala com o pico de tráfego.
- Contenção na mesma tabela/linha de saldo: se o job de acúmulo compete pelo mesmo lock que os resgates estão segurando, ele fica bloqueado atrás deles.

### Causa comum possível

Na minha visão, os três sintomas têm a mesma origem: a tabela de saldo virou um gargalo com o triplo de carga, e não tinha lock direito. Se o saldo é lido e escrito sem uma transação atômica bem feita:
- resgates concorrentes do mesmo cliente colidem → saldo negativo;
- leituras de saldo competem com escritas mais longas → latência;
- o job de acúmulo disputa a mesma linha → atraso no extrato.

Não descarto que possam ser causas independentes, mas a chance de três problemas não relacionados aparecerem juntos no mesmo fim de semana é baixa. Então eu começaria investigando essa hipótese única.

### O que eu olharia primeiro, e por quê

1. **Métricas** (dashboards de latência p50/p95/p99 da API de saldo/resgate, CPU/IO do Postgres, conexões ativas, lock waits) — visão macro, não invasiva, mostra rapidamente se o início do problema coincide com o início da campanha e onde está o gargalo (app, banco, fila).
2. **Logs da aplicação** — buscar timeouts, exceções e padrões (ex.: mesmo `cliente_id` aparecendo duas vezes em janelas de milissegundos para resgate, o que já seria evidência direta da race condition).
3. **`pg_stat_activity` / `pg_locks` no Postgres em tempo real** — confirma se há sessões esperando lock (`idle in transaction`, locks retidos por muito tempo), o que sustentaria a hipótese do hot spot.
4. **`EXPLAIN (ANALYZE, BUFFERS)`** na query de consulta de saldo — separa "é falta de índice/scan sequencial" de "é espera de lock".
5. **Estado das filas/jobs de acúmulo** (tamanho da fila, taxa de consumo, erros, dead-letter) — confirma ou descarta a hipótese de backlog para o sintoma (c).
6. **Traces distribuídos** (se houver instrumentação), para correlacionar uma requisição de resgate lenta com onde o tempo é gasto (aplicação, banco ou fila).

Essa ordem prioriza sinais baratos e não invasivos (métricas/logs) antes de mergulhar em diagnóstico de banco, e só então eu vou para os traces, que geralmente exigem mais tempo para correlacionar.

### Como confirmar a causa raiz

Reproduzir em staging com carga equivalente (ou replay de tráfego real da campanha), disparando resgates concorrentes do mesmo cliente e observando se o saldo fica negativo e se a latência de leitura sobe do mesmo jeito. Cruzar timestamps dos casos reais de saldo negativo em produção com os locks/latência observados no mesmo intervalo — se coincidirem, a causa está confirmada.

### Decisão sob pressão (campanha continua no ar)

**Mitigar agora (reversível, rápido, sem redesenho):**
- Adicionar uma constraint de banco (`CHECK (saldo >= 0)`) imediatamente — não resolve a causa raiz, mas impede que o problema continue gerando prejuízo financeiro enquanto a correção definitiva não sai.
- Garantir que o caminho de resgate leia e escreva o saldo dentro de uma única transação com lock de linha (mesmo que não seja o design final polido) — estanca novos saldos negativos.
- Para a latência: cache de leitura de saldo de poucos segundos (aceitando pequena defasagem), tirando pressão da tabela quente.
- Para o atraso no extrato: aumentar temporariamente o número de consumidores da fila (scale-out horizontal) para dar vazão ao backlog, sem mudar lógica de negócio.

**Deixar para a correção definitiva:**
- Redesenho do modelo de dados conforme item 2 (ledger + saldo materializado com lock bem escopado).
- Revisão de índices e do plano de execução da query de saldo.
- Dimensionamento adequado (autoscaling) da fila de processamento e observabilidade estruturada para detectar isso antes do próximo pico.
- Testes de carga simulando o volume de uma campanha antes da próxima ativação promocional.

**Trade-off:** as medidas de curto prazo seguram o prejuízo agora, mas podem deixar o resgate um pouco mais lento ou custar mais caro em infra. É aceitável — o importante é estancar o sangramento, não fazer bonito. Redesenhar o modelo de dados em produção no meio de uma campanha ativa é loucura; isso fica pra depois, testado em staging.

---

## 2. Modelagem e consistência do saldo de pontos

DDL completo (comentado) em [`sql/ddl_pontos.sql`](sql/ddl_pontos.sql). Resumo das tabelas centrais:

- **`pontos_ledger`** — todo acúmulo, resgate ou estorno gera uma linha imutável. É a fonte da verdade auditável.
- **`pontos_saldo`** — saldo materializado por cliente, atualizado na mesma transação do `INSERT` no ledger. Existe para que a consulta de saldo seja O(1) em vez de somar o ledger inteiro a cada leitura.

### Como garantir que dois resgates concorrentes nunca deixem o saldo negativo

A operação de resgate roda assim, dentro de uma única transação:

```sql
BEGIN;
  SELECT saldo FROM pontos_saldo WHERE cliente_id = $1 FOR UPDATE;
  -- aplicação confere saldo >= valor solicitado; se não, ROLLBACK e retorna 409
  INSERT INTO pontos_ledger (cliente_id, tipo, valor, origem, referencia_externa, idempotency_key)
    VALUES ($1, 'RESGATE', $2, $3, $4, $5);
  UPDATE pontos_saldo SET saldo = saldo - $2, atualizado_em = now() WHERE cliente_id = $1;
COMMIT;
```

O `SELECT ... FOR UPDATE` trava apenas a linha do cliente em questão — um segundo resgate do **mesmo** cliente espera a transação anterior terminar (serializado); resgates de clientes **diferentes** não se afetam. Como defesa em profundidade, `pontos_saldo.saldo` tem `CHECK (saldo >= 0)`: mesmo que um bug de aplicação burle o lock, o banco recusa a escrita.

### Trade-offs considerados

**Ledger vs. saldo materializado puro**
Saldo materializado puro é mais simples e rápido, mas você nunca sabe de onde veio cada ponto. Numa plataforma de fidelidade isso é um problemão — cliente contesta, auditoria quer ver o histórico, e sem ledger você não consegue provar nada. Já ledger puro (saldo calculado por `SUM`) resolve a auditoria, mas é caro de ler em escala. A combinação dos dois é o melhor dos mundos: o ledger é a fonte da verdade, o saldo materializado é uma projeção atualizada na mesma transação — leitura rápida sem perder a auditoria.

**Lock otimista vs. pessimista**
Lock otimista funciona bem quando contenção é baixa, mas aqui não é o caso — dois resgates do mesmo cliente batendo ao mesmo tempo geram uma enxurrada de retries que piora tudo (thundering herd), fora que você precisa implementar retry na aplicação. Lock pessimista (`FOR UPDATE`) é mais previsível: a segunda transação simplesmente espera a primeira terminar, sem retry. E como o lock é por linha (por cliente) e a transação é curta, o impacto numa campanha com centenas de milhares de clientes é baixo — contenção só existe entre requisições do **mesmo** cliente, que é raro e dura milissegundos. Por isso fui de lock pessimista.

**Constraint no banco vs. regra só na aplicação**
Regra só na aplicação é mais fácil de evoluir, mas depende de todo caminho de código (presente e futuro) respeitar a invariante — um bug, uma migração mal feita ou um script administrativo rodando direto no banco podem violá-la silenciosamente. A constraint (`CHECK (saldo >= 0)`) garante a invariante no nível mais baixo possível, independente de quem escreve. Uso as duas camadas: a aplicação valida primeiro (para dar uma resposta de negócio clara, ex. HTTP 409), e a constraint é a rede de segurança final.

---

## 3. API de resgate — núcleo e testes

### Contrato REST

```
POST /api/v1/pontos/resgates
Idempotency-Key: <string> (obrigatório — gerado pelo app mobile por tentativa lógica de resgate)
Content-Type: application/json
```

**Request**
```json
{
  "clienteId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "valorPontos": 500,
  "canal": "app",
  "referenciaOrigem": "pedido-12345"
}
```

**Response 201 Created**
```json
{
  "resgateId": "b1a2c3d4-...",
  "clienteId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "valorDebitado": 500,
  "saldoAtual": 1200,
  "status": "Confirmado",
  "criadoEm": "2026-07-25T14:30:00Z"
}
```

**Erros**
| Código | Quando |
|---|---|
| 400 | Payload inválido (`valorPontos <= 0`, `clienteId` ausente, `Idempotency-Key` ausente) |
| 404 | `clienteId` não encontrado |
| 409 | Saldo insuficiente, **ou** `Idempotency-Key` reenviada com payload diferente do original |
| 201 (reenvio idempotente) | Mesma `Idempotency-Key` + mesmo payload → retorna o mesmo resultado do primeiro processamento, **sem debitar de novo** |

### Núcleo implementado

- [`ResgateService.cs`](src/Resgate.Nucleo/ResgateService.cs) — orquestra validação, checagem de idempotência e delega o débito ao repositório.
- [`IContaPontosRepositorio.cs`](src/Resgate.Nucleo/IContaPontosRepositorio.cs) — abstrai o `SELECT ... FOR UPDATE` + débito descritos no item 2 (sem implementação real de Postgres, conforme escopo pedido).
- [`IIdempotenciaStore.cs`](src/Resgate.Nucleo/IIdempotenciaStore.cs) — abstrai a checagem de reenvio por `Idempotency-Key`.

Idempotência: a chave é comparada pelo valor e por um hash do payload (`ClienteId`, `ValorPontos`, `Canal`, `ReferenciaOrigem`). Mesma chave + mesmo payload retorna o resultado anterior sem novo débito; mesma chave + payload diferente vira conflito. **Limitação:** a checagem de idempotência e o débito não estão na mesma seção crítica no código em memória. Se duas requisições baterem exatamente ao mesmo tempo com a mesma chave (concorrência real, não retry sequencial), a rede de segurança é o índice único de `idempotency_key` no ledger, que rejeitaria a segunda inserção no banco real.

### Os 5 testes que eu escreveria primeiro

Implementados em [`ResgateServiceTests.cs`](tests/Resgate.Nucleo.Tests/ResgateServiceTests.cs):

1. **Saldo suficiente → debita corretamente e retorna novo saldo.** Protege o caminho feliz — a razão de existir do endpoint.
2. **Saldo insuficiente → lança erro e saldo permanece inalterado.** Protege contra a própria causa do incidente do item 1: garantir que nenhum resgate é aplicado parcialmente quando não deveria.
3. **Reenvio com mesma `Idempotency-Key` e mesmo payload → não debita de novo.** Protege exatamente o cenário citado no enunciado: o app mobile reenviando após falha de rede.
4. **Reenvio com mesma `Idempotency-Key` e payload diferente → conflito.** Protege contra reuso indevido da chave de idempotência (ex.: bug no app reaproveitando uma chave para um resgate diferente).
5. **Duas requisições concorrentes do mesmo cliente → saldo nunca fica negativo.** É o teste mais importante à luz do incidente: valida a seção crítica sob concorrência real (via `Task.WhenAll`), não apenas em sequência.

---

## 4. Modernização com retrocompatibilidade

O backoffice de campanhas está acoplado ao monólito .NET com front Angular antigo. A meta é migrar para React com serviços na AWS sem congelar a operação nem quebrar o que já existe.

**Por onde começaria:** *strangler fig* por tela/domínio, não um "big bang". Eu escolheria como primeira fatia uma tela do backoffice que seja (a) usada com frequência o suficiente para gerar aprendizado real, mas (b) de baixo risco de negócio se algo sair errado — por exemplo, uma tela de consulta/relatório de campanhas, não a tela que cria/edita regras de pontuação ativas.

**Convivência entre legado e novo durante a transição:**
- **Gateway/BFF** na frente do backoffice decide pra onde cada rota vai: as que já migraram batem no React + novos serviços, as que não migraram continuam no Angular antigo. O usuário não percebe diferença.
- **Versionamento de API** no monólito (`/api/v1/...`) — os novos serviços consomem contratos estáveis e explícitos, em vez de acessar o banco diretamente.
- **Feature flags** por tela (ou por usuário/loja) — permite reverter instantaneamente sem deploy se algo quebrar.
- Monólito e novos serviços compartilham a mesma base de dados de pontuação, mas de forma controlada: os novos serviços chamam o monólito via API versionada em vez de acessar o banco direto, para não duplicar a lógica de consistência do item 2.

**O que eu NÃO migraria agora:**
- O núcleo de pontuação/resgate — está sendo estabilizado agora, mexer nele e no front ao mesmo tempo multiplica o risco.
- Telas de baixíssimo uso — o custo da migração não se paga.
- Funcionalidades sem dono claro ou sem entendimento — o enunciado mesmo diz que parte do conhecimento só existe no código. Migrar sem entender é reescrever bugs às cegas.

A estratégia acima é incremental e reversível em cada passo, justamente porque o objetivo não é uma reescrita completa de uma vez.

---

## 5. Melhorias por iniciativa própria

Em ordem de ataque, já com a campanha estabilizada:

1. **Observabilidade estruturada** — logs com `resgateId`/`Idempotency-Key`, métricas de latência, tracing. Sem isso, todo incidente futuro começa do zero.
2. **Testes de carga** — simular o perfil de campanha (resgates concorrentes, volume 3x) antes da próxima ativação. Pega o problema antes de acontecer.
3. **Cache Redis no saldo** — com invalidação na hora do débito. Tira pressão da tabela quente sem mexer no núcleo transacional.
4. **Fila (SQS/RabbitMQ) pro acúmulo** — desacopla a escrita da compra do processamento no ledger, com autoscaling. Absorve picos sem degradar o resto.
5. **Documentar os módulos órfãos** — parte do time já saiu e o conhecimento só existe no código. Antes de refatorar, preciso entender o que tá mexendo.

Justificativa da ordem: 1 e 2 previnem o próximo incidente; 3 e 4 atacam os sintomas que já apareceram; 5 reduz risco de longo prazo mas não queima se adiar mais um pouco.
