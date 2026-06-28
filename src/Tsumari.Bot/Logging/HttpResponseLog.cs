using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.Logging
{
    public static partial class HttpResponseLog
    {
        [LoggerMessage(
            EventId = 2200,
            Level = LogLevel.Error,
            Message = "HTTP request failed while {OperationName}. Status: {StatusCode} {ReasonPhrase}. Url: {Url}. Headers: {Headers}. Response body: {ResponseBody}"
        )]
        public static partial void LogHttpRequestFailed(this ILogger logger, string operationName, int statusCode, string? reasonPhrase, string url, string headers, string responseBody);
    }
}
