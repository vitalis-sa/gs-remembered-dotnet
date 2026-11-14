#!/usr/bin/env bash
# Grava as configurações da API como App Settings do Web App. Os valores vêm de variáveis de
# ambiente do terminal; nenhum segredo fica no código nem neste arquivo.
#
# Uso:  export DB_USER=... DB_PASSWORD=... JWT_SECRET=... CLOUDAMQP_URL=... JAVA_BASE_URL=...
#       RM=561489 ./scripts/02-configurar-app.sh
# Plano B (Oracle no ACI): exporte ORACLE_CONNECTION no lugar de DB_USER/DB_PASSWORD.
set -euo pipefail

: "${RM:?Informe RM (ex.: RM=561489)}"
GRUPO="${GRUPO:-rg-cuidar-de-perto}"
APP="${APP:-cuidar-de-perto-dotnet-rm${RM}}"

if [[ -z "${ORACLE_CONNECTION:-}" ]]; then
  : "${DB_USER:?Informe DB_USER (Oracle FIAP) ou ORACLE_CONNECTION}"
  : "${DB_PASSWORD:?Informe DB_PASSWORD (Oracle FIAP) ou ORACLE_CONNECTION}"
  ORACLE_CONNECTION="User Id=${DB_USER};Password=${DB_PASSWORD};Data Source=oracle.fiap.com.br:1521/orcl"
fi
: "${JWT_SECRET:?Informe JWT_SECRET (o mesmo da API Java, 32+ caracteres)}"
: "${JAVA_BASE_URL:?Informe JAVA_BASE_URL (raiz pública da API Java)}"
: "${CLOUDAMQP_URL:?Informe CLOUDAMQP_URL (amqps://... do CloudAMQP)}"

echo ">> App Settings em $APP (valores não são exibidos)"
az webapp config appsettings set --name "$APP" --resource-group "$GRUPO" --output none --settings \
  ASPNETCORE_ENVIRONMENT=Production \
  "ConnectionStrings__Oracle=${ORACLE_CONNECTION}" \
  "Jwt__Key=${JWT_SECRET}" \
  "Jwt__Issuer=${JWT_ISSUER:-remembered-dotnet}" \
  "Jwt__Audience=${JWT_AUDIENCE:-remembered}" \
  "Jwt__IssuersAceitos__0=${JWT_ISSUER_JAVA:-remembered-java}" \
  "Java__BaseUrl=${JAVA_BASE_URL}" \
  RabbitMq__Enabled=true \
  "RabbitMq__Uri=${CLOUDAMQP_URL}" \
  "Push__ExpoAccessToken=${EXPO_ACCESS_TOKEN:-}"

az webapp restart --name "$APP" --resource-group "$GRUPO" --output none
echo ">> Configurado e reiniciado. Saúde: https://$APP.azurewebsites.net/health"
az webapp config appsettings list --name "$APP" --resource-group "$GRUPO" --query "[].name" -o tsv
