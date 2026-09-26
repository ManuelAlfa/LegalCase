namespace LegalCaseManagement.Domain.Entities;

public enum EstadoExpediente
{
    Abierto,
    EnTramite,
    Cerrado,
    Archivado,

    // Añadido al final a propósito: el valor se persiste como entero, así que
    // insertarlo en medio renumeraría los existentes y cambiaría el estado de
    // todos los expedientes ya guardados. Un despacho lo usa de verdad para
    // el expediente que ya está abierto pero espera documentación del
    // cliente, distinto de "en trámite" (donde ya hay actuación procesal).
    PendienteDocumentacion
}

public class Expediente : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    // Número visible del expediente, correlativo por despacho y año
    // ("2026/0152"). Lo asigna el servidor al dar de alta, nunca el cliente,
    // y es único dentro del despacho (índice único sobre TenantId+Numero).
    // Antes vivía incrustado dentro de Titulo, donde no se podía garantizar
    // ni unicidad ni correlatividad, ni mostrarlo en su propia columna.
    public string Numero { get; set; } = string.Empty;

    public string Titulo { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public EstadoExpediente Estado { get; set; } = EstadoExpediente.Abierto;
    public DateTime FechaApertura { get; set; } = DateTime.UtcNow;
    public DateTime? FechaCierre { get; set; }

    // Enlace opcional a la ficha completa del cliente (DNI/CIF, email,
    // teléfono) y a la materia, más allá del nombre libre de arriba —
    // necesario para la generación de documentos por plantilla (ver
    // Infrastructure/Plantillas), que necesita datos fiables del cliente y
    // no solo el nombre escrito a mano en `Cliente`.
    public Guid? ClienteId { get; set; }
    public Guid? MateriaId { get; set; }

    // Abogado del despacho que lleva el expediente. Opcional: un expediente
    // recién abierto puede estar todavía sin asignar.
    public Guid? AbogadoResponsableId { get; set; }

    // Contador atómico para asignar rangos de folio (Bates) a los
    // DocumentoAdjunto del expediente sin colisiones entre documentos
    // procesados en paralelo (ver DocumentoSubidoConsumer): se incrementa
    // con un UPDATE ... RETURNING de una sola sentencia, atómico en Postgres.
    public int UltimoFolio { get; set; } = 0;
}
