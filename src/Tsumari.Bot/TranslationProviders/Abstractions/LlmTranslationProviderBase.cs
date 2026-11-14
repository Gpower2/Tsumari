using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using Tsumari.Bot.Models;
using Tsumari.Bot.Services;

namespace Tsumari.Bot.TranslationProviders.Abstractions
{
    public abstract class LlmTranslationProviderBase : ITranslationProvider
    {
        protected Tsumari.Bot.TranslationProviders.Models.LlmModel? AnalysisModel { get; set; }
        protected Tsumari.Bot.TranslationProviders.Models.LlmModel? TranslationModel { get; set; }

        protected void ConfigureModelProfiles(Microsoft.Extensions.Configuration.IConfiguration configuration, System.Net.Http.IHttpClientFactory clients, string transport)
        {
            var selectedProvider = configuration["Translation:Provider"];
            var parsedProvider = Enum.TryParse<TranslationProvider>(selectedProvider, true, out var parsed) && Enum.IsDefined(parsed)
                ? parsed : TranslationProvider.Ollama;
            var selectedTransport = parsedProvider == TranslationProvider.OpenAI ? "OpenAI" : "Ollama";
            if (selectedTransport != transport || parsedProvider == TranslationProvider.DeepL) return;
            var selected = new Tsumari.Bot.TranslationProviders.Models.LlmModelFactory(configuration, clients).CreateSelected(transport);
            if (selected is null) return;
            AnalysisModel = selected.Value.Analysis;
            TranslationModel = selected.Value.Translation;
        }

        public abstract bool IsActive { get; }

        public bool UsesCharacterQuota => false;

        public virtual TranslationProviderConfigurationReport GetConfigurationReport()
        {
            if (AnalysisModel is not null && TranslationModel is not null)
                return new TranslationProviderConfigurationReport(ProviderName, GetType().Name, IsActive, UsesCharacterQuota,
                [new("Endpoint", TranslationModel.Profile.Endpoint), new("Model", TranslationModel.Profile.Model), new("Analysis profile", AnalysisModel.Profile.Name), new("Analysis model", AnalysisModel.Profile.Model), new("Translation profile", TranslationModel.Profile.Name), new("Prompt revision", TranslationModel.Profile.PromptRevision), new("Capabilities", "Profile-configured language analysis and translation")]);
            return new TranslationProviderConfigurationReport(
                ProviderName,
                GetType().Name,
                IsActive,
                UsesCharacterQuota,
                [
                    new("Endpoint", FormatConfiguredValue(ConfiguredEndpoint)),
                    new("Model", FormatConfiguredValue(ConfiguredModel)),
                    new("Capabilities", "Mixed-language analysis and translation")
                ]);
        }

        public async Task<LanguageAnalysisResult> AnalyzeLanguageAsync(string text)
        {
            if (!IsActive)
            {
                throw new InvalidOperationException("Translation provider is not active.");
            }

            if (AnalysisModel is not null) return await AnalysisModel.AnalyzeAsync(text);

            var prepared = Tsumari.Bot.TranslationProviders.Formatting.TranslateGemmaFormatter.PrepareAnalysis(text);
            var systemPrompt = prepared.System;
            var userPrompt = prepared.Prompt;
            var rawResult = await CallLanguageAnalysisModelAsync(systemPrompt, userPrompt);
            return ParseLanguageAnalysisResult(rawResult);
        }

        public virtual bool SupportsGenerationOptions => TranslationModel?.Profile.SupportsRetry ?? false;

        public Task<string> TranslateTextAsync(string text, string targetLanguageCode, string? sourceLanguageCode = null)
            => TranslateCoreAsync(text, targetLanguageCode, sourceLanguageCode, null);

        public Task<string> TranslateWithOptionsAsync(string text, string targetLanguageCode, string? sourceLanguageCode, TranslationGenerationOptions options)
            => SupportsGenerationOptions ? TranslateCoreAsync(text, targetLanguageCode, sourceLanguageCode, options)
                : throw new NotSupportedException("This provider does not support retry generation settings.");

        private async Task<string> TranslateCoreAsync(string text, string targetLanguageCode, string? sourceLanguageCode, TranslationGenerationOptions? options)
        {
            if (!IsActive)
            {
                throw new InvalidOperationException("Translation provider is not active.");
            }

            if (TranslationModel is not null) return await TranslationModel.TranslateAsync(text, targetLanguageCode, sourceLanguageCode, options);

            var prepared = Tsumari.Bot.TranslationProviders.Formatting.TranslateGemmaFormatter.PrepareTranslation(text, targetLanguageCode, sourceLanguageCode);
            var systemPrompt = prepared.System;
            var userPrompt = prepared.Prompt;
            var result = await CallTranslationModelAsync(systemPrompt, userPrompt, options);
            return new Tsumari.Bot.TranslationProviders.Formatting.TranslateGemmaFormatter().NormalizeTranslation(result);
        }

        protected abstract string ProviderName { get; }

        protected abstract string? ConfiguredEndpoint { get; }

        protected abstract string? ConfiguredModel { get; }

        protected virtual Task<string> CallLanguageAnalysisModelAsync(string systemPrompt, string userPrompt)
        {
            return CallModelAsync(systemPrompt, userPrompt);
        }

        protected virtual Task<string> CallTranslationModelAsync(string systemPrompt, string userPrompt, TranslationGenerationOptions? options)
            => CallModelAsync(systemPrompt, userPrompt);

        protected abstract Task<string> CallModelAsync(string systemPrompt, string userPrompt);

        private static string FormatConfiguredValue(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "not configured"
                : value;
        }

        private static LanguageAnalysisResult ParseLanguageAnalysisResult(string rawResult)
            => Tsumari.Bot.TranslationProviders.Parsing.LanguageAnalysisResponseParser.Parse(rawResult);
    }
}
