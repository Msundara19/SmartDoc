namespace SmartDoc.Api.Models;

public class Flashcard
{
    public string Front { get; set; } = string.Empty;
    public string Back { get; set; } = string.Empty;
    public int? Page { get; set; }
    public string? Section { get; set; }
}

public class FlashcardsResponse
{
    public List<Flashcard> Cards { get; set; } = [];
    public int ChunksUsed { get; set; }
}
