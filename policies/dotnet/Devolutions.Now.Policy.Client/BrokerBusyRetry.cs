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
}