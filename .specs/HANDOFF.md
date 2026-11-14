# Transição

**Data:** 2026-11-14
**Feature:** API .NET do cuidador (seção 4 do plano "Cuidar de Perto")
**Tarefa:** integração com a API Java validada contra serviços reais; push para o GitHub pendente

## Concluído ✓

- Git local na `main` com 3 commits (`ba0867b`, `cd72f23`, `c009454`).
- Contratos alinhados com `vitalis-sa/gs-remembered-java`: exchange `remembered.eventos`, JWT `remembered`/`remembered-dotnet`/`remembered-java`, `consulta.criada` com `criadoPorNome` e `dataHora` em hora de Brasília, `dose.pulada` lido como LocalDateTime de Brasília, relatório com corpo `{ dias }`.
- Oracle 19c: `UseOracleSQLCompatibility(19)` (sem isso dá ORA-00904 no `TRUE/FALSE`).
- `scripts/script-bd.sql` aplicado no schema RM561489: 7 tabelas `NET_` criadas.
- Teste ponta a ponta com Oracle FIAP, CloudAMQP, Groq e API Java locais: cadastro, pareamento, vínculo, local, consulta (aviso chegou na Java), relatório com LLM, `dose.pulada` virando alerta.
- 59 testes passando (33 unidade, 26 integração).

## Pendente

- Push para `vitalis-sa/gs-remembered-dotnet` (público): o classificador de permissão bloqueou a criação do repositório; precisa do OK do usuário.
- Bug na Java: `TRG_AUDITORIA_MEDICAMENTO` (V1__schema_inicial.sql) está INVALID com ORA-00920, porque usa `INSERTING`/`DELETING` dentro do INSERT. Todo POST de medicamento dá 500. Correção: nova migration V2 com `CREATE OR REPLACE TRIGGER` calculando os valores em variáveis PL/SQL antes do INSERT.
- Depois do conserto do trigger, repetir o `dose.pulada` publicado pelo job da Java (hoje testado com mensagem no formato da Java publicada direto no CloudAMQP).
- Dados de teste no Oracle: idosos 1 a 4 (`*@teste.dev`), cuidadores 1 e 2, vínculos, 1 alerta, 1 relatório.
- Seção 8 (DevOps): `azure-pipelines.yml` e scripts `01-provisionar.sh`, `02-configurar-app.sh`, `99-destruir.sh`.

## Contexto

- Segredos ficam fora dos repositórios; para rodar local, exportar as variáveis do `.env.example` (.NET) e do `.env` da Java.
- Riscos: .NET 8 fora de suporte desde 10/11/2026; `System.Security.Cryptography.Pkcs` 8.0.0 com alerta alto no NuGet.
