using Tsumari.Bot.Services;
using Xunit;

namespace Tsumari.Bot.Tests.Unit
{
    public class MirroredMessageNoticeLocalizerTests
    {
        [Fact]
        public void GetOversizedAttachmentNotice_ReturnsLocalizedGermanNotice()
        {
            var result = MirroredMessageNoticeLocalizer.GetOversizedAttachmentNotice("de");

            Assert.Equal("*(Anhang zu gross zum Spiegeln - nutze Original.)*", result);
        }

        [Fact]
        public void GetOversizedAttachmentNotice_FallsBackFromLocaleVariant()
        {
            var result = MirroredMessageNoticeLocalizer.GetOversizedAttachmentNotice("pt-BR");

            Assert.Equal("*(Anexo grande demais para espelhar - use Original.)*", result);
        }

        [Fact]
        public void GetOversizedAttachmentNotice_FallsBackToEnglishForUnknownLanguage()
        {
            var result = MirroredMessageNoticeLocalizer.GetOversizedAttachmentNotice("sv");

            Assert.Equal("*(Attachment too large to mirror - use Original.)*", result);
        }
    }
}
