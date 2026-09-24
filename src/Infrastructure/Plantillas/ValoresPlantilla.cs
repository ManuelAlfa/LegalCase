using LegalCaseManagement.Domain.Entities;

namespace LegalCaseManagement.Infrastructure.Plantillas;

// Construye el diccionario clave→valor que consume IFusionadorDocumentos a
// partir de las entidades del dominio. Las claves usan notación
// "Entidad.Campo" (p.ej. "Cliente.DniCif") para que coincida literalmente
// con el marcador {{Cliente.DniCif}} escrito en la plantilla .docx.
public static class ValoresPlantilla
{
    public static Dictionary<string, string> Construir(Expediente expediente, Cliente? cliente, Materia? materia)
    {
        var valores = new Dictionary<string, string>
        {
            ["Expediente.Titulo"] = expediente.Titulo,
            ["Expediente.Cliente"] = expediente.Cliente,
            ["Expediente.Estado"] = expediente.Estado.ToString(),
            ["Expediente.FechaApertura"] = expediente.FechaApertura.ToString("dd/MM/yyyy"),
            ["Fecha.Hoy"] = DateTime.UtcNow.ToString("dd/MM/yyyy"),
        };

        if (cliente is not null)
        {
            valores["Cliente.Nombre"] = cliente.Nombre;
            valores["Cliente.DniCif"] = cliente.DniCif;
            valores["Cliente.Email"] = cliente.Email;
            valores["Cliente.Telefono"] = cliente.Telefono;
        }

        if (materia is not null)
        {
            valores["Materia.Nombre"] = materia.Nombre;
        }

        return valores;
    }
}
