using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LegalCaseManagement.Domain.Entities;
using LegalCaseManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalCaseManagement.Infrastructure.Auditing;

// Cálculo del encadenado de hashes de AuditLog (estilo blockchain simple):
// cada fila referencia el HashActual de la fila anterior del mismo tenant,
// de forma que alterar o borrar una fila antigua rompe la cadena y es
// detectable.
public class AuditLogHasher
{
    private readonly AppDbContext _db;

    public AuditLogHasher(AppDbContext db)
    {
        _db = db;
    }

    public async Task<string> ComputeHashActualAsync(AuditLog entry, CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters + filtro explícito por TenantId: este cálculo debe
        // funcionar igual desde un contexto sin tenant "ambiente" (p.ej. un
        // futuro consumer de Worker), no solo desde una petición HTTP autenticada.
        var hashAnterior = await _db.AuditLogs
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == entry.TenantId)
            .OrderByDescending(a => a.FechaUtc)
            .Select(a => a.HashActual)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        entry.HashAnterior = hashAnterior;
        return ComputeHash(entry, hashAnterior);
    }

    // Fórmula pura del hash, sin acceso a datos: extraída para que el endpoint
    // de verificación de integridad (AuditLogEndpoints) pueda recorrer una
    // cadena ya persistida y recalcular cada eslabón con la misma fórmula
    // exacta usada al insertar, sin duplicar la lógica de serialización.
    public static string ComputeHash(AuditLog entry, string hashAnterior)
    {
        var payload = string.Join(
            '|',
            entry.TenantId.ToString(),
            entry.UsuarioId.ToString(),
            entry.ExpedienteId.ToString(),
            entry.Accion,
            entry.FechaUtc.ToString("O", CultureInfo.InvariantCulture));

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload + hashAnterior));
        return Convert.ToHexStringLower(hashBytes);
    }
}
