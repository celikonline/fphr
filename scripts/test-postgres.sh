#!/usr/bin/env bash
# TK-032: tek paylaşımlı Postgres sunucusu — tüm .NET testleri buna karşı koşar, test başına konteyner AÇILMAZ.
# senseik-test-postgres adlı postgres:17-alpine konteynerini 55432 portunda (host 5432'deki alakasız
# lexparse_postgres'e dokunmadan) yoksa oluşturur, duruyorsa başlatır, çalışıyorsa aynısını kullanır.
#
# Kullanım:
#   eval "$(scripts/test-postgres.sh)"   # SENSE_TEST_POSTGRES'i geçerli kabukta export eder
# ya da çıktıyı elle uygula:
#   scripts/test-postgres.sh
set -euo pipefail

CONTAINER_NAME="senseik-test-postgres"
HOST_PORT="55432"
POSTGRES_PASSWORD="postgres"

if [ -z "$(docker ps -aq -f "name=^${CONTAINER_NAME}\$")" ]; then
  echo "[test-postgres] ${CONTAINER_NAME} yok, oluşturuluyor..." >&2
  docker run -d --name "$CONTAINER_NAME" \
    -e POSTGRES_USER=postgres \
    -e POSTGRES_PASSWORD="$POSTGRES_PASSWORD" \
    -e POSTGRES_DB=postgres \
    -p "${HOST_PORT}:5432" \
    postgres:17-alpine >&2
elif [ -z "$(docker ps -q -f "name=^${CONTAINER_NAME}\$")" ]; then
  echo "[test-postgres] ${CONTAINER_NAME} duruyor, başlatılıyor..." >&2
  docker start "$CONTAINER_NAME" >&2
else
  echo "[test-postgres] ${CONTAINER_NAME} zaten çalışıyor, aynısı kullanılıyor." >&2
fi

echo "[test-postgres] hazır olması bekleniyor..." >&2
tries=0
until docker exec "$CONTAINER_NAME" pg_isready -U postgres >/dev/null 2>&1; do
  tries=$((tries + 1))
  if [ "$tries" -ge 60 ]; then
    echo "[test-postgres] ${CONTAINER_NAME} 60 sn içinde hazır olmadı." >&2
    exit 1
  fi
  sleep 1
done

HOST_CONNECTION="Host=127.0.0.1;Port=${HOST_PORT};Username=postgres;Password=${POSTGRES_PASSWORD};Database=postgres"
DOCKER_CONNECTION="Host=host.docker.internal;Port=${HOST_PORT};Username=postgres;Password=${POSTGRES_PASSWORD};Database=postgres"

echo "[test-postgres] hazır." >&2
echo "[test-postgres]   host için:          ${HOST_CONNECTION}" >&2
echo "[test-postgres]   SDK konteyneri için: ${DOCKER_CONNECTION} (scripts/dev/dotnet bunu otomatik çevirir)" >&2

# Kabuğa export etmek için: eval "$(scripts/test-postgres.sh)"
echo "export SENSE_TEST_POSTGRES=\"${HOST_CONNECTION}\""
