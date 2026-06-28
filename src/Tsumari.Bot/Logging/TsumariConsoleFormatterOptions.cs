using Microsoft.Extensions.Logging.Console;

namespace Tsumari.Bot.Logging
{
    public sealed class TsumariConsoleFormatterOptions : ConsoleFormatterOptions
    {
        public bool SingleLine { get; set; } = true;

        public LoggerColorBehavior ColorBehavior { get; set; } = LoggerColorBehavior.Default;
    }
}
