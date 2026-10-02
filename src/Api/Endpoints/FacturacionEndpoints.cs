using System.Globalization;
using System.Text;
using LegalCaseManagement.Api.Auth;
using LegalCaseManagement.Domain.Entities;
using LegalCaseManagement.Infrastructure.Auditing;
using LegalCaseManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalCaseManagement.Api.Endpoints;

/// <summary>
/// Lo facturable (D.5 de plan_maestro_actualizado.docx): registro de horas,
/// resumen por expediente, exportación a la gestoría y anotación de la
/// factura que emite la gestoría.
///
/// REGLA QUE NO SE NEGOCIA: Compás no expide facturas. Registra lo facturable
/// y lo exporta; la gestoría del despacho emite la factura oficial con su
/// propio programa. Nada aquí numera facturas, calcula IVA o retenciones, ni
/// genera un documento titulado "factura". Es lo que mantiene la aplicación
/// fuera del reglamento VeriFactu (RD 1007/2023). Decidido el 2026-10-01.
/// </summary>
public static class FacturacionEndpoints
{
    private static readonly CultureInfo Es = new("es-ES");

    public static WebApplication MapFacturacionEndpoints(this WebApplication app)
    {
        // ------------------------------------------------------------------
        // Registro de horas
        // ------------------------------------------------------------------
        app.MapGet("/api/registro-horas", async (Guid? expedienteId, AppDbContext db, CancellationToken ct) =>
                await db.RegistrosHoras
                    .Where(r => expedienteId == null || r.ExpedienteId == expedienteId)
                    .OrderByDescending(r => r.Fecha)
                    .ToListAsync(ct))
            .RequireAuthorization();

        app.MapPost("/api/registro-horas", async (
                RegistroHorasRequest request, AppDbContext db, ICurrentTenantProvider tenant,
                UsuarioActual usuario, CancellationToken ct) =>
            {
                if (usuario.Id is not { } usuarioActual)
                    return SinIdentidad();

                // Un abogado registra solo sus propias horas; Socio y
                // Administrativo pueden registrarlas a nombre de otro.
                var usuarioHoras = request.UsuarioId ?? usuarioActual;
                if (usuarioHoras != usuarioActual && !usuario.GestionaFacturacion)
                    return Prohibido("Solo puedes registrar tus propias horas.");

                var expediente = await db.Expedientes.FirstOrDefaultAsync(e => e.Id == request.ExpedienteId, ct);
                var errores = ValidarHoras(request, expediente);
                if (errores.Count > 0)
                    return Results.ValidationProblem(errores);

                var registro = new RegistroHoras
                {
                    TenantId = tenant.TenantId,
                    ExpedienteId = expediente!.Id,
                    UsuarioId = usuarioHoras,
                    Fecha = FechaUtc(request.Fecha),
                    Horas = request.Horas,
                    // La tarifa del expediente es la propuesta por defecto;
                    // queda copiada en el registro para que un cambio posterior
                    // de tarifa no altere horas ya registradas.
                    TarifaHora = (request.TarifaHora ?? expediente.TarifaHora)!.Value,
                    Descripcion = request.Descripcion?.Trim() ?? string.Empty,
                };
                db.RegistrosHoras.Add(registro);
                await db.SaveChangesAsync(ct);
                return Results.Created($"/api/registro-horas/{registro.Id}", registro);
            })
            .RequireAuthorization();

        app.MapPatch("/api/registro-horas/{id:guid}", async (
                Guid id, RegistroHorasRequest request, AppDbContext db, UsuarioActual usuario, CancellationToken ct) =>
            {
                var registro = await db.RegistrosHoras.FirstOrDefaultAsync(r => r.Id == id, ct);
                if (registro is null)
                    return Results.NotFound();
                if (ComprobarEdicion(registro, usuario) is { } denegado)
                    return denegado;

                var expediente = await db.Expedientes.FirstOrDefaultAsync(e => e.Id == request.ExpedienteId, ct);
                var errores = ValidarHoras(request, expediente, registro.TarifaHora);
                if (errores.Count > 0)
                    return Results.ValidationProblem(errores);

                registro.ExpedienteId = expediente!.Id;
                registro.Fecha = FechaUtc(request.Fecha);
                registro.Horas = request.Horas;
                registro.TarifaHora = request.TarifaHora ?? registro.TarifaHora;
                registro.Descripcion = request.Descripcion?.Trim() ?? string.Empty;
                await db.SaveChangesAsync(ct);
                return Results.Ok(registro);
            })
            .RequireAuthorization();

        app.MapDelete("/api/registro-horas/{id:guid}", async (
                Guid id, AppDbContext db, UsuarioActual usuario, CancellationToken ct) =>
            {
                var registro = await db.RegistrosHoras.FirstOrDefaultAsync(r => r.Id == id, ct);
                if (registro is null)
                    return Results.NotFound();
                if (ComprobarEdicion(registro, usuario) is { } denegado)
                    return denegado;

                db.RegistrosHoras.Remove(registro);
                await db.SaveChangesAsync(ct);
                return Results.NoContent();
            })
            .RequireAuthorization();

        // ------------------------------------------------------------------
        // Resumen por expediente
        // ------------------------------------------------------------------
        app.MapGet("/api/facturacion/resumen", async (
                DateTime? desde, DateTime? hasta, AppDbContext db, CancellationToken ct) =>
            {
                var (ini, fin) = Periodo(desde, hasta);
                var expedientes = await db.Expedientes.OrderByDescending(e => e.FechaApertura).ToListAsync(ct);
                var horas = await db.RegistrosHoras.Where(r => r.Fecha >= ini && r.Fecha < fin).ToListAsync(ct);
                var facturas = await db.Facturas.ToListAsync(ct);
                var provisiones = await db.ProvisionesDeFondos.ToListAsync(ct);
                var ultimaExportacion = await db.ExportacionesGestoria
                    .OrderByDescending(x => x.FechaGeneracion)
                    .Select(x => (DateTime?)x.FechaGeneracion)
                    .FirstOrDefaultAsync(ct);

                var filas = expedientes.Select(e =>
                {
                    var horasExp = horas.Where(h => h.ExpedienteId == e.Id).ToList();
                    var horasSinExportar = horasExp.Where(h => h.Estado == EstadoFacturable.SinExportar).ToList();
                    var facturasExp = facturas.Where(f => f.ExpedienteId == e.Id).ToList();
                    var provisionesExp = provisiones.Where(p => p.ExpedienteId == e.Id).ToList();
                    var conceptosPendientes = facturasExp
                        .Where(f => f.Estado == EstadoFactura.PendienteExportar && f.Fecha >= ini && f.Fecha < fin);

                    return new ResumenExpediente(
                        e.Id, e.Numero, e.Titulo, e.Cliente,
                        Modalidad(e, horasExp, facturasExp),
                        e.TarifaHora,
                        provisionesExp.Sum(p => p.Importe),
                        provisionesExp.Count > 0 && provisionesExp.All(p => p.Aplicada),
                        horasSinExportar.Sum(h => h.Horas),
                        Math.Round(horasSinExportar.Sum(h => h.Horas * h.TarifaHora) + conceptosPendientes.Sum(f => f.Importe), 2),
                        facturasExp.Where(f => f.Estado == EstadoFactura.EmitidaPorGestoria)
                            .Sum(f => f.TotalFacturaGestoria ?? f.Importe));
                }).ToList();

                var hace30Dias = DateTime.UtcNow.AddDays(-30);
                return Results.Ok(new ResumenFacturacion(
                    filas,
                    filas.Sum(f => f.PendienteExportar),
                    provisiones.Where(p => !p.Aplicada).Sum(p => p.Importe),
                    provisiones.Count(p => !p.Aplicada),
                    facturas.Where(f => f.Estado == EstadoFactura.EmitidaPorGestoria
                                        && (f.FechaEmisionGestoria ?? f.Fecha) >= hace30Dias)
                        .Sum(f => f.TotalFacturaGestoria ?? f.Importe),
                    ultimaExportacion));
            })
            .RequireAuthorization();

        // ------------------------------------------------------------------
        // Exportación a la gestoría
        // ------------------------------------------------------------------
        app.MapGet("/api/facturacion/exportaciones", async (AppDbContext db, CancellationToken ct) =>
                await db.ExportacionesGestoria.OrderByDescending(x => x.FechaGeneracion).ToListAsync(ct))
            .RequireAuthorization();

        app.MapPost("/api/facturacion/exportaciones", async (
                ExportacionRequest request, AppDbContext db, ICurrentTenantProvider tenant,
                UsuarioActual usuario, RegistroAuditoria auditoria, CancellationToken ct) =>
            {
                var (ini, fin) = Periodo(request.Desde, request.Hasta);
                if (fin <= ini)
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                        { ["hasta"] = ["La fecha final no puede ser anterior a la inicial."] });

                // Solo lo que todavía no ha salido en ninguna exportación: así
                // repetir la exportación no duplica nada en la gestoría.
                var horas = await db.RegistrosHoras
                    .Where(r => r.Estado == EstadoFacturable.SinExportar && r.Fecha >= ini && r.Fecha < fin)
                    .ToListAsync(ct);
                var conceptos = await db.Facturas
                    .Where(f => f.Estado == EstadoFactura.PendienteExportar && f.Fecha >= ini && f.Fecha < fin)
                    .ToListAsync(ct);
                var provisiones = await db.ProvisionesDeFondos
                    .Where(p => p.ExportacionGestoriaId == null && p.FechaSolicitud >= ini && p.FechaSolicitud < fin)
                    .ToListAsync(ct);

                if (horas.Count == 0 && conceptos.Count == 0 && provisiones.Count == 0)
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                        { ["periodo"] = ["No hay nada pendiente de exportar en ese periodo."] });

                var exportacion = new ExportacionGestoria
                {
                    TenantId = tenant.TenantId,
                    PeriodoDesde = ini,
                    PeriodoHasta = fin.AddDays(-1),
                    UsuarioId = usuario.Id,
                };
                db.ExportacionesGestoria.Add(exportacion);

                // Las horas se agrupan en un concepto por expediente y tarifa:
                // "Honorarios por horas — septiembre 2026 (12,5 h a 85 €/h)". Es
                // lo que la gestoría factura, y deja las horas enlazadas a él
                // para marcarlas como facturadas cuando se anote su factura.
                foreach (var grupo in horas.GroupBy(h => (h.ExpedienteId, h.TarifaHora)))
                {
                    var total = grupo.Sum(h => h.Horas);
                    var concepto = new Factura
                    {
                        TenantId = tenant.TenantId,
                        ExpedienteId = grupo.Key.ExpedienteId,
                        Concepto = $"Honorarios por horas — {EtiquetaPeriodo(ini, fin)} " +
                                   $"({Numero(total)} h a {Numero(grupo.Key.TarifaHora)} €/h)",
                        Importe = Math.Round(total * grupo.Key.TarifaHora, 2),
                        Fecha = fin.AddDays(-1),
                        Modo = ModoFactura.PorHoras,
                    };
                    db.Facturas.Add(concepto);
                    conceptos.Add(concepto);
                    foreach (var h in grupo)
                    {
                        h.FacturaId = concepto.Id;
                        h.Estado = EstadoFacturable.Exportado;
                        h.ExportacionGestoriaId = exportacion.Id;
                    }
                }

                foreach (var c in conceptos)
                {
                    c.Estado = EstadoFactura.Exportada;
                    c.ExportacionGestoriaId = exportacion.Id;
                }
                foreach (var p in provisiones)
                    p.ExportacionGestoriaId = exportacion.Id;

                exportacion.NumeroLineas = conceptos.Count + provisiones.Count;

                foreach (var expedienteId in conceptos.Select(c => c.ExpedienteId)
                             .Concat(provisiones.Select(p => p.ExpedienteId)).Distinct())
                {
                    await auditoria.AnotarAsync(tenant.TenantId, usuario.Id, expedienteId,
                        $"Exportación a la gestoría {exportacion.Id} ({EtiquetaPeriodo(ini, fin)})", ct);
                }

                await db.SaveChangesAsync(ct);

                var csv = await GenerarCsvAsync(db, conceptos, horas, provisiones, ct);
                return Results.File(csv, "text/csv; charset=utf-8", NombreFichero(exportacion));
            })
            .RequireAuthorization(Politicas.GestionFacturacion);

        // Reexportación: el mismo CSV de una exportación ya hecha, por si se
        // perdió el fichero. No cambia ningún estado.
        app.MapGet("/api/facturacion/exportaciones/{id:guid}/csv", async (
                Guid id, AppDbContext db, CancellationToken ct) =>
            {
                var exportacion = await db.ExportacionesGestoria.FirstOrDefaultAsync(x => x.Id == id, ct);
                if (exportacion is null)
                    return Results.NotFound();

                var conceptos = await db.Facturas.Where(f => f.ExportacionGestoriaId == id).ToListAsync(ct);
                var horas = await db.RegistrosHoras.Where(r => r.ExportacionGestoriaId == id).ToListAsync(ct);
                var provisiones = await db.ProvisionesDeFondos.Where(p => p.ExportacionGestoriaId == id).ToListAsync(ct);
                var csv = await GenerarCsvAsync(db, conceptos, horas, provisiones, ct);
                return Results.File(csv, "text/csv; charset=utf-8", NombreFichero(exportacion));
            })
            .RequireAuthorization(Politicas.GestionFacturacion);

        // ------------------------------------------------------------------
        // Anotar la factura que emitió la gestoría
        // ------------------------------------------------------------------
        app.MapPatch("/api/facturas/{id:guid}/emision", async (
                Guid id, EmisionRequest request, AppDbContext db, ICurrentTenantProvider tenant,
                UsuarioActual usuario, RegistroAuditoria auditoria, CancellationToken ct) =>
            {
                var concepto = await db.Facturas.FirstOrDefaultAsync(f => f.Id == id, ct);
                if (concepto is null)
                    return Results.NotFound();

                var errores = new Dictionary<string, string[]>();
                if (concepto.Estado == EstadoFactura.PendienteExportar)
                    errores["estado"] = ["Este concepto todavía no se ha exportado a la gestoría."];
                if (string.IsNullOrWhiteSpace(request.Numero))
                    errores["numero"] = ["Indica el número que la gestoría dio a la factura."];
                if (request.Total < 0)
                    errores["total"] = ["El total no puede ser negativo."];
                if (errores.Count > 0)
                    return Results.ValidationProblem(errores);

                concepto.Estado = EstadoFactura.EmitidaPorGestoria;
                concepto.NumeroFacturaGestoria = request.Numero.Trim();
                concepto.FechaEmisionGestoria = FechaUtc(request.Fecha);
                concepto.TotalFacturaGestoria = request.Total;

                var horas = await db.RegistrosHoras.Where(r => r.FacturaId == id).ToListAsync(ct);
                foreach (var h in horas)
                    h.Estado = EstadoFacturable.Facturado;

                await auditoria.AnotarAsync(tenant.TenantId, usuario.Id, concepto.ExpedienteId,
                    $"Factura de la gestoría {concepto.NumeroFacturaGestoria} anotada en el concepto {concepto.Id}", ct);
                await db.SaveChangesAsync(ct);
                return Results.Ok(concepto);
            })
            .RequireAuthorization(Politicas.GestionFacturacion);

        return app;
    }

    // ----------------------------------------------------------------------

    private static IResult SinIdentidad() => Results.Problem(
        "La sesión no identifica al usuario, y las horas tienen que quedar a nombre de alguien.",
        statusCode: StatusCodes.Status403Forbidden);

    private static IResult Prohibido(string motivo) =>
        Results.Problem(motivo, statusCode: StatusCodes.Status403Forbidden);

    /// <summary>
    /// Un abogado solo modifica sus propias horas; nadie modifica horas que ya
    /// salieron hacia la gestoría (cambiarlas descuadraría lo que se le envió).
    /// </summary>
    private static IResult? ComprobarEdicion(RegistroHoras registro, UsuarioActual usuario)
    {
        if (usuario.Id is null)
            return SinIdentidad();
        if (registro.UsuarioId != usuario.Id && !usuario.GestionaFacturacion)
            return Prohibido("Solo puedes modificar tus propias horas.");
        if (registro.Estado != EstadoFacturable.SinExportar)
            return Prohibido("Estas horas ya se exportaron a la gestoría y no se pueden modificar.");
        return null;
    }

    private static Dictionary<string, string[]> ValidarHoras(
        RegistroHorasRequest request, Expediente? expediente, decimal? tarifaActual = null)
    {
        var errores = new Dictionary<string, string[]>();
        if (expediente is null)
            errores["expedienteId"] = ["El expediente no existe en este despacho."];
        if (request.Horas <= 0 || request.Horas > 24)
            errores["horas"] = ["Las horas tienen que estar entre 0 y 24."];
        if ((request.TarifaHora ?? tarifaActual ?? expediente?.TarifaHora) is not { } tarifa || tarifa <= 0)
            errores["tarifaHora"] = ["Indica la tarifa por hora: el expediente no tiene una por defecto."];
        return errores;
    }

    /// <summary>
    /// Periodo [desde, hasta] por días completos, como intervalo semiabierto
    /// [ini, fin) en UTC. Sin fechas, todo.
    /// </summary>
    private static (DateTime ini, DateTime fin) Periodo(DateTime? desde, DateTime? hasta) =>
        (desde is { } d ? FechaUtc(d) : SinLimiteInicial,
         hasta is { } h ? FechaUtc(h).AddDays(1) : SinLimiteFinal);

    // Límites explícitos en UTC en vez de DateTime.MinValue/MaxValue: los
    // extremos de DateTime se desbordan al convertirlos de zona horaria y
    // llegarían a Postgres sin marcar como UTC.
    private static readonly DateTime SinLimiteInicial = new(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SinLimiteFinal = new(2200, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // Del formulario llegan solo fechas, sin hora ni zona; Postgres guarda
    // timestamptz y Npgsql exige UTC.
    private static DateTime FechaUtc(DateTime fecha) => DateTime.SpecifyKind(fecha.Date, DateTimeKind.Utc);

    private static string Modalidad(Expediente e, List<RegistroHoras> horas, List<Factura> facturas)
    {
        if (e.TarifaHora is not null || horas.Count > 0)
            return nameof(ModoFactura.PorHoras);
        var ultima = facturas.OrderByDescending(f => f.Fecha).FirstOrDefault();
        return ultima is null ? "SinDefinir" : ultima.Modo.ToString();
    }

    private static string EtiquetaPeriodo(DateTime ini, DateTime fin)
    {
        var ultimoDia = fin.AddDays(-1);
        if (ini.Year == ultimoDia.Year && ini.Month == ultimoDia.Month && ini.Day == 1
            && ultimoDia.AddDays(1).Month != ultimoDia.Month)
            return ini.ToString("MMMM yyyy", Es);
        return $"{ini.ToString("dd/MM/yyyy", Es)}–{ultimoDia.ToString("dd/MM/yyyy", Es)}";
    }

    // Formato español sin separador de miles, para que la hoja de cálculo de
    // la gestoría lo lea como número.
    private static string Numero(decimal valor) => valor.ToString("0.##", Es);
    private static string Importe(decimal valor) => valor.ToString("0.00", Es);

    private static string NombreFichero(ExportacionGestoria x) =>
        $"datos_facturacion_{x.PeriodoDesde:yyyyMMdd}_{x.PeriodoHasta:yyyyMMdd}.csv";

    /// <summary>
    /// CSV para la gestoría: UTF-8 con BOM (Excel lo abre con tildes) y punto y
    /// coma (separador que espera Excel en España). La primera línea deja claro
    /// que NO es una factura.
    /// </summary>
    private static async Task<byte[]> GenerarCsvAsync(
        AppDbContext db, List<Factura> conceptos, List<RegistroHoras> horas,
        List<ProvisionDeFondos> provisiones, CancellationToken ct)
    {
        var expedienteIds = conceptos.Select(c => c.ExpedienteId).Concat(provisiones.Select(p => p.ExpedienteId)).Distinct().ToList();
        var expedientes = await db.Expedientes.Where(e => expedienteIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        var clienteIds = expedientes.Values.Where(e => e.ClienteId != null).Select(e => e.ClienteId!.Value).Distinct().ToList();
        var clientes = await db.Clientes.Where(c => clienteIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);

        var sb = new StringBuilder();
        sb.Append("Datos para facturación — no es una factura\r\n");
        sb.Append(Linea("Fecha", "Expediente", "Cliente", "NIF", "Domicilio", "Tipo de cliente",
            "Concepto", "Base sin impuestos", "Horas", "Tarifa"));

        foreach (var c in conceptos.OrderBy(c => c.Fecha))
        {
            var (exp, cli) = Datos(c.ExpedienteId);
            var horasConcepto = horas.Where(h => h.FacturaId == c.Id).ToList();
            sb.Append(Linea(
                c.Fecha.ToString("dd/MM/yyyy", Es), exp?.Numero, cli?.Nombre ?? exp?.Cliente, cli?.DniCif,
                cli?.Domicilio, cli is null ? null : TextoTipoCliente(cli.TipoCliente),
                c.Concepto, Importe(c.Importe),
                horasConcepto.Count > 0 ? Numero(horasConcepto.Sum(h => h.Horas)) : null,
                horasConcepto.Count > 0 ? Importe(horasConcepto[0].TarifaHora) : null));
        }

        foreach (var p in provisiones.OrderBy(p => p.FechaSolicitud))
        {
            var (exp, cli) = Datos(p.ExpedienteId);
            sb.Append(Linea(
                p.FechaSolicitud.ToString("dd/MM/yyyy", Es), exp?.Numero, cli?.Nombre ?? exp?.Cliente, cli?.DniCif,
                cli?.Domicilio, cli is null ? null : TextoTipoCliente(cli.TipoCliente),
                "Provisión de fondos", Importe(p.Importe), null, null));
        }

        var preambulo = Encoding.UTF8.GetPreamble();
        var cuerpo = Encoding.UTF8.GetBytes(sb.ToString());
        return [.. preambulo, .. cuerpo];

        (Expediente?, Cliente?) Datos(Guid expedienteId)
        {
            expedientes.TryGetValue(expedienteId, out var exp);
            Cliente? cli = null;
            if (exp?.ClienteId is { } cid)
                clientes.TryGetValue(cid, out cli);
            return (exp, cli);
        }
    }

    private static string Linea(params string?[] campos) =>
        string.Join(';', campos.Select(Escapar)) + "\r\n";

    private static string Escapar(string? campo)
    {
        if (string.IsNullOrEmpty(campo))
            return string.Empty;
        return campo.IndexOfAny([';', '"', '\r', '\n']) >= 0
            ? "\"" + campo.Replace("\"", "\"\"") + "\""
            : campo;
    }

    private static string TextoTipoCliente(TipoCliente tipo) => tipo switch
    {
        TipoCliente.EmpresarioOProfesional => "Empresario o profesional",
        TipoCliente.Entidad => "Entidad",
        _ => "Particular",
    };
}

public record RegistroHorasRequest(
    Guid ExpedienteId, DateTime Fecha, decimal Horas, decimal? TarifaHora = null,
    string? Descripcion = null, Guid? UsuarioId = null);

public record ExportacionRequest(DateTime? Desde, DateTime? Hasta);

public record EmisionRequest(string Numero, DateTime Fecha, decimal Total);

public record ResumenFacturacion(
    IReadOnlyList<ResumenExpediente> Expedientes,
    decimal PendienteExportar,
    decimal ProvisionesSinAplicar,
    int NumeroProvisionesSinAplicar,
    decimal EmitidoUltimos30Dias,
    DateTime? UltimaExportacion);

public record ResumenExpediente(
    Guid ExpedienteId, string Numero, string Titulo, string Cliente,
    string Modalidad, decimal? TarifaHora,
    decimal Provision, bool ProvisionAplicada,
    decimal HorasSinExportar, decimal PendienteExportar, decimal EmitidoGestoria);
