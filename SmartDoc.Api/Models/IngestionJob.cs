namespace SmartDoc.Api.Models;

public class IngestionJob
{
    public Guid DocumentId { get; set; }
    public string TempFilePath { get; set; } = string.Empty;
    public string Filename { get; set; } = string.Empty;
}
