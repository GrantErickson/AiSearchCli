# Azure AI Search — Multi-Modal File Search CLI

## Overview

Build a .NET 10 command-line application (`AiSearchCli`) that allows users to:

- **Add** a local file (text, HTML, PDF, JPG, etc.) — compute multi-modal embeddings via Azure AI Vision, upload the file to Azure Blob Storage (with a GUID filename), and push an index entry to Azure AI Search.
- **Search** by text query — return the top 10 matches using hybrid search (vector + full-text keyword) with semantic ranking.
- **Remove** a file by original filename or document ID — delete from both the index and Blob Storage.

### Key Design Decisions

| Decision             | Choice                                                                    |
| -------------------- | ------------------------------------------------------------------------- |
| Embedding Model      | Azure AI Vision multimodal embeddings (Florence) — 1024 dimensions        |
| .NET Version         | .NET 10                                                                   |
| Search Type          | Hybrid (vector + full-text) with semantic ranking                         |
| Authentication       | API keys throughout                                                       |
| AI Search Tier       | Free (F) — 1 index, 50 MB storage                                         |
| Blob Access          | Public anonymous read, API key for writes                                 |
| Blob Naming          | GUIDs for blob names; original filenames stored in the index              |
| File Size Limit      | 5 MB max                                                                  |
| Duplicates           | Allowed — each upload creates a new entry with a timestamp                |
| Text Indexing Option | User can opt out of adding extracted text to the full-text index per file |

---

## Part 1: Create the Azure AI Search Instance (Portal)

### 1.1 Create the Resource

1. Go to the [Azure Portal](https://portal.azure.com).
2. Click **Create a resource** → search for **Azure AI Search** → click **Create**.
3. Fill in:
   - **Subscription**: Your subscription.
   - **Resource group**: Create new or use existing (e.g., `rg-aisearch`).
   - **Service name**: A globally unique name (e.g., `aisearch-demo-001`). Note this — it becomes part of your endpoint URL.
   - **Location**: Choose a region close to you that supports Azure AI Vision (e.g., `East US`, `West Europe`).
   - **Pricing tier**: **Free (F)**.
4. Click **Review + create** → **Create**.
5. Once deployed, go to the resource. Note down:
   - **Endpoint URL**: Found on the **Overview** page (e.g., `https://aisearch-demo-001.search.windows.net`).
   - **Admin API Key**: Go to **Settings → Keys** → copy the **Primary admin key**.
   - **Query API Key**: On the same page, copy or create a **Query key** (for search-only operations).

### 1.2 Create the Index

Go to **Search management → Indexes** → **Add index (JSON)**.

Paste the following JSON definition:

```json
{
  "name": "file-index",
  "fields": [
    {
      "name": "id",
      "type": "Edm.String",
      "key": true,
      "filterable": true,
      "searchable": false
    },
    {
      "name": "fileName",
      "type": "Edm.String",
      "searchable": true,
      "filterable": true,
      "sortable": true,
      "facetable": false,
      "retrievable": true
    },
    {
      "name": "fileType",
      "type": "Edm.String",
      "searchable": false,
      "filterable": true,
      "sortable": false,
      "facetable": true,
      "retrievable": true
    },
    {
      "name": "fileSize",
      "type": "Edm.Int64",
      "searchable": false,
      "filterable": true,
      "sortable": true,
      "facetable": false,
      "retrievable": true
    },
    {
      "name": "blobUrl",
      "type": "Edm.String",
      "searchable": false,
      "filterable": false,
      "sortable": false,
      "facetable": false,
      "retrievable": true
    },
    {
      "name": "blobName",
      "type": "Edm.String",
      "searchable": false,
      "filterable": true,
      "sortable": false,
      "facetable": false,
      "retrievable": true
    },
    {
      "name": "uploadDate",
      "type": "Edm.DateTimeOffset",
      "searchable": false,
      "filterable": true,
      "sortable": true,
      "facetable": false,
      "retrievable": true
    },
    {
      "name": "contentText",
      "type": "Edm.String",
      "searchable": true,
      "filterable": false,
      "sortable": false,
      "facetable": false,
      "retrievable": true,
      "analyzer": "en.microsoft"
    },
    {
      "name": "contentVector",
      "type": "Collection(Edm.Single)",
      "searchable": true,
      "retrievable": false,
      "dimensions": 1024,
      "vectorSearchProfile": "vector-profile"
    },
    {
      "name": "textIncludedInSearch",
      "type": "Edm.Boolean",
      "searchable": false,
      "filterable": true,
      "sortable": false,
      "facetable": false,
      "retrievable": true
    }
  ],
  "vectorSearch": {
    "algorithms": [
      {
        "name": "hnsw-algorithm",
        "kind": "hnsw",
        "hnswParameters": {
          "m": 4,
          "efConstruction": 400,
          "efSearch": 500,
          "metric": "cosine"
        }
      }
    ],
    "profiles": [
      {
        "name": "vector-profile",
        "algorithmConfigurationName": "hnsw-algorithm"
      }
    ]
  },
  "semantic": {
    "configurations": [
      {
        "name": "semantic-config",
        "prioritizedFields": {
          "titleField": {
            "fieldName": "fileName"
          },
          "contentFields": [
            {
              "fieldName": "contentText"
            }
          ]
        }
      }
    ]
  }
}
```

#### Index Fields Explained

| Field                  | Type               | Purpose                                                                  |
| ---------------------- | ------------------ | ------------------------------------------------------------------------ |
| `id`                   | String (Key)       | Unique document ID — a GUID generated at upload time                     |
| `fileName`             | String             | Original file name (e.g., `report.pdf`) — searchable for keyword matches |
| `fileType`             | String             | File extension (e.g., `.pdf`, `.jpg`, `.html`) — filterable              |
| `fileSize`             | Int64              | File size in bytes — filterable and sortable                             |
| `blobUrl`              | String             | Full public URL to the file in Blob Storage                              |
| `blobName`             | String             | GUID-based blob name (e.g., `a1b2c3d4-...-.pdf`)                         |
| `uploadDate`           | DateTimeOffset     | When the file was uploaded — sortable for finding newest entries         |
| `contentText`          | String             | Extracted text content for full-text search (optional per file)          |
| `contentVector`        | Collection(Single) | 1024-dimension vector from Azure AI Vision multimodal embeddings         |
| `textIncludedInSearch` | Boolean            | Whether text was included in the full-text index for this document       |

---

## Part 2: Create the Azure AI Vision Resource (Portal)

Azure AI Vision provides the multimodal embeddings API (Florence model) that generates 1024-dimensional vectors for both images and text.

### 2.1 Create the Resource

1. In the [Azure Portal](https://portal.azure.com), click **Create a resource** → search for **Computer Vision** → click **Create**.
2. Fill in:
   - **Subscription**: Same as above.
   - **Resource group**: Same as above (e.g., `rg-aisearch`).
   - **Region**: Must be a region that supports multimodal embeddings. Recommended: **East US**, **West Europe**, **Japan East**, **Southeast Asia**, or **West US**. Check [regional availability](https://learn.microsoft.com/en-us/azure/ai-services/computer-vision/how-to/image-retrieval) for the latest.
   - **Name**: A unique name (e.g., `aivision-demo-001`).
   - **Pricing tier**: **Free (F0)** (20 calls/minute) or **Standard (S1)** for higher throughput.
3. Click **Review + create** → **Create**.
4. Once deployed, go to the resource. Note down:
   - **Endpoint**: Found on **Overview** (e.g., `https://aivision-demo-001.cognitiveservices.azure.com`).
   - **API Key**: Go to **Resource Management → Keys and Endpoint** → copy **Key 1**.

### 2.2 Verify Multimodal Embeddings Support

The multimodal embeddings API endpoint is:

```
POST {endpoint}/computervision/retrieval:vectorizeImage?api-version=2024-02-01&model-version=2023-04-15
POST {endpoint}/computervision/retrieval:vectorizeText?api-version=2024-02-01&model-version=2023-04-15
```

No additional model deployment is needed — the Florence model is built into the Computer Vision resource.

---

## Part 3: Create Azure Blob Storage (Portal)

### 3.1 Create the Storage Account

1. In the [Azure Portal](https://portal.azure.com), click **Create a resource** → search for **Storage account** → click **Create**.
2. Fill in:
   - **Subscription**: Same as above.
   - **Resource group**: Same as above (e.g., `rg-aisearch`).
   - **Storage account name**: A globally unique name, lowercase letters and numbers only (e.g., `aisearchfiles001`).
   - **Region**: Same region as your other resources.
   - **Performance**: **Standard**.
   - **Redundancy**: **LRS** (Locally-redundant storage) is fine for demo purposes.
3. Click **Review + create** → **Create**.

### 3.2 Enable Anonymous Blob Access

1. Go to the storage account → **Settings → Configuration**.
2. Set **Allow Blob anonymous access** to **Enabled**.
3. Click **Save**.

### 3.3 Create the Container

1. Go to **Data storage → Containers** → **+ Container**.
2. Fill in:
   - **Name**: `files`
   - **Anonymous access level**: **Blob (anonymous read access for blobs only)**
3. Click **Create**.

### 3.4 Note Down Keys

1. Go to **Security + networking → Access keys**.
2. Copy **Storage account name** and **Key 1** (or the **Connection string**).

The public blob URL pattern will be:

```
https://<storage-account-name>.blob.core.windows.net/files/<guid-blob-name>
```

---

## Part 4: Permissions Summary

Since we are using API keys throughout, no special RBAC role assignments are needed. Just ensure you have:

| Resource        | Key Needed                       | Where to Find                                                |
| --------------- | -------------------------------- | ------------------------------------------------------------ |
| Azure AI Search | Admin API Key                    | Search service → Settings → Keys                             |
| Azure AI Search | Query API Key                    | Search service → Settings → Keys (for search-only, optional) |
| Azure AI Vision | API Key (Key 1)                  | Computer Vision → Resource Management → Keys and Endpoint    |
| Blob Storage    | Connection String or Account Key | Storage account → Security + networking → Access keys        |

> **Security Note**: For a production application, you should use managed identities and Azure Key Vault instead of API keys. API keys are used here for simplicity.

---

## Part 5: Configuration File

Create an `appsettings.json` file in the project root with the following structure. Replace all placeholder values with your actual keys and endpoints.

```json
{
  "AzureAISearch": {
    "Endpoint": "https://<your-search-service-name>.search.windows.net",
    "AdminApiKey": "<your-search-admin-api-key>",
    "IndexName": "file-index"
  },
  "AzureAIVision": {
    "Endpoint": "https://<your-vision-resource-name>.cognitiveservices.azure.com",
    "ApiKey": "<your-vision-api-key>"
  },
  "AzureBlobStorage": {
    "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=<your-storage-account>;AccountKey=<your-storage-key>;EndpointSuffix=core.windows.net",
    "ContainerName": "files"
  },
  "Settings": {
    "MaxFileSizeMB": 5
  }
}
```

> **Important**: Add `appsettings.json` to your `.gitignore` file to avoid committing secrets to source control. Include an `appsettings.template.json` with empty values as a reference.

---

## Part 6: C# Command-Line Application (`AiSearchCli`)

### 6.1 Project Setup

```bash
dotnet new console -n AiSearchCli --framework net10.0
cd AiSearchCli
```

### 6.2 NuGet Packages

```bash
dotnet add package Azure.Search.Documents
dotnet add package Azure.Storage.Blobs
dotnet add package Microsoft.Extensions.Configuration
dotnet add package Microsoft.Extensions.Configuration.Json
dotnet add package System.CommandLine
```

### 6.3 Application Architecture

```
AiSearchCli/
├── Program.cs                  # Entry point, command definitions
├── Services/
│   ├── SearchService.cs        # Azure AI Search operations (index, query, delete)
│   ├── EmbeddingService.cs     # Azure AI Vision multimodal embedding calls
│   └── BlobService.cs          # Azure Blob Storage upload/delete
├── Models/
│   └── FileDocument.cs         # Model matching the search index schema
├── appsettings.json            # Configuration (gitignored)
├── appsettings.template.json   # Template for configuration
└── AiSearchCli.csproj
```

### 6.4 CLI Commands

#### Add a File

```bash
aisearch add <filepath> [--no-text]
```

- Validates file exists and is under 5 MB.
- Generates a GUID for the document ID and blob name.
- Reads the file, extracts text content (for non-image files).
- Calls Azure AI Vision to vectorize the file:
  - For images (`.jpg`, `.jpeg`, `.png`, `.bmp`, `.gif`, `.tiff`): use `vectorizeImage`.
  - For text-based files (`.txt`, `.html`, `.pdf`, `.md`, `.docx`): extract text, then use `vectorizeText`.
- Uploads the file to Blob Storage with the GUID-based name (preserving the original extension, e.g., `a1b2c3d4.pdf`).
- Pushes a document to the search index containing all metadata + the embedding vector.
- The `--no-text` flag skips adding extracted text to the `contentText` field (the vector is still generated).

**Output:**

```
File added successfully.
  Document ID: a1b2c3d4-5678-9abc-def0-123456789abc
  Blob URL:    https://aisearchfiles001.blob.core.windows.net/files/a1b2c3d4-5678-9abc-def0-123456789abc.pdf
  File:        report.pdf (245,832 bytes)
  Text indexed: Yes
```

#### Search

```bash
aisearch search "<query text>"
```

- Vectorizes the query text using Azure AI Vision `vectorizeText`.
- Executes a hybrid search (vector + full-text keyword) with semantic ranking against the index.
- Displays the top 10 results.

**Output:**

```
Search results for: "quarterly revenue report"

 #  Score   File Name              Type   Size       Uploaded             URL
 1  0.892   revenue-q3-2025.pdf    .pdf   1.2 MB     2025-12-01 14:30     https://...
 2  0.847   annual-report.pdf      .pdf   3.4 MB     2025-11-15 09:12     https://...
 3  0.731   budget-chart.png       .png   890 KB     2025-12-03 16:45     https://...
...
```

#### Remove

```bash
aisearch remove "<filename-or-id>"
```

- First attempts to find the document by exact document ID.
- If not found by ID, searches the index for documents matching the given filename.
- If multiple documents match the filename, lists them and asks the user to specify the document ID.
- Deletes the document from the search index.
- Deletes the corresponding blob from Blob Storage.

**Output (single match):**

```
Removed: report.pdf (ID: a1b2c3d4-5678-9abc-def0-123456789abc)
  Deleted from index: Yes
  Deleted from blob:  Yes
```

**Output (multiple matches):**

```
Multiple files found matching "report.pdf":

  ID                                     Uploaded
  a1b2c3d4-5678-9abc-def0-123456789abc   2025-12-01 14:30
  e5f6a7b8-9012-3456-7890-abcdef012345   2025-12-03 16:45

Please re-run with the specific document ID:
  aisearch remove "a1b2c3d4-5678-9abc-def0-123456789abc"
```

### 6.5 Key Implementation Details

#### Embedding Generation

For **images**, send the raw binary content to the Azure AI Vision `vectorizeImage` endpoint:

```
POST {endpoint}/computervision/retrieval:vectorizeImage?api-version=2024-02-01&model-version=2023-04-15
Content-Type: application/octet-stream
Ocp-Apim-Subscription-Key: {api-key}

<binary image data>
```

For **text**, send the extracted text to the `vectorizeText` endpoint:

```
POST {endpoint}/computervision/retrieval:vectorizeText?api-version=2024-02-01&model-version=2023-04-15
Content-Type: application/json
Ocp-Apim-Subscription-Key: {api-key}

{
  "text": "extracted text content here"
}
```

Both return a response with:

```json
{
  "modelVersion": "2023-04-15",
  "vector": [0.012, -0.034, 0.056, ... ]   // 1024 floats
}
```

#### Text Extraction Strategy

| File Type                               | Extraction Method                                                |
| --------------------------------------- | ---------------------------------------------------------------- |
| `.txt`, `.md`, `.csv`                   | Read file as UTF-8 text directly                                 |
| `.html`                                 | Read as text, strip HTML tags                                    |
| `.pdf`                                  | Use a PDF text extraction library (e.g., `PdfPig` NuGet package) |
| `.docx`                                 | Use `DocumentFormat.OpenXml` NuGet package                       |
| `.jpg`, `.png`, `.bmp`, `.gif`, `.tiff` | No text extraction — use image vectorization only                |

#### Hybrid Search Query

The search request combines vector search, keyword search, and semantic ranking:

```json
{
  "search": "<user query text>",
  "vectorQueries": [
    {
      "kind": "vector",
      "vector": [0.012, -0.034, ...],
      "fields": "contentVector",
      "k": 10
    }
  ],
  "queryType": "semantic",
  "semanticConfiguration": "semantic-config",
  "top": 10,
  "select": "id, fileName, fileType, fileSize, blobUrl, uploadDate, textIncludedInSearch"
}
```

### 6.6 Additional NuGet Packages for Text Extraction

```bash
dotnet add package UglyToad.PdfPig          # PDF text extraction
dotnet add package DocumentFormat.OpenXml    # DOCX text extraction
```

### 6.7 .gitignore

Ensure your `.gitignore` includes:

```
appsettings.json
bin/
obj/
```

---

## Part 7: Build & Run

```bash
cd AiSearchCli
dotnet build
```

### Run Commands

```bash
# Add a file (with text indexing)
dotnet run -- add "C:\Documents\report.pdf"

# Add a file without text indexing
dotnet run -- add "C:\Photos\chart.png" --no-text

# Search
dotnet run -- search "quarterly revenue report"

# Remove by filename
dotnet run -- remove "report.pdf"

# Remove by document ID
dotnet run -- remove "a1b2c3d4-5678-9abc-def0-123456789abc"
```

### Optional: Install as Global Tool

To use the `aisearch` command directly, add this to the `.csproj` file:

```xml
<PropertyGroup>
  <PackAsTool>true</PackAsTool>
  <ToolCommandName>aisearch</ToolCommandName>
</PropertyGroup>
```

Then:

```bash
dotnet pack
dotnet tool install --global --add-source ./nupkg AiSearchCli
```

Now you can use:

```bash
aisearch add "report.pdf"
aisearch search "revenue data"
aisearch remove "report.pdf"
```

---

## Part 8: Free Tier Limitations

Keep these limits in mind when using the Free (F) tier for Azure AI Search:

| Limit                    | Value       |
| ------------------------ | ----------- |
| Indexes                  | 1           |
| Documents per index      | 10,000      |
| Index storage            | 50 MB       |
| Vector index size        | ~28 MB      |
| Semantic ranking queries | 1,000/month |
| Indexers / Data sources  | 3 each      |

With 1024-dimension vectors (4 bytes each), each document's vector is ~4 KB. Combined with metadata, you can expect roughly **5,000–8,000 documents** depending on text content size.

For the AI Vision Free (F0) tier:

- 20 transactions per minute
- 5,000 transactions per month

---

## Appendix: Quick Reference — All Values to Configure

After creating all Azure resources, collect these values and add them to `appsettings.json`:

| Config Path                         | Value                                     | Where to Find It                                  |
| ----------------------------------- | ----------------------------------------- | ------------------------------------------------- |
| `AzureAISearch:Endpoint`            | `https://xxx.search.windows.net`          | AI Search → Overview                              |
| `AzureAISearch:AdminApiKey`         | `xxxxxxxx`                                | AI Search → Settings → Keys → Primary admin key   |
| `AzureAISearch:IndexName`           | `file-index`                              | As defined in Part 1.2                            |
| `AzureAIVision:Endpoint`            | `https://xxx.cognitiveservices.azure.com` | Computer Vision → Overview                        |
| `AzureAIVision:ApiKey`              | `xxxxxxxx`                                | Computer Vision → Keys and Endpoint → Key 1       |
| `AzureBlobStorage:ConnectionString` | `DefaultEndpointsProtocol=https;...`      | Storage account → Access keys → Connection string |
| `AzureBlobStorage:ContainerName`    | `files`                                   | As defined in Part 3.3                            |
