#!/usr/bin/env bash
# ==============================================================================
# Script de Deploy Rápido do Urubu Hunter API para a Oracle Cloud
# ==============================================================================
set -e

SSH_KEY="/home/dtgi/Downloads/ssh-key-2026-10-28.key"
REMOTE_HOST="opc@147.15.35.47"
REMOTE_DIR="/opt/urubu-hunter-api"

echo "🚀 1. Compilando API (.NET 10 self-contained linux-x64)..."
dotnet publish -c Release -r linux-x64 --self-contained true -o ./publish-linux /p:PublishSingleFile=true

echo "📦 2. Enviando novo binário para a VM..."
rsync -avz --progress -e "ssh -i $SSH_KEY" ./publish-linux/UrubuApi $REMOTE_HOST:/home/opc/UrubuApi

echo "🔄 3. Atualizando e reiniciando o serviço na Oracle Cloud..."
ssh -i $SSH_KEY $REMOTE_HOST "
  sudo systemctl stop urubu-api
  sudo cp /home/opc/UrubuApi $REMOTE_DIR/UrubuApi
  sudo chmod +x $REMOTE_DIR/UrubuApi
  sudo systemctl start urubu-api
  sleep 2
  sudo systemctl status urubu-api --no-pager
"

echo "✅ 4. Testando saúde da API em produção..."
curl --retry 5 --retry-connrefused --retry-delay 1 -s http://147.15.35.47:5033/api/health
echo ""
echo "🎉 Deploy concluído com sucesso!"
