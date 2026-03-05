namespace AiSearchCli.Models;

/// <summary>
/// Represents a single search result returned to the user.
/// </summary>
public class SearchResult
{
  public int Rank { get; set; }
  public double Score { get; set; }
  public string Id { get; set; } = string.Empty;
  public string FileName { get; set; } = string.Empty;
  public string FileType { get; set; } = string.Empty;
  public long FileSize { get; set; }
  public string BlobUrl { get; set; } = string.Empty;
  public DateTimeOffset UploadDate { get; set; }
  public bool TextIncludedInSearch { get; set; }
}
