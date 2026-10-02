using System.Globalization;

namespace Devolutions.Now.Policy.Client;

/// <summary>Decides whether a busy broker reply is retried and after which delay.</summary>
internal static class BrokerBusyRetry
{
    private const int ServiceUnavailable = 503;

    /// <summary>
    /// Return the delay before retrying <paramref name="response"/>, or <c>null</c> when it is not a busy reply.
    /// Only <c>503</c> replies carrying a valid <c>Retry-After</c> header are retried: the broker sends them
    /// before processing the request.
    /// </summary>
    internal static TimeSpan? GetRetryDelay(BrokerHttpResponse response, TimeSpan maxDelay, DateTimeOffset now)
    {
        if (response.StatusCode != ServiceUnavailable || string.IsNullOrWhiteSpace(response.RetryAfter))
        {
            return null;
        }

        TimeSpan delay;
        if (long.TryParse(response.RetryAfter, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            delay = seconds >= (long)maxDelay.TotalSeconds + 1 ? maxDelay : TimeSpan.FromSeconds(seconds);
        }
        else if (DateTimeOffset.TryParseExact(
                     response.RetryAfter,
                     "r",
                     CultureInfo.InvariantCulture,
                     DateTimeStyles.AdjustToUniversal,
                     out var retryAt))
        {
            delay = retryAt - now;
        }
        else
        {
            return null;
        }

        if (delay < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return delay > maxDelay ? maxDelay : delay;
    }

    /// <summary>Initial delay before retrying a connection the broker closed without responding.</summary>
    internal static readonly TimeSpan InitialDisconnectRetryDelay = TimeSpan.FromMilliseconds(100);

    /// <summary>Exponential backoff for connections closed without a response: 100 ms, 200 ms, 400 ms, capped.</summary>
    internal static TimeSpan GetDisconnectRetryDelay(int attempt, TimeSpan maxDelay)
    {
        var delay = InitialDisconnectRetryDelay * Math.Pow(2, Math.Min(attempt, 16));
        return delay > maxDelay ? maxDelay : delay;
    }

    /// <summary>
    /// Whether a request may be sent again after the broker closed the connection without responding, once the
    /// request may have reached it. Only requests without side effects qualify.
    /// </summary>
    internal static bool IsSafeToResend(BrokerTransportRequest request)
    {
        if (request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            || request.Method.Equals("HEAD", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var queryStart = request.Path.IndexOf('?');
        var path = queryStart < 0 ? request.Path : request.Path[..queryStart];
        return path is "/v1/package-operations/evaluate" or "/v1/package-operations/get-status" or "/v1/policy/validate";
    }
}