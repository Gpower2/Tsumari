using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using Tsumari.Bot.Models;
using Tsumari.Bot.Services;

namespace Tsumari.Bot.Modules
{
    [Group("tsumari", "Tsumari admin, channel configuration, and diagnostic commands")]
    // Do not use DefaultMemberPermissions or per-subcommand contexts on this grouped command.
    // Discord applies both at the top-level /tsumari group, and mixing guild-only admin
    // commands with DM-capable probe commands otherwise prevents the updated command group
    // from publishing correctly.
    public class InteractionModule : InteractionModuleBase<IInteractionContext>
    {
        private const int MaxInteractionResponseLength = 1900;

        private readonly DatabaseService _dbService;
        private readonly TranslationService _translationService;
        private readonly IHistoricalMessageSyncService _historicalMessageSyncService;
        private readonly ILogger<InteractionModule> _logger;

        public InteractionModule(
            DatabaseService dbService,
            TranslationService translationService,
            IHistoricalMessageSyncService historicalMessageSyncService,
            ILogger<InteractionModule> logger)
        {
            _dbService = dbService;
            _translationService = translationService;
            _historicalMessageSyncService = historicalMessageSyncService;
            _logger = logger;
        }

        [SlashCommand("add-master", "Registers an independent Master Channel.")]
        public async Task AddMasterAsync(
            [Summary("channel", "The channel to register as Master")] IChannel channel)
        {
            if (!await EnsureGuildAdministratorAsync())
            {
                return;
            }

            // Fail-fast validation
            if (channel is not ITextChannel textChannel)
            {
                await RespondAsync("❌ Error: The channel must be a standard Guild Text Channel.", ephemeral: true);
                return;
            }

            try
            {
                // Check if it's already registered as Local
                if (await _dbService.IsLocalizedChannelAsync(channel.Id))
                {
                    await RespondAsync("❌ Error: This channel is already registered as a Localized Channel. Unregister it first.", ephemeral: true);
                    return;
                }

                bool added = await _dbService.AddMasterChannelAsync(channel.Id);
                if (added)
                {
                    _logger.LogMasterChannelRegistered(textChannel.Name, channel.Id, Context.User.Username);
                    await RespondAsync($"✅ Success: Registered <#{channel.Id}> as a Tsumari Master Channel.", ephemeral: false);
                }
                else
                {
                    await RespondAsync($"⚠️ Notice: <#{channel.Id}> is already registered as a Master Channel.", ephemeral: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogMasterChannelRegistrationFailed(ex, channel.Id);
                await RespondAsync("❌ An internal database error occurred while registering the master channel.", ephemeral: true);
            }
        }

        [SlashCommand("register-local", "Links a localized channel to a specific parent Master Channel.")]
        public async Task RegisterLocalAsync(
            [Summary("local-channel", "The localized channel to register")] IChannel localChannel,
            [Summary("master-channel", "The parent Master channel")] IChannel masterChannel,
            [Summary("language-code", "Target language or locale code (e.g. en, el, it, pt-br)")] string languageCode)
        {
            if (!await EnsureGuildAdministratorAsync())
            {
                return;
            }

            // Fail-fast validation
            if (localChannel is not ITextChannel localTextChannel || masterChannel is not ITextChannel masterTextChannel)
            {
                await RespondAsync("❌ Error: Both channels must be standard Guild Text Channels.", ephemeral: true);
                return;
            }

            if (string.IsNullOrWhiteSpace(languageCode))
            {
                await RespondAsync("❌ Error: You must specify a target language code.", ephemeral: true);
                return;
            }

            try
            {
                // Ensure the parent is indeed registered as a Master Channel
                if (!await _dbService.IsMasterChannelAsync(masterChannel.Id))
                {
                    await RespondAsync($"❌ Error: <#{masterChannel.Id}> is not registered as a Master Channel. Register it first using `/tsumari add-master`.", ephemeral: true);
                    return;
                }

                // Ensure local channel is not registered as Master channel itself
                if (await _dbService.IsMasterChannelAsync(localChannel.Id))
                {
                    await RespondAsync("❌ Error: The localized channel cannot be a Master Channel.", ephemeral: true);
                    return;
                }

                // If local channel is the same as master channel
                if (localChannel.Id == masterChannel.Id)
                {
                    await RespondAsync("❌ Error: Localized channel and Master channel cannot be the same.", ephemeral: true);
                    return;
                }

                string lang = LanguageCodeService.NormalizeStoredLanguageCode(languageCode);

                bool registered = await _dbService.RegisterLocalChannelAsync(localChannel.Id, masterChannel.Id, lang);
                if (registered)
                {
                    _logger.LogLocalizedChannelRegistered(localTextChannel.Name, masterTextChannel.Name, lang, Context.User.Username);
                    await RespondAsync($"✅ Success: Linked localized channel <#{localChannel.Id}> to Master <#{masterChannel.Id}> with target language **{LanguageCodeService.NormalizeLanguageCode(lang)}**.", ephemeral: false);
                }
                else
                {
                    await RespondAsync("❌ Error: Failed to register localized channel in database.", ephemeral: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogLocalizedChannelRegistrationFailed(ex, localChannel.Id, masterChannel.Id);
                await RespondAsync("❌ An internal database error occurred while registering the localized channel.", ephemeral: true);
            }
        }

        [SlashCommand("unregister", "Dynamically unregisters a Master or Localized channel.")]
        public async Task UnregisterAsync(
            [Summary("channel", "The channel to remove from Tsumari configuration")] IChannel channel)
        {
            if (!await EnsureGuildAdministratorAsync())
            {
                return;
            }

            // Fail-fast validation
            if (channel is not ITextChannel)
            {
                await RespondAsync("❌ Error: The channel must be a standard Guild Text Channel.", ephemeral: true);
                return;
            }

            try
            {
                bool deleted = await _dbService.UnregisterChannelAsync(channel.Id);
                if (deleted)
                {
                    _logger.LogChannelUnregisteredByUser(channel.Id, Context.User.Username);
                    await RespondAsync($"✅ Success: Unregistered <#{channel.Id}> from Tsumari. Sibling or cascading links have been purged.", ephemeral: false);
                }
                else
                {
                    await RespondAsync($"❌ Error: <#{channel.Id}> is not registered in Tsumari's Master or Localized database.", ephemeral: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogChannelUnregisterByUserFailed(ex, channel.Id);
                await RespondAsync("❌ An internal database error occurred while unregistering the channel.", ephemeral: true);
            }
        }

        [SlashCommand("detect-language", "Runs provider-backed language analysis for ad-hoc testing.")]
        public async Task DetectLanguageAsync(
            [Summary("text", "The text to analyze")] string text)
        {
            if (!await EnsureGuildAdministratorOrAllowDmAsync())
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                await RespondAsync("❌ Error: You must provide text to analyze.", ephemeral: true);
                return;
            }

            if (!_translationService.IsActive)
            {
                await RespondAsync("❌ Error: Translation provider is not active.", ephemeral: true);
                return;
            }

            // Provider-backed probes can exceed Discord's initial response window, so defer first.
            await Context.Interaction.DeferAsync(ephemeral: true);

            try
            {
                var analysis = await _translationService.AnalyzeLanguageAsync(text);
                _logger.LogManualLanguageDetectionCompleted(
                    Context.User.Username,
                    analysis.PrimaryLanguageCode,
                    analysis.IsMixed,
                    analysis.HasClearDominantLanguage);

                await Context.Interaction.FollowupAsync(
                    BuildLanguageDetectionResponse(analysis),
                    ephemeral: true);
            }
            catch (Exception ex)
            {
                _logger.LogManualLanguageDetectionFailed(ex, Context.User.Username);
                await Context.Interaction.FollowupAsync(
                    BuildFailureResponse("Language detection failed"),
                    ephemeral: true);
            }
        }

        [SlashCommand("translate", "Runs provider-backed translation for ad-hoc testing.")]
        public async Task TranslateAsync(
            [Summary("target-language", "Target language or locale code (e.g. en, el, it, pt-br)")] string targetLanguageCode,
            [Summary("text", "The text to translate")] string text)
        {
            if (!await EnsureGuildAdministratorOrAllowDmAsync())
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(targetLanguageCode))
            {
                await RespondAsync("❌ Error: You must specify a target language code.", ephemeral: true);
                return;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                await RespondAsync("❌ Error: You must provide text to translate.", ephemeral: true);
                return;
            }

            if (!_translationService.IsActive)
            {
                await RespondAsync("❌ Error: Translation provider is not active.", ephemeral: true);
                return;
            }

            var normalizedTargetLanguageCode = LanguageCodeService.NormalizeLanguageCode(targetLanguageCode);
            if (string.IsNullOrWhiteSpace(normalizedTargetLanguageCode))
            {
                await RespondAsync("❌ Error: You must specify a valid target language code.", ephemeral: true);
                return;
            }

            await Context.Interaction.DeferAsync(ephemeral: true);

            try
            {
                var contextChannel = Context.Channel;
                var currentChannelLanguageCode = contextChannel != null
                    ? await _dbService.GetTargetLanguageCodeAsync(contextChannel.Id)
                    : null;
                LanguageAnalysisResult? analysis = null;
                SourceLanguageInfo? sourceLanguageInfo = null;
                string? translationSourceLanguageCode = null;
                var analysisFailed = false;

                // Reuse the same analysis + trusted-hint flow as the live routing path so
                // manual probes reflect real message behavior as closely as possible.
                try
                {
                    analysis = await _translationService.AnalyzeLanguageAsync(text);
                    sourceLanguageInfo = LanguageCodeService.ResolveSourceLanguageInfo(analysis, currentChannelLanguageCode);
                    translationSourceLanguageCode = analysis.HasClearDominantLanguage != false
                        ? sourceLanguageInfo.PrimaryLanguageCode
                        : null;
                }
                catch (Exception ex)
                {
                    analysisFailed = true;
                    _logger.LogManualTranslationAnalysisFailed(ex, Context.User.Username, normalizedTargetLanguageCode);
                }

                string translatedText;
                var skippedProviderTranslation = sourceLanguageInfo != null
                    && !MirroredMessageFormatter.ShouldTranslateLinkedMessage(
                        sourceLanguageInfo.PrimaryLanguageCode,
                        normalizedTargetLanguageCode);

                // Mirror the production routing behavior: when source and target already match,
                // we keep the original text instead of paying for a no-op provider round-trip.
                if (skippedProviderTranslation)
                {
                    translatedText = text;
                }
                else
                {
                    translatedText = await _translationService.TranslateTextAsync(text, targetLanguageCode, translationSourceLanguageCode);
                }

                _logger.LogManualTranslationCompleted(
                    Context.User.Username,
                    normalizedTargetLanguageCode,
                    !string.IsNullOrWhiteSpace(translationSourceLanguageCode),
                    translationSourceLanguageCode);

                await Context.Interaction.FollowupAsync(
                    BuildTranslationResponse(
                        analysis,
                        sourceLanguageInfo,
                        normalizedTargetLanguageCode,
                        translationSourceLanguageCode,
                        translatedText,
                        analysisFailed,
                        skippedProviderTranslation),
                    ephemeral: true);
            }
            catch (Exception ex)
            {
                _logger.LogManualTranslationFailed(ex, Context.User.Username, normalizedTargetLanguageCode);
                await Context.Interaction.FollowupAsync(
                    BuildFailureResponse("Translation failed"),
                    ephemeral: true);
            }
        }

        [SlashCommand("status", "Shows the current bot/database status counts.")]
        public async Task StatusAsync()
        {
            if (!await EnsureGuildAdministratorAsync())
            {
                return;
            }

            try
            {
                var status = await _dbService.GetDatabaseStatusSnapshotAsync();
                var providerReport = _translationService.GetProviderConfigurationReport();
                _logger.LogBotStatusReported(
                    Context.User.Username,
                    status.ConfiguredChannelCount,
                    status.LinkedMessageFamilyCount,
                    status.LinkedBotMessageCount);
                await RespondAsync(
                    BuildStatusResponse(
                        status,
                        providerReport),
                    ephemeral: true);
            }
            catch (Exception ex)
            {
                _logger.LogBotStatusReportFailed(ex, Context.User.Username);
                await RespondAsync(BuildFailureResponse("Status lookup failed"), ephemeral: true);
            }
        }

        private const int MaxSyncHours = 168;

        [SlashCommand("sync", "Synchronizes unprocessed messages from the last N hours across a Master channel and its localized channels.")]
        public async Task SyncAsync(
            [Summary("master-channel", "The Master channel to synchronize")] IChannel masterChannel,
            [Summary("hours", "How many hours back to sync (1-168)")] int hours)
        {
            if (!await EnsureGuildAdministratorAsync())
            {
                return;
            }

            if (masterChannel is not ITextChannel)
            {
                await RespondAsync("❌ Error: The channel must be a standard Guild Text Channel.", ephemeral: true);
                return;
            }

            if (!await _dbService.IsMasterChannelAsync(masterChannel.Id))
            {
                await RespondAsync("❌ Error: The channel is not registered as a Master channel.", ephemeral: true);
                return;
            }

            if (hours < 1 || hours > MaxSyncHours)
            {
                await RespondAsync($"❌ Error: Hours must be between 1 and {MaxSyncHours}.", ephemeral: true);
                return;
            }

            if (!_translationService.IsActive)
            {
                await RespondAsync("❌ Error: Translation provider is not active.", ephemeral: true);
                return;
            }

            await Context.Interaction.DeferAsync(ephemeral: true);

            try
            {
                var result = await _historicalMessageSyncService.SyncMasterChannelAsync(
                    masterChannel.Id,
                    TimeSpan.FromHours(hours),
                    CancellationToken.None);

                if (!result.Success)
                {
                    await Context.Interaction.FollowupAsync(
                        $"❌ {result.ErrorMessage ?? "Sync failed"}. Check the bot logs for details.",
                        ephemeral: true);
                    return;
                }

                _logger.LogTsumariSyncCommandCompleted(
                    Context.User.Username,
                    masterChannel.Id,
                    hours,
                    result.ProcessedCount,
                    result.FailedCount,
                    result.SkippedCount);

                var response = $"✅ Sync completed for <#{masterChannel.Id}> ({hours} hours).\n" +
                               $"Processed: **{result.ProcessedCount}**\n" +
                               $"Skipped (already tracked): **{result.SkippedCount}**\n" +
                               $"Failed: **{result.FailedCount}**";

                await Context.Interaction.FollowupAsync(response, ephemeral: true);
            }
            catch (Exception ex)
            {
                _logger.LogTsumariSyncCommandFailed(ex, Context.User.Username, masterChannel.Id, hours);
                await Context.Interaction.FollowupAsync(
                    "❌ Sync failed. Check the bot logs for details.",
                    ephemeral: true);
            }
        }

        private static string BuildLanguageDetectionResponse(LanguageAnalysisResult analysis)
        {
            var lines = new[]
            {
                $"**Dominant:** {analysis.PrimaryLanguageCode}",
                $"**Detected:** {FormatDetectedLanguages(analysis.DetectedLanguages)}",
                $"**Mixed:** {FormatNullableBoolean(analysis.IsMixed)}",
                $"**Clear dominant:** {FormatNullableBoolean(analysis.HasClearDominantLanguage)}"
            };

            return TruncateForDiscord(string.Join("\n", lines));
        }

        private static string BuildTranslationResponse(
            LanguageAnalysisResult? analysis,
            SourceLanguageInfo? sourceLanguageInfo,
            string normalizedTargetLanguageCode,
            string? translationSourceLanguageCode,
            string translatedText,
            bool analysisFailed,
            bool skippedProviderTranslation)
        {
            var lines = new List<string>();
            if (analysis != null)
            {
                lines.Add($"**Detected:** {FormatDetectedLanguages(analysis.DetectedLanguages)}");
                if (skippedProviderTranslation)
                {
                    lines.Add("**Translation skipped:** source already matches target");
                }
                else
                {
                    lines.Add(
                        !string.IsNullOrWhiteSpace(translationSourceLanguageCode)
                            ? $"**Hint used:** {translationSourceLanguageCode}"
                            : analysis.HasClearDominantLanguage == false
                                ? "**Hint used:** none (dominant unclear)"
                                : "**Hint used:** none");
                }
            }
            else if (analysisFailed)
            {
                lines.Add("**Detected:** unavailable");
                lines.Add("**Hint used:** none (analysis failed)");
            }

            lines.Add(
                skippedProviderTranslation
                    ? $"**Output** ({normalizedTargetLanguageCode}):"
                    : sourceLanguageInfo != null
                    ? $"**Translation** {MirroredMessageFormatter.FormatLanguagePair(sourceLanguageInfo, normalizedTargetLanguageCode)}:"
                    : $"**Translation** (?? => {normalizedTargetLanguageCode}):");
            lines.Add(translatedText);

            return TruncateForDiscord(string.Join("\n", lines));
        }

        private static string BuildFailureResponse(string prefix)
        {
            return TruncateForDiscord($"❌ {prefix}. Check the bot logs for details.");
        }

        private static string BuildStatusResponse(
            DatabaseStatusSnapshot status,
            TranslationProviderConfigurationReport providerReport)
        {
            var quotaUsageLine = providerReport.UsesCharacterQuota
                ? $"**Quota-tracked characters this month:** {status.CurrentMonthCharacterCount}"
                : "**Quota-tracked characters this month:** N/A for the current provider";
            var lines = new List<string>
            {
                $"**Translation provider:** {providerReport.ProviderName}",
                $"**Translation provider active:** {FormatNullableBoolean(providerReport.IsActive)}",
            };

            foreach (var detail in providerReport.Details)
            {
                lines.Add($"**Provider {detail.Label}:** {detail.Value}");
            }

            lines.AddRange(
            [
                $"**Master channels:** {status.MasterChannelCount}",
                $"**Localized channels:** {status.LocalizedChannelCount}",
                $"**Configured channels:** {status.ConfiguredChannelCount}",
                $"**Linked message families:** {status.LinkedMessageFamilyCount}",
                $"**Linked bot messages:** {status.LinkedBotMessageCount}",
                $"**Localized message links:** {status.LocalizedMessageLinkCount}",
                quotaUsageLine,
                $"**Database main file size:** {status.DatabaseFileSizeBytes} bytes",
                $"**Database WAL size:** {status.DatabaseWalFileSizeBytes} bytes",
                $"**Database storage size:** {status.DatabaseStorageSizeBytes} bytes",
                $"**DB last activity (UTC):** {FormatUtcTimestamp(status.DatabaseLastActivityUtc)}"
            ]);

            return TruncateForDiscord(string.Join("\n", lines));
        }

        private async Task<bool> EnsureGuildAdministratorAsync()
        {
            if (Context.Guild == null)
            {
                await RespondAsync("❌ Error: This command can only be used inside a guild channel.", ephemeral: true);
                return false;
            }

            return await EnsureGuildAdministratorOrAllowDmAsync();
        }

        private async Task<bool> EnsureGuildAdministratorOrAllowDmAsync()
        {
            if (Context.Guild == null)
            {
                return true;
            }

            if (Context.User is IGuildUser guildUser && guildUser.GuildPermissions.Administrator)
            {
                return true;
            }

            await RespondAsync("❌ Error: This command requires Administrator permissions in the current guild.", ephemeral: true);
            return false;
        }

        private static string FormatUtcTimestamp(DateTime? value)
        {
            return value.HasValue
                ? value.Value.ToString("u", CultureInfo.InvariantCulture)
                : "unknown";
        }

        private static string FormatDetectedLanguages(IReadOnlyList<DetectedLanguage> detectedLanguages)
        {
            if (detectedLanguages.Count == 0)
            {
                return "unknown";
            }

            var entries = new string[detectedLanguages.Count];
            for (var index = 0; index < detectedLanguages.Count; index++)
            {
                entries[index] = FormatDetectedLanguage(detectedLanguages[index]);
            }

            return string.Join(", ", entries);
        }

        private static string FormatDetectedLanguage(DetectedLanguage detectedLanguage)
        {
            if (!detectedLanguage.Share.HasValue)
            {
                return detectedLanguage.LanguageCode;
            }

            var percentage = (detectedLanguage.Share.Value * 100.0)
                .ToString("0.#", CultureInfo.InvariantCulture);
            return $"{detectedLanguage.LanguageCode} ({percentage}%)";
        }

        private static string FormatNullableBoolean(bool? value)
        {
            return value switch
            {
                true => "yes",
                false => "no",
                _ => "unknown"
            };
        }

        private static string TruncateForDiscord(string text)
        {
            if (text.Length <= MaxInteractionResponseLength)
            {
                return text;
            }

            const string suffix = "\n… *(truncated)*";
            var maxBodyLength = MaxInteractionResponseLength - suffix.Length;
            if (maxBodyLength <= 0)
            {
                return suffix.TrimStart();
            }

            var builder = new StringBuilder(MaxInteractionResponseLength);
            builder.Append(text.AsSpan(0, maxBodyLength));
            builder.Append(suffix);
            return builder.ToString();
        }
    }
}
