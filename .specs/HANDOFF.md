# Transição

**Data:** 2026-11-14
**Feature:** API .NET do cuidador (seção 4 do plano "Cuidar de Perto")
**Tarefa:** integração com a API Java validada ponta a ponta contra serviços reais; sem pendências abertas desta sessão

## Concluído ✓

- Repositório público `vitalis-sa/gs-remembered-dotnet`, branch `main`.
- Contratos alinhados com `vitalis-sa/gs-remembered-java`: exchange `remembered.eventos`, JWT `remembered`/`remembered-dotnet`/`remembered-java`, `consulta.criada` com `criadoPorNome` e `dataHora` em hora de Brasília, `dose.pulada` lido como LocalDateTime de Brasília, relatório com corpo `{ dias }`.
- Oracle 19c: `UseOracleSQLCompatibility(19)` (sem isso dá ORA-00904 no `TRUE/FALSE`).
- `scripts/script-bd.sql` aplicado no schema RM561489: 7 tabelas `NET_`.
- Java: PR https://github.com/vitalis-sa/gs-remembered-java/pull/1 com a migration V2 que recria `trg_auditoria_medicamento` (estava INVALID, ORA-00920). A V2 já foi aplicada no Oracle FIAP pelo Flyway.
- Teste ponta a ponta com Oracle FIAP, CloudAMQP, Groq e as duas APIs locais: cadastro, pareamento, vínculo, local, consulta (aviso chegou na Java), relatório com LLM, medicamento crítico na Java e `dose.pulada` do job da Java virando alerta na .NET em 75 ms.
- Dados de teste (`*@teste.dev`) apagados do Oracle; ficaram só os 2 locais semente.
- 59 testes passando (33 unidade, 26 integração).
- Seção 8 (DevOps): cancelada a pedido do usuário; pipeline e scripts revertidos.

## Pendente

- Merge do PR #1 da Java na `main` (o banco já está na V2, então a `main` da Java sem a V2 continua subindo, mas um banco novo nasceria com o trigger inválido).

## Contexto

- Segredos ficam fora dos repositórios; para rodar local, exportar as variáveis do `.env.example` (.NET) e do `.env` da Java.
- Riscos: .NET 8 fora de suporte desde 10/11/2026; `System.Security.Cryptography.Pkcs` 8.0.0 com alerta alto no NuGet.
