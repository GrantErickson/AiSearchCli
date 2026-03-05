namespace AiSearchCli.Models;

/// <summary>
/// Strongly typed configuration sections matching appsettings.json.
/// </summary>
public class AppConfig
{
  public AzureAISearchConfig AzureAISearch { get; set; } = new();
  public AzureAIVisionConfig AzureAIVision { get; set; } = new();
  public AzureBlobStorageConfig AzureBlobStorage { get; set; } = new();
  public SettingsConfig Settings { get; set; } = new();
}

public class AzureAISearchConfig
{
  public string Endpoint { get; set; } = string.Empty;
  public string AdminApiKey { get; set; } = string.Empty;
  public string IndexName { get; set; } = "file-index";
}

public class AzureAIVisionConfig
{
  public string Endpoint { get; set; } = string.Empty;
  public string ApiKey { get; set; } = string.Empty;
}

public class AzureBlobStorageConfig
{
  public string ConnectionString { get; set; } = string.Empty;
  public string ContainerName { get; set; } = "files";
}

public class SettingsConfig
{
  public int MaxFileSizeMB { get; set; } = 5;
}
