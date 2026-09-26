# Imagen de las tres aplicaciones .NET (Api, Web y Worker).
#
# Un solo Dockerfile parametrizado en vez de tres casi idénticos: lo único
# que cambia entre ellas es qué proyecto se publica. El ensamblado a
# ejecutar lo indica cada servicio en docker-compose.yml con `command`, no
# este fichero.
#
#   docker build --build-arg PROYECTO=src/Api/Api.csproj -t legalcase-api .

# ---------------------------------------------------------------------
# Compilación
# ---------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROYECTO
WORKDIR /src

# Primero solo los .csproj: mientras no cambien las dependencias, Docker
# reutiliza la capa de restauración y no vuelve a descargar los paquetes
# NuGet en cada compilación. Se copian todos porque los proyectos se
# referencian entre sí y la restauración de cualquiera de ellos los necesita.
COPY src/Domain/Domain.csproj                 src/Domain/
COPY src/Contracts/Contracts.csproj           src/Contracts/
COPY src/Infrastructure/Infrastructure.csproj src/Infrastructure/
COPY src/Api/Api.csproj                       src/Api/
COPY src/Web/Web.csproj                       src/Web/
COPY src/Worker/Worker.csproj                 src/Worker/
RUN dotnet restore "$PROYECTO"

COPY src/ src/
RUN dotnet publish "$PROYECTO" -c Release -o /app/publish --no-restore

# ---------------------------------------------------------------------
# Ejecución
# ---------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Npgsql intenta cargar Kerberos al abrir cada conexión para negociar
# autenticación GSSAPI. La imagen de runtime de Microsoft no trae esa
# librería, así que sin esto la conexión funciona igual (cae en
# autenticación por contraseña) pero escupe "Cannot load library
# libgssapi_krb5.so.2" en CADA apertura de conexión, lo que hace los logs
# inservibles y parece un error grave sin serlo.
RUN apt-get update \
 && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
 && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Usuario sin privilegios que ya trae la imagen de Microsoft (UID 1654).
# Si alguna vez hay que escribir en disco dentro del contenedor, hará falta
# dar permisos a ese usuario sobre el volumen, no volver a root.
USER $APP_UID

# ENTRYPOINT en forma de lista (no de shell) para que el proceso `dotnet`
# sea el PID 1 y reciba directamente el SIGTERM de `docker stop`: así el
# apagado es ordenado en vez de por tiempo de espera agotado.
ENTRYPOINT ["dotnet"]
