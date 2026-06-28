using Tsumari.Bot.Services;
using Xunit;

namespace Tsumari.Bot.Tests.Unit
{
    public class EditedMessageSyncServiceTests
    {
        [Fact]
        public void ShouldProcessEditedMessage_ReturnsFalse_WhenCachedContentMatches()
        {
            var result = EditedMessageSyncService.ShouldProcessEditedMessage(true, "hello", "hello");

            Assert.False(result);
        }

        [Fact]
        public void ShouldProcessEditedMessage_ReturnsTrue_WhenCachedSnapshotIsMissing()
        {
            var result = EditedMessageSyncService.ShouldProcessEditedMessage(false, "hello", "hello");

            Assert.True(result);
        }

        [Fact]
        public void ShouldProcessEditedMessage_ReturnsTrue_WhenContentChanged()
        {
            var result = EditedMessageSyncService.ShouldProcessEditedMessage(true, "before", "after");

            Assert.True(result);
        }
    }
}
