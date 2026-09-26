using Discord;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tsumari.Bot.Configuration;

namespace Tsumari.Bot.Services;

public sealed record TranslationRetryResult(bool Success, string Message, int? Attempt = null);

public sealed class TranslationRetryService
{
    private readonly DatabaseService _db;
    private readonly IDiscordMessageService _discord;
    private readonly TranslationService _translations;
    private readonly MessageMutationCoordinator _mutations;
    private readonly TranslationRetryOptions _options;
    private readonly ILogger<TranslationRetryService> _logger;
    private readonly DiscordRestOptions _restOptions;
    private readonly SemaphoreSlim _worker = new(1, 1);
    private readonly object _sync = new();
    private readonly HashSet<ulong> _active = [];
    private readonly Dictionary<ulong, DateTimeOffset> _cooldowns = [];

    public TranslationRetryService(DatabaseService db, IDiscordMessageService discord, TranslationService translations,
        MessageMutationCoordinator mutations, IOptions<TranslationRetryOptions> options, ILogger<TranslationRetryService> logger,
        IOptions<DiscordRestOptions>? restOptions = null)
    {
        _db = db; _discord = discord; _translations = translations; _mutations = mutations;
        _options = options.Value; _options.Validate(); _logger = logger;
        _restOptions = restOptions?.Value ?? new DiscordRestOptions();
    }

    public async Task<TranslationRetryResult> RetryAsync(ulong guildId, ulong channelId, ulong messageId, IGuildUser user)
    {
        if (user.IsBot || user.GuildId != guildId) return new(false, "Retry is only available to members of this server.");
        if (await _discord.GetChannelAsync(channelId) is not IGuildChannel channel || channel.GuildId != guildId
            || !user.GetPermissions(channel).ViewChannel || !user.GetPermissions(channel).ReadMessageHistory)
            return new(false, "You cannot access that message.");
        var family = await _db.GetLinkedMessageFamilyAsync(messageId, channelId);
        var link = family?.FindMirroredMessageById(messageId);
        if (family == null || link == null || link.ChannelId != channelId || string.IsNullOrWhiteSpace(link.LanguageCode)
            || link.LanguageCode.Equals("MASTER", StringComparison.OrdinalIgnoreCase))
            return new(false, "Select an existing localized bot copy, not an original or master copy.");
        if (await _discord.GetChannelAsync(family.OriginalChannelId) is not IGuildChannel sourceChannel || sourceChannel.GuildId != guildId
            || !user.GetPermissions(sourceChannel).ViewChannel || !user.GetPermissions(sourceChannel).ReadMessageHistory)
            return new(false, "You cannot access the original message.");
        var source = await _discord.GetFreshMessageAsync(family.OriginalChannelId, family.OriginalMessageId) as IUserMessage;
        if (source == null) return new(false, "The original message is unavailable.");
        if (source.Author.Id != user.Id && !user.GetPermissions(channel).ManageMessages && !user.GuildPermissions.Administrator
            && (_options.RoleId == 0 || !user.RoleIds.Contains(_options.RoleId)))
            return new(false, "Only the original author, moderators, or the configured retry role can retry this message.");
        lock (_sync)
        {
            foreach (var expired in _cooldowns.Where(p => p.Value <= DateTimeOffset.UtcNow).Select(p => p.Key).ToArray()) _cooldowns.Remove(expired);
            if (_active.Contains(messageId)) return new(false, "This message is already being retried.");
            if (_cooldowns.ContainsKey(messageId)) return new(false, "Please wait for the retry cooldown.");
            if (_active.Count >= _options.QueueCapacity) return new(false, "The retry queue is full. Please try again later.");
            _active.Add(messageId);
        }
        int? attempt = null;
        var editSubmitted = false;
        if (!await _worker.WaitAsync(TimeSpan.FromSeconds(60)))
        {
            lock (_sync) _active.Remove(messageId);
            return new(false, "The provider is busy. Please retry later.");
        }
        try
        {
            if (!_translations.IsActive || !_translations.SupportsGenerationOptions)
                return new(false, "The configured provider does not currently support retries with generation settings.");
            var before = await _discord.GetFreshMessageAsync(channelId, messageId) as IUserMessage;
            source = await _discord.GetFreshMessageAsync(family.OriginalChannelId, family.OriginalMessageId) as IUserMessage;
            if (before == null || before.Author.Id != _discord.CurrentUserId || source == null || source.Author.IsBot)
                return new(false, "The original or bot copy is unavailable.");
            if (string.IsNullOrWhiteSpace(source.Content)) return new(false, "There is no text to translate.");
            var count = TranslationRetryRules.ReadCount(before);
            var beforeContent = before.Content;
            if (count >= _options.Temperatures.Length) return new(false, "Retry limit reached. This translation needs review.");
            // Validate the wrapper and provider before recording an attempt. Analysis failures never assume English.
            TranslationRetryRules.ReplaceBody(before.Content, link.LanguageCode, "validation");
            var sourceFingerprint = TranslationRetryRules.Fingerprint(source);
            var mirrorFingerprint = TranslationRetryRules.Fingerprint(before);
            var metadata = TranslationRetryRules.PreservedMetadata(before);
            var analysis = await _translations.AnalyzeLanguageAsync(source.Content);
            if (analysis.HasClearDominantLanguage == false || string.IsNullOrWhiteSpace(analysis.PrimaryLanguageCode))
                return new(false, "Source language is uncertain. This message needs review.");
            if (!await _translations.CanTranslateAsync(source.Content.Length)) return new(false, "Translation quota is unavailable.");
            var next = count + 1;
            await _discord.AddReactionAsync(before, new Emoji(TranslationRetryRules.Counters[next - 1]));
            attempt = next;
            foreach (var reaction in before.Reactions.Where(r => r.Value.IsMe && TranslationRetryRules.IsCounter(r.Key) && r.Key.Name != TranslationRetryRules.Counters[next - 1]).ToArray())
            {
                try { await _discord.RemoveOwnReactionAsync(before, reaction.Key); }
                catch (Exception ex) { _logger.LogWarning(ex, "Could not remove old retry counter on {MessageId}", messageId); }
            }
            var options = new TranslationGenerationOptions(_options.Temperatures[next - 1], _options.TopP);
            var translated = await _translations.RetryTranslationAsync(source.Content, link.LanguageCode, analysis.PrimaryLanguageCode, options);
            if (source.Content.Any(char.IsLetter) && string.Equals(source.Content.Trim(), translated.Trim(), StringComparison.Ordinal)
                && !LanguageCodeService.AreSameLanguageFamily(analysis.PrimaryLanguageCode, link.LanguageCode))
                return new(false, "The provider repeated the source text. The existing message was kept; review may be needed.", attempt);
            var replacement = TranslationRetryRules.ReplaceBody(beforeContent, link.LanguageCode, translated);
            using var lease = await _mutations.AcquireAsync(family.OriginalMessageId);
            var currentSource = await _discord.GetFreshMessageAsync(family.OriginalChannelId, family.OriginalMessageId) as IUserMessage;
            var current = await _discord.GetFreshMessageAsync(channelId, messageId) as IUserMessage;
            if (currentSource == null || current == null || TranslationRetryRules.Fingerprint(currentSource) != sourceFingerprint
                || TranslationRetryRules.Fingerprint(current) != mirrorFingerprint)
                return new(false, "The original or translation changed during this retry. Please retry the updated message.", attempt);
            editSubmitted = true;
            // No automatic PATCH retry: an uncertain delivery must be checked, not blindly overwritten.
            await current.ModifyAsync(p => { p.Content = replacement; p.AllowedMentions = AllowedMentions.None; },
                DiscordRequestOptionsFactory.MessageOptions(_restOptions));
            var after = await _discord.GetFreshMessageAsync(channelId, messageId) as IUserMessage;
            if (after == null || after.Content != replacement || TranslationRetryRules.PreservedMetadata(after) != metadata)
                return new(false, "The edit was submitted, but verification failed. Check the message before retrying.", attempt);
            _logger.LogInformation("Translation retry {Attempt} verified for {MessageId} at temperature {Temperature}", next, messageId, options.Temperature);
            return new(true, $"Retry {next} completed. [Jump to message](https://discord.com/channels/{guildId}/{channelId}/{messageId}).", attempt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Translation retry failed for {MessageId}, attempt {Attempt}", messageId, attempt);
            return new(false, editSubmitted ? "The edit may have reached Discord, but could not be verified. Check the message before retrying."
                : "Retry failed; the existing message was kept. The provider may be unavailable or its output invalid.", attempt);
        }
        finally
        {
            _worker.Release();
            lock (_sync)
            {
                _active.Remove(messageId);
                _cooldowns[messageId] = DateTimeOffset.UtcNow.AddSeconds(_options.CooldownSeconds);
            }
        }
    }
}
