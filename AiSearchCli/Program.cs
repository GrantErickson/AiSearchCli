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
var chatService = new ChatService(appConfig.AzureOpenAI, searchService, embeddingService);

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
    case "ask":
      return await HandleAsk(args);
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
    Console.Error.WriteLine("Usage: aisearch add <file-or-folder> [--no-text]");
    return 1;
  }

  var path = args[1];
  var noText = args.Any(a => a.Equals("--no-text", StringComparison.OrdinalIgnoreCase));

  if (Directory.Exists(path))
    return await HandleAddFolder(path, noText);

  return await AddFileAsync(path, noText);
}

// ── Add folder ──
async Task<int> HandleAddFolder(string folderPath, bool noText)
{
  var dir = new DirectoryInfo(folderPath);
  var files = dir.GetFiles("*", SearchOption.AllDirectories);

  if (files.Length == 0)
  {
    Console.WriteLine("No files found in folder.");
    return 0;
  }

  Console.WriteLine($"Found {files.Length} file(s) in \"{dir.FullName}\".");
  Console.WriteLine();

  // Get existing filenames from the index to skip duplicates
  Console.Write("Checking for duplicates... ");
  var existingDocs = await searchService.GetAllDocumentsAsync();
  var existingNames = new HashSet<string>(
      existingDocs.Select(d => d.FileName),
      StringComparer.OrdinalIgnoreCase);
  Console.WriteLine("Done.");

  int added = 0;
  int skipped = 0;
  int failed = 0;

  foreach (var file in files)
  {
    if (existingNames.Contains(file.Name))
    {
      Console.WriteLine($"  Skipped (duplicate): {file.Name}");
      skipped++;
      continue;
    }

    Console.WriteLine();
    var result = await AddFileAsync(file.FullName, noText);
    if (result == 0)
    {
      existingNames.Add(file.Name);
      added++;
    }
    else
    {
      failed++;
    }
  }

  Console.WriteLine();
  Console.WriteLine($"Folder complete. {added} added, {skipped} skipped (duplicate), {failed} failed.");
  return failed > 0 ? 1 : 0;
}

// ── Add single file ──
async Task<int> AddFileAsync(string filePath, bool noText)
{
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

// ── Ask command ──
async Task<int> HandleAsk(string[] args)
{
  if (args.Length < 2)
  {
    Console.Error.WriteLine("Usage: aisearch ask \"<question>\"");
    return 1;
  }

  var question = args[1];

  Console.WriteLine($"Question: \"{question}\"");
  Console.WriteLine();

  var result = await chatService.AskAsync(question);

  Console.WriteLine();
  if (result.IndexSources.Count > 0)
  {
    Console.WriteLine("Sources:");
    foreach (var doc in result.IndexSources)
    {
      Console.WriteLine($"  [{doc.FileName}] {doc.BlobUrl}");
    }
  }

  return 0;
}

// ── Remove command ──
async Task<int> HandleRemove(string[] args)
{
  if (args.Length < 2)
  {
    Console.Error.WriteLine("Usage: aisearch remove <filename-or-id-or-folder>");
    return 1;
  }

  var identifier = args[1];

  if (Directory.Exists(identifier))
    return await HandleRemoveFolder(identifier);

  return await RemoveSingleAsync(identifier);
}

// ── Remove folder ──
async Task<int> HandleRemoveFolder(string folderPath)
{
  var dir = new DirectoryInfo(folderPath);
  var fileNames = dir.GetFiles("*", SearchOption.AllDirectories)
      .Select(f => f.Name)
      .ToHashSet(StringComparer.OrdinalIgnoreCase);

  if (fileNames.Count == 0)
  {
    Console.WriteLine("No files found in folder.");
    return 0;
  }

  Console.WriteLine($"Found {fileNames.Count} file(s) in \"{dir.FullName}\". Matching against index...");

  var allDocs = await searchService.GetAllDocumentsAsync();
  var toRemove = allDocs.Where(d => fileNames.Contains(d.FileName)).ToList();

  if (toRemove.Count == 0)
  {
    Console.WriteLine("No matching documents found in the index.");
    return 0;
  }

  Console.WriteLine($"Removing {toRemove.Count} document(s)...");
  Console.WriteLine();

  int removed = 0;
  int failed = 0;

  foreach (var doc in toRemove)
  {
    try
    {
      await DeleteDocument(doc.Id, doc.FileName, doc.BlobName);
      removed++;
    }
    catch (Exception ex)
    {
      Console.Error.WriteLine($"  Error removing {doc.FileName}: {ex.Message}");
      failed++;
    }
  }

  Console.WriteLine();
  Console.WriteLine($"Folder removal complete. {removed} removed, {failed} failed.");
  return failed > 0 ? 1 : 0;
}

// ── Remove single file ──
async Task<int> RemoveSingleAsync(string identifier)
{
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
  Console.WriteLine("  aisearch add <file-or-folder> [--no-text]  Add a file or folder to the index");
  Console.WriteLine("  aisearch search \"<query>\"                  Search for matching files (top 10)");
  Console.WriteLine("  aisearch ask \"<question>\"                  Ask a question using AI with index context");
  Console.WriteLine("  aisearch remove <file-id-or-folder>        Remove a file or folder from the index");
  Console.WriteLine("  aisearch create-index                      Create or update the search index");
  Console.WriteLine("  aisearch reindex                           Re-process all documents");
  Console.WriteLine();
  Console.WriteLine("Options:");
  Console.WriteLine("  --no-text    Skip adding extracted text to the full-text index (vector still generated)");
  Console.WriteLine();
  Console.WriteLine("Folder mode processes all files recursively. Duplicate filenames are skipped on add.");
}
