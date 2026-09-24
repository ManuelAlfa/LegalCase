using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Microsoft.Extensions.Options;

namespace LegalCaseManagement.Infrastructure.Storage;

public class S3DocumentStorage : IDocumentStorage
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucketName;

    public S3DocumentStorage(IAmazonS3 s3, IOptions<DocumentStorageOptions> options)
    {
        _s3 = s3;
        _bucketName = options.Value.BucketName;
    }

    public async Task SubirAsync(string claveAlmacenamiento, Stream contenido, string contentType, CancellationToken cancellationToken = default)
    {
        await AsegurarBucketAsync(cancellationToken);

        var request = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = claveAlmacenamiento,
            InputStream = contenido,
            ContentType = contentType,
            AutoCloseStream = false
            // DisablePayloadSigning = true (quitado): AWSSDK.S3 exige HTTPS
            // cuando el payload no va firmado, y SeaweedFS local corre por HTTP.
        };

        await _s3.PutObjectAsync(request, cancellationToken);
    }

    public async Task<byte[]> DescargarAsync(string claveAlmacenamiento, CancellationToken cancellationToken = default)
    {
        var response = await _s3.GetObjectAsync(_bucketName, claveAlmacenamiento, cancellationToken);

        // Se copia a un MemoryStream (no se puede evitar: response.ResponseStream
        // se cierra en cuanto se libera GetObjectResponse, y quien llama necesita
        // leer el contenido después de que este método retorne) pero se devuelve
        // como byte[] directamente, no envuelto de nuevo en un Stream — así quien
        // llama no tiene motivo para copiarlo una segunda vez a su propio buffer.
        using var copia = new MemoryStream();
        await response.ResponseStream.CopyToAsync(copia, cancellationToken);
        return copia.ToArray();
    }

    private async Task AsegurarBucketAsync(CancellationToken cancellationToken)
    {
        var existe = await AmazonS3Util.DoesS3BucketExistV2Async(_s3, _bucketName);
        if (!existe)
        {
            await _s3.PutBucketAsync(new PutBucketRequest { BucketName = _bucketName }, cancellationToken);
        }
    }
}
