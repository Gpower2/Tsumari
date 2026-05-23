using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tsumari.Bot.Services;
using Xunit;

namespace Tsumari.Bot.Tests
{
    public class TranslationServiceTests
    {
        [Fact]
        public async Task CanTranslateAsync_ReturnsTrue_WhenUsageIsUnderQuota()
        {
            // Arrange
            var configMock = new Mock<IConfiguration>();
            configMock.Setup(c => c["DeepL:ApiKey"]).Returns("dummy-api-key");

            var dbLogger = NullLogger<DatabaseService>.Instance;
            var dbPath = $"test_tsumari_trans_{Guid.NewGuid():N}.db";
            
            var dbConfigMock = new Mock<IConfiguration>();
            dbConfigMock.Setup(c => c["Database:FilePath"]).Returns(dbPath);
            
            var dbService = new DatabaseService(dbConfigMock.Object, dbLogger);
            await dbService.InitializeDatabaseAsync();

            var transLogger = NullLogger<TranslationService>.Instance;
            var loggerFactory = new NullLoggerFactory();

            var transService = new TranslationService(configMock.Object, dbService, transLogger, loggerFactory);

            try
            {
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
            finally
            {
                // Clean up database file
                try
                {
                    if (File.Exists(dbPath)) File.Delete(dbPath);
                    var wal = $"{dbPath}-wal";
                    if (File.Exists(wal)) File.Delete(wal);
                }
                catch {}
            }
        }

        [Fact]
        public void ServiceInitialization_SetsActiveFalse_WhenKeyIsEmpty()
        {
            // Arrange
            var configMock = new Mock<IConfiguration>();
            configMock.Setup(c => c["DeepL:ApiKey"]).Returns(string.Empty);

            var dbMock = new Mock<DatabaseService>(new Mock<IConfiguration>().Object, NullLogger<DatabaseService>.Instance);
            var transLogger = NullLogger<TranslationService>.Instance;
            var loggerFactory = new NullLoggerFactory();

            // Act
            var transService = new TranslationService(configMock.Object, dbMock.Object, transLogger, loggerFactory);

            // Assert
            Assert.False(transService.IsActive);
        }
    }
}
