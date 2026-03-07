using System.ComponentModel;
using System.Text;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using AiSearchCli.Models;

namespace AiSearchCli.Services;

/// <summary>
/// Uses Microsoft.Extensions.AI with Azure OpenAI to answer questions.
/// The LLM autonomously calls the AI Search index tool via function calling.
/// </summary>
public class ChatService
{
  private readonly AzureOpenAIConfig _config;
  private readonly SearchService _searchService;
  private readonly EmbeddingService _embeddingService;

  public ChatService(
      AzureOpenAIConfig config,
      SearchService searchService,
      EmbeddingService embeddingService)
  {
    _config = config;
    _searchService = searchService;
    _embeddingService = embeddingService;
  }

  /// <summary>
  /// Asks a question using an AI chat client that can search the index as a tool.
  /// Streams the response to the console.
  /// </summary>
  public async Task<AskResult> AskAsync(string question)
  {
    var indexSources = new List<DocumentSnippet>();

    var tools = new List<AITool>
    {
      AIFunctionFactory.Create(
        async ([Description("The search query to find relevant documents")] string query) =>
        {
          Console.ForegroundColor = ConsoleColor.DarkCyan;
          Console.WriteLine($"  [Tool Call] SearchIndex(\"{query}\")");
          Console.ResetColor();

          var vector = await _embeddingService.VectorizeTextAsync(query);
          var results = await _searchService.HybridSearchWithContentAsync(query, vector);
          indexSources.AddRange(results);

          if (results.Count == 0)
          {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("  [Tool Result] No documents found.");
            Console.ResetColor();
            return "No documents found in the index for this query.";
          }

          Console.ForegroundColor = ConsoleColor.DarkCyan;
          Console.WriteLine($"  [Tool Result] {results.Count} document(s) found:");
          foreach (var doc in results)
          {
            Console.WriteLine($"    - {doc.FileName} (Score: {doc.Score:F3})");
          }
          Console.ResetColor();

          var sb = new StringBuilder();
          foreach (var doc in results)
          {
            sb.AppendLine($"--- {doc.FileName} (Score: {doc.Score:F3}) ---");
            sb.AppendLine(doc.ContentText ?? "(no text content)");
            sb.AppendLine($"URL: {doc.BlobUrl}");
            sb.AppendLine();
          }
          return sb.ToString();
        },
        "SearchIndex",
        "Search the document index for files matching a query. Returns file names and content text.")
    };

    var instructions = """
        You are a helpful assistant that answers questions using your available tools.
        Always search the index first to find relevant documents.
        Cite your sources by referencing file names or URLs.
        """;

    var openAiClient = new AzureOpenAIClient(
        new Uri(_config.Endpoint),
        new AzureKeyCredential(_config.ApiKey));

    IChatClient chatClient = new ChatClientBuilder(
        openAiClient
            .GetChatClient(_config.DeploymentName)
            .AsIChatClient())
        .UseFunctionInvocation()
        .Build();

    var messages = new List<ChatMessage>
    {
      new(ChatRole.System, instructions),
      new(ChatRole.User, question)
    };

    var options = new ChatOptions
    {
      Tools = [.. tools]
    };

    var response = await chatClient.GetResponseAsync(messages, options);
    Console.Write(response.Text);
    Console.WriteLine();

    return new AskResult
    {
      IndexSources = indexSources
    };
  }
}

/// <summary>
/// Result from an ask operation including the sources used.
/// </summary>
public record AskResult
{
  public List<DocumentSnippet> IndexSources { get; init; } = [];
}

/// <summary>
/// A document snippet from the search index used as RAG context.
/// </summary>
public record DocumentSnippet
{
  public string FileName { get; init; } = string.Empty;
  public string? ContentText { get; init; }
  public string BlobUrl { get; init; } = string.Empty;
  public double Score { get; init; }
}
