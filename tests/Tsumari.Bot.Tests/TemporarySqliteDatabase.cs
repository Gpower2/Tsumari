using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tsumari.Bot.Services;

namespace Tsumari.Bot.Tests
{
    public sealed class TemporarySqliteDatabase : IDisposable
    {
        private readonly string _directoryPath;
        private readonly IConfiguration _configuration;

        public TemporarySqliteDatabase(string prefix)
        {
            _directoryPath = Path.Combine(
                Path.GetTempPath(),
                "Tsumari.Bot.Tests",
                $"{prefix}_{Guid.NewGuid():N}");

            Directory.CreateDirectory(_directoryPath);

            DatabasePath = Path.Combine(_directoryPath, "tsumari.db");
            _configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:FilePath"] = DatabasePath
                })
                .Build();
        }

        public string DatabasePath { get; }

        public DatabaseService CreateDatabaseService(ILogger<DatabaseService>? logger = null)
        {
            return new DatabaseService(_configuration, logger ?? NullLogger<DatabaseService>.Instance);
        }

        public void Dispose()
        {
            // Clear only this database's connection pool. Using ClearAllPools can race with
            // other parallel tests that are actively opening connections to their own databases.
            using (var connection = new SqliteConnection($"Data Source={DatabasePath}"))
            {
                try
                {
                    connection.Open();
                    SqliteConnection.ClearPool(connection);
                }
                catch (SqliteException)
                {
                    // The database may not exist if initialization failed; ignore.
                }
            }

            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (Directory.Exists(_directoryPath))
                    {
                        Directory.Delete(_directoryPath, recursive: true);
                    }

                    return;
                }
                catch (IOException) when (attempt < 4)
                {
                    SqliteConnection.ClearAllPools();
                    Thread.Sleep(25);
                }
                catch (UnauthorizedAccessException) when (attempt < 4)
                {
                    SqliteConnection.ClearAllPools();
                    Thread.Sleep(25);
                }
            }
        }
    }
}
