# Manual de publicação · API .NET Cuidar de Perto

Este manual leva a API do zero até uma URL pública que o app mobile consome. Siga as seções na ordem.

## 1. O que precisa existir antes

| Item | Onde conseguir | Quem guarda |
| --- | --- | --- |
| Senha do usuário `REMEMBERED` no Oracle do Azure | Quem criou o container `rm566234-gs-oracle` (RM566234) | Só o grupo |
| Chave JWT de pelo menos 32 caracteres | App Settings da Web App Java `rm566234-gs-api` (seção 2) | Mesmo valor na API .NET e na API Java |
| URL da API Java publicada | `https://rm566234-gs-api.azurewebsites.net` (já no ar) | Público |
| URI do RabbitMQ (`amqps://...`) | Painel do CloudAMQP, mesma instância da Java (exchange `remembered.eventos`), campo "AMQP URL" | Mesmo broker na .NET e na Java |
| Conta Azure com crédito (Azure for Students) | portal.azure.com | Quem faz o deploy |
| Azure CLI instalada e logada | `az login` | Quem faz o deploy |
| Token de acesso do Expo (opcional) | expo.dev, Access Tokens, só se o push seguro estiver ligado | Quem faz o deploy |

A chave do Groq (LLM dos relatórios) fica só na API Java. A API .NET não usa essa chave.

## 2. Chave JWT

A API Java já está publicada, então a chave já existe. Copie o valor do portal do Azure: Web App `rm566234-gs-api`, Configurações, Variáveis de ambiente. Ou, com acesso à assinatura do RM566234:

```bash
az webapp config appsettings list -g <grupo-da-java> -n rm566234-gs-api --query "[?contains(name,'JWT') || contains(name,'Jwt')]"
```

Só gere uma chave nova se o grupo decidir trocar a da Java também. Comando em PowerShell:

```powershell
[Convert]::ToBase64String((1..48 | ForEach-Object { Get-Random -Maximum 256 }))
```

Bash:

```bash
openssl rand -base64 48
```

O resultado vai em `Jwt__Key` aqui e na variável equivalente da API Java. Se as duas forem diferentes, o token do idoso dá 401 na .NET e o vínculo falha.

## 3. Banco Oracle

O banco de produção é um Oracle Free num container do Azure, compartilhado com a API Java.

| Campo | Valor |
| --- | --- |
| Host | `rm566234-gs-oracle.eastus.azurecontainer.io` |
| Porta | `1521` |
| Serviço | `FREEPDB1` |
| Usuário (schema) | `REMEMBERED` |

1. No SQL Developer, crie uma conexão com esses valores (tipo "Nome do serviço": `FREEPDB1`).
2. Confira se as tabelas da .NET já existem: `SELECT TABLE_NAME FROM USER_TABLES WHERE TABLE_NAME LIKE 'NET_%';`
3. Se voltarem menos de 7 linhas, rode `scripts/script-bd.sql` inteiro (F5). Ele cria as 7 tabelas `NET_` e 2 locais semente.
4. Repita a consulta do passo 2. Devem voltar 7 linhas.

As tabelas criadas antes no Oracle FIAP (RM561489) ficam em outro banco e não servem para a produção.

A connection string fica assim:

```text
User Id=REMEMBERED;Password=SENHA;Data Source=rm566234-gs-oracle.eastus.azurecontainer.io:1521/FREEPDB1
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

A API já está publicada em `https://rm566234-gs-dotnet.azurewebsites.net` (16/11/2026). Ela roda no mesmo plano B1 Linux da Java (`plan-rm566234-gs`), sem custo extra. Para republicar uma versão nova, pule para "Gerar o pacote e enviar".

Os comandos usam Bash (Git Bash ou Cloud Shell).

```bash
RG=rg-rm566234-gs
APP=rm566234-gs-dotnet
PLANO=plan-rm566234-gs
```

Criar a Web App no plano da Java. O Azure Linux não oferece mais o runtime .NET 8, então a Web App usa o 10. O projeto compila para `net8.0` com `RollForward=Major`, por isso roda no runtime 10 sem mudança de código.

```bash
az webapp create -g $RG -p $PLANO -n $APP --runtime "DOTNETCORE:10.0"
```

Configurar as variáveis (cole os valores reais entre as aspas):

```bash
az webapp config appsettings set -g $RG -n $APP --settings \
  ASPNETCORE_ENVIRONMENT=Production \
  "ConnectionStrings__Oracle=User Id=REMEMBERED;Password=SENHA;Data Source=rm566234-gs-oracle.eastus.azurecontainer.io:1521/FREEPDB1" \
  "Jwt__Key=CHAVE_DA_SECAO_2" \
  Jwt__Issuer=remembered-dotnet \
  Jwt__Audience=remembered \
  Jwt__IssuersAceitos__0=remembered-java \
  "Java__BaseUrl=https://rm566234-gs-api.azurewebsites.net" \
  RabbitMq__Enabled=true \
  "RabbitMq__Uri=amqps://USUARIO:SENHA@HOST/VHOST"
```

Os valores reais de senha, chave e AMQP URL são os mesmos das App Settings `DB_PASSWORD`, `JWT_SECRET` e `CLOUDAMQP_URL` da Web App Java.

Ligar o health check do Azure, o Always On (o consumidor do RabbitMQ precisa da API acordada) e o HTTPS obrigatório:

```bash
az webapp config set -g $RG -n $APP --always-on true --generic-configurations '{"healthCheckPath":"/health"}'
```

```bash
az webapp update -g $RG -n $APP --https-only true
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

No PowerShell, troque o `zip` por `cd publish; tar.exe -a -c -f ..\app.zip *; cd ..`. O `tar.exe` grava os caminhos com `/`, que o App Service Linux exige para a pasta `runtimes`.

## 6. Conferir a publicação

1. `https://rm566234-gs-dotnet.azurewebsites.net/health` deve responder `Healthy` com Oracle, Java e RabbitMQ.
2. `https://rm566234-gs-dotnet.azurewebsites.net/swagger` deve abrir a documentação.
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

1. Troque a URL base do app para `https://rm566234-gs-dotnet.azurewebsites.net/api/v1`.
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
- O grupo `rg-rm566234-gs` também guarda a API Java e o Oracle. Depois da avaliação, o grupo decide junto se apaga tudo. Para remover só a API .NET: `az webapp delete -g rg-rm566234-gs -n rm566234-gs-dotnet`.
