# Legal Case Management

Aplicación de gestión de expedientes para despachos de abogados: API en
C#/.NET 10, interfaz Blazor Server, multi-tenant desde el modelo de datos,
y un pipeline de digitalización de documentos (OCR, foliado, clasificación)
en microservicios Python. Postgres, RabbitMQ y almacenamiento de objetos en
Docker.

- **¿Máquina nueva, recién clonado el repositorio?** → [`INSTALACION.md`](INSTALACION.md)
- **¿Ya lo tienes instalado y solo quieres arrancarlo?** → sigue leyendo.
- Estado funcional del proyecto → [`ESTADO_PROYECTO.md`](ESTADO_PROYECTO.md)
- Dimensionado para un host de producción → [`DESPLIEGUE.md`](DESPLIEGUE.md)

---

## Arrancar la aplicación

La interfaz web **no funciona sola**: es un cliente de la Api, así que hay
que arrancar las dos. Y las dos necesitan la infraestructura levantada. Ese
es el orden y no se puede saltar ningún paso:

```
Docker (Postgres, RabbitMQ, SeaweedFS, OCR)  →  Api (5000)  →  Web (5100)
```

| Qué | URL |
|---|---|
| Interfaz de usuario | http://localhost:5100 |
| Api y Swagger | http://localhost:5000/swagger |

### Paso 0 — antes de arrancar nada

Una sola vez por sesión de trabajo, levantar la infraestructura:

```bash
docker compose up -d
docker compose ps        # los cinco servicios deben estar "Up"
```

Y solo la primera vez en esa máquina, preparar la base de datos: migraciones
de EF Core, los tres scripts de Row-Level Security y (opcional) los datos de
demostración. Está detallado en [`INSTALACION.md`](INSTALACION.md), apartados
3.3 a 3.5. Si te lo saltas, la Api arranca y falla con
`password authentication failed for user "legalcase_app"`, que despista
porque parece un problema de contraseña cuando en realidad es que el rol
todavía no existe.

### Opción A — VS Code

Es la vía recomendada trabajando en WSL2, porque compilas y ejecutas en el
mismo entorno donde corre Docker.

1. Abre la carpeta **dentro de WSL**: extensión *Remote - WSL*, o
   `code .` desde la terminal de la distro. Abajo a la izquierda debe poner
   `WSL: Ubuntu`. Si abres la carpeta como `\\wsl.localhost\...` desde
   Windows, VS Code editará los ficheros pero compilará con el SDK de
   Windows, que no es el entorno de ejecución real.
2. Instala las extensiones recomendadas cuando te lo ofrezca (están en
   `.vscode/extensions.json`). La imprescindible para arrancar es **C# Dev Kit**.
3. Pulsa **F5** y elige la configuración **«Aplicación completa (Api + Web)»**.

Eso compila la solución entera, arranca Api y Web con el entorno correcto y
abre el navegador en http://localhost:5100 cuando el servidor ya está
escuchando de verdad. Los puntos de interrupción funcionan en los dos
proyectos a la vez.

Las configuraciones vienen en `.vscode/launch.json`, que **sí está
versionado** justamente para que F5 funcione recién clonado el repositorio,
sin configurar nada a mano. Hay cuatro:

| Configuración | Para qué |
|---|---|
| **Aplicación completa (Api + Web)** | lo normal: usar la aplicación por la interfaz |
| **Todo, incluido el Worker** | además, procesar documentos (OCR, foliado, clasificación) |
| Api | depurar solo el backend, con Swagger o `curl` |
| Web (interfaz) | depurar solo la interfaz, con la Api ya arrancada por otra vía |

> Si pulsas F5 sin elegir, VS Code usa la primera configuración de la lista
> (solo la Api) y la interfaz no arrancará. Elige el compuesto en el
> desplegable del panel *Run and Debug*.

### Opción B — Visual Studio

Visual Studio corre en Windows, así que aquí hay una decisión previa que
tomar. Estas instrucciones están escritas a partir de la configuración real
del proyecto, pero **no se han podido probar desde esta máquina**, donde
solo hay VS Code: trátalas como la vía documentada, no como verificada.

**Requisitos en el lado Windows:**

- **.NET 10 SDK instalado en Windows** (el de WSL no le sirve a Visual Studio).
- Una versión de Visual Studio que abra ficheros **`.slnx`**, que es el
  formato de solución que usa este proyecto (VS 2022 17.14 o posterior, y
  VS 2026). Si la tuya no lo abre, genera una solución clásica sin tocar la
  existente:

  ```powershell
  dotnet new sln -n LegalCaseManagementVS
  dotnet sln LegalCaseManagementVS.sln add (Get-ChildItem -Recurse src\*.csproj, tests\*.csproj)
  ```

**Dónde poner el código.** La infraestructura (Postgres, RabbitMQ, OCR) vive
en Docker dentro de WSL2 en cualquiera de los dos casos:

- *Abrir el repositorio de WSL desde Windows* (`\\wsl.localhost\Ubuntu\home\...`):
  no duplicas el código, pero la compilación va notablemente más lenta
  porque cada acceso a fichero cruza la frontera entre los dos sistemas.
- *Clonar aparte en una ruta de Windows*: compila rápido, a cambio de
  mantener dos copias del repositorio.

**Configurar el arranque conjunto**, que es el equivalente al compuesto de
VS Code:

1. Clic derecho sobre la solución → **Configurar proyectos de inicio**.
2. **Varios proyectos de inicio**, y en la lista marca `Api` = *Iniciar* y
   `Web` = *Iniciar*. Todo lo demás, *Ninguno*.
3. Súbelos en ese orden, `Api` por encima de `Web`.
4. F5.

Los puertos (5000 y 5100) y el entorno `Development` salen de los ficheros
`Properties/launchSettings.json` de cada proyecto, que Visual Studio respeta
igual que VS Code — no hay que configurarlos en el IDE.

**Si la aplicación arranca pero todas las pantallas salen vacías o con
error de conexión**, es que el proceso de Windows no está alcanzando a
Postgres dentro de WSL. Comprueba que `.wslconfig` tiene
`networkingMode=mirrored`: con ese modo, los puertos publicados por Docker
en WSL se ven desde Windows como `localhost` y todo funciona sin tocar nada.
Sin él tendrías que sustituir `localhost` por la IP de la distro (`wsl
hostname -I`) en las cadenas de conexión, que es bastante más incómodo.

### Opción C — terminal, sin IDE

```bash
./scripts/dev-arrancar.sh
```

Levanta la infraestructura, espera a que Postgres responda, compila, arranca
Api y Web y comprueba que los dos contestan antes de devolverte el control.
Es la vía más reproducible y la que conviene usar para verificar algo "como
lo verá el usuario", sin depurador de por medio.

Dos detalles: el script levanta todo menos `doc-classifier` (solo hace falta
para procesar documentos, no para navegar), así que si vas a usar el Worker
añade `docker compose up -d doc-classifier`. Y como los dos procesos quedan
en segundo plano, su salida va a `/tmp/legalcase-api.log` y
`/tmp/legalcase-web.log` — es el primer sitio donde mirar si algo no
responde.

A mano, el detalle importante es **arrancar cada aplicación desde su
carpeta**, no desde la raíz del repositorio:

```bash
cd src/Api && ASPNETCORE_ENVIRONMENT=Development dotnet run
cd src/Web && ASPNETCORE_ENVIRONMENT=Development dotnet run   # en otra terminal
```

ASP.NET Core resuelve la ruta de contenido al directorio actual. Lanzarlo
desde otro sitio hace que no encuentre `appsettings.json`: la Api falla con
un error claro, pero el Worker se queda callado usando unas credenciales por
defecto equivocadas, que es mucho peor de diagnosticar.

### Comprobar que ha arrancado bien

```bash
curl -s -o /dev/null -w 'Api: %{http_code}\n' http://localhost:5000/swagger/index.html
curl -s -o /dev/null -w 'Web: %{http_code}\n' http://localhost:5100/login
```

Dos veces `200` y listo. En el navegador, http://localhost:5100 debe
mostrar la pantalla de login con los datos ya rellenos; al entrar, el panel
principal con los plazos próximos y la tabla de expedientes.

### Parar la aplicación

```bash
./scripts/dev-arrancar.sh --parar   # para Api y Web por PID
docker compose stop                 # para además la infraestructura
```

Desde un IDE basta con detener la depuración (Mayús+F5), que cierra los dos
procesos a la vez gracias a `stopAll`.

Importante: **no uses `pkill -f dotnet`** para limpiar procesos sueltos. Si
el patrón aparece en el propio comando, `pkill -f` se mata a sí mismo y a la
terminal desde la que lo lanzaste — ya ha pasado en este proyecto. Usa
`--parar`, o busca el PID con `ps aux | grep '[d]otnet'`.

### Qué es solo de desarrollo

Todo lo anterior arranca en entorno `Development`, y eso activa cosas que
**no deben exponerse fuera de tu máquina**:

- El endpoint `POST /api/auth/dev-token` emite un token válido a partir de
  un `tenantId` **sin comprobar ninguna contraseña**. Solo se registra en
  `Development`, pero mientras exista, cualquiera que alcance el puerto 5000
  y conozca un `tenantId` entra como ese despacho.
- Las contraseñas de Postgres, RabbitMQ y SeaweedFS de los `appsettings.json`
  versionados son de desarrollo local, y así consta en sus comentarios.
- `Jwt:SigningKey` es una clave fija y en claro dentro del repositorio.

Por eso los dos procesos escuchan en `localhost` y no en `0.0.0.0`: son
accesibles desde tu equipo, no desde la red. No cambies eso para "probar
desde el móvil" sin entender lo que abres. Para un despliegue real, ver
[`DESPLIEGUE.md`](DESPLIEGUE.md).

---

## Estructura

```
legal-case-management/
  docker-compose.yml        # Postgres, RabbitMQ, SeaweedFS, OCR, clasificador
  scripts/dev-arrancar.sh   # arranque completo desde terminal
  src/
    Domain/                 # Entidades puras, sin dependencias
    Contracts/              # Mensajes compartidos entre Api y Worker
    Infrastructure/         # EF Core + Npgsql, RLS, foliado, almacenamiento
    Api/                    # ASP.NET Core minimal API + Swagger
    Web/                    # Interfaz Blazor Server (MudBlazor)
    Worker/                 # Consumidores MassTransit: OCR, foliado, clasificación
  services/
    ocr-paddle/             # Microservicio Python de OCR (PaddleOCR PP-OCRv6)
    doc-classifier/         # Microservicio Python de clasificación (spaCy)
  tests/
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
- **.NET 10 SDK** dentro de WSL2 (y también en Windows si vas a usar Visual
  Studio).
- **VS Code** con las extensiones recomendadas, o Visual Studio.

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
  clic — útil para probar los endpoints sin salir del editor ni usar
  `curl` a mano.
- **YAML** (`redhat.vscode-yaml`): autocompletado y validación al editar
  `docker-compose.yml`.
- **GitLens** (`eamodio.gitlens`): historial y autoría por línea, útil
  incluso trabajando solo para recordar por qué se cambió algo.
- **Error Lens** (`usernamehw.errorlens`): muestra errores y warnings del
  compilador en la misma línea del código, en vez de tener que mirar el
  panel de problemas aparte — ahorra bastante tiempo depurando.
- **PostgreSQL Client** (`cweijan.vscode-postgresql-client2`): explorar
  las tablas de Postgres sin salir de VS Code (puerto **15432**).

Todas son gratuitas.

## Cómo se resuelve el tenant hoy

El aislamiento entre despachos tiene dos capas:

1. **Filtro global de EF Core** en `AppDbContext`, que protege mientras todo
   el acceso pase por ahí. El tenant sale del claim `tenant_id` del JWT
   (`JwtTenantProvider`), no de una cabecera manipulable por el cliente.
2. **Row-Level Security de Postgres**, que protege aunque algo no pase por
   `AppDbContext`. Para que sirva de algo, la aplicación se conecta con el
   rol `legalcase_app`, que **no** es superusuario: los superusuarios se
   saltan siempre la RLS. Ese rol lo crea el script
   `003_create_app_role.sql`.

Para probar el aislamiento a mano, pide un token con
`POST /api/auth/dev-token` (ver Swagger) y repite la misma llamada con
tokens de dos tenants distintos: cada uno debe ver solo lo suyo. Si un
tenant ve datos del otro, eso es lo primero que hay que arreglar antes de
seguir construyendo encima.

## Decisiones de arquitectura ya tomadas (y por qué)

- **Postgres + Row-Level Security, no MongoDB**: necesitamos garantías ACID
  fuertes para integridad de expedientes y auditoría.
- **Multi-tenancy híbrida**: de momento todo vive en el mismo Postgres
  (modelo *pooled*, `TenantId` en cada fila + filtro global de EF Core).
  Cuando aparezca un cliente muy grande, ese tenant se mueve a un
  clúster/instancia dedicada (modelo *siloed*) sin cambiar el modelo de
  datos, porque ya está diseñado con `TenantId` desde el día uno.
- **RabbitMQ por defecto, Kafka opcional después**: para desacoplar tareas
  asíncronas. Si un tenant grande necesita streaming de alto volumen o
  auditoría tipo log inmutable, se añade Kafka para ese caso sin tocar el
  resto — por eso la mensajería va a través de `MassTransit` y no contra la
  API de RabbitMQ directamente.
- **Documentos fuera de la base de datos**: los adjuntos van a
  almacenamiento de objetos (SeaweedFS, compatible con S3), no a Postgres.
- **Sin librerías AGPL**: nada de PyMuPDF/fitz ni iText7 sin licencia
  comercial, por el riesgo legal en un SaaS. Se usa PDFsharp, pypdf,
  pikepdf y reportlab.
