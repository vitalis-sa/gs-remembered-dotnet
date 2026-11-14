#!/usr/bin/env bash
# Apaga o grupo de recursos inteiro (Web App, plano e, se houver, o container Oracle).
# O Oracle da FIAP não é afetado.
#
# Uso:  ./scripts/99-destruir.sh        (pede confirmação)
#       ./scripts/99-destruir.sh -y     (sem perguntar)
set -euo pipefail

GRUPO="${GRUPO:-rg-cuidar-de-perto}"

if ! az group exists --name "$GRUPO" | grep -q true; then
  echo ">> Grupo $GRUPO não existe; nada a apagar."
  exit 0
fi

echo ">> Recursos em $GRUPO:"
az resource list --resource-group "$GRUPO" --query "[].{nome:name, tipo:type}" -o table

if [[ "${1:-}" != "-y" ]]; then
  read -r -p "Apagar o grupo $GRUPO e tudo acima? Digite o nome do grupo para confirmar: " resposta
  [[ "$resposta" == "$GRUPO" ]] || { echo "Cancelado."; exit 1; }
fi

az group delete --name "$GRUPO" --yes --no-wait
echo ">> Exclusão de $GRUPO iniciada (leva alguns minutos)."
