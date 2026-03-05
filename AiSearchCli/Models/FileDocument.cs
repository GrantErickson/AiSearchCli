namespace AiSearchCli.Models;

/// <summary>
/// Represents a document in the Azure AI Search index.
/// Field names match the index schema exactly.
/// </summary>
public class FileDocument
{
  public string Id { get; set; } = string.Empty;
  public string FileName { get; set; } = string.Empty;
  public string FileType { get; set; } = string.Empty;
  public long FileSize { get; set; }
  public string BlobUrl { get; set; } = string.Empty;
  public string BlobName { get; set; } = string.Empty;
  public DateTimeOffset UploadDate { get; set; }
  public string? ContentText { get; set; }
  public float[]? ContentVector { get; set; }
  public bool TextIncludedInSearch { get; set; }
}
