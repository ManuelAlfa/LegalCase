namespace LegalCaseManagement.Infrastructure.Plantillas;

public interface IFusionadorDocumentos
{
    // Sustituye los marcadores {{Marcador}} de la plantilla .docx por los
    // valores dados (buscados por clave exacta, p.ej. "Cliente.Nombre") y
    // devuelve el documento resultante. Los marcadores sin valor conocido se
    // dejan tal cual, para que el hueco sea visible en el documento en vez
    // de desaparecer en silencio.
    byte[] Fusionar(byte[] plantilla, IReadOnlyDictionary<string, string> valores);
}
