-- Segunda capa de aislamiento multi-tenant, a nivel de Postgres.
-- Complementa (no sustituye) el HasQueryFilter de EF Core en AppDbContext:
-- ese filtro protege mientras todo el acceso pase por AppDbContext; esta
-- policy protege incluso si algo no pasa por ahí (SQL crudo, otra app,
-- una consulta a la que se le olvidó el filtro, un bug).
--
-- Cómo aplicarlo (Paso 5 de la guía): después de generar y aplicar la
-- migración ModeloConsolidado (dotnet ef migrations add / database update),
-- aplica este script contra la base de datos de desarrollo:
--   psql -h localhost -U legalcase -d legalcase -f 001_enable_row_level_security.sql
-- o pega su contenido dentro de migrationBuilder.Sql("...") al final del
-- método Up() de esa misma migración.
--
-- Antes de ejecutar, comprueba que estos nombres de tabla/columna coinciden
-- exactamente con lo que generó tu migración (snake_case, Paso 2):
--   \d tenants
--   \d expedientes

ALTER TABLE tenants ENABLE ROW LEVEL SECURITY;
ALTER TABLE expedientes ENABLE ROW LEVEL SECURITY;

-- FORCE es imprescindible: sin él, RLS no se aplica al propietario de la
-- tabla, y en desarrollo la aplicación casi siempre se conecta con el
-- usuario propietario. FORCE hace que la policy se cumpla también para ese
-- rol. En producción, además, la app debería conectarse con un rol NO
-- superusuario y NO propietario de las tablas (ver nota abajo).
ALTER TABLE tenants FORCE ROW LEVEL SECURITY;
ALTER TABLE expedientes FORCE ROW LEVEL SECURITY;

-- La policy compara tenant_id con la variable de sesión app.tenant_id, que
-- TenantSessionInterceptor (Infrastructure/Persistence/TenantSessionInterceptor.cs)
-- fija en cada conexión a partir del claim del JWT autenticado.
-- El segundo argumento `true` de current_setting hace que, si la variable no
-- se ha fijado nunca (conexión fuera de la app, por ejemplo un psql manual),
-- devuelva NULL en lugar de lanzar error — y NULL no hace match con nada,
-- así que el resultado por defecto es "no ver filas", no "verlas todas".
CREATE POLICY tenant_isolation_tenants ON tenants
    USING (id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

CREATE POLICY tenant_isolation_expedientes ON expedientes
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

-- Nota para producción: crear un rol de aplicación dedicado (NOSUPERUSER,
-- sin ser el owner de las tablas) y hacer que la connection string de la Api
-- use ese rol. RLS con FORCE ya cubre el caso del owner, pero un rol propio
-- y sin privilegios de superusuario es defensa adicional y buena práctica
-- de mínimo privilegio, relevante de cara a ISO 27001 / ENS.
