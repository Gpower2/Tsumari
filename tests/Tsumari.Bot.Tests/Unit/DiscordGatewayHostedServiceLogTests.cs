using System.Linq;
using Discord;
using Microsoft.Extensions.Logging;
using Tsumari.Bot;
using Tsumari.Bot.Logging;
using Tsumari.Bot.Tests;
using Xunit;

namespace Tsumari.Bot.Tests.Unit
{
    public class DiscordGatewayHostedServiceLogTests
    {
        [Fact]
        public void GatewayTraceLogMethods_EmitTraceEntries()
        {
            var logger = new ListLogger<DiscordGatewayHostedService>();

            logger.LogReadyEventReceived();
            logger.LogInteractionCreatedEventReceived("SlashCommand", 10UL, 20UL);
            logger.LogMessageReceivedEventReceived(30UL, 40UL, MessageSource.User, true);
            logger.LogMessageDeletedEventReceived(50UL, 60UL, true);
            logger.LogMessagesBulkDeletedEventReceived(3, 70UL, true);
            logger.LogMessageUpdatedEventReceived(80UL, 90UL, hadCachedSnapshot: true, enqueued: true);
            logger.LogReactionAddedEventReceived(100UL, 110UL, 120UL, "👍", ReactionType.Normal, true);
            logger.LogReactionRemovedEventReceived(130UL, 140UL, 150UL, "🔥", ReactionType.Burst, false);
            logger.LogReactionsClearedEventReceived(160UL, 170UL, true);
            logger.LogReactionsRemovedForEmoteEventReceived(180UL, 190UL, "✅", false);

            Assert.Equal(10, logger.Entries.Count);
            Assert.All(logger.Entries, entry => Assert.Equal(LogLevel.Trace, entry.Level));
            Assert.Collection(
                logger.Entries.Select(entry => entry.EventId.Id),
                id => Assert.Equal(2321, id),
                id => Assert.Equal(2322, id),
                id => Assert.Equal(2323, id),
                id => Assert.Equal(2324, id),
                id => Assert.Equal(2325, id),
                id => Assert.Equal(2326, id),
                id => Assert.Equal(2327, id),
                id => Assert.Equal(2328, id),
                id => Assert.Equal(2329, id),
                id => Assert.Equal(2330, id));
            Assert.Contains(logger.Entries, entry => entry.EventId.Id == 2323 && entry.Message.Contains("message 30") && entry.Message.Contains("Enqueued: True"));
            Assert.Contains(logger.Entries, entry => entry.EventId.Id == 2327 && entry.Message.Contains("emoji 👍") && entry.Message.Contains("type Normal"));
            Assert.Contains(logger.Entries, entry => entry.EventId.Id == 2328 && entry.Message.Contains("emoji 🔥") && entry.Message.Contains("Enqueued: False"));
        }
    }
}
