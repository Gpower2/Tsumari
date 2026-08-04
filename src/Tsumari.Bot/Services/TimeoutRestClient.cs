using System.Net;
using Discord.Net.Rest;

namespace Tsumari.Bot.Services
{
    /// <summary>
    /// Wraps a Discord.Net REST client and enforces a per-request timeout on every outgoing call.
    /// This prevents a single stalled Discord API request from blocking the entire message pipeline.
    /// </summary>
    public sealed class TimeoutRestClient : IRestClient
    {
        private readonly IRestClient _inner;
        private readonly TimeSpan _timeout;

        public TimeoutRestClient(IRestClient inner, TimeSpan timeout)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _timeout = timeout;
        }

        public void Dispose()
        {
            _inner.Dispose();
        }

        public void SetHeader(string key, string value)
        {
            _inner.SetHeader(key, value);
        }

        public void SetCancelToken(CancellationToken cancelToken)
        {
            _inner.SetCancelToken(cancelToken);
        }

        public Task<RestResponse> SendAsync(
            string method,
            string endpoint,
            CancellationToken cancelToken,
            bool headerOnly = false,
            string? reason = null,
            IEnumerable<KeyValuePair<string, IEnumerable<string>>>? requestHeaders = null)
        {
            return ExecuteWithTimeoutAsync(ct => _inner.SendAsync(method, endpoint, ct, headerOnly, reason, requestHeaders), cancelToken);
        }

        public Task<RestResponse> SendAsync(
            string method,
            string endpoint,
            string json,
            CancellationToken cancelToken,
            bool headerOnly = false,
            string? reason = null,
            IEnumerable<KeyValuePair<string, IEnumerable<string>>>? requestHeaders = null)
        {
            return ExecuteWithTimeoutAsync(ct => _inner.SendAsync(method, endpoint, json, ct, headerOnly, reason, requestHeaders), cancelToken);
        }

        public Task<RestResponse> SendAsync(
            string method,
            string endpoint,
            IReadOnlyDictionary<string, object> multipartParams,
            CancellationToken cancelToken,
            bool headerOnly = false,
            string? reason = null,
            IEnumerable<KeyValuePair<string, IEnumerable<string>>>? requestHeaders = null)
        {
            return ExecuteWithTimeoutAsync(ct => _inner.SendAsync(method, endpoint, multipartParams, ct, headerOnly, reason, requestHeaders), cancelToken);
        }

        private async Task<RestResponse> ExecuteWithTimeoutAsync(Func<CancellationToken, Task<RestResponse>> operation, CancellationToken cancelToken)
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);
            timeoutSource.CancelAfter(_timeout);
            try
            {
                return await operation(timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (!cancelToken.IsCancellationRequested)
            {
                // The application token is still valid, which means the timeout triggered before
                // the caller asked for cancellation. Surface this as an explicit timeout so retry
                // logic can distinguish it from a genuine shutdown/cancellation signal.
                throw new TimeoutException($"Discord REST request timed out after {_timeout}.", ex);
            }
        }
    }

    /// <summary>
    /// Provides Discord.Net REST clients that are wrapped with a per-request timeout.
    /// </summary>
    public static class TimeoutRestClientProvider
    {
        public static RestClientProvider Create(TimeSpan timeout)
        {
            return url => new TimeoutRestClient(DefaultRestClientProvider.Instance(url), timeout);
        }
    }
}
