# Transição

**Data:** 2026-11-14
**Feature:** API .NET do cuidador (seção 4 do plano "Cuidar de Perto")
**Tarefa:** implementação da seção 4 concluída; validação com serviços reais pendente

## Concluído ✓

- Solução `CuidarDePerto.sln` em .NET 8: `src/CuidarDePerto.Api`, `tests/CuidarDePerto.UnitTests`, `tests/CuidarDePerto.IntegrationTests`.
- Endpoints: auth, vínculos, alertas, consultas, locais, relatórios (repasse à Java), `/health`.
- JWT com claim `idosos` em array, política `CuidadorVinculado`, PBKDF2, idempotência (`NET_IDEMPOTENCIA`), rate limit 3/60 por minuto, Polly (20 s, retry só em GET, circuit breaker).
- RabbitMQ: consome `dose.pulada` (fila de retry + DLQ, idempotência por eventId em `NET_EVENTO_PROCESSADO`), publica `consulta.criada`.
- `scripts/script-bd.sql`, `docker-compose.yml`, `.env.example`, `README.md`.
- Build com 0 erros; 29 testes de unidade e 26 de integração passando (`dotnet test`).

## Em Andamento

- Nada em execução.

## Pendente

- Rodar contra Oracle, RabbitMQ e API Java reais (nunca testado fora do SQLite e dos falsos).
- Combinar com o time Java: Issuer/Audience/Key, campo `criadoPorTipo` em `consulta.criada`, `horarioPrevisto` com fuso em `dose.pulada`, resposta `{idosoId, nome}` de `/pareamento/validar`.
- Seção 8 (DevOps): `azure-pipelines.yml` e scripts Azure CLI `01-provisionar.sh`, `02-configurar-app.sh`, `99-destruir.sh`. Usar `Java__BaseUrl` (o plano cita também `JavaApi__BaseUrl`).
- `git init` e primeiro commit.

## Bloqueios

- Nenhum. Máquina local só tem runtime 9 e 10: testes de integração rodam em net10.0 quando o SDK é 10 (condição no `.csproj`).

## Contexto

- Branch: sem repositório git.
- Não commitado: todo o projeto.
- Riscos: .NET 8 fora de suporte desde 10/11/2026; `System.Security.Cryptography.Pkcs` 8.0.0 (transitivo do Oracle) com alerta alto no NuGet.
