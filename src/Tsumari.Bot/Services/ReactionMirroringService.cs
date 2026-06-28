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
                return;
            }

            await ReconcileReactionFamilyAsync(triggerMessageId, triggerChannelId, emote);
        }

        public async Task MirrorReactionRemovedAsync(ulong triggerMessageId, ulong triggerChannelId, IEmote emote, ReactionType reactionType, ulong reactingUserId)
        {
            if (ShouldIgnoreReactionEvent(reactingUserId, _discordMessageService.CurrentUserId, reactionType))
            {
                return;
            }

            await ReconcileReactionFamilyAsync(triggerMessageId, triggerChannelId, emote);
        }

        public async Task MirrorReactionsClearedAsync(ulong triggerMessageId, ulong triggerChannelId)
        {
            await ReconcileReactionFamilyAsync(triggerMessageId, triggerChannelId, emote: null);
        }

        public async Task MirrorReactionsRemovedForEmoteAsync(ulong triggerMessageId, ulong triggerChannelId, IEmote emote)
        {
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

        private async Task ReconcileReactionFamilyAsync(ulong triggerMessageId, ulong triggerChannelId, IEmote? emote)
        {
            var family = await _dbService.GetLinkedMessageFamilyAsync(triggerMessageId, triggerChannelId);
            if (family == null)
            {
                return;
            }

            var familyLock = _familyLocks.GetOrAdd(family.OriginalMessageId, _ => new SemaphoreSlim(1, 1));
            await familyLock.WaitAsync();
            try
            {
                var familyMessages = await FetchFamilyMessagesAsync(family);
                if (familyMessages.Count == 0)
                {
                    return;
                }

                var trackedEmotes = emote != null
                    ? new List<IEmote> { emote }
                    : GetTrackedNormalEmotes(familyMessages);

                foreach (var trackedEmote in trackedEmotes)
                {
                    await ReconcileEmoteAcrossFamilyAsync(familyMessages, trackedEmote);
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

        private async Task ReconcileEmoteAcrossFamilyAsync(List<FamilyMessageState> familyMessages, IEmote emote)
        {
            var hasHumanReaction = await HasHumanReactionAsync(familyMessages, emote);

            foreach (var state in familyMessages)
            {
                var hasMetadata = TryGetReactionMetadata(state.Message, emote, out _, out var metadata);
                var hasNormalReaction = hasMetadata && metadata.NormalCount > 0;
                var hasBotReaction = hasMetadata && metadata.IsMe;

                try
                {
                    if (hasHumanReaction)
                    {
                        if (!hasNormalReaction)
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
