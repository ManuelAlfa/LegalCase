-- Extiende la Row-Level Security de 001_enable_row_level_security.sql a las
-- tablas del modelo de dominio ampliado (ver fase0_dominio_ux.docx, tareas
-- D.1 y D.2): clientes, partes_contrarias, materias, plazos,
-- documentos_adjuntos, provisiones_de_fondos, facturas y usuarios.
--
-- AJUSTADO tras generar la migración ModeloConsolidado: los nombres reales
-- en Postgres son `provisiones_de_fondos` (no `provisiones_fondos`) y
-- `audit_logss` (no `audit_logs`, en plural como el resto de tablas). Este
-- script se corrigió para coincidir con lo que EF Core generó; no se ha
-- renombrado nada a mano en la base de datos.
--
-- REVISADO (ver plan_maestro_actualizado.docx): se añaden también las tres
-- tablas de la fase de IA documental — fragmentos_documento,
-- entidades_extraidas y eventos_cronologia — para que exista una única
-- migración y un único script de RLS que cubran D.1/D.2 y las tareas IA.1
-- en adelante, en vez de ir añadiendo scripts sueltos cada vez que se amplía
-- el modelo.
--
-- REQUISITO PREVIO IMPORTANTE — leer antes de ejecutar:
-- Este script asume que las columnas de tenant se llaman `tenant_id` (snake_case),
-- igual que ya asume 001_enable_row_level_security.sql para `expedientes`.
-- Por defecto, Entity Framework Core NO convierte a snake_case automáticamente:
-- si en las clases de Domain la propiedad se llama `TenantId`, sin más
-- configuración EF Core generará una columna `"TenantId"` (con mayúsculas,
-- entre comillas), no `tenant_id`. Si tus migraciones ya generaron columnas
-- en PascalCase, este script fallará porque las tablas/columnas en minúscula
-- no existen.
--
-- La forma más simple de evitarlo, y la que se recomienda antes de generar
-- la migración de las tareas D.1/D.2: añadir el paquete NuGet
-- `EFCore.NamingConventions` al proyecto Infrastructure y, en Program.cs,
-- encadenar `.UseSnakeCaseNamingConvention()` a la configuración de
-- `UseNpgsql(...)`. Esto convierte automáticamente TODAS las tablas y
-- columnas (las existentes y las nuevas) a snake_case, sin tocar una por
-- una con HasColumnName. Si ya tienes migraciones aplicadas con nombres en
-- PascalCase, hazlo ANTES de aplicar cualquier migración nueva y genera una
-- migración limpia desde cero en un entorno de desarrollo (no en producción
-- con datos reales, porque renombrar columnas con datos ya cargados exige
-- una migración de datos, no solo de esquema).
--
-- Verifica los nombres reales antes de ejecutar, por si acaso:
--   \d clientes
--   \d plazos
-- (o el comando equivalente de tu cliente de Postgres) y ajusta el script
-- si tus nombres de tabla/columna no coinciden exactamente.

-- ---------------------------------------------------------------------
-- clientes
-- ---------------------------------------------------------------------
ALTER TABLE clientes ENABLE ROW LEVEL SECURITY;
ALTER TABLE clientes FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation_clientes ON clientes
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

-- ---------------------------------------------------------------------
-- partes_contrarias
-- ---------------------------------------------------------------------
ALTER TABLE partes_contrarias ENABLE ROW LEVEL SECURITY;
ALTER TABLE partes_contrarias FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation_partes_contrarias ON partes_contrarias
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

-- ---------------------------------------------------------------------
-- materias
-- Catálogo de materias: también lleva tenant_id (cada despacho puede tener
-- su propio catálogo, aunque el contenido de partida sea el mismo), así que
-- recibe el mismo tratamiento que el resto, no un catálogo global sin RLS.
-- ---------------------------------------------------------------------
ALTER TABLE materias ENABLE ROW LEVEL SECURITY;
ALTER TABLE materias FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation_materias ON materias
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

-- ---------------------------------------------------------------------
-- plazos
-- La tabla más sensible de las nuevas: un plazo filtrado por error hacia el
-- tenant equivocado es, en la práctica, el mismo riesgo que perder el plazo.
-- ---------------------------------------------------------------------
ALTER TABLE plazos ENABLE ROW LEVEL SECURITY;
ALTER TABLE plazos FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation_plazos ON plazos
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

-- ---------------------------------------------------------------------
-- documentos_adjuntos
-- Solo se protegen aquí los metadatos en Postgres. El contenido real de los
-- archivos vive en S3/MinIO (Fase 2 del plan técnico) y su aislamiento por
-- tenant depende de cómo se construyan las claves de almacenamiento y de los
-- permisos del bucket, no de esta policy — no lo olvides al implementar
-- IDocumentStorage.
-- ---------------------------------------------------------------------
ALTER TABLE documentos_adjuntos ENABLE ROW LEVEL SECURITY;
ALTER TABLE documentos_adjuntos FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation_documentos_adjuntos ON documentos_adjuntos
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

-- ---------------------------------------------------------------------
-- provisiones_de_fondos
-- ---------------------------------------------------------------------
ALTER TABLE provisiones_de_fondos ENABLE ROW LEVEL SECURITY;
ALTER TABLE provisiones_de_fondos FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation_provisiones_de_fondos ON provisiones_de_fondos
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

-- ---------------------------------------------------------------------
-- facturas
-- Datos económicos del cliente: junto con plazos, la tabla donde una fuga
-- entre tenants sería más grave de cara a un despacho grande (sección 4 del
-- documento de contexto, ISO 27001/ENS).
-- ---------------------------------------------------------------------
ALTER TABLE facturas ENABLE ROW LEVEL SECURITY;
ALTER TABLE facturas FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation_facturas ON facturas
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

-- ---------------------------------------------------------------------
-- usuarios
-- Un usuario pertenece a un único tenant (no hay usuarios compartidos entre
-- despachos), así que se aísla igual que el resto.
-- ---------------------------------------------------------------------
ALTER TABLE usuarios ENABLE ROW LEVEL SECURITY;
ALTER TABLE usuarios FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation_usuarios ON usuarios
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

-- ---------------------------------------------------------------------
-- fragmentos_documento
-- Trozos de texto extraídos de cada documento (con su embedding de pgvector)
-- que alimentan tanto el chat con citas como la extracción de entidades y la
-- cronología (ver plan_maestro_actualizado.docx, tareas IA.1 en adelante).
-- Es la tabla de la que sale el "documento origen, página, párrafo" que cita
-- el chat — si esto no tuviera RLS, una respuesta del chat podría filtrar
-- texto de un expediente de otro despacho sin que ninguna capa de la
-- aplicación lo detectara.
-- ---------------------------------------------------------------------
ALTER TABLE fragmentos_documento ENABLE ROW LEVEL SECURITY;
ALTER TABLE fragmentos_documento FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation_fragmentos_documento ON fragmentos_documento
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

-- ---------------------------------------------------------------------
-- entidades_extraidas
-- Personas, empresas, fechas e importes que la IA propone a partir de los
-- documentos, pendientes de que el abogado las confirme o descarte.
-- ---------------------------------------------------------------------
ALTER TABLE entidades_extraidas ENABLE ROW LEVEL SECURITY;
ALTER TABLE entidades_extraidas FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation_entidades_extraidas ON entidades_extraidas
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

-- ---------------------------------------------------------------------
-- eventos_cronologia
-- ---------------------------------------------------------------------
ALTER TABLE eventos_cronologia ENABLE ROW LEVEL SECURITY;
ALTER TABLE eventos_cronologia FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation_eventos_cronologia ON eventos_cronologia
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

-- ---------------------------------------------------------------------
-- audit_logs
-- Registro de auditoría con encadenado criptográfico (ver plan_maestro_actualizado.docx,
-- tarea C.0). RLS aquí es tan importante como en cualquier otra tabla: un
-- registro de auditoría íntegro pero visible entre tenants seguiría siendo
-- una fuga de privacidad, aunque nadie pudiera alterarlo.
-- ---------------------------------------------------------------------
ALTER TABLE audit_logs ENABLE ROW LEVEL SECURITY;
ALTER TABLE audit_logs FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation_audit_logs ON audit_logs
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

-- Nota: audit_logs debería además restringir UPDATE/DELETE a nivel de permisos
-- de rol (solo INSERT y SELECT para el rol de aplicación), no solo con RLS —
-- el encadenado de hashes detecta la manipulación, pero es mejor que el rol
-- de aplicación ni siquiera tenga permiso de modificar o borrar filas ya
-- escritas. Añadir esto al mismo rol de aplicación no-superusuario que ya se
-- documentó como pendiente para producción en 001_enable_row_level_security.sql.

-- ---------------------------------------------------------------------
-- Cómo aplicarlo (igual que 001_enable_row_level_security.sql):
--   1) Como parte de la migración de EF Core de las tareas D.1/D.2:
--      pega el contenido de este archivo dentro de un migrationBuilder.Sql("...")
--      al final del método Up() de esa migración, DESPUÉS de que EF Core
--      haya creado las tablas.
--   2) A mano contra la base de datos de desarrollo, una vez aplicada la
--      migración de EF Core:
--      psql -h localhost -U legalcase -d legalcase -f 002_extend_row_level_security.sql
--
-- No hace falta repetir aquí la nota sobre el rol de aplicación no-superusuario
-- ni sobre FORCE con el propietario de la tabla: aplica exactamente igual que
-- se explicó en 001_enable_row_level_security.sql.
