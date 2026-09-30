#!/usr/bin/env bash
# Mide un documento por el pipeline COMPLETO, como lo usaría la aplicación:
# Api -> RabbitMQ -> Worker -> servicio de OCR -> fragmentos en Postgres.
#
#   services/ocr-paddle/pruebas/medir_pipeline.sh <pdf> <texto_salida>
#
# Deja en <texto_salida> el texto de los fragmentos, en orden de página y
# párrafo, para compararlo con fidelidad.py. Imprime el tiempo total y por
# página.
#
# ATENCIÓN: borra antes TODOS los documentos, fragmentos, entidades y
# eventos de cronología de la base de datos, y pone a 0 los folios de los
# expedientes. Es una herramienta para la base de datos de desarrollo, no
# para una con datos reales.
#
# Necesita la aplicación en marcha: infraestructura (docker compose up -d),
# Api (scripts/dev-arrancar.sh) y el Worker de .NET.
set -euo pipefail

PDF="$1"
SALIDA="$2"
API="${API:-http://localhost:5000}"
TENANT="${TENANT:-00000000-0000-0000-0000-000000000001}"
PSQL="docker exec -i legalcase-postgres psql -U legalcase -d legalcase -t -A -q"

$PSQL -c "DELETE FROM fragmentos_documento; DELETE FROM entidades_extraidas;
          DELETE FROM eventos_cronologia; DELETE FROM documentos_adjuntos;
          UPDATE expedientes SET ultimo_folio = 0;"

TOKEN=$(curl -sf -X POST "$API/api/auth/dev-token" -H 'Content-Type: application/json' \
  -d "{\"tenantId\":\"$TENANT\"}" | python3 -c "import sys,json;print(json.load(sys.stdin)['accessToken'])")
EXPEDIENTE=$(curl -sf "$API/api/expedientes" -H "Authorization: Bearer $TOKEN" \
  | python3 -c "import sys,json;print(json.load(sys.stdin)[0]['id'])")

inicio=$(date +%s.%N)
curl -sf -X POST "$API/api/expedientes/$EXPEDIENTE/documentos" \
  -H "Authorization: Bearer $TOKEN" -F "file=@$PDF" -o /dev/null

# 2 = Completado, 3 = Error (EstadoProcesamientoDocumento)
estado=""
for _ in $(seq 1 7200); do
  estado=$($PSQL -c "SELECT estado_procesamiento FROM documentos_adjuntos LIMIT 1")
  if [ "$estado" = "2" ] || [ "$estado" = "3" ]; then break; fi
  sleep 1
done
fin=$(date +%s.%N)

$PSQL -c "SELECT string_agg(texto_fragmento, E'\n\n' ORDER BY pagina, parrafo) FROM fragmentos_documento;" > "$SALIDA"
paginas=$($PSQL -c "SELECT count(DISTINCT pagina) FROM fragmentos_documento;")

python3 - "$inicio" "$fin" "$paginas" "$estado" <<'PY'
import sys
inicio, fin, paginas, estado = float(sys.argv[1]), float(sys.argv[2]), int(sys.argv[3] or 0), sys.argv[4]
total = fin - inicio
nombre = {"2": "Completado", "3": "ERROR"}.get(estado, f"sin terminar ({estado})")
print(f"estado={nombre}  páginas={paginas}  total={total:.1f}s  = {total / max(paginas, 1):.1f} s/página")
PY
