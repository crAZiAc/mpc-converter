using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anthropic;
using Anthropic.Models.Messages;

namespace MpcConverter.Core.Classification;

/// <summary>
/// Default <see cref="IAnthropicInvoker"/> backed by the official Anthropic .NET
/// SDK. Sends one message and returns the model's text response. Only the prompt
/// (pad sample names) is transmitted.
/// </summary>
public sealed class AnthropicSdkInvoker : IAnthropicInvoker
{
    private readonly AnthropicClient _client;

    public AnthropicSdkInvoker(string apiKey)
    {
        _client = new AnthropicClient { ApiKey = apiKey };
    }

    public async Task<string> GetGroupingJsonAsync(string model, string prompt, CancellationToken ct)
    {
        var response = await _client.Messages.Create(new MessageCreateParams
        {
            Model = model,
            MaxTokens = 4096,
            Messages = [new() { Role = Role.User, Content = prompt }],
        });

        return string.Concat(
            response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
    }
}
