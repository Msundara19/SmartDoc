using System.Text;
using System.Text.RegularExpressions;
using SmartDoc.Api.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace SmartDoc.Api.Services;

public interface IChunkingService
{
    (string DocType, List<Chunk> Chunks, int PageCount) ChunkDocument(Guid documentId, byte[] pdfBytes);
}

public class ChunkingService : IChunkingService
{
    private const int MaxTokens = 800;
    private const int OverlapTokens = 50;
    private const int MinParagraphChars = 100;

    // Research paper heading patterns
    private static readonly string[] ResearchHeadings =
        ["abstract", "introduction", "background", "related work", "methods", "methodology",
         "results", "experiments", "discussion", "conclusion", "conclusions", "references",
         "acknowledgements", "acknowledgments", "appendix"];

    // Legal keyword markers
    private static readonly string[] LegalKeywords =
        ["whereas", "hereinafter", "witnesseth", "herein", "thereof", "pursuant",
         "notwithstanding", "hereto", "indemnify", "indemnification"];

    private static readonly Regex ArticlePattern =
        new(@"^\s*(article|section|clause)\s+\d+[\.\:]?\s+", RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private static readonly Regex HeadingPattern =
        new(@"^\s*(\d+\.?\s+)?([A-Z][A-Z\s]{2,40})\s*$", RegexOptions.Multiline);

    // Numbered section heading: section number must not be preceded by a digit or dot
    // (rules out metric fragments like ".83" from "138.83").
    // Matches "1 Introduction", "2.3.1 Model Architecture Pipeline", etc.
    private static readonly Regex TechnicalSectionHeading =
        new(@"(?<![0-9.])([1-9]\d?(\.\d{1,2})*)\s+([A-Z][a-zA-Z]{2,}(?:\s+[A-Za-z]+){0,5})");

    public (string DocType, List<Chunk> Chunks, int PageCount) ChunkDocument(Guid documentId, byte[] pdfBytes)
    {
        var (pages, pageCount) = ExtractPages(pdfBytes);
        var fullText = string.Join("\n", pages.Select(p => p.Text));

        var docType = DetectDocumentType(fullText);
        var chunks = docType switch
        {
            DocumentType.ResearchPaper => ChunkResearchPaper(documentId, pages),
            DocumentType.Legal        => ChunkLegalDocument(documentId, pages),
            DocumentType.Technical    => ChunkTechnical(documentId, pages),
            _                         => ChunkGeneral(documentId, pages)
        };

        // Prepend a synthetic overview chunk so broad meta-queries ("main topics",
        // "key findings") have a high-similarity target instead of scoring across
        // many weak chunk matches.
        var overview = BuildOverviewChunk(documentId, chunks, pages);
        if (overview != null) chunks.Insert(0, overview);

        return (docType, chunks, pageCount);
    }

    // --- Document type detection ---

    public string DetectDocumentType(string text)
    {
        var lower = text.ToLowerInvariant();

        int headingMatches = ResearchHeadings.Count(h =>
            Regex.IsMatch(lower, $@"^\s*{Regex.Escape(h)}\s*$", RegexOptions.Multiline));

        if (headingMatches >= 3)
            return DocumentType.ResearchPaper;

        var words = lower.Split(' ', '\n', '\r', '\t');
        var totalWords = words.Length;
        if (totalWords == 0) return DocumentType.General;

        int legalHits = LegalKeywords.Sum(kw =>
            words.Count(w => w.Trim('.', ',', ';', ':') == kw));

        double legalDensity = (double)legalHits / totalWords;
        if (legalDensity > 0.005 || ArticlePattern.IsMatch(text))
            return DocumentType.Legal;

        // Technical docs: 3+ pages contain a numbered section heading in their first 400 chars.
        int technicalHeadings = text.Split('\n').Count(line =>
        {
            var search = line.Length > 400 ? line[..400] : line;
            return TechnicalSectionHeading.IsMatch(search);
        });
        if (technicalHeadings >= 3)
            return DocumentType.Technical;

        return DocumentType.General;
    }

    // --- Research paper: split on heading boundaries ---

    private List<Chunk> ChunkResearchPaper(Guid documentId, List<PageText> pages)
    {
        var result = new List<Chunk>();
        var currentSection = "Introduction";
        var currentLines = new List<string>();
        int currentPage = 1;
        int currentCharStart = 0;
        int runningChar = 0;
        int chunkIndex = 0;

        foreach (var page in pages)
        {
            if (IsTocPage(page.Text)) continue;

            var lines = page.Text.Split('\n');
            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd();
                var lower = line.Trim().ToLowerInvariant();

                bool isHeading = ResearchHeadings.Contains(lower) ||
                    ResearchHeadings.Any(h => Regex.IsMatch(lower, $@"^\d+\.?\s+{Regex.Escape(h)}$"));

                if (isHeading && currentLines.Count > 0)
                {
                    var text = string.Join("\n", currentLines).Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        var subChunks = SubSplitIfNeeded(documentId, text, currentSection,
                            page.PageNumber, currentCharStart, chunkIndex);
                        result.AddRange(subChunks);
                        chunkIndex += subChunks.Count;
                    }
                    currentLines.Clear();
                    currentSection = NormalizeHeading(line.Trim());
                    currentCharStart = runningChar;
                }
                else
                {
                    currentLines.Add(line);
                }
                runningChar += line.Length + 1;
            }
            currentPage = page.PageNumber;
        }

        // Flush last section
        if (currentLines.Count > 0)
        {
            var text = string.Join("\n", currentLines).Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                var subChunks = SubSplitIfNeeded(documentId, text, currentSection,
                    currentPage, currentCharStart, chunkIndex);
                result.AddRange(subChunks);
            }
        }

        return result.Count > 0 ? result : FallbackChunk(documentId, pages);
    }

    // --- Legal: split on Article/Section/Clause boundaries ---

    private List<Chunk> ChunkLegalDocument(Guid documentId, List<PageText> pages)
    {
        var result = new List<Chunk>();
        var fullText = string.Join("\n", pages.Select(p => p.Text));
        var pageMap = BuildPageMap(pages);

        var matches = ArticlePattern.Matches(fullText);
        if (matches.Count == 0)
            return ChunkGeneral(documentId, pages);

        int chunkIndex = 0;
        for (int i = 0; i < matches.Count; i++)
        {
            int start = matches[i].Index;
            int end = i + 1 < matches.Count ? matches[i + 1].Index : fullText.Length;
            var text = fullText[start..end].Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;

            var sectionName = matches[i].Value.Trim().TrimEnd(':', '.');
            int pageNum = FindPageNumber(pageMap, start);

            var subChunks = SubSplitIfNeeded(documentId, text, sectionName, pageNum, start, chunkIndex);
            result.AddRange(subChunks);
            chunkIndex += subChunks.Count;
        }

        return result.Count > 0 ? result : FallbackChunk(documentId, pages);
    }

    // --- Technical: paragraph chunking with section names extracted per page ---
    // Like ChunkGeneral but annotates each chunk with the section heading found
    // in the first 400 chars of the page (e.g. "2.3.1 Model Architecture Pipeline").

    private List<Chunk> ChunkTechnical(Guid documentId, List<PageText> pages)
    {
        var result = new List<Chunk>();
        string? currentSection = null;
        int chunkIndex = 0;

        foreach (var page in pages)
        {
            if (IsTocPage(page.Text)) continue;

            // Check first 400 chars for a new section heading
            var searchZone = page.Text.Length > 400 ? page.Text[..400] : page.Text;
            var headingMatch = TechnicalSectionHeading.Match(searchZone);
            if (headingMatch.Success)
            {
                var raw = $"{headingMatch.Groups[1].Value} {headingMatch.Groups[3].Value}".Trim();
                currentSection = raw.Length > 70 ? raw[..70] : raw;
            }

            var rawParagraphs = page.Text.Split(["\n\n", "\r\n\r\n"], StringSplitOptions.RemoveEmptyEntries);
            var merged = MergeShortParagraphs(rawParagraphs);

            int charOffset = 0;
            foreach (var para in merged)
            {
                if (string.IsNullOrWhiteSpace(para)) { charOffset += para.Length + 2; continue; }
                if (IsJunkChunk(para)) { charOffset += para.Length + 2; continue; }

                var subChunks = SubSplitIfNeeded(documentId, para, currentSection,
                    page.PageNumber, charOffset, chunkIndex);
                result.AddRange(subChunks);
                chunkIndex += subChunks.Count;
                charOffset += para.Length + 2;
            }
        }

        return result.Count > 0 ? result : FallbackChunk(documentId, pages);
    }

    // --- Overview chunk: synthetic document-level index for broad meta-queries ---

    private static Chunk? BuildOverviewChunk(Guid documentId, List<Chunk> chunks, List<PageText> pages)
    {
        // Collect unique section names from structural chunks
        var sections = chunks
            .Where(c => c.SectionName != null)
            .Select(c => c.SectionName!)
            .Distinct()
            .ToList();

        // Fall back to first-page content if no sections detected
        string overviewText;
        if (sections.Count >= 2)
        {
            overviewText = $"Document overview. Topics and sections covered: {string.Join("; ", sections)}. " +
                           $"Total sections: {sections.Count}.";
        }
        else
        {
            // For general docs: use first non-junk page as overview
            var firstPage = pages.FirstOrDefault(p => !IsTocPage(p.Text) && p.Text.Length > 100);
            if (firstPage == null) return null;
            var firstContent = firstPage.Text.Length > 600
                ? firstPage.Text[..600]
                : firstPage.Text;
            overviewText = firstContent.Trim();
        }

        if (string.IsNullOrWhiteSpace(overviewText)) return null;

        return new Chunk
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            ChunkText = overviewText,
            PageNumber = 1,
            SectionName = "Document Overview",
            ChunkType = ChunkTypes.Structural,
            ChunkIndex = -1
        };
    }

    // --- General: paragraph-based with short-paragraph merging ---

    private List<Chunk> ChunkGeneral(Guid documentId, List<PageText> pages)
    {
        var result = new List<Chunk>();
        int chunkIndex = 0;

        foreach (var page in pages)
        {
            if (IsTocPage(page.Text)) continue;

            var rawParagraphs = page.Text.Split(["\n\n", "\r\n\r\n"], StringSplitOptions.RemoveEmptyEntries);
            var merged = MergeShortParagraphs(rawParagraphs);

            int charOffset = 0;
            foreach (var para in merged)
            {
                if (string.IsNullOrWhiteSpace(para)) { charOffset += para.Length + 2; continue; }
                if (IsJunkChunk(para)) { charOffset += para.Length + 2; continue; }

                var subChunks = SubSplitIfNeeded(documentId, para, null, page.PageNumber, charOffset, chunkIndex);
                result.AddRange(subChunks);
                chunkIndex += subChunks.Count;
                charOffset += para.Length + 2;
            }
        }

        return result.Count > 0 ? result : FallbackChunk(documentId, pages);
    }

    // --- Token-based sub-splitting for oversized chunks ---

    private List<Chunk> SubSplitIfNeeded(
        Guid documentId, string text, string? sectionName, int pageNumber, int charStart, int startIndex)
    {
        var tokens = ApproximateTokens(text);
        if (tokens <= MaxTokens)
        {
            return [new Chunk
            {
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                ChunkText = text,
                PageNumber = pageNumber,
                SectionName = sectionName,
                ChunkType = sectionName != null ? ChunkTypes.Structural : ChunkTypes.Fallback,
                CharStart = charStart,
                CharEnd = charStart + text.Length,
                ChunkIndex = startIndex
            }];
        }

        // Split into overlapping windows
        var words = text.Split(' ');
        var chunks = new List<Chunk>();
        int idx = startIndex;
        int i = 0;

        while (i < words.Length)
        {
            var sb = new StringBuilder();
            int count = 0;
            int j = i;

            while (j < words.Length && count < MaxTokens)
            {
                sb.Append(words[j]).Append(' ');
                count += ApproximateTokens(words[j]);
                j++;
            }

            var chunkText = sb.ToString().Trim();
            int localCharStart = charStart + string.Join(" ", words[..i]).Length;

            if (!string.IsNullOrWhiteSpace(chunkText))
            {
                chunks.Add(new Chunk
                {
                    Id = Guid.NewGuid(),
                    DocumentId = documentId,
                    ChunkText = chunkText,
                    PageNumber = pageNumber,
                    SectionName = sectionName,
                    ChunkType = ChunkTypes.Fallback,
                    CharStart = localCharStart,
                    CharEnd = localCharStart + chunkText.Length,
                    ChunkIndex = idx++
                });
            }

            // Advance with overlap
            i = Math.Max(i + 1, j - OverlapTokens);
        }

        return chunks;
    }

    // --- Fallback: 512-token windows on entire document ---

    private List<Chunk> FallbackChunk(Guid documentId, List<PageText> pages)
    {
        var result = new List<Chunk>();
        int chunkIndex = 0;

        foreach (var page in pages)
        {
            var words = page.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int i = 0;
            while (i < words.Length)
            {
                var take = Math.Min(512, words.Length - i);
                var text = string.Join(" ", words.Skip(i).Take(take));

                result.Add(new Chunk
                {
                    Id = Guid.NewGuid(),
                    DocumentId = documentId,
                    ChunkText = text,
                    PageNumber = page.PageNumber,
                    SectionName = null,
                    ChunkType = ChunkTypes.Fallback,
                    ChunkIndex = chunkIndex++
                });

                i += Math.Max(1, take - OverlapTokens);
            }
        }

        return result;
    }

    // --- Junk / TOC detection ---

    // A page whose non-space chars are >25% dots is a table-of-contents leader page.
    private static bool IsTocPage(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var nonSpace = text.Where(c => c != ' ' && c != '\n' && c != '\r' && c != '\t').ToArray();
        if (nonSpace.Length == 0) return false;
        double dotRatio = (double)nonSpace.Count(c => c == '.') / nonSpace.Length;
        return dotRatio > 0.25;
    }

    // A chunk that is mostly dots (TOC leaders) or too short carries no semantic value.
    private static bool IsJunkChunk(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length < 40) return true;
        var nonSpace = trimmed.Where(c => c != ' ').ToArray();
        if (nonSpace.Length == 0) return true;
        double dotRatio = (double)nonSpace.Count(c => c == '.') / nonSpace.Length;
        return dotRatio > 0.20;
    }

    // --- Helpers ---

    private static (List<PageText> Pages, int PageCount) ExtractPages(byte[] pdfBytes)
    {
        var pages = new List<PageText>();
        using var doc = PdfDocument.Open(pdfBytes);
        foreach (var page in doc.GetPages())
        {
            var sb = new StringBuilder();
            foreach (var word in page.GetWords())
                sb.Append(word.Text).Append(' ');
            pages.Add(new PageText(page.Number, sb.ToString()));
        }
        return (pages, doc.NumberOfPages);
    }

    private static List<string> MergeShortParagraphs(string[] paragraphs)
    {
        var result = new List<string>();
        var buffer = new StringBuilder();

        foreach (var para in paragraphs)
        {
            var trimmed = para.Trim();
            if (trimmed.Length < MinParagraphChars)
            {
                buffer.Append(' ').Append(trimmed);
            }
            else
            {
                if (buffer.Length > 0)
                {
                    result.Add((buffer.ToString().Trim() + " " + trimmed).Trim());
                    buffer.Clear();
                }
                else
                {
                    result.Add(trimmed);
                }
            }
        }

        if (buffer.Length > 0)
            result.Add(buffer.ToString().Trim());

        return result;
    }

    private static Dictionary<int, int> BuildPageMap(List<PageText> pages)
    {
        var map = new Dictionary<int, int>();
        int pos = 0;
        foreach (var page in pages)
        {
            map[pos] = page.PageNumber;
            pos += page.Text.Length + 1;
        }
        return map;
    }

    private static int FindPageNumber(Dictionary<int, int> pageMap, int charPos)
    {
        int page = 1;
        foreach (var (start, pageNum) in pageMap.OrderBy(x => x.Key))
        {
            if (charPos >= start) page = pageNum;
            else break;
        }
        return page;
    }

    private static string NormalizeHeading(string heading)
    {
        var clean = Regex.Replace(heading, @"^\d+\.?\s+", "");
        return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(clean.ToLower());
    }

    // Rough approximation: 1 token ≈ 4 characters
    private static int ApproximateTokens(string text) => Math.Max(1, text.Length / 4);
}

public record PageText(int PageNumber, string Text);

// Deconstruct helper for tuple return
file static class PageExtensions
{
    public static void Deconstruct(this (List<PageText> Pages, int PageCount) tuple,
        out List<PageText> pages, out int pageCount)
    {
        pages = tuple.Pages;
        pageCount = tuple.PageCount;
    }
}
