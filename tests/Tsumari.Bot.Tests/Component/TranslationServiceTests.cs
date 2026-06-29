using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
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
        public async Task DetectLanguageAsync_LogsTruncatedPreview()
        {
            using var database = new TemporarySqliteDatabase("translation-service");
            var dbService = database.CreateDatabaseService(NullLogger<DatabaseService>.Instance);
            await dbService.InitializeDatabaseAsync();

            var transLogger = new ListLogger<TranslationService>();
            var loggerFactory = new NullLoggerFactory();
            var providerMock = new Mock<ITranslationProvider>();
            providerMock.SetupGet(p => p.UsesCharacterQuota).Returns(false);
            providerMock.SetupGet(p => p.IsActive).Returns(true);
            providerMock.Setup(p => p.DetectLanguageAsync(It.IsAny<string>())).ReturnsAsync("EN");
            var transService = new TranslationService(dbService, providerMock.Object, transLogger, loggerFactory);

            var result = await transService.DetectLanguageAsync("12345678901234567890");

            Assert.Equal("EN", result);
            Assert.Contains(
                transLogger.Entries,
                entry => entry.Level == LogLevel.Information
                    && entry.Message.Contains("Language detected: 'EN'")
                    && entry.Message.Contains("123456789012345")
                    && !entry.Message.Contains("1234567890123456"));
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
