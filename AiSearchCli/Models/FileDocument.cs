using System.Text.Json.Serialization;
using Azure.Search.Documents.Indexes;

namespace AiSearchCli.Models;

/// <summary>
/// Represents a document in the Azure AI Search index.
/// Attributes define the index schema; JSON names match the camelCase field names.
/// </summary>
public class FileDocument
{
  [SimpleField(IsKey = true, IsFilterable = true)]
  [JsonPropertyName("id")]
  public string Id { get; set; } = string.Empty;

  [SearchableField(IsFilterable = true, IsSortable = true)]
  [JsonPropertyName("fileName")]
  public string FileName { get; set; } = string.Empty;

  [SimpleField(IsFilterable = true, IsFacetable = true)]
  [JsonPropertyName("fileType")]
  public string FileType { get; set; } = string.Empty;

  [SimpleField(IsFilterable = true, IsSortable = true)]
  [JsonPropertyName("fileSize")]
  public long FileSize { get; set; }

  [SimpleField]
  [JsonPropertyName("blobUrl")]
  public string BlobUrl { get; set; } = string.Empty;

  [SimpleField(IsFilterable = true)]
  [JsonPropertyName("blobName")]
  public string BlobName { get; set; } = string.Empty;

  [SimpleField(IsFilterable = true, IsSortable = true)]
  [JsonPropertyName("uploadDate")]
  public DateTimeOffset UploadDate { get; set; }

  [SearchableField(AnalyzerName = "en.microsoft")]
  [JsonPropertyName("contentText")]
  public string? ContentText { get; set; }

  [VectorSearchField(VectorSearchDimensions = 1024, VectorSearchProfileName = "vector-profile")]
  [JsonPropertyName("contentVector")]
  public float[]? ContentVector { get; set; }

  [SimpleField(IsFilterable = true)]
  [JsonPropertyName("textIncludedInSearch")]
  public bool TextIncludedInSearch { get; set; }
}
