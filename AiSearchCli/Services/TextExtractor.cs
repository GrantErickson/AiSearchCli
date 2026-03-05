using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using DocumentFormat.OpenXml.Packaging;

namespace AiSearchCli.Services;

/// <summary>
/// Extracts text content from various file types for full-text indexing.
/// </summary>
public static partial class TextExtractor
{
  private static readonly HashSet<string> PlainTextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".csv", ".log", ".json", ".xml", ".yaml", ".yml"
    };

  /// <summary>
  /// Returns true if text can be extracted from this file type.
  /// </summary>
  public static bool CanExtractText(string extension)
  {
    return PlainTextExtensions.Contains(extension)
        || extension.Equals(".html", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".htm", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".docx", StringComparison.OrdinalIgnoreCase);
  }

  /// <summary>
  /// Extracts text from the file based on its type.
  /// Returns null if extraction is not supported.
  /// </summary>
  public static string? ExtractText(string filePath, string extension)
  {
    if (PlainTextExtensions.Contains(extension))
      return File.ReadAllText(filePath, Encoding.UTF8);

    if (extension.Equals(".html", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".htm", StringComparison.OrdinalIgnoreCase))
      return ExtractFromHtml(filePath);

    if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
      return ExtractFromPdf(filePath);

    if (extension.Equals(".docx", StringComparison.OrdinalIgnoreCase))
      return ExtractFromDocx(filePath);

    return null;
  }

  private static string ExtractFromHtml(string filePath)
  {
    var html = File.ReadAllText(filePath, Encoding.UTF8);
    // Strip HTML tags to get plain text
    var text = HtmlTagRegex().Replace(html, " ");
    // Collapse whitespace
    text = WhitespaceRegex().Replace(text, " ").Trim();
    return text;
  }

  private static string ExtractFromPdf(string filePath)
  {
    var sb = new StringBuilder();
    using var document = PdfDocument.Open(filePath);
    foreach (var page in document.GetPages())
    {
      sb.AppendLine(page.Text);
    }
    return sb.ToString().Trim();
  }

  private static string ExtractFromDocx(string filePath)
  {
    using var doc = WordprocessingDocument.Open(filePath, false);
    var body = doc.MainDocumentPart?.Document?.Body;
    return body?.InnerText ?? string.Empty;
  }

  [GeneratedRegex("<[^>]+>")]
  private static partial Regex HtmlTagRegex();

  [GeneratedRegex(@"\s+")]
  private static partial Regex WhitespaceRegex();
}
