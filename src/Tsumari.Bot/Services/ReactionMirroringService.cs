using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.Services
{
    public class ReactionMirroringService
    {
        private readonly IDiscordMessageService _discordMessageService;
        private readonly DatabaseService _dbService;
        private readonly ILogger<ReactionMirroringService> _logger;
        private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _familyLocks = new();

        public ReactionMirroringService(
            IDiscordMessageService discordMessageService,
            DatabaseService dbService,
            ILogger<ReactionMirroringService> logger)
        {
            _discordMessageService = discordMessageService;
            _dbService = dbService;
            _logger = logger;
        }

        public async Task MirrorReactionAddedAsync(ulong triggerMessageId, ulong triggerChannelId, IEmote emote, ReactionType reactionType, ulong reactingUserId)
        {
            if (ShouldIgnoreReactionEvent(reactingUserId, _discordMessageService.CurrentUserId, reactionType))
            {
                _logger.LogReactionEventIgnored(triggerMessageId, triggerChannelId, reactingUserId, emote.ToString() ?? string.Empty, reactionType);
                return;
            }

            _logger.LogProcessingReactionAdded(triggerMessageId, triggerChannelId, reactingUserId, emote.ToString() ?? string.Empty, reactionType);

            // Discord can deliver ReactionAdded before a fresh GetMessageAsync() reflects the new
            // reaction in message metadata. The gateway event itself is therefore our source of truth
            // that at least one human reaction now exists somewhere in this linked family.
            await ReconcileReactionFamilyAsync(triggerMessageId, triggerChannelId, emote, humanReactionKnownPresent: true);
        }

        public async Task MirrorReactionRemovedAsync(ulong triggerMessageId, ulong triggerChannelId, IEmote emote, ReactionType reactionType, ulong reactingUserId)
        {
            if (ShouldIgnoreReactionEvent(reactingUserId, _discordMessageService.CurrentUserId, reactionType))
            {
                _logger.LogReactionEventIgnored(triggerMessageId, triggerChannelId, reactingUserId, emote.ToString() ?? string.Empty, reactionType);
                return;
            }

            _logger.LogProcessingReactionRemoved(triggerMessageId, triggerChannelId, reactingUserId, emote.ToString() ?? string.Empty, reactionType);

            // For removals we need the post-remove state, so we cannot trust the event alone. We
            // must re-read the family and determine whether any human reactions still remain.
            await ReconcileReactionFamilyAsync(triggerMessageId, triggerChannelId, emote);
        }

        public async Task MirrorReactionsClearedAsync(ulong triggerMessageId, ulong triggerChannelId)
        {
            _logger.LogProcessingReactionsCleared(triggerMessageId, triggerChannelId);
            await ReconcileReactionFamilyAsync(triggerMessageId, triggerChannelId, emote: null);
        }

        public async Task MirrorReactionsRemovedForEmoteAsync(ulong triggerMessageId, ulong triggerChannelId, IEmote emote)
        {
            _logger.LogProcessingReactionsRemovedForEmote(triggerMessageId, triggerChannelId, emote.ToString() ?? string.Empty);
            await ReconcileReactionFamilyAsync(triggerMessageId, triggerChannelId, emote);
        }

        public static bool ShouldIgnoreReactionEvent(ulong reactingUserId, ulong botUserId, ReactionType reactionType)
        {
            return reactingUserId == botUserId || reactionType != ReactionType.Normal;
        }

        public static string GetReactionKey(IEmote emote)
        {
            return emote is Emote customEmote
                ? $"custom:{customEmote.Id}"
                : $"unicode:{emote.Name}";
        }

        public static bool ContainsNonBotUsers(IEnumerable<IUser> users)
        {
            return users.Any(user => !user.IsBot);
        }

        private async Task ReconcileReactionFamilyAsync(
            ulong triggerMessageId,
            ulong triggerChannelId,
            IEmote? emote,
            bool humanReactionKnownPresent = false)
        {
            var family = await _dbService.GetLinkedMessageFamilyAsync(triggerMessageId, triggerChannelId);
            if (family == null)
            {
                _logger.LogReactionFamilyNotFound(triggerMessageId, triggerChannelId);
                return;
            }

            var familyLock = _familyLocks.GetOrAdd(family.OriginalMessageId, _ => new SemaphoreSlim(1, 1));
            await familyLock.WaitAsync();
            try
            {
                var familyMessages = await FetchFamilyMessagesAsync(family);
                if (familyMessages.Count == 0)
                {
                    _logger.LogReactionFamilyMessagesNotFetched(family.OriginalMessageId, family.OriginalChannelId);
                    return;
                }

                var trackedEmotes = emote != null
                    ? new List<IEmote> { emote }
                    // Clear events do not identify a single emote, so re-derive the tracked set
                    // from the currently fetched reaction metadata across the family.
                    : GetTrackedNormalEmotes(familyMessages);

                foreach (var trackedEmote in trackedEmotes)
                {
                    await ReconcileEmoteAcrossFamilyAsync(
                        familyMessages,
                        trackedEmote,
                        triggerMessageId,
                        triggerChannelId,
                        humanReactionKnownPresent);
                }
            }
            finally
            {
                familyLock.Release();
            }
        }

        private async Task<List<FamilyMessageState>> FetchFamilyMessagesAsync(LinkedMessageFamily family)
        {
            var states = new List<FamilyMessageState>();
            var familyEntries = new List<(ulong MessageId, ulong ChannelId, bool IsOriginal)>
            {
                (family.OriginalMessageId, family.OriginalChannelId, true)
            };

            familyEntries.AddRange(family.MirroredMessages.Select(link => (link.MirroredMessageId, link.ChannelId, false)));

            foreach (var entry in familyEntries)
            {
                try
                {
                    var channel = await _discordMessageService.GetChannelAsync(entry.ChannelId);
                    if (channel == null)
                    {
                        _logger.LogReactionChannelNotResolved(entry.ChannelId, entry.MessageId);
                        continue;
                    }

                    var message = await channel.GetMessageAsync(entry.MessageId);
                    if (message == null)
                    {
                        _logger.LogReactionMessageNotFetched(entry.MessageId, entry.ChannelId);
                        continue;
                    }

                    states.Add(new FamilyMessageState(entry.MessageId, entry.ChannelId, entry.IsOriginal, message));
                }
                catch (Exception ex)
                {
                    _logger.LogReactionMessageFetchFailed(ex, entry.MessageId, entry.ChannelId);
                }
            }

            return states;
        }

        private static List<IEmote> GetTrackedNormalEmotes(IEnumerable<FamilyMessageState> familyMessages)
        {
            var emotes = new Dictionary<string, IEmote>(StringComparer.Ordinal);

            foreach (var state in familyMessages)
            {
                foreach (var reaction in state.Message.Reactions)
                {
                    if (reaction.Value.NormalCount <= 0)
                    {
                        continue;
                    }

                    emotes[GetReactionKey(reaction.Key)] = reaction.Key;
                }
            }

            return emotes.Values.ToList();
        }

        private async Task ReconcileEmoteAcrossFamilyAsync(
            List<FamilyMessageState> familyMessages,
            IEmote emote,
            ulong triggerMessageId,
            ulong triggerChannelId,
            bool humanReactionKnownPresent)
        {
            // ReactionAdded can short-circuit this check because the inbound event already proved a
            // human reaction exists. Remove/clear paths still have to compute that from Discord.
            var hasHumanReaction = humanReactionKnownPresent || await HasHumanReactionAsync(familyMessages, emote);

            foreach (var state in familyMessages)
            {
                var hasMetadata = TryGetReactionMetadata(state.Message, emote, out _, out var metadata);
                var hasNormalReaction = hasMetadata && metadata.NormalCount > 0;
                var hasBotReaction = hasMetadata && metadata.IsMe;
                var isTriggerMessage = state.MessageId == triggerMessageId && state.ChannelId == triggerChannelId;

                try
                {
                    if (hasHumanReaction)
                    {
                        // When the trigger is a human add event, skip echoing the reaction back onto
                        // the same message. Discord already applied it there, even if our fetched
                        // metadata has not caught up yet.
                        if (!hasNormalReaction && !(humanReactionKnownPresent && isTriggerMessage))
                        {
                            await _discordMessageService.AddReactionAsync(state.Message, emote);
                        }
                    }
                    else if (hasBotReaction)
                    {
                        await _discordMessageService.RemoveOwnReactionAsync(state.Message, emote);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogReactionReconcileFailed(ex, emote.ToString() ?? string.Empty, state.MessageId, state.ChannelId);
                }
            }
        }

        private async Task<bool> HasHumanReactionAsync(IEnumerable<FamilyMessageState> familyMessages, IEmote emote)
        {
            foreach (var state in familyMessages)
            {
                if (!TryGetReactionMetadata(state.Message, emote, out var matchedEmote, out var metadata) || metadata.NormalCount <= 0)
                {
                    continue;
                }

                // ReactionMetadata tells us counts, but not whether those counts come from humans or
                // only from bot mirrors, so we page the actual reacting users when needed.
                await foreach (var users in _discordMessageService.GetReactionUsersAsync(state.Message, matchedEmote, metadata.NormalCount))
                {
                    if (ContainsNonBotUsers(users))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryGetReactionMetadata(IMessage message, IEmote emote, out IEmote matchedEmote, out ReactionMetadata metadata)
        {
            var reactionKey = GetReactionKey(emote);
            foreach (var reaction in message.Reactions)
            {
                if (GetReactionKey(reaction.Key) == reactionKey)
                {
                    matchedEmote = reaction.Key;
                    metadata = reaction.Value;
                    return true;
                }
            }

            matchedEmote = emote;
            metadata = default;
            return false;
        }

        private sealed class FamilyMessageState
        {
            public FamilyMessageState(ulong messageId, ulong channelId, bool isOriginal, IMessage message)
            {
                MessageId = messageId;
                ChannelId = channelId;
                IsOriginal = isOriginal;
                Message = message;
            }

            public ulong MessageId { get; }

            public ulong ChannelId { get; }

            public bool IsOriginal { get; }

            public IMessage Message { get; }
        }
    }
}
