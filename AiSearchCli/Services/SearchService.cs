using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using AiSearchCli.Models;

namespace AiSearchCli.Services;

/// <summary>
/// Handles all interactions with the Azure AI Search index:
/// uploading documents, running hybrid + semantic searches, and deleting documents.
/// </summary>
public class SearchService
{
  private readonly SearchClient _searchClient;

  public SearchService(AzureAISearchConfig config)
  {
    var credential = new AzureKeyCredential(config.AdminApiKey);
    _searchClient = new SearchClient(new Uri(config.Endpoint), config.IndexName, credential);
  }

  /// <summary>
  /// Uploads (or merges) a document into the search index.
  /// </summary>
  public async Task UploadDocumentAsync(FileDocument document)
  {
    var batch = IndexDocumentsBatch.Upload(new[] { document });
    await _searchClient.IndexDocumentsAsync(batch);
  }

  /// <summary>
  /// Executes a hybrid search (vector + full-text keyword) with semantic ranking.
  /// Returns the top 10 results.
  /// </summary>
  public async Task<List<SearchResult>> HybridSearchAsync(string queryText, float[] queryVector)
  {
    var options = new SearchOptions
    {
      Size = 10,
      QueryType = SearchQueryType.Semantic,
      SemanticSearch = new SemanticSearchOptions
      {
        SemanticConfigurationName = "semantic-config",
      },
      Select =
            {
                "id", "fileName", "fileType", "fileSize",
                "blobUrl", "uploadDate", "textIncludedInSearch"
            },
      VectorSearch = new VectorSearchOptions
      {
        Queries =
                {
                    new VectorizedQuery(queryVector)
                    {
                        KNearestNeighborsCount = 10,
                        Fields = { "contentVector" }
                    }
                }
      }
    };

    var response = await _searchClient.SearchAsync<FileDocument>(queryText, options);

    var results = new List<SearchResult>();
    int rank = 1;

    await foreach (var result in response.Value.GetResultsAsync())
    {
      var doc = result.Document;
      results.Add(new SearchResult
      {
        Rank = rank++,
        Score = result.Score ?? 0,
        Id = doc.Id,
        FileName = doc.FileName,
        FileType = doc.FileType,
        FileSize = doc.FileSize,
        BlobUrl = doc.BlobUrl,
        UploadDate = doc.UploadDate,
        TextIncludedInSearch = doc.TextIncludedInSearch
      });
    }

    return results;
  }

  /// <summary>
  /// Finds a document by its exact ID.
  /// Returns null if not found.
  /// </summary>
  public async Task<FileDocument?> GetDocumentByIdAsync(string id)
  {
    try
    {
      var response = await _searchClient.GetDocumentAsync<FileDocument>(id);
      return response.Value;
    }
    catch (RequestFailedException ex) when (ex.Status == 404)
    {
      return null;
    }
  }

  /// <summary>
  /// Searches for documents matching a given filename.
  /// Returns all matches (for disambiguation when removing).
  /// </summary>
  public async Task<List<FileDocument>> FindByFileNameAsync(string fileName)
  {
    var options = new SearchOptions
    {
      Filter = $"fileName eq '{EscapeFilterValue(fileName)}'",
      Select = { "id", "fileName", "fileType", "fileSize", "blobUrl", "blobName", "uploadDate" },
      Size = 50
    };

    var response = await _searchClient.SearchAsync<FileDocument>(null, options);

    var results = new List<FileDocument>();
    await foreach (var result in response.Value.GetResultsAsync())
    {
      results.Add(result.Document);
    }

    return results;
  }

  /// <summary>
  /// Deletes a document from the index by its ID.
  /// </summary>
  public async Task DeleteDocumentAsync(string id)
  {
    var batch = IndexDocumentsBatch.Delete("id", new[] { id });
    await _searchClient.IndexDocumentsAsync(batch);
  }

  private static string EscapeFilterValue(string value)
  {
    return value.Replace("'", "''");
  }
}
