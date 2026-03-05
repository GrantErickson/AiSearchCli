# Azure Blob Storage — Setup Instructions

Azure Blob Storage is used to store the uploaded files. Files are stored with GUID-based names for privacy, while the original filenames are recorded in the search index.

## 1. Create the Storage Account

1. In the [Azure Portal](https://portal.azure.com), click **Create a resource**.
2. Search for **Storage account** → click **Create**.
3. Fill in:
   - **Subscription**: Your subscription.
   - **Resource group**: Same resource group (e.g., `rg-aisearch`).
   - **Storage account name**: Globally unique, lowercase letters and numbers only (e.g., `aisearchfiles001`).
   - **Region**: Same region as your other resources.
   - **Performance**: **Standard**.
   - **Redundancy**: **LRS** (Locally-redundant storage) — sufficient for demo/dev use.
4. Click **Review + create** → **Create**.

## 2. Enable Anonymous Blob Access

1. Go to the storage account → **Settings → Configuration**.
2. Set **Allow Blob anonymous access** to **Enabled**.
3. Click **Save**.

## 3. Create the Container

1. Go to **Data storage → Containers**.
2. Click **+ Container**.
3. Fill in:
   - **Name**: `files`
   - **Anonymous access level**: **Blob (anonymous read access for blobs only)**
4. Click **Create**.

## 4. Note Down Connection Information

Go to **Security + networking → Access keys** and record:

| Value                    | Where to Find                                  |
| ------------------------ | ---------------------------------------------- |
| **Storage account name** | Shown at the top of the Access keys page       |
| **Key 1**                | Access keys page → Key 1                       |
| **Connection string**    | Access keys page → Connection string for Key 1 |

## 5. Public URL Pattern

Files will be accessible at:

```
https://<storage-account-name>.blob.core.windows.net/files/<guid-blob-name>
```

For example:

```
https://aisearchfiles001.blob.core.windows.net/files/a1b2c3d4-5678-9abc-def0-123456789abc.pdf
```

## 6. Blob Naming Convention

- Blob names are GUIDs with the original file extension preserved (e.g., `a1b2c3d4-5678-9abc-def0-123456789abc.pdf`).
- The original filename is stored in the search index `fileName` field.
- This provides a degree of obscurity for public URLs while keeping files browseable by type.

## 7. Values Required for `appsettings.json`

```json
{
  "AzureBlobStorage": {
    "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=<your-storage-account>;AccountKey=<your-storage-key>;EndpointSuffix=core.windows.net",
    "ContainerName": "files"
  }
}
```
