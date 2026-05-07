namespace SmartDoc.Api.Models;

public class Document
{
    public Guid Id { get; set; }
    public string Filename { get; set; } = string.Empty;
    public DateTimeOffset UploadTime { get; set; }
    public string Status { get; set; } = DocumentStatus.Pending;
    public string? DocType { get; set; }
    public int? PageCount { get; set; }
    public string? ErrorMessage { get; set; }
    public long? FileSizeBytes { get; set; }
    public string? Summary { get; set; }
}

public static class DocumentStatus
{
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string Ready = "ready";
    public const string Failed = "failed";
}

public static class DocumentType
{
    public const string ResearchPaper = "research_paper";
    public const string Legal = "legal";
    public const string Technical = "technical";
    public const string General = "general";
}
