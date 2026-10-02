-- =====================================================================
-- Datos semilla ficticios — legal-case-management
-- Generado: 2026-09-19, a partir del esquema real verificado en src.zip
-- (migración 20260811225120_AddConfianzaOcrFragmentoDocumento, la más
-- reciente aplicada) y de los 6 expedientes ya usados en el mockup
-- mockup_pantallas_compas.html, para que al conectar el Login y el Panel
-- principal reales a la API veas la MISMA demo que ya has revisado en el
-- mockup, no un despacho distinto inventado de cero.
--
-- CÓMO EJECUTARLO
-- Como el rol `legalcase` (propietario, superusuario de bootstrap — el
-- mismo que corre las migraciones), NO como `legalcase_app`: RLS
-- (001/002_..._row_level_security.sql) bloquearía estos INSERT si
-- `app.tenant_id` no está fijado en la sesión, y `legalcase_app` ni
-- siquiera tiene permiso sobre columnas que no están aquí. Como
-- `legalcase` es superusuario, `rolbypassrls` es true y las policies no
-- se aplican (ver el comentario de cabecera de 003_create_app_role.sql).
--
-- Puerto 15432 (no el 5432 estándar): ver el comentario de la sección
-- `ports` en docker-compose.yml.
--
--   psql -h localhost -p 15432 -U legalcase -d legalcase -f seed_datos_ficticios.sql
--
-- Alternativa sin cliente psql instalado en el host, ejecutándolo dentro
-- del propio contenedor:
--
--   docker exec -i legalcase-postgres psql -U legalcase -d legalcase < seed_datos_ficticios.sql
--
-- QUÉ NO INCLUYE (deliberado, fuera del alcance de este primer relleno)
-- - documentos_adjuntos / fragmentos_documento / entidades_extraidas /
--   eventos_cronologia: sembrar la pantalla de OCR/Foliado, Búsqueda,
--   Cronología o Revisión de entidades con datos reales exige también un
--   objeto real en SeaweedFS por cada documento (o al menos una ruta de
--   almacenamiento coherente) y no tiene sentido hacerlo a la vez que el
--   Login/Panel principal, que es lo que pediste primero. Es el siguiente
--   paso natural una vez el Panel principal ya lea de la base de datos
--   real — ver el documento de próximas acciones.
-- - abogado_responsable_id en expedientes: ese campo NO existe todavía en
--   el modelo real (la migración de la ampliación del 2026-08-31 sigue
--   sin aplicarse — ver `plan_maestro_actualizado.docx`). Los usuarios
--   Marta Alonso/Diego Ruiz/Carmen Soto se siembran igualmente para que
--   ya existan en la base de datos el día que esa migración se aplique.
-- - password_hash real: todavía no hay flujo de alta de usuario que haga
--   el hashing (ver el comentario del propio UsuariosEndpoints.cs), así
--   que se deja un valor placeholder que NO es un hash válido de ninguna
--   contraseña real — no sirve para iniciar sesión de verdad hasta que la
--   tarea 3.1 (login real) exista.
-- =====================================================================

BEGIN;

-- ---------------------------------------------------------------------
-- Tenant único: el despacho de la demo ya usado en el mockup ("Herrera &
-- Asociados", visible en la cabecera de usuario del Panel principal).
-- ---------------------------------------------------------------------
-- anio_numeracion y ultimo_numero_expediente dejan el contador correlativo
-- justo detrás del último expediente sembrado (2026/0151), de modo que el
-- primer alta desde la interfaz sea 2026/0152.
INSERT INTO tenants (id, nombre, plan, created_at_utc, anio_numeracion, ultimo_numero_expediente) VALUES
  ('00000000-0000-0000-0000-000000000001', 'Herrera & Asociados', 'pooled', '2026-01-15T09:00:00Z', 2026, 151);

-- ---------------------------------------------------------------------
-- Materias: catálogo mínimo que cubre las 5 áreas de práctica que
-- aparecen en los 6 expedientes de la demo.
-- ---------------------------------------------------------------------
INSERT INTO materias (id, tenant_id, nombre) VALUES
  ('00000000-0000-0000-0000-000000001001', '00000000-0000-0000-0000-000000000001', 'Civil'),
  ('00000000-0000-0000-0000-000000001002', '00000000-0000-0000-0000-000000000001', 'Laboral'),
  ('00000000-0000-0000-0000-000000001003', '00000000-0000-0000-0000-000000000001', 'Familia'),
  ('00000000-0000-0000-0000-000000001004', '00000000-0000-0000-0000-000000000001', 'Mercantil'),
  ('00000000-0000-0000-0000-000000001005', '00000000-0000-0000-0000-000000000001', 'Extranjería');

-- ---------------------------------------------------------------------
-- Usuarios: los 3 abogados ya nombrados como "abogado responsable" en el
-- mockup (Marta Alonso es además quien aparece con sesión iniciada en el
-- Panel principal). rol: Socio=0, Abogado=1, ProcuradorGraduadoSocial=2,
-- Administrativo=3.
-- ---------------------------------------------------------------------
INSERT INTO usuarios (id, tenant_id, nombre, email, password_hash, rol) VALUES
  ('00000000-0000-0000-0000-000000002001', '00000000-0000-0000-0000-000000000001', 'Marta Alonso', 'marta.alonso@herreraasociados.es', 'PENDIENTE_HASH_REAL_NO_USAR_PARA_LOGIN', 0),
  ('00000000-0000-0000-0000-000000002002', '00000000-0000-0000-0000-000000000001', 'Diego Ruiz',   'diego.ruiz@herreraasociados.es',   'PENDIENTE_HASH_REAL_NO_USAR_PARA_LOGIN', 1),
  ('00000000-0000-0000-0000-000000002003', '00000000-0000-0000-0000-000000000001', 'Carmen Soto',  'carmen.soto@herreraasociados.es',  'PENDIENTE_HASH_REAL_NO_USAR_PARA_LOGIN', 1);

-- ---------------------------------------------------------------------
-- Clientes: uno por expediente salvo Beatriz García Serrano, que en el
-- mockup ya tiene dos expedientes distintos (0139 laboral y 0151
-- familia) — se siembra una sola fila de Cliente y se referencia desde
-- los dos expedientes, tal como haría un despacho real.
-- ---------------------------------------------------------------------
-- domicilio y tipo_cliente (D.5): los necesita la gestoría para emitir la
-- factura y decidir retenciones. tipo_cliente: 0 Particular, 1 Empresario o
-- profesional, 2 Entidad.
INSERT INTO clientes (id, tenant_id, nombre, dni_cif, email, telefono, canal_preferido, domicilio, tipo_cliente) VALUES
  ('00000000-0000-0000-0000-000000003001', '00000000-0000-0000-0000-000000000001', 'Antonio Rodríguez Marín',            '22334455C', 'antonio.rodriguezm@correo-ficticio.es', '611223344', 'Email', 'C/ Colón 14, 3.º B, 46004 Valencia', 0),
  ('00000000-0000-0000-0000-000000003002', '00000000-0000-0000-0000-000000000001', 'Beatriz García Serrano',             '33445566D', 'beatriz.garcias@correo-ficticio.es',    '622334455', 'WhatsApp', 'Av. del Puerto 120, 2.º, 46023 Valencia', 0),
  ('00000000-0000-0000-0000-000000003003', '00000000-0000-0000-0000-000000000001', 'José Martín Cobo',                   '44556677E', 'jose.martinc@correo-ficticio.es',        '633445566', 'Teléfono', 'Pol. Ind. Fuente del Jarro, C/ Ciudad de Sevilla 8, 46988 Paterna', 1),
  ('00000000-0000-0000-0000-000000003004', '00000000-0000-0000-0000-000000000001', 'Comunidad de Propietarios Ronda Sur 12', 'H12345678', 'administracion@rondasur12-ficticio.es', '644556677', 'Email', 'C/ Ronda Sur 12, 46013 Valencia', 2),
  ('00000000-0000-0000-0000-000000003005', '00000000-0000-0000-0000-000000000001', 'Marina Hidalgo Torres',              '55667788F', 'marina.hidalgot@correo-ficticio.es',     '655667788', 'WhatsApp', 'C/ Sueca 41, 1.º, 46006 Valencia', 0);

-- ---------------------------------------------------------------------
-- Expedientes: los 6 ya validados visualmente en el mockup, con el mismo
-- número, cliente y materia. estado: Abierto=0, EnTramite=1, Cerrado=2,
-- Archivado=3 — el enum real no tiene un valor propio para "Pendiente de
-- documentación" (0132 en el mockup), así que se mapea a EnTramite(1)
-- como el más cercano; si quieres distinguirlo de verdad haría falta
-- ampliar el enum, no es algo que este script pueda decidir por su
-- cuenta. `titulo` incorpora el número de expediente porque el campo
-- numero_expediente autogenerado (ampliación del 2026-08-31) tampoco
-- existe aún en el modelo real.
-- ---------------------------------------------------------------------
-- El numero ya no vive dentro del titulo: tiene su propia columna, unica
-- dentro del despacho. El titulo queda como asunto del expediente.
-- tarifa_hora (D.5): solo el 2026/0143 se factura por horas, a 85 €/h.
INSERT INTO expedientes (id, tenant_id, numero, titulo, cliente, estado, fecha_apertura, fecha_cierre, cliente_id, materia_id, abogado_responsable_id, ultimo_folio, tarifa_hora) VALUES
  ('00000000-0000-0000-0000-000000005001', '00000000-0000-0000-0000-000000000001', '2026/0143', 'Rodríguez Marín vs. Ayuntamiento de Valencia', 'Antonio Rodríguez Marín',            0, '2026-06-02T09:00:00Z', NULL, '00000000-0000-0000-0000-000000003001', '00000000-0000-0000-0000-000000001001', '00000000-0000-0000-0000-000000002001', 0, 85.00),
  ('00000000-0000-0000-0000-000000005002', '00000000-0000-0000-0000-000000000001', '2026/0139', 'García Serrano (despido)',                     'Beatriz García Serrano',             1, '2026-07-10T09:00:00Z', NULL, '00000000-0000-0000-0000-000000003002', '00000000-0000-0000-0000-000000001002', '00000000-0000-0000-0000-000000002002', 0, NULL),
  ('00000000-0000-0000-0000-000000005003', '00000000-0000-0000-0000-000000000001', '2026/0151', 'García Serrano (custodia y régimen de visitas)', 'Beatriz García Serrano',           0, '2026-09-08T09:00:00Z', NULL, '00000000-0000-0000-0000-000000003002', '00000000-0000-0000-0000-000000001003', '00000000-0000-0000-0000-000000002001', 0, NULL),
  ('00000000-0000-0000-0000-000000005004', '00000000-0000-0000-0000-000000000001', '2026/0132', 'Martín Cobo (reclamación de cantidad)',        'José Martín Cobo',                   1, '2026-05-20T09:00:00Z', NULL, '00000000-0000-0000-0000-000000003003', '00000000-0000-0000-0000-000000001004', '00000000-0000-0000-0000-000000002003', 0, NULL),
  ('00000000-0000-0000-0000-000000005005', '00000000-0000-0000-0000-000000000001', '2026/0128', 'Comunidad Propietarios Ronda Sur 12',           'Comunidad de Propietarios Ronda Sur 12', 0, '2026-03-11T09:00:00Z', NULL, '00000000-0000-0000-0000-000000003004', '00000000-0000-0000-0000-000000001001', '00000000-0000-0000-0000-000000002002', 0, NULL),
  ('00000000-0000-0000-0000-000000005006', '00000000-0000-0000-0000-000000000001', '2026/0091', 'Hidalgo Torres (extranjería)',                  'Marina Hidalgo Torres',              3, '2025-11-04T09:00:00Z', '2026-08-01T09:00:00Z', '00000000-0000-0000-0000-000000003005', '00000000-0000-0000-0000-000000001005', '00000000-0000-0000-0000-000000002003', 0, NULL);

-- ---------------------------------------------------------------------
-- Partes contrarias: una por expediente activo (se omite en 0151, donde
-- el mockup no llegó a definir contraparte, y en 0091 por tratarse de un
-- expediente ya archivado sin necesidad de reflejarla).
-- ---------------------------------------------------------------------
INSERT INTO partes_contrarias (id, tenant_id, expediente_id, nombre, tipo, representante_legal) VALUES
  ('00000000-0000-0000-0000-000000004001', '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000005001', 'Ayuntamiento de Valencia',            'Administración pública', 'Letrado municipal (por determinar)'),
  ('00000000-0000-0000-0000-000000004002', '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000005002', 'Confecciones Llobet S.A.',            'Empresa',                 'Letrado propio de la empresa'),
  ('00000000-0000-0000-0000-000000004003', '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000005004', 'Suministros Metálicos Peña S.L.',     'Empresa',                 'Sin letrado conocido hasta que conteste'),
  ('00000000-0000-0000-0000-000000004004', '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000005005', 'Construcciones Vaquero S.L.',         'Empresa',                 'Letrado propio de la empresa');

-- ---------------------------------------------------------------------
-- Plazos: los 3 ya visibles en la tarjeta "Plazos próximos" del mockup,
-- con fechas relativas a CURRENT_DATE (no a una fecha fija ya pasada)
-- para que sigan siendo "próximos" de verdad el día que se ejecute este
-- script. tipo se deja como texto libre porque así está modelado hoy
-- (no es un catálogo).
-- ---------------------------------------------------------------------
INSERT INTO plazos (id, tenant_id, expediente_id, descripcion, fecha_limite, tipo, dias_aviso_previo) VALUES
  ('00000000-0000-0000-0000-000000006001', '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000005001', 'Contestación a la demanda',                     (CURRENT_DATE + INTERVAL '2 days'),  'Procesal', ARRAY[15,7,2]),
  ('00000000-0000-0000-0000-000000006002', '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000005002', 'Recurso de apelación — plazo de interposición', (CURRENT_DATE + INTERVAL '5 days'),  'Procesal', ARRAY[15,7,2]),
  ('00000000-0000-0000-0000-000000006003', '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000005003', 'Vista oral señalada',                           (CURRENT_DATE + INTERVAL '12 days'), 'Vista',    ARRAY[7,2]);

-- ---------------------------------------------------------------------
-- Provisiones de fondos: coherentes con lo ya documentado para estos
-- mismos expedientes en la pantalla de Facturación del mockup
-- (memoria del proyecto, ampliación del 2026-09-06). 0151 se deja sin
-- provisión a propósito: expediente recién creado, sin modo de
-- facturación definido todavía.
-- ---------------------------------------------------------------------
INSERT INTO provisiones_de_fondos (id, tenant_id, expediente_id, importe, fecha_solicitud, aplicada) VALUES
  ('00000000-0000-0000-0000-000000007001', '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000005001', 500.00, '2026-06-02T09:00:00Z', false),
  ('00000000-0000-0000-0000-000000007002', '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000005002', 300.00, '2026-07-10T09:00:00Z', true),
  ('00000000-0000-0000-0000-000000007003', '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000005004', 600.00, '2026-05-20T09:00:00Z', false),
  ('00000000-0000-0000-0000-000000007004', '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000005006', 450.00, '2025-11-04T09:00:00Z', true);

-- ---------------------------------------------------------------------
-- Facturas: modo TantoAlzado=0, PorHoras=1. 0143 factura por horas
-- (julio y agosto ya emitidas; septiembre queda deliberadamente sin
-- facturar todavía, igual que en el mockup). 0132 se deja sin ninguna
-- factura (coherente con "pendiente de documentación, cero facturas").
-- ---------------------------------------------------------------------
-- Desde D.5 una "factura" es un concepto facturable; estos son conceptos que
-- la gestoría ya facturó (estado 2 = EmitidaPorGestoria), con el número, la
-- fecha y el total que anotó el despacho al recibir su factura. Datos
-- ficticios: Compás no calcula impuestos, el total lo da la gestoría.
INSERT INTO facturas (id, tenant_id, expediente_id, concepto, importe, fecha, modo, estado, numero_factura_gestoria, fecha_emision_gestoria, total_factura_gestoria) VALUES
  ('00000000-0000-0000-0000-000000008001', '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000005001', 'Honorarios por horas — julio 2026 (10 h a 85 €/h)',   850.00,  '2026-08-03T09:00:00Z', 1, 2, 'G-2026/0298', '2026-08-05T09:00:00Z', 1028.50),
  ('00000000-0000-0000-0000-000000008002', '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000005001', 'Honorarios por horas — agosto 2026 (9 h a 85 €/h)',    765.00, '2026-09-03T09:00:00Z', 1, 2, 'G-2026/0371', '2026-09-05T09:00:00Z', 925.65),
  ('00000000-0000-0000-0000-000000008003', '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000005002', 'Honorarios despido improcedente — tanto alzado',       950.00, '2026-07-15T09:00:00Z', 0, 2, 'G-2026/0255', '2026-07-17T09:00:00Z', 1149.50),
  ('00000000-0000-0000-0000-000000008004', '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000005005', 'Honorarios Comunidad de Propietarios Ronda Sur 12',   1200.00, '2026-04-02T09:00:00Z', 0, 2, 'G-2026/0118', '2026-04-06T09:00:00Z', 1452.00),
  ('00000000-0000-0000-0000-000000008005', '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000005006', 'Honorarios expediente extranjería — liquidación final', 450.00, '2026-08-01T09:00:00Z', 0, 2, 'G-2026/0290', '2026-08-03T09:00:00Z', 544.50);

COMMIT;

-- Verificación rápida tras ejecutar:
--   SELECT count(*) FROM expedientes;   -- debe dar 6
--   SELECT numero, titulo FROM expedientes; -- (numero no existe aún, ver nota arriba)
