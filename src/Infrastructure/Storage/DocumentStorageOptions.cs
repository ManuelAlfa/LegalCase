namespace LegalCaseManagement.Infrastructure.Storage;

public class DocumentStorageOptions
{
    public const string SectionName = "DocumentStorage";

    public string Endpoint { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string BucketName { get; set; } = "documentos-adjuntos";
}
