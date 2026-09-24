using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LegalCaseManagement.Infrastructure.Persistence;

// Fija la variable de sesión de Postgres `app.tenant_id` en cada conexión,
// a partir del tenant del claim JWT autenticado (ICurrentTenantProvider).
// Es el complemento necesario de las policies de Row-Level Security
// (Sql/001_enable_row_level_security.sql y 002_extend_row_level_security.sql):
// sin esto, `current_setting('app.tenant_id', true)` siempre da NULL y RLS
// no deja ver ni escribir ninguna fila.
public class TenantSessionInterceptor : DbConnectionInterceptor
{
    private readonly ICurrentTenantProvider _currentTenant;

    public TenantSessionInterceptor(ICurrentTenantProvider currentTenant)
    {
        _currentTenant = currentTenant;
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var command = CreateSetTenantCommand(connection);
        await command.ExecuteScalarAsync(cancellationToken);

        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = CreateSetTenantCommand(connection);
        command.ExecuteScalar();

        base.ConnectionOpened(connection, eventData);
    }

    private DbCommand CreateSetTenantCommand(DbConnection connection)
    {
        var command = connection.CreateCommand();

        // set_config(..., is_local: false) en vez de `SET app.tenant_id = '...'`:
        // SET no admite parámetros de comando, y concatenar el GUID a mano
        // abriría la puerta a SQL injection si TenantId dejara alguna vez de
        // venir garantizado de un claim ya validado del JWT. is_local=false
        // fija la variable a nivel de sesión (no solo de la transacción
        // actual), necesario porque muchas consultas de solo lectura no abren
        // una transacción explícita y con SET LOCAL fuera de una transacción
        // el valor no llegaría a aplicarse.
        command.CommandText = "SELECT set_config('app.tenant_id', @tenantId, false)";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "tenantId";
        parameter.Value = _currentTenant.TenantId.ToString();
        command.Parameters.Add(parameter);

        return command;
    }
}
