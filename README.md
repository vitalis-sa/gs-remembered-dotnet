# Cuidar de Perto · API .NET (cuidador)

Integrantes: Ana Flavia Camelo (RM561489), Gustavo Kenji Terada (RM562745), João Guilherme Carvalho Novaes (RM566234), Pedro Chasci Puga (RM565154) e Lucas Figueiredo Vieira (RM561342).

A API .NET atende o cuidador: cadastro e login com JWT, vínculo com idosos por código de pareamento, alertas de dose pulada, consultas, locais de atendimento e pedido de relatórios de adesão. O relatório é repassado à API Java, onde está o LLM.

Stack: .NET 8, Minimal API, EF Core com Oracle, JWT Bearer, RabbitMQ.Client 6, Polly (Microsoft.Extensions.Http.Resilience), Swagger (Swashbuckle), xUnit e WebApplicationFactory.

## Arquitetura

```
App (React Native) ──REST+JWT──► API .NET ──HTTP (token do cuidador)──► API Java
                                    │  ▲
              consulta.criada ──────┘  └────── dose.pulada
                                 RabbitMQ (exchange remembered.eventos, topic)
                                    │
                               Oracle (tabelas NET_*)
```

| Pasta | Conteúdo |
| --- | --- |
| `src/CuidarDePerto.Api/Features` | Um arquivo de endpoints por funcionalidade: Auth, Vinculos, Alertas, Consultas, Locais, Relatorios |
| `src/CuidarDePerto.Api/Comum` | Segurança (política `CuidadorVinculado`), idempotência, validação, ProblemDetails, paginação |
| `src/CuidarDePerto.Api/Infra` | EF Core, cliente da API Java, RabbitMQ, push do Expo, health checks, registro de serviços |
| `tests/` | Testes unitários e de integração |
| `scripts/script-bd.sql` | DDL Oracle das tabelas NET_ |

## Como rodar

1. Crie as tabelas: rode `scripts/script-bd.sql` no Oracle.
2. Suba o RabbitMQ local: `docker compose up -d` (painel em http://localhost:15672, guest/guest).
3. Configure as variáveis de ambiente (modelo em `.env.example`).
4. Rode a API: `dotnet run --project src/CuidarDePerto.Api`.
5. Abra o Swagger em http://localhost:5080/swagger. Use o botão Authorize com o token do login.

Publicação no Azure, credenciais e montagem da entrega: [docs/MANUAL-DEPLOY.md](docs/MANUAL-DEPLOY.md).

### Variáveis de ambiente

| Variável | Uso |
| --- | --- |
| `ConnectionStrings__Oracle` | Conexão Oracle, ex.: `User Id=RM00000;Password=...;Data Source=oracle.fiap.com.br:1521/ORCL` |
| `Jwt__Key` | Chave HS256 com pelo menos 32 bytes. A mesma da API Java |
| `Jwt__Issuer` | Emissor deste token (`remembered-dotnet`) |
| `Jwt__Audience` | Audience comum às duas APIs (`remembered`) |
| `Jwt__IssuersAceitos__0` | Emissor da API Java (`remembered-java`) |
| `Java__BaseUrl` | Raiz da API Java, ex.: `https://cuidar-java.azurewebsites.net` |
| `RabbitMq__Enabled` | `false` desliga publicação e consumo |
| `RabbitMq__Uri` | URI AMQP completa (CloudAMQP). Se vazia, usa `RabbitMq__Host`, `__Port`, `__Usuario`, `__Senha` |
| `Push__ExpoAccessToken` | Opcional, só se o projeto Expo exigir push seguro |

A API sobe sem o broker e sem a Java no ar. Nesse caso o `/health` fica `Degraded`.

## Endpoints (prefixo `/api/v1`)

| Grupo | Endpoints | Quem acessa |
| --- | --- | --- |
| Autenticação | `POST /auth/cadastro`, `POST /auth/login`, `POST /auth/renovar`, `GET /me`, `PUT /me/push-token` | Público no cadastro e login; CUIDADOR nos demais |
| Vínculos | `POST /vinculos` (código de pareamento), `GET /vinculos`, `DELETE /vinculos/{id}` | CUIDADOR cria; CUIDADOR e IDOSO listam e removem os seus |
| Alertas | `GET /alertas?lido=&idosoId=`, `PATCH /alertas/{id}/lido` | CUIDADOR |
| Consultas | `GET/POST /idosos/{idosoId}/consultas`, `GET/PUT/DELETE /consultas/{id}` | CUIDADOR vinculado ou o próprio IDOSO |
| Locais | `GET/POST /locais`, `GET/PUT/DELETE /locais/{id}` | Autenticado; só quem criou altera |
| Relatórios | `POST/GET /idosos/{idosoId}/relatorios-adesao`, `GET /relatorios-adesao/{id}` | CUIDADOR vinculado |
| Saúde | `GET /health` (também em `/api/v1/health`) | Público |

Listas paginadas usam `?pagina=0&tamanho=20` e devolvem `{ itens, numero, tamanho, total }`. Todo erro sai em ProblemDetails com `status`, `title`, `detail`, `path` e, na validação, `errors` por campo.

### Regras principais

- O token do cuidador traz `sub`, `role=CUIDADOR`, `nome`, `email` e `idosos` (array de ids vinculados). `POST /vinculos` e `POST /auth/renovar` devolvem um token novo, porque o antigo não tem o idoso recém-vinculado.
- A política `CuidadorVinculado` libera `/idosos/{idosoId}/...` se o `idosoId` está na claim `idosos` (cuidador) ou é o próprio `sub` (idoso com token da Java).
- Consultas do mesmo idoso com menos de 60 min de diferença são recusadas com 409 e a consulta conflitante no corpo.
- Consulta REALIZADA ou CANCELADA não muda mais de status.
- Senhas com PBKDF2 (`PasswordHasher`). Login errado devolve a mesma mensagem para e-mail inexistente e senha errada.

### Idempotência

`POST /idosos/{idosoId}/consultas` e `POST /idosos/{idosoId}/relatorios-adesao` exigem o cabeçalho `Idempotency-Key`. A mesma chave com o mesmo corpo devolve a resposta salva, com o cabeçalho `Idempotent-Replayed: true`, sem gravar de novo nem chamar a Java. A mesma chave com outro corpo devolve 422. A chave vale 24 h. Respostas 5xx não são guardadas, então o cliente pode reenviar depois de uma falha.

```bash
curl -X POST http://localhost:5080/api/v1/idosos/10/consultas \
  -H "Authorization: Bearer $TOKEN" \
  -H "Idempotency-Key: 3f2b8c1e-6a4d-4e0b-9a51-2c7d8e9f0a11" \
  -H "Content-Type: application/json" \
  -d '{"especialidade":"Cardiologia","localId":1,"dataHora":"2026-11-20T14:00:00-03:00","lembrarAntesMin":60}'
```

### Rate limit

Janela fixa de 1 minuto: 3 relatórios por cuidador (cada um custa uma chamada ao LLM) e 60 requisições por usuário no restante. Acima disso a resposta é 429 com `Retry-After`. Os limites ficam em `Limites__RelatoriosPorMinuto` e `Limites__GeralPorMinuto`.

### Integração com a API Java

`HttpClient` tipado com timeout de 20 s, 1 nova tentativa (só em GET; POST não repete porque o pareamento consome o código e o relatório chama o LLM) e circuit breaker. O `Authorization` recebido é repassado, sem token de serviço. Java fora do ar, timeout ou circuito aberto viram 503; erro 5xx da Java vira 502. O corpo da resposta do relatório é repassado como veio.

### Mensageria (RabbitMQ)

| Evento | Papel desta API | Efeito |
| --- | --- | --- |
| `dose.pulada` | Consome da fila `cuidador.alerta-dose` | Cria um `NET_ALERTA` por cuidador vinculado (no máximo um por dose) e envia push pelo Expo |
| `consulta.criada` | Publica depois do commit | A Java grava `TB_AVISO` e avisa o idoso quando `criadoPorTipo` é `CUIDADOR` |

Contrato das mensagens (JSON em camelCase, datas em ISO-8601 sem fuso, na hora de Brasília, como o LocalDateTime da Java):

```json
// dose.pulada (publicado pela Java)
{ "eventId": "uuid", "doseId": 500, "idosoId": 10, "idosoNome": "Maria", "medicamentoNome": "Losartana",
  "severidade": "ALTA", "horarioPrevisto": "2026-11-14T08:00:00", "ocorridoEm": "2026-11-14T08:31:00" }

// consulta.criada (publicado por esta API)
{ "eventId": "uuid", "consultaId": 7, "idosoId": 10, "criadoPorNome": "Ana", "criadoPorTipo": "CUIDADOR",
  "especialidade": "Cardiologia", "dataHora": "2026-11-20T14:00:00", "localNome": "Clínica Centro" }
```

O consumidor usa confirmação manual. Uma falha volta à fila depois de 5 s e de 25 s pela fila `cuidador.alerta-dose.retry`. Na terceira falha a mensagem vai para `cuidador.alerta-dose.dlq`. Cada `eventId` processado fica em `NET_EVENTO_PROCESSADO`, e a restrição única (`cuidador_id`, `dose_id`) impede alerta duplicado.

### Health checks

`GET /health` verifica o Oracle (`Unhealthy` se cair), o `GET /actuator/health` da Java (`Degraded` se cair) e o broker (`Degraded` se cair).

## Usuário de teste

Os dados são fictícios. Crie o cuidador pelo Swagger ou com:

```bash
curl -X POST http://localhost:5080/api/v1/auth/cadastro -H "Content-Type: application/json" \
  -d '{"nome":"Ana Cuidadora","email":"ana.cuidadora@teste.com","senha":"Cuidar@2026"}'
```

Depois, com o código gerado pelo idoso na API Java (`POST /api/v1/me/codigo-pareamento`), chame `POST /api/v1/vinculos` com `{"codigo":"...","parentesco":"Filha"}`.

## Testes

```bash
dotnet test
```

| Tipo | O que cobre |
| --- | --- |
| Unidade (33) | Regra de vínculo, claim `idosos` sempre em array, hash e conferência de senha, filtro de idempotência (mesmo corpo, corpo diferente, sem chave), alerta sem duplicata, validações |
| Integração (26) | `WebApplicationFactory` com SQLite em memória e um `HttpMessageHandler` falso no lugar da Java: login e 401/403, vínculo, idempotência ponta a ponta, 429 na 4ª chamada de relatório, 502 e 503 quando a Java falha, `/health` Healthy e Degraded, alertas |

O projeto de integração roda em `net10.0` quando o SDK instalado é o 10, porque o `TestServer` precisa ser da mesma versão do runtime ASP.NET. Com o SDK 8 (pipeline) ele roda em `net8.0`.

## Limitações conhecidas

- Um vínculo removido só deixa de valer quando o token do cuidador expira (24 h), porque a autorização lê a claim `idosos`.
- A publicação de `consulta.criada` acontece depois do commit, sem outbox. Se o broker estiver fora nesse momento, a consulta fica gravada, o erro vai para o log e o idoso não recebe o aviso.
- O .NET 8 saiu de suporte em 10/11/2026. O plano do grupo fixa .NET 8; para migrar, troque `TargetFramework` em `Directory.Build.props` e as versões 8.x dos pacotes.

## Uso de IA generativa

Parte do código foi escrita com apoio do Claude (Anthropic) e revisada pelo grupo. Todos os dados de saúde usados nos testes e na demonstração são fictícios (LGPD).
