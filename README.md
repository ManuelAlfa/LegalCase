222

clk

NOTA: Ejecutar los dos comandos siguientes, la primera vez que se abra esta aplicación  en cada session de trabajo:

```shell
docker compose up -d      # arranca Postgres/RabbitMQ/SeaweedFS/OCR/clasificador
cd src/Api && dotnet run  # arranca la API (no queda corriendo en segundo plano)
```

Las tablas están creadas correctamente pero aún sin datos cargados.

Para hacerlo directamente desde `psql` en el futuro, la instrucción combinada es:

```sql
SET search_path TO public;
\dt
SELECT * FROM tenants;
SELECT * FROM expedientes;
```

# Legal Case Management — scaffold inicial

Primer paso concreto: API en C#/.NET, multi-tenant desde el modelo de datos,
Postgres y RabbitMQ en Docker. Todo pensado para escalar de "despacho
pequeño" a "cliente enterprise" sin rehacer nada (ver decisiones de
arquitectura más abajo).

## Estructura

```
legal-case-management/
  docker-compose.yml        # Postgres + RabbitMQ locales
  src/
    Domain/                 # Entidades puras, sin dependencias (Tenant, Expediente...)
    Infrastructure/         # EF Core + Npgsql, AppDbContext con filtro multi-tenant
    Api/                    # ASP.NET Core Web API (minimal API), Swagger
```

## Requisitos

- Windows con **WSL2** habilitado, con **Docker Engine nativo** instalado
  dentro de la distro (`docker-ce`/`docker-ce-cli`/`containerd.io`/
  `docker-compose-plugin` vía el repo apt oficial de Docker, con `systemd`
  activo en `/etc/wsl.conf`) — **no Docker Desktop**: migrado el 2026-08-24
  precisamente porque depender de Docker Desktop dejaba todo el stack (y la
  sesión de trabajo) a merced de que esa aplicación de Windows se cayera o
  se cerrara. Con el motor nativo, los servicios (`restart: unless-stopped`)
  sobreviven a cerrar VS Code o la terminal — solo se paran si se para el
  propio Docker Engine o se apaga WSL. Nota conocida: en algunas instalaciones
  de WSL2 el motor nativo necesita `iptables-legacy` en vez de `iptables-nft`
  para que la publicación de puertos y la resolución DNS dentro de los
  contenedores funcionen (`sudo update-alternatives --set iptables
  /usr/sbin/iptables-legacy`, ídem para `ip6tables`, y reiniciar el servicio
  `docker`) — si un contenedor no resuelve DNS o no se puede conectar a un
  puerto publicado nada más migrar, es la primera causa a comprobar.
- **.NET 10 SDK** (dentro de WSL2, o en Windows si prefieres correr `dotnet`
  fuera de la distro — pero para consistencia con producción, mejor dentro)
- **VS Code** con las extensiones recomendadas (ver abajo)

## Extensiones de VS Code

Ya están declaradas en `.vscode/extensions.json` — al abrir la carpeta en
VS Code te ofrecerá instalarlas todas de golpe con un clic ("Install
Recommended Extensions"). El detalle de cada una:

**Imprescindibles**

- **Remote - WSL** (`ms-vscode-remote.remote-wsl`): abre el proyecto
  *dentro* del entorno Linux de WSL2 mientras usas VS Code en Windows con
  normalidad. Sin esto, VS Code editaría los archivos desde Windows pero
  compilaría/ejecutaría en un entorno distinto al de Docker, lo que da
  problemas de rutas y de rendimiento.
- **C# Dev Kit** (`ms-dotnettools.csdevkit`): el paquete oficial de
  Microsoft para C#/.NET — IntelliSense, explorador de solución, debugger
  integrado (breakpoints, inspección de variables), y ejecución de tests.
  Instala automáticamente la extensión base de C# como dependencia.
- **Docker** (`ms-azuretools.vscode-docker`): gestionar contenedores,
  ver logs de Postgres/RabbitMQ y levantar/parar el `docker-compose.yml`
  con un clic en vez de recordar comandos.

**Recomendadas, no estrictamente obligatorias**

- **REST Client** (`humao.rest-client`): permite escribir peticiones HTTP
  en un archivo `.http` dentro del propio VS Code y ejecutarlas con un
  clic — útil para probar `/api/expedientes` con distintos `X-Tenant-Id`
  sin salir del editor ni usar `curl` a mano.
- **YAML** (`redhat.vscode-yaml`): autocompletado y validación al editar
  `docker-compose.yml`.
- **GitLens** (`eamodio.gitlens`): historial y autoría por línea, útil
  incluso trabajando solo para recordar por qué se cambió algo.
- **Error Lens** (`usernamehw.errorlens`): muestra errores y warnings del
  compilador en la misma línea del código, en vez de tener que mirar el
  panel de problemas aparte — ahorra bastante tiempo depurando.
- **PostgreSQL Client** (`cweijan.vscode-postgresql-client2`): explorar
  las tablas de Postgres (por ejemplo, comprobar a ojo que `expedientes`
  tiene filas con distinto `tenant_id`) sin salir de VS Code.

Todas son gratuitas.

## Primeros pasos

1. Levantar la infraestructura local:

   ```bash
   cd legal-case-management
   docker compose up -d
   ```

   Esto deja Postgres en `localhost:5432` y el panel de RabbitMQ en
   `http://localhost:15672` (usuario/contraseña: `legalcase` / `legalcase_dev`).
2. Generar el `.sln` y añadir los proyectos (no se incluye en el scaffold
   para no arrastrar un archivo generado a mano):

   ```bash
   dotnet new sln -n LegalCaseManagement
   dotnet sln add src/Domain/Domain.csproj src/Infrastructure/Infrastructure.csproj src/Api/Api.csproj
   ```
3. Restaurar y crear la primera migración de EF Core:

   ```bash
   cd src/Api
   dotnet restore
   dotnet tool install --global dotnet-ef   # si no lo tienes ya
   dotnet ef migrations add InitialCreate --project ../Infrastructure --startup-project .
   dotnet ef database update --project ../Infrastructure --startup-project .
   ```
4. Arrancar la API:

   ```bash
   dotnet run
   ```

   Swagger disponible en `https://localhost:xxxx/swagger` (el puerto lo
   indica la consola al arrancar).

## Probar el aislamiento multi-tenant

Todos los endpoints de `/api/expedientes` filtran automáticamente por el
tenant indicado en el header `X-Tenant-Id` (esto es un placeholder de
desarrollo — en producción el tenant saldrá de un claim del JWT, no de un
header manipulable por el cliente).

```bash
# Crear un expediente para el tenant A
curl -X POST http://localhost:PORT/api/expedientes \
  -H "X-Tenant-Id: 11111111-1111-1111-1111-111111111111" \
  -H "Content-Type: application/json" \
  -d '{"titulo":"Demanda X","cliente":"Acme SL"}'

# Listar expedientes del tenant B (debería salir vacío, aunque el tenant A tenga datos)
curl http://localhost:PORT/api/expedientes \
  -H "X-Tenant-Id: 22222222-2222-2222-2222-222222222222"
```

Si el segundo listado devuelve el expediente del tenant A, el filtro global
de `AppDbContext` no está funcionando — es la primera cosa a comprobar
antes de seguir construyendo encima.

## Decisiones de arquitectura ya tomadas (y por qué)

- **Postgres + Row-Level Security más adelante, no MongoDB**: necesitamos
  garantías ACID fuertes para integridad de expedientes y auditoría.
- **Multi-tenancy híbrida**: de momento todo vive en el mismo Postgres
  (modelo *pooled*, `TenantId` en cada fila + filtro global de EF Core).
  Cuando aparezca un cliente tipo Garrigues, ese tenant se mueve a un
  clúster/instancia dedicada (modelo *siloed*) sin cambiar el modelo de
  datos, porque ya está diseñado con `TenantId` desde el día uno.
  El filtro global de EF Core es una red de seguridad a nivel de
  aplicación; antes de producción hay que añadir Row-Level Security a
  nivel de Postgres como segunda capa (defensa en profundidad).
- **RabbitMQ por defecto, Kafka opcional después**: para desacoplar tareas
  async (notificaciones, generación de documentos). Si un tenant grande
  necesita streaming de eventos de alto volumen o auditoría estricta tipo
  log inmutable, se añade Kafka para ese caso sin tocar el resto — para
  eso conviene meter `MassTransit` como capa de abstracción de mensajería
  en el siguiente paso, en vez de hablar directamente contra la API de
  RabbitMQ.
- **Documentos fuera de la base de datos**: los adjuntos de los
  expedientes irán a almacenamiento de objetos (S3/SeaweedFS), no a Postgres.
  Todavía no implementado en este scaffold.

## Siguiente paso sugerido

Con esto corriendo y el aislamiento comprobado, el siguiente bloque lógico
es autenticación real (JWT con claim de tenant) para sustituir el
`HeaderTenantProvider` de desarrollo, antes de seguir metiendo lógica de
negocio encima de un tenant "de mentira".
