# Roles de Postgres y Row-Level Security

Este documento explica los dos roles de Postgres que existen ahora en el
proyecto, por qué hacen falta los dos, y qué rol usar en cada situación.
Es el resultado de un hallazgo concreto: al probar los endpoints nuevos, un
`INSERT` sin `app.tenant_id` fijado **se coló sin que la policy de RLS lo
bloqueara**. La causa: la Api se conectaba con `legalcase`, que resultó ser
superusuario de Postgres — y los superusuarios omiten Row-Level Security
siempre, con o sin `FORCE ROW LEVEL SECURITY`. Esa es la pieza que faltaba
además de `TenantSessionInterceptor`.

## Los dos roles

| | `legalcase` | `legalcase_app` |
|---|---|---|
| Tipo | Superusuario (bootstrap de `POSTGRES_USER` en la imagen de Docker) | Rol de aplicación normal |
| `rolsuper` | `t` | `f` |
| `rolbypassrls` | `t` (implícito por ser superusuario) | `f` |
| Propietario de las tablas | Sí | No |
| Sujeto a Row-Level Security | **No, nunca** (ni con FORCE) | **Sí** |
| Permisos DDL (crear/alterar tablas) | Sí | No |
| Permisos DML (SELECT/INSERT/UPDATE/DELETE) | Sí (implícito) | Sí, explícitos por tabla (ver abajo) |
| Quién lo usa | `dotnet ef` (vía `AppDbContextFactory`), `psql` manual de administración | La Api en tiempo de ejecución (`appsettings.json`) |
| Dónde está definido | Ya existía (`POSTGRES_USER` en `docker-compose.yml`) | `src/Infrastructure/Persistence/Sql/003_create_app_role.sql` |

**La implicación central**: que las policies de `001_enable_row_level_security.sql`
y `002_extend_row_level_security.sql` existan y estén `ENABLE`/`FORCE` **no
significa que estén protegiendo nada** si la conexión que las ejecuta es
superusuaria. RLS solo es una barrera real para roles no-superusuario. Por
eso `legalcase_app` no es opcional ni un detalle de higiene: sin él, todo lo
hecho en RLS es papel mojado.

## Privilegios de `legalcase_app` por tabla

Concedidos en `003_create_app_role.sql`:

| Tablas | Privilegios |
|---|---|
| `tenants`, `expedientes`, `clientes`, `partes_contrarias`, `materias`, `plazos`, `documentos_adjuntos`, `provisiones_de_fondos`, `facturas`, `usuarios`, `fragmentos_documento`, `entidades_extraidas`, `eventos_cronologia` | `SELECT, INSERT, UPDATE, DELETE` |
| `audit_logs` | **Solo `SELECT, INSERT`** — nunca `UPDATE`/`DELETE`, ni siquiera con RLS de por medio. El encadenado de hashes (`AuditLogHasher`) detecta la manipulación, pero es mejor que el rol de aplicación ni siquiera tenga el permiso de intentarlo. |
| `__EFMigrationsHistory` | Ninguno, a propósito. `legalcase_app` no debe poder ver ni tocar el historial de migraciones. |

Además hay `ALTER DEFAULT PRIVILEGES FOR ROLE legalcase ...`, así que **las
tablas que cree una migración futura de EF Core heredan automáticamente**
`SELECT/INSERT/UPDATE/DELETE` para `legalcase_app` sin tener que volver a
tocar este script — salvo que la tabla nueva necesite el mismo tratamiento
especial que `audit_logs` (restringir a solo lectura+inserción), en cuyo caso
hay que añadir un `REVOKE` explícito después de la migración, igual que se
hizo aquí.

## Qué rol usar en cada situación

| Situación | Rol / conexión a usar | Por qué |
|---|---|---|
| La Api corriendo normalmente (`dotnet run`, o desplegada) | `legalcase_app`, vía `appsettings.json` → `ConnectionStrings:Default` | Es el único camino por el que RLS + `TenantSessionInterceptor` protegen de verdad. Nunca cambiar esto a `legalcase` "para probar algo rápido" sin revertirlo. |
| `dotnet ef migrations add` / `dotnet ef database update` | `legalcase`, resuelto automáticamente por `AppDbContextFactory` (`src/Infrastructure/Persistence/AppDbContextFactory.cs`) | Crear/alterar tablas es DDL; `legalcase_app` no tiene esos permisos ni falta que le hagan. La factory ignora `appsettings.json` a propósito — así un cambio futuro en la connection string de la Api nunca rompe las migraciones, y viceversa. |
| Inspeccionar datos de un tenant concreto a mano por `psql` (debug normal) | `legalcase_app`, y fijar la sesión antes de consultar: `SELECT set_config('app.tenant_id', '<guid-del-tenant>', false);` | Reproduce exactamente lo que ve la Api para ese tenant. Si no fijas `app.tenant_id`, con `legalcase_app` verás **0 filas** en todas las tablas con RLS (el `NULLIF(current_setting(...), true)` da `NULL`, que no hace match con nada) — no es un bug, es el fallo seguro por defecto. |
| Inspeccionar/arreglar datos saltándote el aislamiento (admin real, incidentes, migraciones de datos) | `legalcase` (`psql -U legalcase -d legalcase`) | Ve todas las filas de todos los tenants sin restricción, precisamente porque bypassea RLS. Usar con cuidado: es el mismo motivo por el que la Api NUNCA debe conectarse con este rol. |
| Añadir una tabla nueva en una migración | Ninguna acción extra en el script de roles (los default privileges la cubren), **salvo** que la tabla sea sensible como `audit_logs` | Revisa igualmente que `HasQueryFilter` + policy RLS se añadan para la tabla nueva (pasos ya establecidos), y decide si necesita el mismo `REVOKE UPDATE, DELETE` que `audit_logs`. |
| Ejecutar el futuro `Worker` (consumers de MassTransit) contra la base de datos | Debería usar `legalcase_app` también, con su propio `ICurrentTenantProvider` (no `JwtTenantProvider`, que depende de `HttpContext` y no existe en un Worker) | Pendiente de implementar cuando se construyan los consumers; documentado aquí para no repetir el mismo error de conectar como superusuario "porque es más fácil". |
| CI / entorno de tests de integración (`Api.IntegrationTests`, Testcontainers) | `legalcase_app` para las aserciones de comportamiento de la Api; `legalcase` solo si el propio test necesita levantar el esquema (migraciones) | Un test de integración que use `legalcase` para todo no detectaría una regresión de permisos o de policies — perdería exactamente el tipo de bug que motivó este documento. |

## Cómo se aplicó (para reproducirlo o revertirlo)

1. `src/Infrastructure/Persistence/Sql/003_create_app_role.sql` — crea el rol,
   concede privilegios por tabla y configura los default privileges. Aplicado con:
   ```bash
   docker exec -i legalcase-postgres psql -U legalcase -d legalcase < src/Infrastructure/Persistence/Sql/003_create_app_role.sql
   ```
2. `src/Infrastructure/Persistence/TenantSessionInterceptor.cs` — interceptor
   de EF Core que ejecuta `SELECT set_config('app.tenant_id', @tenantId, false)`
   en cada apertura de conexión, usando el `TenantId` del `ICurrentTenantProvider`
   resuelto (scoped) para esa petición.
3. `src/Infrastructure/Persistence/AppDbContextFactory.cs` — factory de diseño
   para que `dotnet ef` conecte como `legalcase`, independiente de la
   connection string de la Api.
4. `src/Api/appsettings.json` — `ConnectionStrings:Default` apunta ahora a
   `legalcase_app`.
5. `src/Api/Program.cs` — registra `TenantSessionInterceptor` como servicio
   *scoped* y lo añade con `.AddInterceptors(...)` en la configuración de
   `AppDbContext`, usando el overload `(sp, options) => ...` para poder
   resolverlo desde el contenedor de DI de la petición en curso.

Verificado con dos tenants distintos (`POST /api/materias` con tenant A,
`GET /api/materias` con tenant B): el tenant B ve una lista vacía —
aislamiento real, no solo el `HasQueryFilter` de aplicación.

## Contraseña de `legalcase_app`

`legalcase_app_dev` — es un secreto de **desarrollo local**, igual que
`legalcase_dev` (contraseña de `legalcase`) y el `SigningKey` de JWT en
`appsettings.json`. Antes de cualquier despliegue real: contraseña fuerte
gestionada fuera de control de versiones (secret manager / variables de
entorno), y considerar rotarla junto con la de `legalcase`.
