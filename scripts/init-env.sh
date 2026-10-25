#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ -e .env ]]; then echo '.env уже существует; файл сохранён без изменений.'; exit 0; fi
umask 077
{
  for name in POSTGRES_PASSWORD OPERATOR_PASSWORD MEDIA_PUBLISH_SECRET MEDIA_HOOK_SECRET; do
    printf '%s=%s\n' "$name" "$(openssl rand -hex 24)"
  done
  printf 'PUBLIC_ORIGIN=http://localhost:8088\nHTTP_BIND=127.0.0.1\nGEOIP_DATABASE=GeoLite2-City.mmdb\n'
} > .env
mkdir -p geoip
echo 'Создан .env. Пароль для входа находится в OPERATOR_PASSWORD.'
