using System.Globalization;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Tsumari.Bot.Logging
{
    public sealed class TsumariConsoleFormatter : ConsoleFormatter
    {
        public const string FormatterName = "tsumari";
        private const int TimestampBufferSize = 64;

        private readonly IOptionsMonitor<TsumariConsoleFormatterOptions> _options;

        public TsumariConsoleFormatter(IOptionsMonitor<TsumariConsoleFormatterOptions> options)
            : base(FormatterName)
        {
            _options = options;
        }

        public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
        {
            var options = _options.CurrentValue;
            var message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);

            if (string.IsNullOrEmpty(message) && logEntry.Exception == null)
            {
                return;
            }

            WriteTimestamp(textWriter, options);

            var color = GetAnsiColorEscapeCode(logEntry.LogLevel, options.ColorBehavior);
            if (!color.IsEmpty)
            {
                textWriter.Write(color);
            }

            textWriter.Write(GetLogLevelLabel(logEntry.LogLevel));

            if (!color.IsEmpty)
            {
                textWriter.Write("\u001b[0m");
            }

            if (!string.IsNullOrEmpty(message))
            {
                textWriter.Write(' ');
                WriteMessage(textWriter, message.AsSpan(), options.SingleLine);
            }

            if (logEntry.Exception != null)
            {
                textWriter.Write(options.SingleLine ? " " : Environment.NewLine);
                WriteMessage(textWriter, logEntry.Exception.ToString().AsSpan(), options.SingleLine);
            }

            textWriter.WriteLine();
        }

        private static void WriteTimestamp(TextWriter textWriter, TsumariConsoleFormatterOptions options)
        {
            if (string.IsNullOrEmpty(options.TimestampFormat))
            {
                return;
            }

            var now = options.UseUtcTimestamp ? DateTimeOffset.UtcNow : DateTimeOffset.Now;

            textWriter.Write('[');

            Span<char> timestampBuffer = stackalloc char[TimestampBufferSize];
            if (now.TryFormat(timestampBuffer, out var written, options.TimestampFormat, CultureInfo.InvariantCulture))
            {
                textWriter.Write(timestampBuffer[..written]);
            }
            else
            {
                textWriter.Write(now.ToString(options.TimestampFormat, CultureInfo.InvariantCulture));
            }

            textWriter.Write("] ");
        }

        private static ReadOnlySpan<char> GetLogLevelLabel(LogLevel logLevel)
        {
            return logLevel switch
            {
                LogLevel.Trace => "trc:",
                LogLevel.Debug => "dbg:",
                LogLevel.Information => "inf:",
                LogLevel.Warning => "wrn:",
                LogLevel.Error => "err:",
                LogLevel.Critical => "crt:",
                _ => "---:"
            };
        }

        private static ReadOnlySpan<char> GetAnsiColorEscapeCode(LogLevel logLevel, LoggerColorBehavior colorBehavior)
        {
            if (!ShouldUseColors(colorBehavior))
            {
                return [];
            }

            return logLevel switch
            {
                LogLevel.Trace => "\u001b[90m",
                LogLevel.Debug => "\u001b[36m",
                LogLevel.Information => "\u001b[32m",
                LogLevel.Warning => "\u001b[33m",
                LogLevel.Error => "\u001b[31m",
                LogLevel.Critical => "\u001b[97;41m",
                _ => []
            };
        }

        private static bool ShouldUseColors(LoggerColorBehavior colorBehavior)
        {
            return colorBehavior switch
            {
                LoggerColorBehavior.Enabled => true,
                LoggerColorBehavior.Disabled => false,
                _ => !Console.IsOutputRedirected && !Console.IsErrorRedirected
            };
        }

        private static void WriteMessage(TextWriter textWriter, ReadOnlySpan<char> message, bool singleLine)
        {
            if (!singleLine)
            {
                textWriter.Write(message);
                return;
            }

            var segmentStart = 0;
            var index = 0;

            while (index < message.Length)
            {
                var value = message[index];
                if (value == '\r' || value == '\n')
                {
                    if (index > segmentStart)
                    {
                        textWriter.Write(message[segmentStart..index]);
                    }

                    textWriter.Write(' ');

                    if (value == '\r' && index + 1 < message.Length && message[index + 1] == '\n')
                    {
                        index++;
                    }

                    segmentStart = index + 1;
                }

                index++;
            }

            if (segmentStart < message.Length)
            {
                textWriter.Write(message[segmentStart..]);
            }
        }
    }
}
