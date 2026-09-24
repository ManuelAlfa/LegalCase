import logging
from collections import Counter
from typing import Dict, List

import spacy
from fastapi import FastAPI
from pydantic import BaseModel

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger("doc-classifier")

app = FastAPI(title="Clasificador de tipo de documento (spaCy)")

# es_core_news_sm (no _lg/_trf): aquí solo hace falta tokenizar y lematizar
# texto que IA.2 ya extrajo/OCRizó, para un clasificador ligero por palabras
# clave — no entrenar un modelo propio todavía. es_core_news_lg/es_dep_news_trf
# quedan reservados para IA.3 (extracción de entidades), una tarea distinta
# con necesidades distintas. Se desactivan ner/parser: no se usan aquí y
# acelerar el pipeline importa porque este servicio debe responder en
# milisegundos (a diferencia de ocr-paddle, que sí puede tardar minutos).
_nlp = spacy.load("es_core_news_sm", disable=["ner", "parser"])

# Límite de caracteres analizados: las primeras páginas de un documento ya
# suelen bastar para identificar su tipo, no hace falta lematizar cientos de
# páginas.
_MAX_CHARS = 20_000

# Cada categoría combina lemas de una sola palabra (se comparan contra los
# lemas del texto, así detectan variantes como "resuelve"/"resolviendo") y
# frases (se buscan como subcadena en el texto en minúsculas, porque una
# frase como "base imponible" no sobrevive a lematizar palabra por palabra).
_CATEGORIAS: Dict[str, Dict[str, List[str]]] = {
    "Factura": {
        "lemas": ["factura", "iva", "importe", "cif", "nif", "impuesto"],
        "frases": ["base imponible", "total a pagar", "numero de factura", "fecha de emision"],
    },
    "Contrato": {
        "lemas": ["contrato", "clausula", "contratante", "contratista", "firmante", "vigencia"],
        "frases": ["objeto del contrato", "de una parte", "de otra parte", "en prueba de conformidad"],
    },
    "Escrito judicial": {
        "lemas": ["juzgado", "procurador", "letrado", "demanda", "recurso", "alegacion"],
        "frases": ["al juzgado", "suplico al juzgado", "en su virtud"],
    },
    "Sentencia": {
        "lemas": ["sentencia", "fallo", "magistrado", "condena", "resuelvo"],
        "frases": ["fundamentos de derecho", "administrando justicia", "vistos los articulos"],
    },
    "Poder": {
        "lemas": ["poder", "apoderado", "otorgante", "notario", "escritura"],
        "frases": ["poder general", "escritura publica", "ante mi"],
    },
    "Correspondencia": {
        "lemas": ["estimado", "atentamente", "saludo", "remite", "asunto"],
        "frases": ["reciba un cordial saludo", "quedo a su disposicion"],
    },
}


class ClasificarRequest(BaseModel):
    texto: str


class ClasificarResponse(BaseModel):
    tipo_documento: str
    confianza: float


def _clasificar(texto: str) -> ClasificarResponse:
    fragmento = texto[:_MAX_CHARS]
    texto_lower = fragmento.lower()
    doc = _nlp(fragmento)
    lemas_texto = {token.lemma_.lower() for token in doc if not token.is_stop and not token.is_punct}

    puntuacion: Counter = Counter()
    for categoria, patrones in _CATEGORIAS.items():
        for lema in patrones["lemas"]:
            if lema in lemas_texto:
                puntuacion[categoria] += 1
        for frase in patrones["frases"]:
            if frase in texto_lower:
                # Una frase completa es una señal más fuerte que un lema suelto.
                puntuacion[categoria] += 2

    if not puntuacion:
        return ClasificarResponse(tipo_documento="Sin clasificar", confianza=0.0)

    tipo_ganador, puntos_ganador = puntuacion.most_common(1)[0]
    total = sum(puntuacion.values())
    confianza = round(puntos_ganador / total, 2)
    return ClasificarResponse(tipo_documento=tipo_ganador, confianza=confianza)


@app.post("/clasificar", response_model=ClasificarResponse)
async def clasificar(request: ClasificarRequest) -> ClasificarResponse:
    if not request.texto or not request.texto.strip():
        return ClasificarResponse(tipo_documento="Sin clasificar", confianza=0.0)

    resultado = _clasificar(request.texto)
    logger.info("Clasificado como %s (confianza %.2f)", resultado.tipo_documento, resultado.confianza)
    return resultado


@app.get("/health")
async def health():
    return {"status": "ok"}
