using System.Net;
using Discord;
using Discord.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tsumari.Bot.Configuration;
using Tsumari.Bot.Services;
using Xunit;

namespace Tsumari.Bot.Tests.Unit
{
    public class DiscordRestRetryPolicyTests
    {
        [Fact]
        public async Task ExecuteAsync_Returns_Result_When_Operation_Succeeds()
        {
            var policy = CreatePolicy();

            var result = await policy.ExecuteAsync(() => Task.FromResult(42), "test");

            Assert.Equal(42, result);
        }

        [Fact]
        public async Task ExecuteAsync_Retries_On_TimeoutException_And_Succeeds()
        {
            var policy = CreatePolicy(maxRetries: 2, delayMs: 10);
            var attempts = 0;

            var result = await policy.ExecuteAsync<int>(() =>
            {
                attempts++;
                if (attempts < 2)
                {
                    throw new TimeoutException();
                }

                return Task.FromResult(42);
            }, "test");

            Assert.Equal(42, result);
            Assert.Equal(2, attempts);
        }

        [Fact]
        public async Task ExecuteAsync_Retries_On_HttpRequestException_And_Succeeds()
        {
            var policy = CreatePolicy(maxRetries: 2, delayMs: 10);
            var attempts = 0;

            var result = await policy.ExecuteAsync<int>(() =>
            {
                attempts++;
                if (attempts < 2)
                {
                    throw new HttpRequestException();
                }

                return Task.FromResult(42);
            }, "test");

            Assert.Equal(42, result);
            Assert.Equal(2, attempts);
        }

        [Fact]
        public async Task ExecuteAsync_Retries_On_Retryable_HttpException_And_Succeeds()
        {
            var policy = CreatePolicy(maxRetries: 2, delayMs: 10);
            var attempts = 0;

            var result = await policy.ExecuteAsync<int>(() =>
            {
                attempts++;
                if (attempts < 2)
                {
                    throw CreateHttpException(HttpStatusCode.ServiceUnavailable);
                }

                return Task.FromResult(42);
            }, "test");

            Assert.Equal(42, result);
            Assert.Equal(2, attempts);
        }

        [Fact]
        public async Task ExecuteAsync_Does_Not_Retry_On_ClientError_HttpException()
        {
            var policy = CreatePolicy(maxRetries: 2, delayMs: 10);
            var attempts = 0;

            var ex = await Assert.ThrowsAsync<HttpException>(() => policy.ExecuteAsync<int>(() =>
            {
                attempts++;
                throw CreateHttpException(HttpStatusCode.Forbidden);
            }, "test"));

            Assert.Equal(HttpStatusCode.Forbidden, ex.HttpCode);
            Assert.Equal(1, attempts);
        }

        [Fact]
        public async Task ExecuteAsync_Does_Not_Retry_On_User_Cancellation()
        {
            var policy = CreatePolicy(maxRetries: 2, delayMs: 10);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => policy.ExecuteAsync(
                async () => await Task.FromCanceled(cts.Token),
                "test"));
        }

        [Fact]
        public async Task ExecuteAsync_Throws_Last_Exception_When_All_Retries_Exhausted()
        {
            var policy = CreatePolicy(maxRetries: 1, delayMs: 10);
            var attempts = 0;

            var ex = await Assert.ThrowsAsync<TimeoutException>(() => policy.ExecuteAsync<int>(() =>
            {
                attempts++;
                throw new TimeoutException();
            }, "test"));

            Assert.NotNull(ex);
            Assert.Equal(2, attempts);
        }

        [Fact]
        public async Task ExecuteAsync_Applies_Increasing_Delay_Between_Retries()
        {
            var policy = CreatePolicy(maxRetries: 3, delayMs: 50);
            var attempts = 0;
            var timestamps = new List<DateTimeOffset>();

            var ex = await Assert.ThrowsAsync<TimeoutException>(() => policy.ExecuteAsync<int>(() =>
            {
                attempts++;
                timestamps.Add(DateTimeOffset.UtcNow);
                throw new TimeoutException();
            }, "test"));

            Assert.Equal(4, attempts);
            // First attempt has no delay; subsequent delays are 50ms, 100ms, 150ms.
            var delay1 = timestamps[1] - timestamps[0];
            var delay2 = timestamps[2] - timestamps[1];
            var delay3 = timestamps[3] - timestamps[2];
            Assert.True(delay1 >= TimeSpan.FromMilliseconds(40), $"First retry delay was {delay1}");
            Assert.True(delay2 >= TimeSpan.FromMilliseconds(90), $"Second retry delay was {delay2}");
            Assert.True(delay3 >= TimeSpan.FromMilliseconds(140), $"Third retry delay was {delay3}");
        }

        private static DiscordRestRetryPolicy CreatePolicy(int maxRetries = 2, int delayMs = 10)
        {
            var options = new DiscordRestOptions
            {
                MaxRetries = maxRetries,
                RetryDelay = TimeSpan.FromMilliseconds(delayMs)
            };

            return new DiscordRestRetryPolicy(
                new OptionsWrapper<DiscordRestOptions>(options),
                NullLogger<DiscordRestRetryPolicy>.Instance);
        }

        private static HttpException CreateHttpException(HttpStatusCode statusCode)
        {
            return new HttpException(statusCode, null!);
        }
    }
}
