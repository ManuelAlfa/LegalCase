"""Pruebas de las piezas de main.py que no necesitan el modelo de OCR.

Protegen correcciones que ya se rompieron una vez en producción:
  - orden de lectura de los renglones (2026-09-28);
  - cajas muy altas que desordenaban las líneas (2026-09-28);
  - detección de maquetación a dos columnas (2026-09-28);
  - enderezado que giraba 90° páginas rectas y doblaba la inclinación de
    las torcidas (2026-09-29);
  - texto vertical del margen colándose entre el texto (2026-09-30).

Usan datos sintéticos, así que no dependen de los PDF de prueba (que no
están en el repositorio) y funcionan en cualquier máquina. Se ejecutan con
la imagen del servicio, que ya trae numpy y OpenCV:

    docker run --rm --entrypoint python \\
        -v "$PWD/services/ocr-paddle":/src \\
        legal-case-management-ocr-paddle:latest /src/pruebas/unitarias.py

Salen con código 1 si falla alguna.
"""
import sys

import cv2
import numpy as np

sys.path.insert(0, "/src")
import main  # noqa: E402

fallos = []


def comprobar(nombre, condicion, detalle=""):
    print(f"  {'OK ' if condicion else 'MAL'}  {nombre}{'' if condicion else '  -> ' + detalle}")
    if not condicion:
        fallos.append(nombre)


def caja(x0, y0, x1, y1):
    return np.array([[x0, y0], [x1, y0], [x1, y1], [x0, y1]], dtype=float)


rng = np.random.default_rng(7)


def barajar(polys, textos):
    orden = rng.permutation(len(textos))
    return [polys[i] for i in orden], [textos[i] for i in orden]


# ---------------------------------------------------------------------------
print("Orden de lectura")

# Una columna, escaneo ligeramente torcido y renglones de distinta longitud:
# con la antigua agrupación en rejilla salían líneas consecutivas cruzadas.
polys, textos = [], []
for n in range(10):
    y = 100 + n * 22 + n * 1.5
    ancho = 700 if n % 3 else 380
    polys.append(caja(60, y, 60 + ancho, y + 18))
    textos.append(f"linea{n:02d}")
esperado = list(textos)
resultado = main._ordenar_por_lectura(*barajar(polys, textos))
comprobar("una columna, torcida y con renglones cortos", resultado == esperado, str(resultado))

# Texto apretado como el de la Gaceta: renglones casi pegados (paso de 42 px
# con cajas de 40), cada uno detectado en dos trozos y con la página algo
# torcida, así que el trozo derecho queda unos píxeles más abajo. Es lo que
# rompía la agrupación en rejilla: dos renglones seguidos caían en el mismo
# grupo y se intercalaban sus trozos.
polys, textos = [], []
for n in range(25):
    y = 100 + n * 42
    polys.append(caja(60, y, 600, y + 40))
    textos.append(f"r{n:02d}a")
    polys.append(caja(620, y + 6, 1300, y + 46))
    textos.append(f"r{n:02d}b")
esperado = list(textos)
resultado = main._ordenar_por_lectura(*barajar(polys, textos))
comprobar("texto apretado, renglones partidos y torcidos", resultado == esperado,
          str([t for t, e in zip(resultado, esperado) if t != e][:6]))

# Dos columnas: se lee entera la izquierda y después la derecha.
polys, textos = [], []
for n in range(6):
    y = 100 + n * 22
    polys.append(caja(60 + (12 if n % 2 else 0), y, 440, y + 18))
    textos.append(f"izq{n}")
    polys.append(caja(520 + (12 if n % 3 else 0), y, 900, y + 18))
    textos.append(f"der{n}")
esperado = [f"izq{n}" for n in range(6)] + [f"der{n}" for n in range(6)]
resultado = main._ordenar_por_lectura(*barajar(polys, textos))
comprobar("dos columnas", resultado == esperado, str(resultado))

# Una caja muy alta (letra de un texto girado) no debe arrastrar a los
# renglones que cruza: seguían saliendo de izquierda a derecha.
polys, textos = [], []
for n in range(6):
    y = 300 + n * 60
    # Longitudes distintas, como en un texto real: si la caja alta arrastra
    # a varios renglones a la misma línea, se reordenan por su centro
    # horizontal y salen cruzados.
    polys.append(caja(300, y, 1400 if n % 2 == 0 else 900, y + 50))
    textos.append(f"linea{n}")
polys.append(caja(200, 330, 265, 533))  # 203 px de alto, como la "O" de "USO OFICIAL"
textos.append("O")
resultado = main._ordenar_por_lectura(polys, textos)
cuerpo = [t for t in resultado if t.startswith("linea")]
comprobar("caja muy alta no desordena los renglones", cuerpo == [f"linea{n}" for n in range(6)], str(resultado))


# ---------------------------------------------------------------------------
print("Texto vertical del margen")

# Cuerpo de 20 renglones, una pila vertical en el margen izquierdo (como las
# letras de "USO OFICIAL") y un número de apartado colgado, aislado, también
# en el margen: la pila se separa, el número NO (es contenido).
polys, textos = [], []
for n in range(20):
    y = 200 + n * 70
    polys.append(caja(300, y, 1400, y + 50))
    textos.append(f"renglon del cuerpo numero {n}")
pila = [("OIAL", caja(185, 950, 223, 1137)), ("0", caja(188, 1170, 217, 1209)),
        ("S", caja(187, 1206, 218, 1239)), ("U", caja(186, 1237, 218, 1275))]
for t, p in pila:
    textos.append(t)
    polys.append(p)
textos.append("1.")
polys.append(caja(225, 480, 262, 520))

cuerpo, pilas = main._separar_texto_vertical_del_margen(polys, textos)
separados = sorted(textos[i] for p in pilas for i in p)
comprobar("la pila del margen se separa", separados == sorted(t for t, _ in pila), str(separados))
comprobar("el número colgado se queda en el cuerpo", textos.index("1.") in cuerpo)
comprobar("ningún renglón del cuerpo se separa",
          all(i in cuerpo for i, t in enumerate(textos) if t.startswith("renglon")))

# Sin renglones suficientes para saber dónde está el cuerpo (una tabla, un
# formulario) no se separa nada.
pocos = polys[:3] + [p for _, p in pila]
cuerpo, pilas = main._separar_texto_vertical_del_margen(pocos, textos[:3] + [t for t, _ in pila])
comprobar("sin referencia de cuerpo no se separa nada", pilas == [])


# ---------------------------------------------------------------------------
print("Enderezado")


def pagina_sintetica():
    """Página blanca con 30 renglones de texto, perfectamente recta."""
    img = np.full((2000, 1400), 255, dtype=np.uint8)
    for n in range(30):
        cv2.putText(img, "Linea de texto de prueba numero %02d del documento" % n,
                    (120, 150 + n * 58), cv2.FONT_HERSHEY_SIMPLEX, 1.1, 0, 2, cv2.LINE_AA)
    return img


def torcer(gris, grados):
    alto, ancho = gris.shape
    m = cv2.getRotationMatrix2D((ancho / 2, alto / 2), grados, 1.0)
    return cv2.warpAffine(gris, m, (ancho, alto), flags=cv2.INTER_CUBIC, borderValue=255)


def inclinacion(gris):
    """Inclinación medida por un método distinto al de main.py: el ángulo
    que deja los renglones más marcados en el perfil horizontal de tinta."""
    _, b = cv2.threshold(gris, 0, 255, cv2.THRESH_BINARY_INV | cv2.THRESH_OTSU)
    b = cv2.resize(b, None, fx=0.5, fy=0.5, interpolation=cv2.INTER_AREA)
    mejor, mejor_var = 0.0, -1.0
    for a in np.arange(-8, 8.01, 0.1):
        alto, ancho = b.shape
        rot = cv2.warpAffine(b, cv2.getRotationMatrix2D((ancho / 2, alto / 2), a, 1.0),
                             (ancho, alto), flags=cv2.INTER_NEAREST)
        v = np.var(rot.sum(axis=1))
        if v > mejor_var:
            mejor, mejor_var = a, v
    return -mejor


def girada_90(gris):
    """True si los renglones han quedado en vertical. Dentro del bloque de
    texto, una página con renglones horizontales tiene muchas filas vacías
    (los huecos entre renglones) y casi ninguna columna vacía; girada, al
    revés."""
    _, b = cv2.threshold(gris, 0, 255, cv2.THRESH_BINARY_INV | cv2.THRESH_OTSU)
    ys, xs = np.nonzero(b)
    bloque = b[ys.min():ys.max() + 1, xs.min():xs.max() + 1] > 0
    filas_vacias = np.mean(~bloque.any(axis=1))
    columnas_vacias = np.mean(~bloque.any(axis=0))
    return columnas_vacias > filas_vacias


recta = pagina_sintetica()
comprobar("el detector de giro de 90° de la prueba funciona",
          not girada_90(recta) and girada_90(np.ascontiguousarray(np.rot90(recta))))
for grados in (0, 1.5, -1.5, 4, -4):
    entrada = torcer(recta, grados) if grados else recta
    salida = cv2.cvtColor(main._deskew_y_limpiar(cv2.cvtColor(entrada, cv2.COLOR_GRAY2BGR)), cv2.COLOR_BGR2GRAY)
    residuo, vuelta = inclinacion(salida), girada_90(salida)
    comprobar(f"página torcida {grados:+.1f}° queda recta",
              abs(residuo) <= 0.4 and not vuelta,
              f"queda a {residuo:+.1f}°{' y GIRADA 90°' if vuelta else ''}")


# ---------------------------------------------------------------------------
print("Posición de los renglones en la página original (capa de texto del PDF)")

# Un renglón conocido en la página original se lleva a la imagen enderezada
# con la misma matriz que usa _enderezar; la función tiene que devolverlo a
# su sitio. Un signo cambiado dejaría la capa de texto desplazada al revés.
ancho, alto = 1400, 2000
original = np.array([[200, 600], [1200, 600], [1200, 650], [200, 650]], dtype=float)
for grados in (0.0, 2.0, -3.5):
    if grados:
        m = cv2.getRotationMatrix2D((ancho // 2, alto // 2), grados, 1.0)
        enderezado = original @ m[:, :2].T + m[:, 2]
    else:
        enderezado = original
    r = main._renglones_en_pagina_original([("texto", enderezado)], grados, ancho, alto)[0]
    esperado = (200 / ancho, 600 / alto, 1200 / ancho, 650 / alto)
    obtenido = (r["x0"], r["y0"], r["x1"], r["y1"])
    comprobar(f"renglón vuelve a su sitio tras enderezar {grados:+.1f}°",
              np.allclose(obtenido, esperado, atol=0.002), f"{obtenido} en vez de {esperado}")

r = main._renglones_en_pagina_original([("x", np.array([[-50, -50], [100, -50], [100, 30], [-50, 30]]))], 0, ancho, alto)[0]
comprobar("coordenadas acotadas a la página", min(r["x0"], r["y0"]) >= 0 and max(r["x1"], r["y1"]) <= 1)


# ---------------------------------------------------------------------------
print("Altura de carácter (resolución adaptativa)")
alto_letra = main._altura_mediana_caracter(recta)
comprobar("mide una altura de letra plausible", 15 <= alto_letra <= 40, f"{alto_letra:.0f}px")
comprobar("página en blanco -> 0", main._altura_mediana_caracter(np.full((500, 400), 255, np.uint8)) == 0)


print()
if fallos:
    print(f"FALLAN {len(fallos)}: {fallos}")
    sys.exit(1)
print("Todas las pruebas pasan.")
