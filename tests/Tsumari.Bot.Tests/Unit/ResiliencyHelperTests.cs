using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Tsumari.Bot.Services;
using Xunit;

namespace Tsumari.Bot.Tests.Unit
{
    public class ResiliencyHelperTests
    {
        [Fact]
        public async Task ExecuteAsync_SucceedsOnFirstAttempt_UnderNormalConditions()
        {
            // Arrange
            var helper = new ResiliencyHelper(
                failureThreshold: 2,
                breakDuration: TimeSpan.FromSeconds(5),
                maxRetryAttempts: 3,
                initialRetryDelay: TimeSpan.FromMilliseconds(10),
                logger: NullLogger<ResiliencyHelper>.Instance
            );

            int callCount = 0;
            Func<Task<string>> action = () =>
            {
                callCount++;
                return Task.FromResult("Success");
            };

            // Act
            var result = await helper.ExecuteAsync(action);

            // Assert
            Assert.Equal("Success", result);
            Assert.Equal(1, callCount);
            Assert.Equal(CircuitState.Closed, helper.State);
        }

        [Fact]
        public async Task ExecuteAsync_RetriesAndEventuallySucceeds_OnTransientFailures()
        {
            // Arrange
            var helper = new ResiliencyHelper(
                failureThreshold: 5, // high threshold to prevent tripping circuit
                breakDuration: TimeSpan.FromSeconds(5),
                maxRetryAttempts: 3,
                initialRetryDelay: TimeSpan.FromMilliseconds(5),
                logger: NullLogger<ResiliencyHelper>.Instance
            );

            int callCount = 0;
            Func<Task<string>> action = () =>
            {
                callCount++;
                if (callCount < 3)
                {
                    throw new Exception("Transient failure");
                }
                return Task.FromResult("Succeeded finally");
            };

            // Act
            var result = await helper.ExecuteAsync(action);

            // Assert
            Assert.Equal("Succeeded finally", result);
            Assert.Equal(3, callCount);
            Assert.Equal(CircuitState.Closed, helper.State);
        }

        [Fact]
        public async Task ExecuteAsync_TripsToOpen_OnExceedingFailureThreshold()
        {
            // Arrange
            var helper = new ResiliencyHelper(
                failureThreshold: 3,
                breakDuration: TimeSpan.FromSeconds(5),
                maxRetryAttempts: 1, // Fail fast on first retry attempt to let threshold count up
                initialRetryDelay: TimeSpan.FromMilliseconds(1),
                logger: NullLogger<ResiliencyHelper>.Instance
            );

            Func<Task<string>> action = () => throw new Exception("Persistent error");

            // Act & Assert: run failures up to threshold
            for (int i = 0; i < 3; i++)
            {
                await Assert.ThrowsAsync<Exception>(() => helper.ExecuteAsync(action));
            }

            // The circuit should trip to OPEN
            Assert.Equal(CircuitState.Open, helper.State);

            // Subsequent requests should fail fast immediately with InvalidOperationException
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => helper.ExecuteAsync(action));
            Assert.Contains("Circuit breaker is currently open", ex.Message);
        }

        [Fact]
        public async Task CircuitBreaker_TransitionsToHalfOpen_AfterBreakDuration()
        {
            // Arrange
            var helper = new ResiliencyHelper(
                failureThreshold: 1,
                breakDuration: TimeSpan.FromMilliseconds(50), // short break duration for test speed
                maxRetryAttempts: 1,
                initialRetryDelay: TimeSpan.FromMilliseconds(1),
                logger: NullLogger<ResiliencyHelper>.Instance
            );

            Func<Task<string>> action = () => throw new Exception("Trigger fail");

            // Trip the circuit to OPEN
            await Assert.ThrowsAsync<Exception>(() => helper.ExecuteAsync(action));
            Assert.Equal(CircuitState.Open, helper.State);

            // Wait for break duration to expire
            await Task.Delay(60);

            // A successful call should transition the circuit state back to CLOSED
            int callCount = 0;
            Func<Task<string>> successAction = () =>
            {
                callCount++;
                return Task.FromResult("Recovered");
            };

            var result = await helper.ExecuteAsync(successAction);

            // Assert
            Assert.Equal("Recovered", result);
            Assert.Equal(1, callCount);
            Assert.Equal(CircuitState.Closed, helper.State);
        }
    }
}
