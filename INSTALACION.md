# Del clon a la aplicación funcionando

Guía para dejar el proyecto compilando y operativo en una máquina nueva,
partiendo solo de `git clone`. Al terminar tendrás la interfaz web en
`http://localhost:5100`, la Api en `http://localhost:5000` y el pipeline de
OCR listo para procesar documentos.

Complementa a los documentos que ya existen, no los sustituye:

- `DESPLIEGUE.md` — dimensionado de CPU y memoria del OCR, timeouts
  acoplados, checklist para un host de producción.
- `ESTADO_PROYECTO.md` — qué hay construido y qué no.
- `README.md` — **parcialmente desactualizado** (habla del puerto 5432 y de
  generar el `.sln` a mano); para poner en marcha el proyecto, esta guía es
  la de referencia.

---

## 1. Qué hay que tener instalado

| Herramienta | Para qué | Nota |
|---|---|---|
| **Git** | clonar | — |
| **.NET SDK 10** | compilar y ejecutar Api, Worker y Web | `dotnet --version` debe dar 10.x |
| **Docker Engine** + plugin Compose | Postgres, RabbitMQ, SeaweedFS y los dos microservicios Python | En Windows, **dentro de WSL2 y nativo, no Docker Desktop** (ver apartado 6) |
| **dotnet-ef** | crear el esquema de la base de datos | `dotnet tool install --global dotnet-ef` |

No hace falta instalar Python ni ninguna de sus librerías en la máquina: los
dos microservicios (`services/ocr-paddle` y `services/doc-classifier`) se
construyen y se ejecutan dentro de sus propios contenedores.

Tampoco hace falta un cliente `psql` instalado: todos los comandos de base de
datos de esta guía tienen una alternativa que se ejecuta dentro del
contenedor de Postgres.

---

## 2. Qué trae el repositorio y qué no

El repositorio versiona el código y los ficheros de texto que describen el
proyecto. Todo lo demás se reconstruye o se descarga. Esto es lo que **no**
viene en el clon y de dónde sale cada cosa:

| Qué falta | De dónde sale | ¿Hay que hacer algo? |
|---|---|---|
| Paquetes NuGet | `dotnet restore` (automático al compilar) | No |
| Paquetes Python | `requirements.txt` + `Dockerfile` de cada servicio, al construir las imágenes | No |
| Modelo lingüístico de spaCy (`es_core_news_sm`) | Se descarga al construir la imagen de `doc-classifier` | No, pero necesita conexión |
| Modelos de PaddleOCR (PP-OCRv6) | Se descargan solos la primera vez que se procesa un documento, al volumen `ocr_paddle_models` | No, pero la primera vez tarda |
| Esquema de la base de datos | Migraciones de EF Core, que **sí** están en el repositorio | Sí, paso 3.3 |
| Row-Level Security y rol de aplicación | Scripts SQL versionados en `src/Infrastructure/Persistence/Sql/` | Sí, paso 3.4 — **no se aplican solos** |
| Datos de ejemplo | `seed_datos_ficticios.sql` | Opcional, paso 3.5 |
| PDFs de las pruebas de OCR (~2,3 GB) | No están y no hacen falta para ejecutar la aplicación | Solo si vas a repetir las pruebas de rendimiento |
| Clave de OpenAI (embeddings) | La pones tú por `user-secrets` | Solo si quieres búsqueda semántica (ver apartado 5) |

Las tipografías de la interfaz (`Fraunces`, `Public Sans`) y la del foliado
de PDFs (`DejaVuSans-Bold.ttf`) **sí** están versionadas: no hay que
descargar nada ni depender de Google Fonts en tiempo de ejecución.

---

## 3. Puesta en marcha

### 3.1. Clonar

```bash
git clone https://github.com/ManuelAlfa/LegalCase.git
cd LegalCase
```

### 3.2. Levantar la infraestructura

```bash
docker compose up -d
```

Arranca cinco servicios: `postgres`, `rabbitmq`, `seaweedfs`, `ocr-paddle` y
`doc-classifier`.

**La primera vez tarda bastante** (de varios minutos a media hora larga según
la conexión): tiene que descargar las imágenes base y construir las dos
imágenes Python, y solo `paddlepaddle` pesa unos 193 MB. El `Dockerfile` de
`ocr-paddle` ya guarda la caché de descargas de pip fuera de las capas de la
imagen, así que si la construcción se corta a mitad, al reintentarla no se
vuelve a descargar lo que ya bajó.

Comprobar que todo está arriba:

```bash
docker compose ps
```

### 3.3. Crear el esquema de la base de datos

```bash
cd src/Api
dotnet ef database update --project ../Infrastructure --startup-project .
cd ../..
```

Tiene que ejecutarse desde `src/Api` y con esos dos parámetros: el proyecto
`Infrastructure` por sí solo no referencia `Microsoft.EntityFrameworkCore.Design`
y el comando falla.

Las migraciones se aplican con el rol `legalcase` (propietario de las tablas),
no con el rol de la aplicación — lo resuelve `AppDbContextFactory`, que las
herramientas de EF Core usan automáticamente.

### 3.4. Aplicar Row-Level Security y crear el rol de aplicación

**Este paso no es opcional y no se ejecuta solo.** Sin él la Api no arranca
correctamente: falla con `password authentication failed for user
"legalcase_app"`, porque ese rol todavía no existe.

Van en orden, después de las migraciones (dependen de que las tablas existan):

```bash
docker exec -i legalcase-postgres psql -U legalcase -d legalcase \
  < src/Infrastructure/Persistence/Sql/001_enable_row_level_security.sql
docker exec -i legalcase-postgres psql -U legalcase -d legalcase \
  < src/Infrastructure/Persistence/Sql/002_extend_row_level_security.sql
docker exec -i legalcase-postgres psql -U legalcase -d legalcase \
  < src/Infrastructure/Persistence/Sql/003_create_app_role.sql
```

Con un cliente `psql` instalado en la máquina, el equivalente es
`psql -h localhost -p 15432 -U legalcase -d legalcase -f <fichero>`
(contraseña `legalcase_dev`). Ojo al **puerto 15432**, no el 5432 habitual:
ver el apartado 6.

El tercer script es idempotente y además corrige la contraseña del rol si ya
existía con otra distinta, así que volver a aplicarlo siempre es seguro.

Por qué hacen falta los tres: el aislamiento entre despachos tiene dos capas.
La primera es el filtro de EF Core, que protege mientras todo el acceso pase
por `AppDbContext`. La segunda son estas policies de Postgres, que protegen
aunque algo no pase por ahí. Y para que la segunda capa sirva de algo, la
aplicación tiene que conectarse con un rol que **no** sea superusuario: los
superusuarios se saltan siempre la Row-Level Security. Eso es exactamente lo
que crea el script 003.

### 3.5. Cargar datos de ejemplo (opcional, recomendado)

```bash
docker exec -i legalcase-postgres psql -U legalcase -d legalcase \
  < seed_datos_ficticios.sql
```

Siembra un despacho de demostración («Herrera & Asociados») con 6
expedientes, 5 clientes, 3 abogados, materias, plazos, facturas y provisiones
de fondos. Los plazos se crean con fechas relativas al día en que ejecutas el
script, así que siempre aparecen como «próximos» en el panel.

Sin este paso la aplicación funciona igual, pero todas las pantallas salen
vacías y no hay ningún tenant con el que iniciar sesión.

### 3.6. Compilar y arrancar la aplicación

```bash
./scripts/dev-arrancar.sh
```

El script levanta la infraestructura si hace falta, espera a que Postgres
responda, compila Api y Web, los arranca y comprueba que los dos contestan.
Para pararlos: `./scripts/dev-arrancar.sh --parar`.

Si prefieres hacerlo a mano, ten en cuenta que **hay que lanzar cada
aplicación desde su directorio de salida**, no desde la raíz del repositorio:

```bash
cd src/Api/bin/Debug/net10.0 && ASPNETCORE_ENVIRONMENT=Development dotnet Api.dll
```

ASP.NET Core resuelve la ruta de contenido al directorio actual, así que
lanzarlo desde otro sitio hace que no encuentre `appsettings.json`. La Api
falla con un error claro, pero el Worker se queda en silencio usando unas
credenciales por defecto equivocadas, que es mucho peor de diagnosticar.

El Worker (procesa los documentos en segundo plano) no lo arranca el script,
porque no hace falta para navegar por la interfaz. Cuando lo necesites:

```bash
cd src/Worker/bin/Debug/net10.0 && ASPNETCORE_ENVIRONMENT=Development dotnet Worker.dll
```

### 3.7. Comprobar que funciona

Abre **http://localhost:5100** y pulsa «Iniciar sesión» (los datos vienen
rellenos con el tenant de la demo). Deberías ver el panel principal con los
plazos próximos y los 6 expedientes.

| Qué comprobar | Dónde |
|---|---|
| Interfaz web | http://localhost:5100 |
| Api y Swagger | http://localhost:5000/swagger |
| Panel de RabbitMQ | http://localhost:15672 (`legalcase` / `legalcase_dev`) |
| Objetos de SeaweedFS | http://localhost:8888 |
| Microservicio de OCR | http://localhost:8001/docs |

---

## 4. Puertos y credenciales

Todo lo que sigue son credenciales **de desarrollo local**, y así consta en
los comentarios de los propios ficheros de configuración.

| Servicio | Puerto en el host | Usuario / clave |
|---|---|---|
| Interfaz web (Blazor) | 5100 | — |
| Api | 5000 | — |
| Postgres | **15432** | `legalcase` / `legalcase_dev` (propietario), `legalcase_app` / `legalcase_app_dev` (aplicación) |
| RabbitMQ | 5672, panel en 15672 | `legalcase` / `legalcase_dev` |
| SeaweedFS (S3) | 9000, filer en 8888 | `legalcase` / `legalcase_dev` |
| OCR (PaddleOCR) | 8001 | — |
| Clasificador de documentos | 8002 | — |

El usuario `legalcase` de RabbitMQ se crea de forma declarativa en cada
arranque desde `rabbitmq-definitions.json`, así que en una máquina nueva ya
está. (Las variables `RABBITMQ_DEFAULT_USER/PASS` del `docker-compose.yml`
solo crean `admin`, y solo en la primera inicialización del volumen.)

El bucket de documentos (`documentos-adjuntos`) se crea solo la primera vez
que se sube un archivo; no hay que prepararlo.

**Antes de cualquier despliegue real** hay que sustituir, como mínimo:

- `Jwt:SigningKey` en `src/Api/appsettings.json` — hoy es una clave de
  desarrollo fija y en claro dentro del repositorio.
- Las contraseñas de Postgres, RabbitMQ y SeaweedFS.
- El endpoint `POST /api/auth/dev-token`, que emite un token válido a partir
  de un `tenantId` **sin comprobar ninguna contraseña**. Está registrado solo
  cuando el entorno es `Development`, pero mientras exista, cualquiera que
  conozca un `tenantId` puede entrar como ese despacho. Su sustituto es el
  login real (tarea 3.1 del plan de desarrollo), todavía sin construir.

---

## 5. Qué no queda operativo solo con clonar

- **Embeddings y búsqueda semántica.** Necesitan una clave de pago de OpenAI,
  que no está en el repositorio. Se configura por `user-secrets`, nunca en
  `appsettings.json`:

  ```bash
  cd src/Worker
  dotnet user-secrets set "Embeddings:ApiKey" "sk-..."
  ```

  Ver `AVISO_EMBEDDINGS_DESACTIVADOS.md`. Sin la clave, el resto del pipeline
  (OCR, foliado, clasificación, fragmentación) funciona igual.

- **Las pruebas de rendimiento de OCR.** Los PDFs que se usaron (documentos de
  500 y 928 páginas, escaneos degradados) pesan unos 2,3 GB y no están
  versionados. Los resultados medidos sí están documentados en `DESPLIEGUE.md`
  y en los `.md` de `test-data/resultados/`.

- **Pantallas de Documentos, Cronología, Chat y Revisión de entidades.** No es
  que falte configuración: todavía no están construidas (tareas 6.5 a 6.7).
  El menú lateral solo muestra lo que existe.

---

## 6. Problemas conocidos

**El puerto 5432 está ocupado y Postgres no arranca.**
En esta máquina de desarrollo el 5432 está retenido por algo que no aparece
ni en `ss`, ni en `docker port`, ni en `fuser`, así que Postgres se publica en
el **15432** y las cadenas de conexión apuntan ahí. Si en tu máquina el 5432
está libre y prefieres el puerto estándar, cambia el mapeo en
`docker-compose.yml` y el puerto en estos tres sitios:
`src/Api/appsettings.json`, `src/Worker/appsettings.json` y
`src/Infrastructure/Persistence/AppDbContextFactory.cs`.
La causa real del bloqueo sigue sin identificarse; el cambio de puerto es un
rodeo, no un diagnóstico.

**En WSL2, los contenedores no resuelven DNS o no se puede acceder a un puerto
publicado.**
Suele ser que el motor de Docker está usando `iptables-nft`. Cambiar a
`iptables-legacy` y reiniciar Docker:

```bash
sudo update-alternatives --set iptables /usr/sbin/iptables-legacy
sudo update-alternatives --set ip6tables /usr/sbin/ip6tables-legacy
sudo systemctl restart docker
```

**`dotnet ef` no encuentra el SDK (instalación por snap).**
Con .NET instalado desde snap hay que exportar la ruta del SDK:

```bash
export DOTNET_ROOT=/var/snap/dotnet/common/dotnet
export DOTNET_ROLL_FORWARD=LatestMajor
```

**El primer documento que se procesa tarda muchísimo.**
Normal: PaddleOCR descarga sus modelos la primera vez (unos 6 ficheros por
modelo, varios modelos). Quedan cacheados en el volumen `ocr_paddle_models` y
a partir de ahí el arranque es inmediato.

**El OCR muere sin mensaje al procesar documentos grandes.**
Casi siempre es memoria. En WSL2 conviene fijarla explícitamente en
`.wslconfig` (en esta máquina: 20 GB de memoria, 14 CPUs, 8 GB de swap).
Ver `DESPLIEGUE.md`, apartados 2 y 3, para el dimensionado según el host.

**La Api responde 401 en todo, o la sesión se pierde constantemente.**
Comprueba que aplicaste el script `003_create_app_role.sql` (apartado 3.4) y
que la Api está arrancando desde su directorio de salida (apartado 3.6).

---

## 7. Resumen, todo seguido

Para una máquina que ya tenga .NET 10, Docker y `dotnet-ef`:

```bash
git clone https://github.com/ManuelAlfa/LegalCase.git
cd LegalCase

docker compose up -d                       # la primera vez tarda: construye las imágenes Python

cd src/Api
dotnet ef database update --project ../Infrastructure --startup-project .
cd ../..

for f in 001_enable_row_level_security 002_extend_row_level_security 003_create_app_role; do
  docker exec -i legalcase-postgres psql -U legalcase -d legalcase \
    < "src/Infrastructure/Persistence/Sql/$f.sql"
done

docker exec -i legalcase-postgres psql -U legalcase -d legalcase < seed_datos_ficticios.sql

./scripts/dev-arrancar.sh
```

Y abrir http://localhost:5100.
