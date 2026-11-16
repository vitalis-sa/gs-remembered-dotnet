# Manual de publicação · API .NET Cuidar de Perto

Este manual leva a API do zero até uma URL pública que o app mobile consome. Siga as seções na ordem.

## 1. O que precisa existir antes

| Item | Onde conseguir | Quem guarda |
| --- | --- | --- |
| Usuário e senha do Oracle FIAP (RM do dono do schema) | Portal FIAP, e-mail de acesso ao banco | Só o dono do RM |
| Chave JWT de pelo menos 32 caracteres | Gerada uma vez pelo grupo (comando na seção 2) | Mesmo valor na API .NET e na API Java |
| URL da API Java publicada | Deploy do repositório `gs-remembered-java` | Público |
| URI do RabbitMQ (`amqps://...`) | Painel do CloudAMQP, instância do grupo, campo "AMQP URL" | Mesmo broker na .NET e na Java |
| Conta Azure com crédito (Azure for Students) | portal.azure.com | Quem faz o deploy |
| Azure CLI instalada e logada | `az login` | Quem faz o deploy |
| Token de acesso do Expo (opcional) | expo.dev, Access Tokens, só se o push seguro estiver ligado | Quem faz o deploy |

A chave do Groq (LLM dos relatórios) fica só na API Java. A API .NET não usa essa chave.

## 2. Gerar a chave JWT (uma vez para o grupo)

PowerShell:

```powershell
[Convert]::ToBase64String((1..48 | ForEach-Object { Get-Random -Maximum 256 }))
```

Bash:

```bash
openssl rand -base64 48
```

O resultado vai em `Jwt__Key` aqui e na variável equivalente da API Java. Se as duas forem diferentes, o token do idoso dá 401 na .NET e o vínculo falha.

## 3. Banco Oracle

1. Abra o SQL Developer com o usuário do RM que vai hospedar as tabelas.
2. Rode `scripts/script-bd.sql` inteiro (F5). Ele cria 7 tabelas `NET_` e 2 locais semente.
3. Confira com `SELECT TABLE_NAME FROM USER_TABLES WHERE TABLE_NAME LIKE 'NET_%';` (devem voltar 7 linhas).

O schema já foi criado no RM561489. Pule esta seção se for usar o mesmo RM.

A connection string fica assim:

```text
User Id=RM000000;Password=SENHA;Data Source=oracle.fiap.com.br:1521/ORCL
```

## 4. Rodar local antes de publicar

1. Copie `.env.example` e preencha os valores reais. O arquivo `.env` está no `.gitignore`.
2. Exporte as variáveis no terminal (PowerShell):

```powershell
Get-Content .env | Where-Object { $_ -match '^[^#].*=' } | ForEach-Object {
  $n, $v = $_ -split '=', 2; [Environment]::SetEnvironmentVariable($n, $v)
}
dotnet run --project src/CuidarDePerto.Api
```

3. Abra http://localhost:5080/health. O esperado é `"status":"Healthy"`.
4. `Degraded` indica que a Java ou o RabbitMQ não respondem. `Unhealthy` indica falha no Oracle.

## 5. Publicar no Azure App Service

Os comandos usam Bash (Git Bash ou Cloud Shell). Troque `cuidar-dotnet-grupo` por um nome único.

```bash
RG=rg-cuidar-de-perto
APP=cuidar-dotnet-grupo
PLANO=plan-cuidar-dotnet
REGIAO=brazilsouth
```

Criar o grupo, o plano (F1 gratuito ou B1) e a Web App Linux .NET 8:

```bash
az group create -n $RG -l $REGIAO
```

```bash
az appservice plan create -g $RG -n $PLANO --is-linux --sku B1
```

```bash
az webapp create -g $RG -p $PLANO -n $APP --runtime "DOTNETCORE:8.0"
```

Configurar as variáveis (cole os valores reais entre as aspas):

```bash
az webapp config appsettings set -g $RG -n $APP --settings \
  ASPNETCORE_ENVIRONMENT=Production \
  "ConnectionStrings__Oracle=User Id=RM000000;Password=SENHA;Data Source=oracle.fiap.com.br:1521/ORCL" \
  "Jwt__Key=CHAVE_DA_SECAO_2" \
  Jwt__Issuer=remembered-dotnet \
  Jwt__Audience=remembered \
  Jwt__IssuersAceitos__0=remembered-java \
  "Java__BaseUrl=https://URL-DA-API-JAVA" \
  RabbitMq__Enabled=true \
  "RabbitMq__Uri=amqps://USUARIO:SENHA@HOST/VHOST"
```

Ligar o health check do Azure:

```bash
az webapp config set -g $RG -n $APP --generic-configurations '{"healthCheckPath":"/health"}'
```

Gerar o pacote e enviar:

```bash
dotnet publish src/CuidarDePerto.Api -c Release -o publish
```

```bash
cd publish && zip -r ../app.zip . && cd ..
```

```bash
az webapp deploy -g $RG -n $APP --src-path app.zip --type zip
```

No PowerShell, troque o `zip` por `Compress-Archive -Path publish\* -DestinationPath app.zip -Force`.

## 6. Conferir a publicação

1. `https://$APP.azurewebsites.net/health` deve responder `Healthy`.
2. `https://$APP.azurewebsites.net/swagger` deve abrir a documentação.
3. No Swagger, faça `POST /api/v1/auth/cadastro` e `POST /api/v1/auth/login`, cole o token em Authorize e chame `GET /api/v1/me`.
4. Logs em tempo real: `az webapp log tail -g $RG -n $APP`.

Se a API não subir, o log mostra qual opção falhou. As mensagens mais comuns:

| Mensagem no log | Causa | Correção |
| --- | --- | --- |
| `Jwt:Key precisa ter pelo menos 32 bytes` | `Jwt__Key` vazia ou curta | Use a chave da seção 2 |
| `Java:BaseUrl precisa ser uma URL absoluta` | `Java__BaseUrl` vazia ou sem `https://` | Preencha com a URL completa da Java |
| `ORA-01017` | Usuário ou senha do Oracle errados | Revise a connection string |
| `ORA-00942` | Tabelas não criadas nesse schema | Rode a seção 3 com o mesmo usuário |

## 7. Ligar o app mobile

1. Troque a URL base do app para `https://$APP.azurewebsites.net/api/v1`.
2. O login do cuidador usa a API .NET. O login do idoso usa a API Java.
3. O token do idoso funciona na .NET porque as duas APIs usam a mesma `Jwt__Key`.

## 8. Montar a entrega no Portal do Aluno

1. Preencha `entrega/identificacao.txt` com turma, nomes, RMs e a URL do Swagger.
2. Confira que o README do GitHub está atualizado e o repositório está público.
3. Gere o .zip com o `.txt` e, se houver, o PowerPoint:

```powershell
Compress-Archive -Path entrega\identificacao.txt -DestinationPath GS-DOTNET-GRUPO.zip -Force
```

4. Abra o .zip antes de enviar, para confirmar que o arquivo não está corrompido.

## 9. Regras sobre credenciais

- Valores reais ficam só no `.env` local e nas App Settings do Azure.
- `appsettings.json` continua com campos vazios no GitHub.
- Antes de cada push, `git diff --cached` deve mostrar só código, sem senha nem chave.
- Se uma senha vazar num commit, troque a senha no serviço. Apagar o commit não basta, porque o GitHub mantém cópias.
- Depois da avaliação, apague o grupo de recursos para não gastar crédito: `az group delete -n $RG`.
