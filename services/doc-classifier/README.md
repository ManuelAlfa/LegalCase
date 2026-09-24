# doc-classifier

Microservicio Python (FastAPI) que expone `POST /clasificar`: recibe texto
ya extraído (por `ocr-paddle` o extracción directa) y devuelve un tipo de
documento sugerido con su confianza.

## Por qué un servicio separado de `ocr-paddle`

`POST /ocr` en `ocr-paddle` es bloqueante (no usa `run_in_threadpool` ni
varios workers de uvicorn) y puede tardar minutos por página. Si la
clasificación viviera en ese mismo proceso, cada petición a `/clasificar`
(que debería responder en milisegundos) quedaría en cola detrás de un OCR en
curso. Al ser un servicio aparte, la clasificación nunca se ve bloqueada por
el OCR ni al revés.

## Clasificador

Ligero, por palabras clave lematizadas con spaCy (`es_core_news_sm`, ~15MB) —
no hace falta entrenar un modelo propio para el primer MVP. `es_core_news_lg`
/ `es_dep_news_trf` quedan reservados para IA.3 (extracción de entidades),
que es una tarea distinta con necesidades distintas.

## Licencias (uso comercial/SaaS)

| Componente | Licencia | Nota |
|---|---|---|
| spaCy | MIT | |
| `es_core_news_sm` | MIT | Modelo de spaCy para español. |
| FastAPI | MIT | |

## Endpoint

```
POST /clasificar
Content-Type: application/json
{ "texto": "..." }

200 OK
{ "tipo_documento": "Factura", "confianza": 0.75 }
```

`GET /health` para comprobación de vida del contenedor.
