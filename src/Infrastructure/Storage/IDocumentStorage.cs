namespace LegalCaseManagement.Infrastructure.Storage;

// Abstrae el almacenamiento de objetos (SeaweedFS en desarrollo, S3 real en
// producción — mismo protocolo, mismo cliente) para que ni el endpoint de
// subida ni el consumer que descarga el documento dependan de un proveedor
// concreto. Los documentos en sí nunca viven en Postgres (ver
// DocumentoAdjunto.RutaAlmacenamiento).
public interface IDocumentStorage
{
    Task SubirAsync(string claveAlmacenamiento, Stream contenido, string contentType, CancellationToken cancellationToken = default);

    // Devuelve byte[], no Stream: la implementación (S3DocumentStorage) ya
    // tiene que volcar la respuesta entera a memoria por dentro (el stream
    // de S3 se cierra en cuanto se libera GetObjectResponse), así que
    // exponerlo como Stream no da streaming real — solo invita a quien
    // llama a copiarlo OTRA VEZ a su propio buffer (y a menudo una tercera
    // con ToArray()), multiplicando la memoria usada sin necesidad.
    // Confirmado el 2026-09-02: con un documento real de 1.5GB, esas
    // copias redundantes sumaban varios GB de más y contribuyeron a un
    // OOM real del Worker.
    Task<byte[]> DescargarAsync(string claveAlmacenamiento, CancellationToken cancellationToken = default);
}
