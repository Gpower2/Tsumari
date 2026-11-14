using Tsumari.Bot.TranslationProviders.Models;
using Tsumari.Bot.TranslationProviders.Requests;
namespace Tsumari.Bot.TranslationProviders.Clients;

public interface ILlmClient
{
    Task<ModelResponse> GenerateAsync(ModelProfile profile, PreparedModelRequest request, TranslationGenerationOptions? options = null, CancellationToken cancellationToken = default);
}
