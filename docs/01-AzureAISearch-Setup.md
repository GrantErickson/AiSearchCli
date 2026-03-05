# Azure AI Search — Setup Instructions

## 1. Create the Azure AI Search Resource

1. Go to the [Azure Portal](https://portal.azure.com).
2. Click **Create a resource** → search for **Azure AI Search** → click **Create**.
3. Fill in:
   - **Subscription**: Your subscription.
   - **Resource group**: Create new or use existing (e.g., `rg-aisearch`).
   - **Service name**: A globally unique name (e.g., `aisearch-demo-001`). This becomes your endpoint URL.
   - **Location**: Choose a region that also supports Azure AI Vision multimodal embeddings (e.g., `East US`, `West Europe`).
   - **Pricing tier**: **Free (F)**.
4. Click **Review + create** → **Create**.

## 2. Note Down Connection Information

Once deployed, go to the resource and record:

| Value             | Where to Find                                                        |
| ----------------- | -------------------------------------------------------------------- |
| **Endpoint URL**  | Overview page (e.g., `https://aisearch-demo-001.search.windows.net`) |
| **Admin API Key** | Settings → Keys → Primary admin key                                  |
| **Query API Key** | Settings → Keys → Query key (create one if none exist)               |

## 3. Create the Index

1. Go to **Search management → Indexes**.
2. Click **Add index (JSON)**.
3. Paste the following JSON and click **Save**:

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
        "algorithm": "hnsw-algorithm"
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
          "prioritizedContentFields": [
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

## 4. Index Fields Reference

| Field                  | Type               | Purpose                                                |
| ---------------------- | ------------------ | ------------------------------------------------------ |
| `id`                   | String (Key)       | Unique document ID (GUID)                              |
| `fileName`             | String             | Original file name (searchable)                        |
| `fileType`             | String             | File extension (e.g., `.pdf`) — filterable             |
| `fileSize`             | Int64              | File size in bytes                                     |
| `blobUrl`              | String             | Public URL to the blob                                 |
| `blobName`             | String             | GUID-based blob name in storage                        |
| `uploadDate`           | DateTimeOffset     | Upload timestamp                                       |
| `contentText`          | String             | Extracted text for full-text search (English analyzer) |
| `contentVector`        | Collection(Single) | 1024-dim multimodal embedding vector                   |
| `textIncludedInSearch` | Boolean            | Whether text was indexed for this document             |

## 5. Free Tier Limits

| Limit                    | Value       |
| ------------------------ | ----------- |
| Indexes                  | 1           |
| Documents per index      | 10,000      |
| Index storage            | 50 MB       |
| Vector index size        | ~28 MB      |
| Semantic ranking queries | 1,000/month |

With 1024-dimension vectors (~4 KB each) plus metadata, expect roughly 5,000–8,000 documents.

## 6. Values Required for `appsettings.json`

```json
{
  "AzureAISearch": {
    "Endpoint": "https://<your-search-service-name>.search.windows.net",
    "AdminApiKey": "<primary-admin-key>",
    "IndexName": "file-index"
  }
}
```
