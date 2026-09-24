# Manual práctico: psql con el Postgres de Docker

Esta guía usa como referencia tu propio contenedor (`legalcase-postgres`, definido en [docker-compose.yml](docker-compose.yml)) para que cada comando lo veas aplicado a algo real.

**Tus credenciales** (de `docker-compose.yml`):

| Campo | Valor |
|---|---|
| Usuario | `legalcase` |
| Contraseña | `legalcase_dev` |
| Base de datos | `legalcase` |
| Host (desde WSL) | `localhost` / `127.0.0.1` |
| Puerto | `5432` |

---

## 0. Requisito: el contenedor tiene que estar levantado

```bash
docker compose up -d
docker compose ps        # comprueba que "legalcase-postgres" está "Up"
```

---

## 1. Entrar a psql

Todo pasa por `docker compose exec`, que ejecuta un comando **dentro** del contenedor.

```bash
docker compose exec postgres psql -U legalcase -d legalcase
```

- `-U legalcase` → usuario (⚠️ no es `postgres`, ese rol no existe en este contenedor)
- `-d legalcase` → base de datos a la que te conectas

Verás el prompt cambiar a:
```
legalcase=#
```
Ahí ya estás dentro de psql, no en bash.

**Salir de psql:**
```sql
\q
```

---

## 2. Ejecutar un solo comando sin entrar interactivamente

Útil para scripts, o cuando solo quieres un dato rápido sin quedarte dentro:

```bash
docker compose exec postgres psql -U legalcase -d legalcase -c "SELECT * FROM tenants;"
```

`-c "..."` ejecuta esa sentencia y vuelve directo al prompt de bash.

---

## 3. Explorar la estructura (comandos `\`)

Estos son comandos propios de psql (no SQL), siempre empiezan con `\`.

| Comando | Qué hace |
|---|---|
| `\l` | Lista todas las bases de datos |
| `\dt` | Lista las tablas del esquema actual (`public` por defecto) |
| `\dt+` | Igual, pero con tamaño y descripción |
| `\dt *.*` | Lista tablas de **todos** los esquemas |
| `\dn` | Lista los esquemas |
| `\d nombre_tabla` | Muestra columnas, tipos y claves de una tabla |
| `\du` | Lista los roles/usuarios (verías solo `legalcase`) |
| `\conninfo` | Confirma a qué host/puerto/base/usuario estás conectado ahora mismo |
| `\x` | Activa modo "expandido" (una columna por línea, mejor para filas anchas) |
| `\x auto` | Igual, pero solo se activa si la tabla no cabe en el ancho de la terminal |
| `\q` | Salir |

Ejemplo real con tus tablas actuales (`expedientes`, `tenants`, `__EFMigrationsHistory`):

```sql
\dt public.*
\d expedientes
```

---

## 4. Consultas SQL básicas (SELECT)

Dentro de psql, cualquier cosa que **no** empiece con `\` es SQL normal y corriente, y termina en `;`.

```sql
-- Ver todas las filas
SELECT * FROM expedientes;

-- Ver solo algunas columnas
SELECT titulo, cliente, estado FROM expedientes;

-- Filtrar por tenant (así es como tu API filtra internamente)
SELECT * FROM expedientes WHERE "TenantId" = '11111111-1111-1111-1111-111111111111';

-- Contar filas
SELECT COUNT(*) FROM expedientes;

-- Últimos 10 creados
SELECT * FROM expedientes ORDER BY "FechaApertura" DESC LIMIT 10;
```

⚠️ Ojo con mayúsculas: EF Core creó las columnas como `"TenantId"`, `"FechaApertura"`, etc. (con mayúscula y comillas). En Postgres, si no usas comillas dobles, los nombres se pasan a minúsculas automáticamente y la consulta fallaría (`column tenantid does not exist`). Por eso hay que escribir `"TenantId"` tal cual, entre comillas dobles, cuando el nombre tiene mayúsculas.

---

## 5. Insertar, actualizar, borrar datos de prueba

```sql
-- Insertar un tenant de prueba
INSERT INTO tenants ("Id", "Nombre", "Plan", "CreatedAtUtc")
VALUES ('11111111-1111-1111-1111-111111111111', 'Bufete Demo', 'free', now());

-- Actualizar
UPDATE expedientes SET "Estado" = 1 WHERE "Id" = 'algún-guid';

-- Borrar
DELETE FROM expedientes WHERE "Id" = 'algún-guid';
```

⚠️ No hay confirmación tipo "¿seguro?" en SQL. Un `DELETE FROM tabla;` sin `WHERE` borra **todas** las filas. Revisa siempre el `WHERE` antes de dar enter.

---

## 6. Ver y controlar migraciones de EF Core

```sql
SELECT * FROM "__EFMigrationsHistory";
```

Te dice qué migraciones ya se aplicaron a esta base (normalmente no se toca a mano, es informativo).

---

## 7. Backups y restauración (desde bash, no desde psql)

**Exportar (dump) la base completa:**
```bash
docker compose exec postgres pg_dump -U legalcase -d legalcase > backup.sql
```

**Restaurar desde un dump:**
```bash
cat backup.sql | docker compose exec -T postgres psql -U legalcase -d legalcase
```
(`-T` desactiva el pseudo-terminal de `exec`, necesario cuando le haces `pipe` de un archivo)

---

## 8. Herramientas gráficas (alternativa a la terminal)

Si prefieres explorar visualmente, la extensión **PostgreSQL Client** de VS Code (`cweijan.vscode-postgresql-client2`, ya recomendada en el [README](README.md)) se conecta con estos mismos datos:

| Campo | Valor |
|---|---|
| Host | `127.0.0.1` |
| Port | `5432` |
| Username | `legalcase` |
| Password | `legalcase_dev` |
| Database | `legalcase` |

---

## 9. Resumen rápido (chuleta)

```bash
# Conectarte
docker compose exec postgres psql -U legalcase -d legalcase

# Un comando suelto sin quedarte dentro
docker compose exec postgres psql -U legalcase -d legalcase -c "SELECT * FROM tenants;"
```
```sql
\dt              -- listar tablas
\d expedientes    -- ver estructura de una tabla
\x auto           -- modo legible para filas anchas
SELECT * FROM expedientes;
\q                -- salir
```
