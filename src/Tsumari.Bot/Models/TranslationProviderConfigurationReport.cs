namespace Tsumari.Bot.Models
{
    public sealed record TranslationProviderConfigurationReport(
        string ProviderName,
        string ImplementationName,
        bool IsActive,
        bool UsesCharacterQuota,
        IReadOnlyList<TranslationProviderConfigurationItem> Details);

    public sealed record TranslationProviderConfigurationItem(
        string Label,
        string Value);
}
