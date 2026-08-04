using System.Net;
using Discord;
using Discord.Net;
using Microsoft.Extensions.Options;
using Tsumari.Bot.Configuration;
using Tsumari.Bot.Logging;

namespace Tsumari.Bot.Services
{
    /// <summary>
    /// Retries Discord.Net REST operations when they fail with transient errors such as network
    /// timeouts, connection failures, or Discord server-side errors.
    /// </summary>
    public interface IDiscordRestRetryPolicy
    {
        Task ExecuteAsync(Func<Task> operation, string operationName);
        Task<T> ExecuteAsync<T>(Func<Task<T>> operation, string operationName);
    }

    public sealed class DiscordRestRetryPolicy : IDiscordRestRetryPolicy
    {
        private readonly DiscordRestOptions _options;
        private readonly ILogger<DiscordRestRetryPolicy> _logger;

        public DiscordRestRetryPolicy(IOptions<DiscordRestOptions> options, ILogger<DiscordRestRetryPolicy> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public Task ExecuteAsync(Func<Task> operation, string operationName)
        {
            return ExecuteAsync(async () =>
            {
                await operation().ConfigureAwait(false);
                return true;
            }, operationName);
        }

        public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, string operationName)
        {
            Exception? lastException = null;

            for (var attempt = 0; attempt <= _options.MaxRetries; attempt++)
            {
                try
                {
                    if (attempt > 0)
                    {
                        _logger.LogDiscordRestRetry(operationName, attempt, _options.MaxRetries);
                    }

                    return await operation().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Caller-initiated cancellation (e.g. shutdown) must not be retried.
                    throw;
                }
                catch (HttpException ex) when (IsRetryableHttpCode(ex.HttpCode))
                {
                    lastException = ex;
                    _logger.LogDiscordRestRetryableFailure(ex, operationName, attempt, _options.MaxRetries, (int)ex.HttpCode);
                }
                catch (HttpRequestException ex)
                {
                    lastException = ex;
                    _logger.LogDiscordRestRetryableFailure(ex, operationName, attempt, _options.MaxRetries, null);
                }
                catch (TimeoutException ex)
                {
                    lastException = ex;
                    _logger.LogDiscordRestTimeout(ex, operationName, attempt, _options.MaxRetries);
                }

                if (attempt < _options.MaxRetries)
                {
                    var delay = TimeSpan.FromTicks(_options.RetryDelay.Ticks * (attempt + 1));
                    await Task.Delay(delay).ConfigureAwait(false);
                }
            }

            var failureException = lastException ?? new InvalidOperationException($"Discord REST operation '{operationName}' failed but no exception was captured.");
            _logger.LogDiscordRestOperationFailed(failureException, operationName, _options.MaxRetries);
            throw failureException;
        }

        private static bool IsRetryableHttpCode(HttpStatusCode httpCode)
        {
            return httpCode is >= HttpStatusCode.InternalServerError and <= HttpStatusCode.HttpVersionNotSupported
                or HttpStatusCode.RequestTimeout
                or HttpStatusCode.TooManyRequests;
        }
    }
}
