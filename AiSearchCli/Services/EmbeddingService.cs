using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AiSearchCli.Models;

namespace AiSearchCli.Services;

/// <summary>
/// Generates multimodal embeddings using the Azure AI Vision (Florence) API.
/// Supports both image vectorization and text vectorization.
/// </summary>
public class EmbeddingService
{
  private readonly string _endpoint;
  private readonly string _apiKey;
  private readonly HttpClient _httpClient;

  private const string ApiVersion = "2024-02-01";
  private const string ModelVersion = "2023-04-15";

  private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif"
    };

  public EmbeddingService(AzureAIVisionConfig config)
  {
    _endpoint = config.Endpoint.TrimEnd('/');
    _apiKey = config.ApiKey;
    _httpClient = new HttpClient();
  }

  /// <summary>
  /// Returns true if the file extension indicates an image format.
  /// </summary>
  public static bool IsImageFile(string extension)
  {
    return ImageExtensions.Contains(extension);
  }

  /// <summary>
  /// Vectorizes an image file by sending its binary content to the vectorizeImage endpoint.
  /// </summary>
  public async Task<float[]> VectorizeImageAsync(string filePath)
  {
    var url = $"{_endpoint}/computervision/retrieval:vectorizeImage?api-version={ApiVersion}&model-version={ModelVersion}";

    var fileBytes = await File.ReadAllBytesAsync(filePath);
    using var content = new ByteArrayContent(fileBytes);
    content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

    using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
    request.Headers.Add("Ocp-Apim-Subscription-Key", _apiKey);

    var response = await _httpClient.SendAsync(request);
    response.EnsureSuccessStatusCode();

    return await ParseVectorResponse(response);
  }

  /// <summary>
  /// Vectorizes a text string using the vectorizeText endpoint.
  /// </summary>
  public async Task<float[]> VectorizeTextAsync(string text)
  {
    var url = $"{_endpoint}/computervision/retrieval:vectorizeText?api-version={ApiVersion}&model-version={ModelVersion}";

    var payload = JsonSerializer.Serialize(new { text });
    using var content = new StringContent(payload, Encoding.UTF8, "application/json");

    using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
    request.Headers.Add("Ocp-Apim-Subscription-Key", _apiKey);

    var response = await _httpClient.SendAsync(request);
    response.EnsureSuccessStatusCode();

    return await ParseVectorResponse(response);
  }

  private static async Task<float[]> ParseVectorResponse(HttpResponseMessage response)
  {
    var json = await response.Content.ReadAsStringAsync();
    using var doc = JsonDocument.Parse(json);
    var vectorElement = doc.RootElement.GetProperty("vector");

    var vector = new float[vectorElement.GetArrayLength()];
    int i = 0;
    foreach (var element in vectorElement.EnumerateArray())
    {
      vector[i++] = element.GetSingle();
    }

    return vector;
  }
}
