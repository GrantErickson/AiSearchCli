using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using AiSearchCli.Models;

namespace AiSearchCli.Services;

/// <summary>
/// Handles all interactions with the Azure AI Search index:
/// creating the index, uploading documents, running hybrid + semantic searches, and deleting documents.
/// </summary>
public class SearchService
{
  private readonly SearchClient _searchClient;
  private readonly SearchIndexClient _indexClient;
  private readonly string _indexName;

  public SearchService(AzureAISearchConfig config)
  {
    var credential = new AzureKeyCredential(config.AdminApiKey);
    _indexName = config.IndexName;
    _indexClient = new SearchIndexClient(new Uri(config.Endpoint), credential);
    _searchClient = _indexClient.GetSearchClient(_indexName);
  }

  /// <summary>
  /// Creates or updates the search index based on the FileDocument model attributes.
  /// Includes vector search (HNSW) and semantic ranking configuration.
  /// </summary>
  public async Task EnsureIndexAsync()
  {
    var fields = new FieldBuilder().Build(typeof(FileDocument));

    var index = new SearchIndex(_indexName, fields)
    {
      VectorSearch = new VectorSearch
      {
        Algorithms =
        {
          new HnswAlgorithmConfiguration("hnsw-algorithm")
          {
            Parameters = new HnswParameters
            {
              M = 4,
              EfConstruction = 400,
              EfSearch = 500,
              Metric = VectorSearchAlgorithmMetric.Cosine
            }
          }
        },
        Profiles =
        {
          new VectorSearchProfile("vector-profile", "hnsw-algorithm")
        }
      },
      SemanticSearch = new SemanticSearch
      {
        Configurations =
        {
          new SemanticConfiguration("semantic-config", new SemanticPrioritizedFields
          {
            TitleField = new SemanticField("fileName"),
            ContentFields = { new SemanticField("contentText") }
          })
        }
      }
    };

    try
    {
      await _indexClient.DeleteIndexAsync(_indexName);
    }
    catch (RequestFailedException ex) when (ex.Status == 404)
    {
      // Index doesn't exist yet — that's fine
    }

    await _indexClient.CreateIndexAsync(index);
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

    var response = await _searchClient.SearchAsync<Azure.Search.Documents.Models.SearchDocument>(queryText, options);

    var results = new List<SearchResult>();
    int rank = 1;

    await foreach (var result in response.Value.GetResultsAsync())
    {
      results.Add(new SearchResult
      {
        Rank = rank++,
        Score = result.Score ?? 0,
        Id = result.Document["id"]?.ToString() ?? "",
        FileName = result.Document["fileName"]?.ToString() ?? "",
        FileType = result.Document["fileType"]?.ToString() ?? "",
        FileSize = result.Document.TryGetValue("fileSize", out var size) ? Convert.ToInt64(size) : 0,
        BlobUrl = result.Document["blobUrl"]?.ToString() ?? "",
        UploadDate = result.Document.TryGetValue("uploadDate", out var date)
              ? DateTimeOffset.Parse(date.ToString()!)
              : DateTimeOffset.MinValue,
        TextIncludedInSearch = result.Document.TryGetValue("textIncludedInSearch", out var textFlag)
              && Convert.ToBoolean(textFlag)
      });
    }

    return results;
  }

  /// <summary>
  /// Returns all documents in the index (metadata only, no vectors).
  /// </summary>
  public async Task<List<FileDocument>> GetAllDocumentsAsync()
  {
    var options = new SearchOptions
    {
      Size = 1000,
      Select = { "id", "fileName", "fileType", "fileSize", "blobUrl", "blobName", "uploadDate", "contentText", "textIncludedInSearch" }
    };

    var response = await _searchClient.SearchAsync<FileDocument>("*", options);

    var results = new List<FileDocument>();
    await foreach (var result in response.Value.GetResultsAsync())
    {
      results.Add(result.Document);
    }

    return results;
  }

  /// <summary>
  /// Finds a document by its exact ID.
  /// Returns null if not found or if the ID contains invalid key characters.
  /// </summary>
  public async Task<FileDocument?> GetDocumentByIdAsync(string id)
  {
    try
    {
      var response = await _searchClient.GetDocumentAsync<FileDocument>(id);
      return response.Value;
    }
    catch (RequestFailedException ex) when (ex.Status is 404 or 400)
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
