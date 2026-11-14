#!/usr/bin/env bash
# Cria a infraestrutura da API .NET na Azure: grupo de recursos, plano do App Service (Linux)
# e Web App com runtime .NET 8. Banco: plano A usa o Oracle da FIAP (nada a criar aqui);
# plano B (BANCO=aci) sobe um container Oracle no Azure Container Instances.
#
# Uso:  az login && RM=561489 ./scripts/01-provisionar.sh
#       RM=561489 BANCO=aci ORACLE_SENHA_ADMIN=... ./scripts/01-provisionar.sh
set -euo pipefail

: "${RM:?Informe RM (ex.: RM=561489), usado para o nome único do Web App}"
GRUPO="${GRUPO:-rg-cuidar-de-perto}"
LOCAL="${LOCAL:-brazilsouth}"
PLANO="${PLANO:-plan-cuidar-de-perto}"
SKU="${SKU:-B1}"
APP="${APP:-cuidar-de-perto-dotnet-rm${RM}}"
RUNTIME="${RUNTIME:-DOTNETCORE:8.0}"
BANCO="${BANCO:-fiap}"

echo ">> Grupo de recursos $GRUPO em $LOCAL"
az group create --name "$GRUPO" --location "$LOCAL" --output none

echo ">> Plano do App Service $PLANO ($SKU, Linux)"
az appservice plan create --name "$PLANO" --resource-group "$GRUPO" --location "$LOCAL" \
  --sku "$SKU" --is-linux --output none

echo ">> Web App $APP ($RUNTIME)"
az webapp create --name "$APP" --resource-group "$GRUPO" --plan "$PLANO" \
  --runtime "$RUNTIME" --https-only true --output none

# Always On mantém o consumidor do RabbitMQ vivo; /health é o mesmo endpoint dos health checks da API.
az webapp config set --name "$APP" --resource-group "$GRUPO" \
  --always-on true --http20-enabled true --min-tls-version 1.2 \
  --generic-configurations '{"healthCheckPath": "/health"}' --output none
az webapp log config --name "$APP" --resource-group "$GRUPO" \
  --application-logging filesystem --level information --output none

if [[ "$BANCO" == "aci" ]]; then
  : "${ORACLE_SENHA_ADMIN:?Informe ORACLE_SENHA_ADMIN para o container Oracle}"
  : "${ORACLE_SENHA_APP:?Informe ORACLE_SENHA_APP para o usuário CUIDAR}"
  ORACLE_ACI="${ORACLE_ACI:-aci-oracle-cuidar-rm${RM}}"
  echo ">> Container Oracle $ORACLE_ACI (plano B)"
  az container create --name "$ORACLE_ACI" --resource-group "$GRUPO" --location "$LOCAL" \
    --image gvenzl/oracle-xe:21-slim --os-type Linux --cpu 2 --memory 4 \
    --ports 1521 --ip-address Public --dns-name-label "$ORACLE_ACI" \
    --secure-environment-variables ORACLE_PASSWORD="$ORACLE_SENHA_ADMIN" APP_USER_PASSWORD="$ORACLE_SENHA_APP" \
    --environment-variables APP_USER=CUIDAR --output none
  FQDN=$(az container show --name "$ORACLE_ACI" --resource-group "$GRUPO" --query ipAddress.fqdn -o tsv)
  echo "   Oracle em $FQDN:1521/XEPDB1 (usuário CUIDAR). Rode scripts/script-bd.sql nele."
  echo "   ORACLE_CONNECTION=\"User Id=CUIDAR;Password=<senha>;Data Source=$FQDN:1521/XEPDB1\""
fi

echo ">> Pronto: https://$APP.azurewebsites.net (próximo passo: scripts/02-configurar-app.sh)"
