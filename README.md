# AiSearchCli

A .NET 10 command-line tool for adding, searching, and removing multi-modal files (text, HTML, PDF, images, etc.) using Azure AI Search, Azure AI Vision multimodal embeddings, and Azure Blob Storage.

## Features

- **Add files** — Generates 1024-dim vector embeddings via Azure AI Vision (Florence model), uploads the file to Blob Storage with a GUID-based name, and indexes it in Azure AI Search.
- **Hybrid search** — Combines vector similarity, full-text keyword search, and semantic ranking to return the top 10 results.
- **Remove files** — Deletes from both the search index and Blob Storage by filename or document ID.
- Supports images (JPG, PNG, BMP, GIF, TIFF), PDFs, DOCX, HTML, and plain text files.
- Files are limited to 5 MB. Duplicates are allowed (each upload gets a unique ID and timestamp).

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Azure subscription with the following resources created manually:
  - **Azure AI Search** (Free tier) — see [docs/01-AzureAISearch-Setup.md](docs/01-AzureAISearch-Setup.md)
  - **Azure AI Vision** (Computer Vision) — see [docs/02-AzureAIVision-Setup.md](docs/02-AzureAIVision-Setup.md)
  - **Azure Blob Storage** — see [docs/03-AzureBlobStorage-Setup.md](docs/03-AzureBlobStorage-Setup.md)

## Configuration

### Option 1: `appsettings.json`

Copy the template and fill in your values:

```bash
cd AiSearchCli
cp appsettings.template.json appsettings.json
```

Then edit `appsettings.json`:

```json
{
  "AzureAISearch": {
    "Endpoint": "https://<your-search-service>.search.windows.net",
    "AdminApiKey": "<your-admin-key>",
    "IndexName": "file-index"
  },
  "AzureAIVision": {
    "Endpoint": "https://<your-vision-resource>.cognitiveservices.azure.com",
    "ApiKey": "<your-vision-key>"
  },
  "AzureBlobStorage": {
    "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=<account>;AccountKey=<key>;EndpointSuffix=core.windows.net",
    "ContainerName": "files"
  },
  "Settings": {
    "MaxFileSizeMB": 5
  }
}
```

> **`appsettings.json` is gitignored** — it will not be committed to source control.

### Option 2: User Secrets (recommended for development)

Store secrets outside the project directory:

```bash
cd AiSearchCli
dotnet user-secrets set "AzureAISearch:Endpoint" "https://<your-search-service>.search.windows.net"
dotnet user-secrets set "AzureAISearch:AdminApiKey" "<your-admin-key>"
dotnet user-secrets set "AzureAIVision:Endpoint" "https://<your-vision-resource>.cognitiveservices.azure.com"
dotnet user-secrets set "AzureAIVision:ApiKey" "<your-vision-key>"
dotnet user-secrets set "AzureBlobStorage:ConnectionString" "<your-connection-string>"
```

User secrets override `appsettings.json` values and are stored in `%APPDATA%\Microsoft\UserSecrets\aisearch-cli\secrets.json`.

## Build

```bash
cd AiSearchCli
dotnet build
```

## Usage

```bash
# Add a file (with full-text indexing)
dotnet run -- add "C:\Documents\report.pdf"

# Add a file without text indexing (vector-only)
dotnet run -- add "C:\Photos\chart.png" --no-text

# Search for files
dotnet run -- search "quarterly revenue report"

# Remove by filename
dotnet run -- remove "report.pdf"

# Remove by document ID
dotnet run -- remove "a1b2c3d4-5678-9abc-def0-123456789abc"
```

### Commands

| Command | Description |
|---|---|
| `add <filepath> [--no-text]` | Upload file to Blob Storage, generate embeddings, and index. `--no-text` skips adding extracted text to the keyword index. |
| `search "<query>"` | Hybrid vector + keyword search with semantic ranking. Returns top 10 results. |
| `remove "<filename-or-id>"` | Remove a file from the index and Blob Storage. If multiple files share a name, you'll be prompted to use the document ID. |

## Project Structure

```
AiSearchCli/
├── Program.cs                  # Entry point and CLI command routing
├── Models/
│   ├── AppConfig.cs            # Strongly typed configuration classes
│   ├── FileDocument.cs         # Search index document model
│   └── SearchResult.cs         # Search result display model
├── Services/
│   ├── BlobService.cs          # Azure Blob Storage upload/delete
│   ├── EmbeddingService.cs     # Azure AI Vision multimodal embeddings
│   ├── SearchService.cs        # Azure AI Search index/query/delete
│   └── TextExtractor.cs       # Text extraction (PDF, DOCX, HTML, plain text)
├── appsettings.json            # Local config (gitignored)
└── appsettings.template.json   # Config template with placeholders
```

## Azure Setup Guides

Step-by-step portal instructions for creating each resource:

- [01-AzureAISearch-Setup.md](docs/01-AzureAISearch-Setup.md) — Search service + index JSON definition
- [02-AzureAIVision-Setup.md](docs/02-AzureAIVision-Setup.md) — Computer Vision resource for multimodal embeddings
- [03-AzureBlobStorage-Setup.md](docs/03-AzureBlobStorage-Setup.md) — Storage account with public anonymous read access
