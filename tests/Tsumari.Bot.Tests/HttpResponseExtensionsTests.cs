using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging;
using Tsumari.Bot.Services;

namespace Tsumari.Bot.Tests
{
    public class HttpResponseExtensionsTests
    {
        [Fact]
        public async Task ReadStringWithStatusCheckAsync_ReturnsBodyOnSuccess()
        {
            using var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "ok": true }""", Encoding.UTF8, "application/json")
            };

            var logger = new ListLogger();

            var result = await response.ReadStringWithStatusCheckAsync(logger, "reading a payload");

            Assert.Equal("""{ "ok": true }""", result);
            Assert.Empty(logger.Entries);
        }

        [Fact]
        public async Task ReadStringWithStatusCheckAsync_ThrowsAndLogsResponseBodyOnFailure()
        {
            using var response = new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                ReasonPhrase = "Bad Gateway",
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://example.com/fail"),
                Content = new StringContent("<html>proxy error cf-ray body</html>", Encoding.UTF8, "text/html")
            };
            response.Headers.Add("CF-Ray", "abc123");

            var logger = new ListLogger();

            var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
                response.ReadStringWithStatusCheckAsync(logger, "fetching a payload"));

            Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
            Assert.Contains("proxy error cf-ray body", exception.Message);
            Assert.Contains("https://example.com/fail", exception.Message);
            Assert.Contains(
                logger.Entries,
                entry => entry.Level == LogLevel.Error
                    && entry.Message.Contains("CF-Ray=abc123")
                    && entry.Message.Contains("proxy error cf-ray body"));
        }

        [Fact]
        public async Task ReadBytesWithStatusCheckAsync_ThrowsAndLogsResponseBodyOnFailure()
        {
            using var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                ReasonPhrase = "Forbidden",
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://cdn.example.com/file.png"),
                Content = new StringContent("blocked by edge", Encoding.UTF8, "text/plain")
            };
            response.Headers.Add("CF-Ray", "edge-proxy");

            var logger = new ListLogger();

            var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
                response.ReadBytesWithStatusCheckAsync(logger, "downloading a file"));

            Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
            Assert.Contains("blocked by edge", exception.Message);
            Assert.Contains(
                logger.Entries,
                entry => entry.Level == LogLevel.Error
                    && entry.Message.Contains("CF-Ray=edge-proxy")
                    && entry.Message.Contains("blocked by edge"));
        }

        [Fact]
        public async Task ReadStringWithStatusCheckAsync_HandlesResponsesWithoutBodies()
        {
            using var response = new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                ReasonPhrase = "Bad Gateway",
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://example.com/empty")
            };

            var logger = new ListLogger();

            var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
                response.ReadStringWithStatusCheckAsync(logger, "fetching an empty payload"));

            Assert.Contains("Response body: (empty)", exception.Message);
            Assert.Contains(
                logger.Entries,
                entry => entry.Level == LogLevel.Error
                    && entry.Message.Contains("Response body: (empty)"));
        }

        private sealed class ListLogger : ILogger
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
