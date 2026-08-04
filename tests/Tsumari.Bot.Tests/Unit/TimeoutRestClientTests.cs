using System.Net;
using Discord.Net.Rest;
using Tsumari.Bot.Services;
using Xunit;

namespace Tsumari.Bot.Tests.Unit
{
    public class TimeoutRestClientTests
    {
        [Fact]
        public async Task SendAsync_Applies_Timeout_And_Throws_TimeoutException()
        {
            var inner = new DelayedRestClient(TimeSpan.FromSeconds(10));
            var client = new TimeoutRestClient(inner, TimeSpan.FromMilliseconds(50));

            var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
                client.SendAsync("GET", "test", CancellationToken.None));

            Assert.Contains("timed out", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task SendAsync_Returns_Response_When_Inner_Completes_Quickly()
        {
            var expected = new RestResponse(HttpStatusCode.OK, new Dictionary<string, string>(), Stream.Null);
            var inner = new ImmediateRestClient(expected);
            var client = new TimeoutRestClient(inner, TimeSpan.FromSeconds(5));

            var response = await client.SendAsync("GET", "test", CancellationToken.None);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task SendAsync_Respects_Caller_CancellationToken()
        {
            var inner = new DelayedRestClient(TimeSpan.FromSeconds(10));
            var client = new TimeoutRestClient(inner, TimeSpan.FromSeconds(5));
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                client.SendAsync("GET", "test", cts.Token));
        }

        [Fact]
        public void SetHeader_Forwards_To_Inner()
        {
            var inner = new ImmediateRestClient(new RestResponse(HttpStatusCode.OK, new Dictionary<string, string>(), Stream.Null));
            var client = new TimeoutRestClient(inner, TimeSpan.FromSeconds(5));

            client.SetHeader("X-Test", "value");

            Assert.Equal("value", inner.GetHeader("X-Test"));
        }

        private sealed class DelayedRestClient : IRestClient
        {
            private readonly TimeSpan _delay;

            public DelayedRestClient(TimeSpan delay)
            {
                _delay = delay;
            }

            public void Dispose()
            {
            }

            public void SetHeader(string key, string value)
            {
            }

            public void SetCancelToken(CancellationToken cancelToken)
            {
            }

            public async Task<RestResponse> SendAsync(string method, string endpoint, CancellationToken cancelToken, bool headerOnly = false, string? reason = null, IEnumerable<KeyValuePair<string, IEnumerable<string>>>? requestHeaders = null)
            {
                await Task.Delay(_delay, cancelToken);
                return new RestResponse(HttpStatusCode.OK, new Dictionary<string, string>(), Stream.Null);
            }

            public async Task<RestResponse> SendAsync(string method, string endpoint, string json, CancellationToken cancelToken, bool headerOnly = false, string? reason = null, IEnumerable<KeyValuePair<string, IEnumerable<string>>>? requestHeaders = null)
            {
                await Task.Delay(_delay, cancelToken);
                return new RestResponse(HttpStatusCode.OK, new Dictionary<string, string>(), Stream.Null);
            }

            public async Task<RestResponse> SendAsync(string method, string endpoint, IReadOnlyDictionary<string, object> multipartParams, CancellationToken cancelToken, bool headerOnly = false, string? reason = null, IEnumerable<KeyValuePair<string, IEnumerable<string>>>? requestHeaders = null)
            {
                await Task.Delay(_delay, cancelToken);
                return new RestResponse(HttpStatusCode.OK, new Dictionary<string, string>(), Stream.Null);
            }
        }

        private sealed class ImmediateRestClient : IRestClient
        {
            private readonly RestResponse _response;
            private readonly Dictionary<string, string> _headers = new();

            public ImmediateRestClient(RestResponse response)
            {
                _response = response;
            }

            public void Dispose()
            {
            }

            public void SetHeader(string key, string value)
            {
                _headers[key] = value;
            }

            public string? GetHeader(string key)
            {
                return _headers.TryGetValue(key, out var value) ? value : null;
            }

            public void SetCancelToken(CancellationToken cancelToken)
            {
            }

            public Task<RestResponse> SendAsync(string method, string endpoint, CancellationToken cancelToken, bool headerOnly = false, string? reason = null, IEnumerable<KeyValuePair<string, IEnumerable<string>>>? requestHeaders = null)
            {
                return Task.FromResult(_response);
            }

            public Task<RestResponse> SendAsync(string method, string endpoint, string json, CancellationToken cancelToken, bool headerOnly = false, string? reason = null, IEnumerable<KeyValuePair<string, IEnumerable<string>>>? requestHeaders = null)
            {
                return Task.FromResult(_response);
            }

            public Task<RestResponse> SendAsync(string method, string endpoint, IReadOnlyDictionary<string, object> multipartParams, CancellationToken cancelToken, bool headerOnly = false, string? reason = null, IEnumerable<KeyValuePair<string, IEnumerable<string>>>? requestHeaders = null)
            {
                return Task.FromResult(_response);
            }
        }
    }
}
