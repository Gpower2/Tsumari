using System.Linq;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.Extensions
{
    public static class HttpResponseExtensions
    {
        private const int MaxLoggedResponseBodyLength = 4096;

        public static async Task<string> ReadStringWithStatusCheckAsync(
            this HttpResponseMessage response,
            ILogger logger,
            string operationName)
        {
            var responseBody = await ReadResponseBodyAsStringAsync(response);

            if (!response.IsSuccessStatusCode)
            {
                LogFailure(logger, response, operationName, responseBody);
                throw CreateHttpRequestException(response, operationName, responseBody);
            }

            return responseBody;
        }

        public static async Task<byte[]> ReadBytesWithStatusCheckAsync(
            this HttpResponseMessage response,
            ILogger logger,
            string operationName)
        {
            if (response.IsSuccessStatusCode)
            {
                return response.Content == null
                    ? Array.Empty<byte>()
                    : await response.Content.ReadAsByteArrayAsync();
            }

            var responseBody = await ReadResponseBodyAsStringAsync(response);
            LogFailure(logger, response, operationName, responseBody);
            throw CreateHttpRequestException(response, operationName, responseBody);
        }

        private static void LogFailure(
            ILogger logger,
            HttpResponseMessage response,
            string operationName,
            string responseBody)
        {
            logger.LogHttpRequestFailed(
                operationName,
                (int)response.StatusCode,
                response.ReasonPhrase,
                response.RequestMessage?.RequestUri?.ToString() ?? "(unknown)",
                FormatHeaders(response),
                Truncate(responseBody));
        }

        private static HttpRequestException CreateHttpRequestException(
            HttpResponseMessage response,
            string operationName,
            string responseBody)
        {
            var message =
                $"HTTP request failed while {operationName}. " +
                $"Status: {(int)response.StatusCode} {response.ReasonPhrase}. " +
                $"Url: {response.RequestMessage?.RequestUri?.ToString() ?? "(unknown)"}. " +
                $"Response body: {Truncate(responseBody)}";

            return new HttpRequestException(message, null, response.StatusCode);
        }

        private static string FormatHeaders(HttpResponseMessage response)
        {
            var headers = response.Headers
                .Select(header => $"{header.Key}={string.Join(",", header.Value)}");
            var contentHeaders = response.Content?.Headers
                .Select(header => $"{header.Key}={string.Join(",", header.Value)}")
                ?? Enumerable.Empty<string>();

            return string.Join("; ", headers.Concat(contentHeaders));
        }

        private static async Task<string> ReadResponseBodyAsStringAsync(HttpResponseMessage response)
        {
            return response.Content == null
                ? string.Empty
                : await response.Content.ReadAsStringAsync();
        }

        private static string Truncate(string responseBody)
        {
            if (string.IsNullOrEmpty(responseBody))
            {
                return "(empty)";
            }

            if (responseBody.Length <= MaxLoggedResponseBodyLength)
            {
                return responseBody;
            }

            return string.Create(
                MaxLoggedResponseBodyLength + 3,
                responseBody,
                static (destination, source) =>
                {
                    source.AsSpan(0, MaxLoggedResponseBodyLength).CopyTo(destination);
                    "...".AsSpan().CopyTo(destination[MaxLoggedResponseBodyLength..]);
                });
        }
    }
}
