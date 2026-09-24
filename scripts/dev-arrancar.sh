#!/usr/bin/env bash
# Arranque del entorno de desarrollo completo (Fase 6).
#
# Levanta la infraestructura en Docker y, sobre el host, la Api y la interfaz
# Blazor. Sustituye al arranque manual a base de `nohup dotnet ... &`, que era
# fácil de dejar a medias (procesos huérfanos, PID equivocado al reiniciar).
#
# Pendiente de decidir: si la Api y el Web deben pasar a ser servicios del
# propio docker-compose.yml, como ya lo es ocr-paddle. Ver la memoria del
# proyecto (pendiente_endurecer_despliegue_fase6).
#
#   ./scripts/dev-arrancar.sh          arranca todo
#   ./scripts/dev-arrancar.sh --parar  para la Api y el Web (deja Docker)
set -euo pipefail

RAIZ="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
URL_API="http://localhost:5000"
URL_WEB="http://localhost:5100"
LOG_API="/tmp/legalcase-api.log"
LOG_WEB="/tmp/legalcase-web.log"

parar_procesos() {
  # Por PID explícito y con el patrón entre corchetes: `pkill -f` llega a
  # matar el propio shell que lo invoca cuando el patrón aparece en su
  # línea de comandos.
  local pids
  pids="$(ps ax -o pid=,args= | grep -E "[d]otnet (Api|Web)\.dll" | awk '{print $1}' || true)"
  if [[ -n "$pids" ]]; then
    echo "Parando Api/Web (PID: $(echo "$pids" | tr '\n' ' '))"
    # shellcheck disable=SC2086
    kill $pids 2>/dev/null || true
    sleep 2
  else
    echo "No había procesos de Api/Web en marcha."
  fi
}

if [[ "${1:-}" == "--parar" ]]; then
  parar_procesos
  exit 0
fi

echo "==> Infraestructura (Docker)"
cd "$RAIZ"
docker compose up -d postgres rabbitmq seaweedfs ocr-paddle

echo "==> Esperando a Postgres"
for _ in {1..30}; do
  if docker exec legalcase-postgres pg_isready -U legalcase -d legalcase >/dev/null 2>&1; then
    echo "    Postgres listo."
    break
  fi
  sleep 1
done

echo "==> Compilando"
dotnet build "$RAIZ/src/Api/Api.csproj" -v quiet --nologo
dotnet build "$RAIZ/src/Web/Web.csproj" -v quiet --nologo

parar_procesos

# Se arranca desde el directorio de salida: ASP.NET Core resuelve el content
# root al directorio actual, y lanzarlo desde la raíz del repo hace que no
# encuentre appsettings.json (la Api falla con un error claro, pero el Worker
# se quedaba en silencio usando credenciales por defecto equivocadas).
echo "==> Api en $URL_API"
cd "$RAIZ/src/Api/bin/Debug/net10.0"
ASPNETCORE_ENVIRONMENT=Development setsid dotnet Api.dll > "$LOG_API" 2>&1 < /dev/null &

echo "==> Web en $URL_WEB"
cd "$RAIZ/src/Web/bin/Debug/net10.0"
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="$URL_WEB" \
  setsid dotnet Web.dll > "$LOG_WEB" 2>&1 < /dev/null &

esperar_http() {
  local url="$1" nombre="$2"
  for _ in {1..40}; do
    if curl -sf -o /dev/null --max-time 2 "$url"; then
      echo "    $nombre listo."
      return 0
    fi
    sleep 1
  done
  echo "    $nombre NO respondió a tiempo. Revisa el log."
  return 1
}

echo "==> Comprobando"
esperar_http "$URL_API/swagger/index.html" "Api"   || true
esperar_http "$URL_WEB/login"              "Web"   || true

cat <<FIN

Listo.
  Interfaz : $URL_WEB
  Api      : $URL_API/swagger
  Logs     : $LOG_API
             $LOG_WEB
  Parar    : ./scripts/dev-arrancar.sh --parar
FIN
