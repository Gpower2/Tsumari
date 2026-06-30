using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tsumari.Bot.Models;
using Tsumari.Bot.Services;
using Xunit;

namespace Tsumari.Bot.Tests.Component
{
    public class TranslationServiceTests
    {
        [Fact]
        public async Task CanTranslateAsync_ReturnsTrue_WhenUsageIsUnderQuota()
        {
            // Arrange
            using var database = new TemporarySqliteDatabase("translation-service");
            var dbService = database.CreateDatabaseService(NullLogger<DatabaseService>.Instance);
            await dbService.InitializeDatabaseAsync();

            var transLogger = NullLogger<TranslationService>.Instance;
            var loggerFactory = new NullLoggerFactory();
            var providerMock = new Mock<ITranslationProvider>();
            providerMock.SetupGet(p => p.UsesCharacterQuota).Returns(true);
            providerMock.SetupGet(p => p.IsActive).Returns(true);
            var transService = new TranslationService(dbService, providerMock.Object, transLogger, loggerFactory);

            // Act: Initial limit check (should be 0 usage)
            bool canTranslateSmall = await transService.CanTranslateAsync(100);
            bool canTranslateLimit = await transService.CanTranslateAsync(500000);
            bool canTranslateOver = await transService.CanTranslateAsync(500001);

            // Assert
            Assert.True(canTranslateSmall);
            Assert.True(canTranslateLimit);
            Assert.False(canTranslateOver);

            // Increment usage in database and test again
            await dbService.IncrementUsageAsync(400000);

            Assert.True(await transService.CanTranslateAsync(100000));
            Assert.False(await transService.CanTranslateAsync(100001)); // Would exceed 500,000 limit
        }

        [Fact]
        public async Task CanTranslateAsync_IgnoresQuota_ForNonQuotaProvider()
        {
            using var database = new TemporarySqliteDatabase("translation-service");
            var dbService = database.CreateDatabaseService(NullLogger<DatabaseService>.Instance);
            await dbService.InitializeDatabaseAsync();
            await dbService.IncrementUsageAsync(TranslationService.MonthlyCharacterLimit);

            var transLogger = NullLogger<TranslationService>.Instance;
            var loggerFactory = new NullLoggerFactory();
            var providerMock = new Mock<ITranslationProvider>();
            providerMock.SetupGet(p => p.UsesCharacterQuota).Returns(false);
            providerMock.SetupGet(p => p.IsActive).Returns(true);
            var transService = new TranslationService(dbService, providerMock.Object, transLogger, loggerFactory);

            Assert.True(await transService.CanTranslateAsync(1000));
        }

        [Fact]
        public void IsActive_ReflectsProviderState()
        {
            var dbMock = new Mock<DatabaseService>(new Mock<IConfiguration>().Object, NullLogger<DatabaseService>.Instance);
            var transLogger = NullLogger<TranslationService>.Instance;
            var loggerFactory = new NullLoggerFactory();
            var providerMock = new Mock<ITranslationProvider>();
            providerMock.SetupGet(p => p.IsActive).Returns(false);

            var transService = new TranslationService(dbMock.Object, providerMock.Object, transLogger, loggerFactory);

            Assert.False(transService.IsActive);
        }

        [Fact]
        public async Task TranslateTextAsync_PassesSourceLanguageHintToProvider()
        {
            using var database = new TemporarySqliteDatabase("translation-service");
            var dbService = database.CreateDatabaseService(NullLogger<DatabaseService>.Instance);
            await dbService.InitializeDatabaseAsync();

            var transLogger = NullLogger<TranslationService>.Instance;
            var loggerFactory = new NullLoggerFactory();
            var providerMock = new Mock<ITranslationProvider>();
            providerMock.SetupGet(p => p.UsesCharacterQuota).Returns(false);
            providerMock.SetupGet(p => p.IsActive).Returns(true);
            providerMock
                .Setup(p => p.TranslateTextAsync("Si Tasos , sanno essere divertenti", "EN", "IT"))
                .ReturnsAsync("Yes, Tasos, they can be fun.");
            var transService = new TranslationService(dbService, providerMock.Object, transLogger, loggerFactory);

            var result = await transService.TranslateTextAsync("Si Tasos , sanno essere divertenti", "EN", "IT");

            Assert.Equal("Yes, Tasos, they can be fun.", result);
            providerMock.Verify(p => p.TranslateTextAsync("Si Tasos , sanno essere divertenti", "EN", "IT"), Times.Once);
        }

        [Fact]
        public async Task AnalyzeLanguageAsync_FailureDoesNotBlockSubsequentTranslation()
        {
            using var database = new TemporarySqliteDatabase("translation-service");
            var dbService = database.CreateDatabaseService(NullLogger<DatabaseService>.Instance);
            await dbService.InitializeDatabaseAsync();

            var transLogger = NullLogger<TranslationService>.Instance;
            var loggerFactory = new NullLoggerFactory();
            var providerMock = new Mock<ITranslationProvider>();
            providerMock.SetupGet(p => p.UsesCharacterQuota).Returns(false);
            providerMock.SetupGet(p => p.IsActive).Returns(true);
            providerMock
                .Setup(p => p.AnalyzeLanguageAsync("Updated text"))
                .ThrowsAsync(new InvalidOperationException("analysis failed"));
            providerMock
                .Setup(p => p.TranslateTextAsync("Updated text", "DE", "EN"))
                .ReturnsAsync("Aktualisierter Text");
            var transService = new TranslationService(dbService, providerMock.Object, transLogger, loggerFactory);

            await Assert.ThrowsAsync<InvalidOperationException>(() => transService.AnalyzeLanguageAsync("Updated text"));

            var result = await transService.TranslateTextAsync("Updated text", "DE", "EN");

            Assert.Equal("Aktualisierter Text", result);
            providerMock.Verify(p => p.TranslateTextAsync("Updated text", "DE", "EN"), Times.Once);
        }

        [Fact]
        public async Task AnalyzeLanguageAsync_LogsTruncatedPreview()
        {
            using var database = new TemporarySqliteDatabase("translation-service");
            var dbService = database.CreateDatabaseService(NullLogger<DatabaseService>.Instance);
            await dbService.InitializeDatabaseAsync();

            var transLogger = new ListLogger<TranslationService>();
            var loggerFactory = new NullLoggerFactory();
            var providerMock = new Mock<ITranslationProvider>();
            providerMock.SetupGet(p => p.UsesCharacterQuota).Returns(false);
            providerMock.SetupGet(p => p.IsActive).Returns(true);
            providerMock.Setup(p => p.AnalyzeLanguageAsync(It.IsAny<string>())).ReturnsAsync(LanguageAnalysisResult.SingleLanguage("EN"));
            var transService = new TranslationService(dbService, providerMock.Object, transLogger, loggerFactory);

            var result = await transService.AnalyzeLanguageAsync("12345678901234567890");

            Assert.Equal("EN", result.PrimaryLanguageCode);
            Assert.Contains(
                transLogger.Entries,
                entry => entry.Level == LogLevel.Information
                    && entry.Message.Contains("primary 'EN'")
                    && entry.Message.Contains("123456789012345")
                    && !entry.Message.Contains("1234567890123456"));
        }

        [Fact]
        public void LogProviderConfiguration_LogsProviderReportDetails()
        {
            var dbMock = new Mock<DatabaseService>(new Mock<IConfiguration>().Object, NullLogger<DatabaseService>.Instance);
            var transLogger = new Tsumari.Bot.Tests.ListLogger<TranslationService>();
            var loggerFactory = new NullLoggerFactory();
            var providerMock = new Mock<ITranslationProvider>();
            providerMock.SetupGet(p => p.IsActive).Returns(true);
            providerMock.SetupGet(p => p.UsesCharacterQuota).Returns(false);
            providerMock
                .Setup(p => p.GetConfigurationReport())
                .Returns(new TranslationProviderConfigurationReport(
                    "Ollama",
                    "OllamaTranslationProvider",
                    IsActive: true,
                    UsesCharacterQuota: false,
                    [
                        new TranslationProviderConfigurationItem("Endpoint", "http://localhost:11434/api/generate"),
                        new TranslationProviderConfigurationItem("Model", "translategemma:12b"),
                        new TranslationProviderConfigurationItem("Capabilities", "Mixed-language analysis and translation")
                    ]));

            var transService = new TranslationService(dbMock.Object, providerMock.Object, transLogger, loggerFactory);

            transService.LogProviderConfiguration();

            Assert.Contains(
                transLogger.Entries,
                entry => entry.EventId.Id == 1404
                    && entry.Message.Contains("Name=Ollama", StringComparison.Ordinal)
                    && entry.Message.Contains("Model=translategemma:12b", StringComparison.Ordinal)
                    && entry.Message.Contains("Endpoint=http://localhost:11434/api/generate", StringComparison.Ordinal));
        }

        private sealed class ListLogger<T> : ILogger<T>
        {
            public List<(LogLevel Level, string Message)> Entries { get; } = [];

            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull
            {
                return NullScope.Instance;
            }

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                Entries.Add((logLevel, formatter(state, exception)));
            }

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();

                public void Dispose()
                {
                }
            }
        }
    }
}
