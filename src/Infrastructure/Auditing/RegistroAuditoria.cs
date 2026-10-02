using LegalCaseManagement.Domain.Entities;
using LegalCaseManagement.Infrastructure.Persistence;

namespace LegalCaseManagement.Infrastructure.Auditing;

/// <summary>
/// Añade entradas al registro de auditoría con su hash encadenado. Las
/// entradas se añaden al contexto y se guardan con el SaveChanges de la
/// operación que auditan: o se guarda todo, o nada.
///
/// Varias entradas en una misma operación (una exportación a la gestoría toca
/// varios expedientes) se encadenan entre sí EN MEMORIA: si cada una tomara
/// como eslabón anterior el último hash guardado en la base de datos, todas
/// apuntarían al mismo y la cadena quedaría rota. Por lo mismo, cada entrada
/// lleva una fecha estrictamente posterior a la anterior: la verificación de
/// integridad recorre la cadena ordenando por fecha.
/// </summary>
public class RegistroAuditoria(AppDbContext db, AuditLogHasher hasher)
{
    private string? _ultimoHash;
    private DateTime _ultimaFecha = DateTime.MinValue;

    public async Task AnotarAsync(
        Guid tenantId, Guid? usuarioId, Guid expedienteId, string accion, CancellationToken ct = default)
    {
        // Postgres guarda la hora en microsegundos y .NET la tiene en
        // décimas de microsegundo: si el hash se calculara con la hora
        // completa, al releerla de la base de datos ya no coincidiría y la
        // verificación daría la cadena por rota. Se recorta antes de calcular.
        var fecha = HastaMicrosegundos(DateTime.UtcNow);
        if (fecha <= _ultimaFecha)
        {
            fecha = _ultimaFecha.AddTicks(TicksPorMicrosegundo);
        }

        var entrada = new AuditLog
        {
            TenantId = tenantId,
            // Guid.Empty = usuario no identificado (token sin usuario, hasta el
            // login real de la tarea 3.1).
            UsuarioId = usuarioId ?? Guid.Empty,
            ExpedienteId = expedienteId,
            Accion = accion,
            FechaUtc = fecha,
        };

        if (_ultimoHash is null)
        {
            entrada.HashActual = await hasher.ComputeHashActualAsync(entrada, ct);
        }
        else
        {
            entrada.HashAnterior = _ultimoHash;
            entrada.HashActual = AuditLogHasher.ComputeHash(entrada, _ultimoHash);
        }

        _ultimoHash = entrada.HashActual;
        _ultimaFecha = fecha;
        db.AuditLogs.Add(entrada);
    }

    private const long TicksPorMicrosegundo = TimeSpan.TicksPerMillisecond / 1000;

    private static DateTime HastaMicrosegundos(DateTime fecha) =>
        new(fecha.Ticks - fecha.Ticks % TicksPorMicrosegundo, fecha.Kind);
}
