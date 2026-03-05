using Azure.Storage.Blobs;
using AiSearchCli.Models;

namespace AiSearchCli.Services;

/// <summary>
/// Handles uploading and deleting files in Azure Blob Storage.
/// Files are stored with GUID-based names to obscure original filenames.
/// </summary>
public class BlobService
{
  private readonly BlobContainerClient _containerClient;

  public BlobService(AzureBlobStorageConfig config)
  {
    var serviceClient = new BlobServiceClient(config.ConnectionString);
    _containerClient = serviceClient.GetBlobContainerClient(config.ContainerName);
  }

  /// <summary>
  /// Uploads a file to blob storage using a GUID-based name.
  /// Returns the public URL of the uploaded blob.
  /// </summary>
  public async Task<string> UploadAsync(string localFilePath, string blobName)
  {
    var blobClient = _containerClient.GetBlobClient(blobName);

    using var stream = File.OpenRead(localFilePath);
    await blobClient.UploadAsync(stream, overwrite: true);

    return blobClient.Uri.ToString();
  }

  /// <summary>
  /// Deletes a blob by its GUID-based name.
  /// Returns true if deleted, false if not found.
  /// </summary>
  public async Task<bool> DeleteAsync(string blobName)
  {
    var blobClient = _containerClient.GetBlobClient(blobName);
    var response = await blobClient.DeleteIfExistsAsync();
    return response.Value;
  }
}
