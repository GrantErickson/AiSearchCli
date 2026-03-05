using Microsoft.Extensions.Configuration;
using AiSearchCli.Models;
using AiSearchCli.Services;

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddUserSecrets<Program>(optional: true)
    .Build();

var appConfig = new AppConfig();
config.Bind(appConfig);

if (args.Length == 0)
{
  PrintUsage();
  return 1;
}

var blobService = new BlobService(appConfig.AzureBlobStorage);
var embeddingService = new EmbeddingService(appConfig.AzureAIVision);
var searchService = new SearchService(appConfig.AzureAISearch);

long maxFileSize = appConfig.Settings.MaxFileSizeMB * 1024L * 1024L;

var command = args[0].ToLowerInvariant();

try
{
  switch (command)
  {
    case "add":
      return await HandleAdd(args);
    case "search":
      return await HandleSearch(args);
    case "remove":
      return await HandleRemove(args);
    case "create-index":
      return await HandleCreateIndex();
    case "reindex":
      return await HandleReindex();
    default:
      Console.Error.WriteLine($"Unknown command: {args[0]}");
      PrintUsage();
      return 1;
  }
}
catch (Exception ex)
{
  Console.Error.WriteLine($"Error: {ex.Message}");
  return 1;
}

// ── Add command ──
async Task<int> HandleAdd(string[] args)
{
  if (args.Length < 2)
  {
    Console.Error.WriteLine("Usage: aisearch add <filepath> [--no-text]");
    return 1;
  }

  var filePath = args[1];
  var noText = args.Any(a => a.Equals("--no-text", StringComparison.OrdinalIgnoreCase));

  var file = new FileInfo(filePath);
  if (!file.Exists)
  {
    Console.Error.WriteLine($"File not found: {file.FullName}");
    return 1;
  }

  if (file.Length > maxFileSize)
  {
    Console.Error.WriteLine($"File exceeds {appConfig.Settings.MaxFileSizeMB} MB limit ({FormatSize(file.Length)}).");
    return 1;
  }

  var extension = file.Extension.ToLowerInvariant();
  var documentId = Guid.NewGuid().ToString();
  var blobName = $"{documentId}{extension}";

  Console.WriteLine($"Processing: {file.Name} ({FormatSize(file.Length)})...");

  // 1. Generate embeddings
  Console.Write("  Generating embeddings... ");
  float[] vector;
  if (EmbeddingService.IsImageFile(extension))
  {
    vector = await embeddingService.VectorizeImageAsync(file.FullName);
  }
  else
  {
    var text = TextExtractor.ExtractText(file.FullName, extension);
    if (string.IsNullOrWhiteSpace(text))
    {
      Console.Error.WriteLine("Could not extract text from file for embedding generation.");
      return 1;
    }
    vector = await embeddingService.VectorizeTextAsync(text);
  }
  Console.WriteLine("Done.");

  // 2. Upload to blob storage
  Console.Write("  Uploading to blob storage... ");
  var blobUrl = await blobService.UploadAsync(file.FullName, blobName);
  Console.WriteLine("Done.");

  // 3. Extract text for full-text search (optional)
  string? contentText = null;
  bool textIncluded = false;
  if (!noText)
  {
    if (EmbeddingService.IsImageFile(extension))
    {
      // Generate captions for images using Azure AI Vision
      Console.Write("  Generating image captions... ");
      contentText = await embeddingService.GenerateCaptionAsync(file.FullName);
      textIncluded = !string.IsNullOrWhiteSpace(contentText);
      Console.WriteLine("Done.");
    }
    else if (TextExtractor.CanExtractText(extension))
    {
      contentText = TextExtractor.ExtractText(file.FullName, extension);
      textIncluded = !string.IsNullOrWhiteSpace(contentText);
    }
  }

  // 4. Push to search index
  Console.Write("  Indexing document... ");
  var document = new FileDocument
  {
    Id = documentId,
    FileName = file.Name,
    FileType = extension,
    FileSize = file.Length,
    BlobUrl = blobUrl,
    BlobName = blobName,
    UploadDate = DateTimeOffset.UtcNow,
    ContentText = contentText,
    ContentVector = vector,
    TextIncludedInSearch = textIncluded
  };

  await searchService.UploadDocumentAsync(document);
  Console.WriteLine("Done.");

  Console.WriteLine();
  Console.WriteLine("File added successfully.");
  Console.WriteLine($"  Document ID:   {documentId}");
  Console.WriteLine($"  Blob URL:      {blobUrl}");
  Console.WriteLine($"  File:          {file.Name} ({FormatSize(file.Length)})");
  Console.WriteLine($"  Text indexed:  {(textIncluded ? "Yes" : "No")}");
  return 0;
}

// ── Search command ──
async Task<int> HandleSearch(string[] args)
{
  if (args.Length < 2)
  {
    Console.Error.WriteLine("Usage: aisearch search \"<query text>\"");
    return 1;
  }

  var query = args[1];

  Console.WriteLine($"Searching for: \"{query}\"...");
  Console.WriteLine();

  var queryVector = await embeddingService.VectorizeTextAsync(query);
  var results = await searchService.HybridSearchAsync(query, queryVector);

  if (results.Count == 0)
  {
    Console.WriteLine("No results found.");
    return 0;
  }

  Console.WriteLine($" {"#",-4} {"Score",-8} {"File Name",-30} {"Type",-7} {"Size",-12} {"Uploaded",-22} URL");
  Console.WriteLine(new string('-', 130));

  foreach (var r in results)
  {
    Console.WriteLine(
        $" {r.Rank,-4} {r.Score,-8:F3} {Truncate(r.FileName, 28),-30} {r.FileType,-7} {FormatSize(r.FileSize),-12} {r.UploadDate:yyyy-MM-dd HH:mm,-22} {r.BlobUrl}");
  }

  return 0;
}

// ── Remove command ──
async Task<int> HandleRemove(string[] args)
{
  if (args.Length < 2)
  {
    Console.Error.WriteLine("Usage: aisearch remove \"<filename-or-id>\"");
    return 1;
  }

  var identifier = args[1];

  // First try to find by document ID
  var doc = await searchService.GetDocumentByIdAsync(identifier);

  if (doc != null)
  {
    await DeleteDocument(doc.Id, doc.FileName, doc.BlobName);
    return 0;
  }

  // Try to find by filename
  var matches = await searchService.FindByFileNameAsync(identifier);

  if (matches.Count == 0)
  {
    Console.Error.WriteLine($"No document found matching \"{identifier}\".");
    return 1;
  }

  if (matches.Count == 1)
  {
    var match = matches[0];
    await DeleteDocument(match.Id, match.FileName, match.BlobName);
    return 0;
  }

  // Multiple matches — ask user to specify
  Console.WriteLine($"Multiple files found matching \"{identifier}\":");
  Console.WriteLine();
  Console.WriteLine($"  {"ID",-40} {"Uploaded"}");
  foreach (var m in matches)
  {
    Console.WriteLine($"  {m.Id,-40} {m.UploadDate:yyyy-MM-dd HH:mm}");
  }
  Console.WriteLine();
  Console.WriteLine("Please re-run with the specific document ID:");
  Console.WriteLine($"  aisearch remove \"{matches[0].Id}\"");
  return 1;
}

// ── Create-index command ──
async Task<int> HandleCreateIndex()
{
  Console.Write("Creating/updating search index... ");
  await searchService.EnsureIndexAsync();
  Console.WriteLine("Done.");
  Console.WriteLine($"  Index: {appConfig.AzureAISearch.IndexName}");
  return 0;
}

// ── Reindex command ──
async Task<int> HandleReindex()
{
  Console.WriteLine("Fetching all documents from index...");
  var documents = await searchService.GetAllDocumentsAsync();

  if (documents.Count == 0)
  {
    Console.WriteLine("No documents found in index.");
    return 0;
  }

  Console.WriteLine($"Found {documents.Count} document(s).");

  // Recreate the index so it's clean and matches the current schema
  Console.Write("Recreating search index... ");
  await searchService.EnsureIndexAsync();
  Console.WriteLine("Done.");

  Console.WriteLine("Re-processing all documents...");
  Console.WriteLine();

  int success = 0;
  int failed = 0;

  foreach (var doc in documents)
  {
    var tempPath = Path.Combine(Path.GetTempPath(), doc.BlobName);
    try
    {
      Console.WriteLine($"Processing: {doc.FileName} ({doc.FileType})");

      // Download from blob storage
      Console.Write("  Downloading from blob... ");
      await blobService.DownloadAsync(doc.BlobName, tempPath);
      Console.WriteLine("Done.");

      var extension = doc.FileType;

      // Generate embeddings
      Console.Write("  Generating embeddings... ");
      float[] vector;
      if (EmbeddingService.IsImageFile(extension))
      {
        vector = await embeddingService.VectorizeImageAsync(tempPath);
      }
      else
      {
        var text = TextExtractor.ExtractText(tempPath, extension);
        if (string.IsNullOrWhiteSpace(text))
        {
          Console.WriteLine("Skipped (no text could be extracted).");
          failed++;
          continue;
        }
        vector = await embeddingService.VectorizeTextAsync(text);
      }
      Console.WriteLine("Done.");

      // Generate content text
      string? contentText = null;
      bool textIncluded = false;
      if (EmbeddingService.IsImageFile(extension))
      {
        Console.Write("  Generating image captions... ");
        contentText = await embeddingService.GenerateCaptionAsync(tempPath);
        textIncluded = !string.IsNullOrWhiteSpace(contentText);
        Console.WriteLine("Done.");
      }
      else if (TextExtractor.CanExtractText(extension))
      {
        contentText = TextExtractor.ExtractText(tempPath, extension);
        textIncluded = !string.IsNullOrWhiteSpace(contentText);
      }

      // Update in index
      Console.Write("  Updating index... ");
      var updated = new FileDocument
      {
        Id = doc.Id,
        FileName = doc.FileName,
        FileType = doc.FileType,
        FileSize = doc.FileSize,
        BlobUrl = doc.BlobUrl,
        BlobName = doc.BlobName,
        UploadDate = doc.UploadDate,
        ContentText = contentText,
        ContentVector = vector,
        TextIncludedInSearch = textIncluded
      };
      await searchService.UploadDocumentAsync(updated);
      Console.WriteLine("Done.");
      Console.WriteLine($"  Text indexed: {(textIncluded ? "Yes" : "No")}");

      success++;
    }
    catch (Exception ex)
    {
      Console.Error.WriteLine($"  Error: {ex.Message}");
      failed++;
    }
    finally
    {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  Console.WriteLine();
  Console.WriteLine($"Re-index complete. {success} succeeded, {failed} failed.");
  return failed > 0 ? 1 : 0;
}

// ── Helper methods ──

async Task DeleteDocument(string id, string fileName, string blobName)
{
  Console.Write($"Removing: {fileName} (ID: {id})... ");

  await searchService.DeleteDocumentAsync(id);
  var blobDeleted = await blobService.DeleteAsync(blobName);

  Console.WriteLine("Done.");
  Console.WriteLine($"  Deleted from index: Yes");
  Console.WriteLine($"  Deleted from blob:  {(blobDeleted ? "Yes" : "Not found")}");
}

static string FormatSize(long bytes)
{
  return bytes switch
  {
    >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
    >= 1024 => $"{bytes / 1024.0:F0} KB",
    _ => $"{bytes} B"
  };
}

static string Truncate(string value, int maxLength)
{
  return value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength - 3), "...");
}

static void PrintUsage()
{
  Console.WriteLine("AiSearchCli — Add, search, and remove files from Azure AI Search");
  Console.WriteLine();
  Console.WriteLine("Usage:");
  Console.WriteLine("  aisearch add <filepath> [--no-text]    Add a file to the index and blob storage");
  Console.WriteLine("  aisearch search \"<query>\"              Search for matching files (top 10)");
  Console.WriteLine("  aisearch remove \"<filename-or-id>\"     Remove a file from index and blob storage");
  Console.WriteLine("  aisearch create-index                  Create or update the search index");
  Console.WriteLine("  aisearch reindex                       Re-process all documents (regenerate embeddings & captions)");
  Console.WriteLine();
  Console.WriteLine("Options:");
  Console.WriteLine("  --no-text    Skip adding extracted text to the full-text index (vector still generated)");
}
