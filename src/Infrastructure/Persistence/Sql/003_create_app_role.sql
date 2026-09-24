-- Rol de aplicación dedicado, NO superusuario y NO propietario de las tablas
-- (que siguen perteneciendo a `legalcase`, el rol con el que corren las
-- migraciones de EF Core). Esto es lo que hace que Row-Level Security
-- (001/002) tenga efecto real: los superusuarios omiten RLS SIEMPRE, con o
-- sin FORCE ROW LEVEL SECURITY. Se comprobó exactamente ese fallo al probar
-- los endpoints nuevos: con la Api conectada como `legalcase` (superusuario,
-- porque POSTGRES_USER se convierte en superusuario de bootstrap en la
-- imagen oficial de Postgres), un INSERT sin `app.tenant_id` fijado se coló
-- sin que la policy lo bloqueara — pg_roles.rolbypassrls confirmaba `t`.
--
-- Cómo aplicarlo:
--   psql -h localhost -U legalcase -d legalcase -f 003_create_app_role.sql
-- (con el rol legalcase — hace falta ser superusuario o tener CREATEROLE
-- para poder crear un rol nuevo).

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'legalcase_app') THEN
        CREATE ROLE legalcase_app LOGIN PASSWORD 'legalcase_app_dev'
            NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
    END IF;
END
$$;

-- Fuera del IF NOT EXISTS a propósito: si el rol ya existía (p.ej. un
-- volumen de un entorno anterior) pero con otra contraseña, este script
-- volvía a ser un no-op y la Api fallaba con "password authentication
-- failed" hasta corregirlo a mano contra el contenedor vivo — una
-- corrección que no sobrevive a que el volumen se reinicie. Reaplicar la
-- contraseña en cada ejecución de este script es idempotente y hace que el
-- rol quede siempre consistente con lo que espera appsettings.json.
ALTER ROLE legalcase_app WITH PASSWORD 'legalcase_app_dev';

GRANT CONNECT ON DATABASE legalcase TO legalcase_app;
GRANT USAGE ON SCHEMA public TO legalcase_app;

-- Acceso completo (lo que necesitan los endpoints GET/POST/PATCH ya
-- implementados) a las tablas de dominio con aislamiento por tenant.
GRANT SELECT, INSERT, UPDATE, DELETE ON
    tenants, expedientes, clientes, partes_contrarias, materias, plazos,
    documentos_adjuntos, provisiones_de_fondos, facturas, usuarios,
    fragmentos_documento, entidades_extraidas, eventos_cronologia
TO legalcase_app;

-- audit_logs es la excepción ya documentada en
-- 002_extend_row_level_security.sql: el rol de aplicación solo debe poder
-- insertar y leer, nunca modificar ni borrar filas ya escritas. El
-- encadenado de hashes (AuditLogHasher) detecta la manipulación, pero es
-- mejor que el rol ni siquiera tenga el permiso.
GRANT SELECT, INSERT ON audit_logs TO legalcase_app;

-- Privilegios por defecto para tablas FUTURAS creadas por `legalcase`
-- (próximas migraciones de EF Core): sin esto, cada migración nueva
-- necesitaría un GRANT manual adicional en un script aparte, y se rompería
-- en cuanto la Api intentara usar una tabla nueva ("permission denied").
ALTER DEFAULT PRIVILEGES FOR ROLE legalcase IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO legalcase_app;

-- __EFMigrationsHistory NO se concede a propósito: legalcase_app nunca debe
-- ejecutar ni ver migraciones, eso es tarea exclusiva de `legalcase` (o de
-- AppDbContextFactory, ver Persistence/AppDbContextFactory.cs) vía dotnet-ef.
