using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Tsumari.Bot.Logging;
using Xunit;

namespace Tsumari.Bot.Tests.Unit
{
    public class TsumariConsoleFormatterTests
    {
        [Fact]
        public void Write_FormatsMessageWithoutCategoryOrEventId()
        {
            var formatter = CreateFormatter(LoggerColorBehavior.Disabled);
            var writer = new StringWriter();
            var entry = new LogEntry<string>(
                LogLevel.Information,
                "Tsumari.Bot.DiscordGatewayHostedService",
                new EventId(2304),
                "[Discord.Net] Gateway: Connecting",
                null,
                static (state, _) => state);

            formatter.Write(in entry, null, writer);

            var output = writer.ToString();
            Assert.StartsWith("[", output);
            Assert.Contains("info: [Discord.Net] Gateway: Connecting", output);
            Assert.DoesNotContain("Tsumari.Bot.DiscordGatewayHostedService", output);
            Assert.DoesNotContain("[2304]", output);
        }

        [Fact]
        public void Write_FlattensMultilineMessageAndException_WhenSingleLineIsEnabled()
        {
            var formatter = CreateFormatter(LoggerColorBehavior.Disabled);
            var writer = new StringWriter();
            var exception = new InvalidOperationException("boom");
            var entry = new LogEntry<string>(
                LogLevel.Error,
                "Category",
                new EventId(17),
                "line1\r\nline2",
                exception,
                static (state, _) => state);

            formatter.Write(in entry, null, writer);

            var output = writer.ToString();
            Assert.Contains("fail: line1 line2", output);
            Assert.Contains("System.InvalidOperationException: boom", output);
            Assert.DoesNotContain("Category", output);
            Assert.DoesNotContain("[17]", output);
        }

        [Fact]
        public void Write_EmitsAnsiColorCodes_WhenColorsAreEnabled()
        {
            var formatter = CreateFormatter(LoggerColorBehavior.Enabled);
            var writer = new StringWriter();
            var entry = new LogEntry<string>(
                LogLevel.Warning,
                "Category",
                new EventId(99),
                "watch out",
                null,
                static (state, _) => state);

            formatter.Write(in entry, null, writer);

            var output = writer.ToString();
            Assert.Contains("\u001b[33mwarn:\u001b[0m watch out", output);
            Assert.DoesNotContain("Category", output);
            Assert.DoesNotContain("[99]", output);
        }

        private static TsumariConsoleFormatter CreateFormatter(LoggerColorBehavior colorBehavior)
        {
            return new TsumariConsoleFormatter(new StaticOptionsMonitor<TsumariConsoleFormatterOptions>(
                new TsumariConsoleFormatterOptions
                {
                    TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff zzz",
                    UseUtcTimestamp = false,
                    SingleLine = true,
                    ColorBehavior = colorBehavior
                }));
        }

        private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
        {
            public StaticOptionsMonitor(T currentValue)
            {
                CurrentValue = currentValue;
            }

            public T CurrentValue { get; }

            public T Get(string? name) => CurrentValue;

            public IDisposable OnChange(Action<T, string?> listener)
            {
                return NullDisposable.Instance;
            }

            private sealed class NullDisposable : IDisposable
            {
                public static readonly NullDisposable Instance = new();

                public void Dispose()
                {
                }
            }
        }
    }
}
