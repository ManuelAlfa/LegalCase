using LegalCaseManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalCaseManagement.Infrastructure.Expedientes;

/// <summary>
/// Asigna el número correlativo de expediente de un despacho ("2026/0152").
///
/// El contador vive en la fila del propio despacho (tenants) y se incrementa
/// con un UPDATE ... RETURNING de una sola sentencia. Eso lo hace atómico en
/// Postgres: dos altas simultáneas no pueden obtener el mismo número, porque
/// la segunda espera al bloqueo de fila de la primera. Es el mismo patrón
/// que ya usa el foliado con Expediente.UltimoFolio.
///
/// La alternativa evidente —SELECT MAX(numero) + 1— no sirve: entre el
/// SELECT y el INSERT cabe otra alta, y ambas acabarían con el mismo número.
/// El índice único sobre (tenant_id, numero) lo impediría, pero fallando la
/// operación en vez de resolverla.
/// </summary>
public sealed class NumeradorExpedientes(AppDbContext db)
{
    public async Task<string> SiguienteNumeroAsync(Guid tenantId, int anio, CancellationToken ct = default)
    {
        // La serie se reinicia cada año natural, como en un despacho real:
        // si el año guardado no es el actual, el contador vuelve a 1. Va en
        // la misma sentencia para que la comprobación y el incremento no se
        // puedan intercalar con otra alta.
        var resultados = await db.Database
            .SqlQuery<int>($@"
                UPDATE tenants
                SET ultimo_numero_expediente =
                        CASE WHEN anio_numeracion = {anio} THEN ultimo_numero_expediente + 1 ELSE 1 END,
                    anio_numeracion = {anio}
                WHERE id = {tenantId}
                RETURNING ultimo_numero_expediente")
            .ToListAsync(ct);

        if (resultados.Count == 0)
        {
            // Pasaría si el despacho no existe, o si la Row-Level Security
            // impide ver su fila porque la sesión no lleva el tenant fijado.
            // Mejor fallar aquí que dar de alta un expediente sin número.
            throw new InvalidOperationException(
                $"No se pudo asignar número de expediente: el despacho {tenantId} no es accesible en esta sesión.");
        }

        // UPDATE ... RETURNING no es SQL componible, así que se materializa
        // con ToListAsync() y se toma el único resultado en memoria (EF Core
        // no puede envolverlo en un SELECT externo).
        return $"{anio}/{resultados.Single():D4}";
    }
}
