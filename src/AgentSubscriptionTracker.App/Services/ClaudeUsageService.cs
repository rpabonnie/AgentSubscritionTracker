using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentSubscriptionTracker.App.Services;

/// <summary>
/// SPEC-0001 Claude usage service: read-only fresh-per-poll credential discovery,
/// in-memory-only OAuth refresh, defensive response mapping, min-poll-interval cache
/// with stale-data carry-over, and full token redaction. Never throws to the caller
/// except for the caller's own cancellation.
/// </summary>
public sealed class ClaudeUsageService : IClaudeUsageService, IDisposable
{
    private const string AnthropicBetaHeaderName = "anthropic-beta";
    private const string AnthropicBetaHeaderValue = "oauth-2025-04-20";

    /// <summary>Usage calls may only target api.anthropic.com (CLAUDE.md network allowlist).</summary>
    private const string AllowedUsageHost = "api.anthropic.com";

    /// <summary>The refresh grant (which carries the refresh token) may only target these hosts (ADR-0002).</summary>
    private static readonly string[] AllowedRefreshHosts = ["platform.claude.com", "console.anthropic.com"];

    /// <summary>Responses are untrusted input; bound the payload read to 1 MiB.</summary>
    private const int MaxResponseChars = 1024 * 1024;

    private readonly IClaudeCredentialsReader _credentialsReader;
    private readonly TimeProvider _timeProvider;
    private readonly ClaudeUsageServiceOptions _options;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _pollGate = new(1, 1);

    /// <summary>Refreshed token kept for the process lifetime only — never written to disk.</summary>
    private ClaudeOAuthCredentials? _refreshedCredentials;
    private ClaudeUsageSnapshot? _lastResult;
    private ClaudeUsageSnapshot? _lastSuccess;
    private DateTimeOffset? _lastNetworkPollAt;
    private bool _disposed;

    /// <summary>Creates the service. The handler is injected for testability and not disposed here.</summary>
    public ClaudeUsageService(
        IClaudeCredentialsReader credentialsReader,
        HttpMessageHandler httpMessageHandler,
        TimeProvider timeProvider,
        ClaudeUsageServiceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(credentialsReader);
        ArgumentNullException.ThrowIfNull(httpMessageHandler);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _credentialsReader = credentialsReader;
        _timeProvider = timeProvider;
        _options = options ?? new ClaudeUsageServiceOptions();
        ValidateEndpointAllowlist(_options);
        _httpClient = new HttpClient(httpMessageHandler, disposeHandler: false)
        {
            Timeout = _options.HttpTimeout,
        };
    }

    /// <summary>
    /// Tokens are only ever sent to allowlisted HTTPS hosts; a misconfigured options object
    /// must fail at construction, never silently exfiltrate (mirrors CopilotQuotaService).
    /// </summary>
    private static void ValidateEndpointAllowlist(ClaudeUsageServiceOptions options)
    {
        if (!IsAllowed(options.UsageEndpoint, AllowedUsageHost))
        {
            throw new ArgumentException(
                $"UsageEndpoint must be an https URI on {AllowedUsageHost} (allowlisted host).",
                nameof(options));
        }

        foreach (var endpoint in options.TokenRefreshEndpoints)
        {
            if (!AllowedRefreshHosts.Any(host => IsAllowed(endpoint, host)))
            {
                throw new ArgumentException(
                    $"TokenRefreshEndpoints must be https URIs on {string.Join(" or ", AllowedRefreshHosts)} (ADR-0002).",
                    nameof(options));
            }
        }

        static bool IsAllowed(Uri endpoint, string host) =>
            endpoint.IsAbsoluteUri &&
            Uri.UriSchemeHttps.Equals(endpoint.Scheme, StringComparison.OrdinalIgnoreCase) &&
            host.Equals(endpoint.Host, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task<ClaudeUsageSnapshot> GetUsageAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Serialize polls so overlapping calls never produce overlapping network requests.
        await _pollGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await PollAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _pollGate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _httpClient.Dispose();
        _pollGate.Dispose();
    }

    private async Task<ClaudeUsageSnapshot> PollAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();

        // §4.1 step 1 — min-interval gate (plus Retry-After honoring when rate limited).
        if (TryGetGatedSnapshot(now) is { } gated)
        {
            return gated;
        }

        // §4.1 step 2 — fresh credential read each non-gated poll. NotSignedIn does not
        // start the min-interval window (file reads are cheap; sign-in must be picked up fast).
        var fileCredentials = _credentialsReader.Read();
        if (fileCredentials is null)
        {
            return new ClaudeUsageSnapshot
            {
                State = ClaudeProviderState.NotSignedIn,
                RetrievedAt = now,
            };
        }

        var credentials = PreferFresherCredentials(fileCredentials);
        var refreshedThisPoll = false;

        // §4.1 step 3 — proactive in-memory refresh; the credentials file is never rewritten.
        if (credentials.ExpiresAt <= now)
        {
            if (string.IsNullOrWhiteSpace(credentials.RefreshToken))
            {
                // No network was touched; do not start the min-interval window.
                return BuildFailureSnapshot(ClaudeProviderState.TokenExpired, now, retryAfter: null);
            }

            var refreshed = await TryRefreshTokenAsync(credentials, cancellationToken).ConfigureAwait(false);
            if (refreshed is null)
            {
                return CompletePoll(
                    BuildFailureSnapshot(ClaudeProviderState.TokenExpired, now, retryAfter: null), now);
            }

            credentials = refreshed;
            refreshedThisPoll = true;
        }

        // §4.1 steps 4–6.
        var snapshot = await RequestUsageAsync(credentials, refreshedThisPoll, now, cancellationToken)
            .ConfigureAwait(false);
        return CompletePoll(snapshot, now);
    }

    private ClaudeUsageSnapshot? TryGetGatedSnapshot(DateTimeOffset now)
    {
        if (_lastNetworkPollAt is not { } lastPollAt || _lastResult is not { } lastResult)
        {
            return null;
        }

        var window = _options.MinPollInterval;
        if (lastResult.State == ClaudeProviderState.RateLimited &&
            lastResult.RetryAfter is { } retryAfter &&
            retryAfter > window)
        {
            window = retryAfter;
        }

        return now - lastPollAt < window
            ? lastResult with { IsFromCache = true }
            : null;
    }

    private ClaudeOAuthCredentials PreferFresherCredentials(ClaudeOAuthCredentials fileCredentials) =>
        _refreshedCredentials is { } refreshed && refreshed.ExpiresAt > fileCredentials.ExpiresAt
            ? refreshed
            : fileCredentials;

    private ClaudeUsageSnapshot CompletePoll(ClaudeUsageSnapshot snapshot, DateTimeOffset now)
    {
        _lastNetworkPollAt = now;
        _lastResult = snapshot;
        if (snapshot is { State: ClaudeProviderState.Ok, IsFromCache: false })
        {
            _lastSuccess = snapshot;
        }

        return snapshot;
    }

    /// <summary>
    /// §4.1 step 6 — stale-data carry-over: a failure after a previous success keeps the
    /// cached buckets (and the cached RetrievedAt, so data age keeps growing) with the
    /// new error state.
    /// </summary>
    private ClaudeUsageSnapshot BuildFailureSnapshot(
        ClaudeProviderState state, DateTimeOffset now, TimeSpan? retryAfter)
    {
        if (_lastSuccess is { } lastSuccess)
        {
            return lastSuccess with
            {
                State = state,
                IsFromCache = true,
                RetryAfter = retryAfter,
            };
        }

        return new ClaudeUsageSnapshot
        {
            State = state,
            RetrievedAt = now,
            RetryAfter = retryAfter,
        };
    }

    private async Task<ClaudeUsageSnapshot> RequestUsageAsync(
        ClaudeOAuthCredentials credentials,
        bool refreshAlreadyUsed,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var transientAttempts = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            HttpResponseMessage? response = null;
            try
            {
                try
                {
                    using var request = CreateUsageRequest(credentials.AccessToken);
                    response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException)
                {
                    if (transientAttempts >= _options.MaxRetries)
                    {
                        return BuildFailureSnapshot(ClaudeProviderState.Unavailable, now, retryAfter: null);
                    }

                    await BackoffAsync(transientAttempts, cancellationToken).ConfigureAwait(false);
                    transientAttempts++;
                    continue;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // HttpClient timeout (not the caller's token) — transient.
                    if (transientAttempts >= _options.MaxRetries)
                    {
                        return BuildFailureSnapshot(ClaudeProviderState.Unavailable, now, retryAfter: null);
                    }

                    await BackoffAsync(transientAttempts, cancellationToken).ConfigureAwait(false);
                    transientAttempts++;
                    continue;
                }

                switch (response.StatusCode)
                {
                    case HttpStatusCode.OK:
                    {
                        var snapshot = await TryParseUsageResponseAsync(response, credentials, now, cancellationToken)
                            .ConfigureAwait(false);
                        return snapshot
                            ?? BuildFailureSnapshot(ClaudeProviderState.Unavailable, now, retryAfter: null);
                    }

                    case HttpStatusCode.Unauthorized:
                    {
                        // §4.1 step 5 — exactly one refresh + one retried GET; a second 401
                        // (or a failed refresh) means the user must re-login in Claude Code.
                        if (refreshAlreadyUsed || string.IsNullOrWhiteSpace(credentials.RefreshToken))
                        {
                            return BuildFailureSnapshot(ClaudeProviderState.TokenExpired, now, retryAfter: null);
                        }

                        var refreshed = await TryRefreshTokenAsync(credentials, cancellationToken)
                            .ConfigureAwait(false);
                        if (refreshed is null)
                        {
                            return BuildFailureSnapshot(ClaudeProviderState.TokenExpired, now, retryAfter: null);
                        }

                        credentials = refreshed;
                        refreshAlreadyUsed = true;
                        continue;
                    }

                    case HttpStatusCode.Forbidden:
                        // Token is valid but not authorized; refresh would not help.
                        return BuildFailureSnapshot(ClaudeProviderState.Unavailable, now, retryAfter: null);

                    case HttpStatusCode.TooManyRequests:
                    {
                        var retryAfter = ParseRetryAfter(response, now) ?? _options.MinPollInterval;
                        return BuildFailureSnapshot(ClaudeProviderState.RateLimited, now, retryAfter);
                    }

                    default:
                    {
                        if ((int)response.StatusCode >= 500)
                        {
                            if (transientAttempts >= _options.MaxRetries)
                            {
                                return BuildFailureSnapshot(ClaudeProviderState.Unavailable, now, retryAfter: null);
                            }

                            await BackoffAsync(transientAttempts, cancellationToken).ConfigureAwait(false);
                            transientAttempts++;
                            continue;
                        }

                        return BuildFailureSnapshot(ClaudeProviderState.Unavailable, now, retryAfter: null);
                    }
                }
            }
            finally
            {
                response?.Dispose();
            }
        }
    }

    private HttpRequestMessage CreateUsageRequest(string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, _options.UsageEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation(AnthropicBetaHeaderName, AnthropicBetaHeaderValue);
        request.Headers.UserAgent.ParseAdd(_options.UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    /// <summary>§4.2 — refresh endpoints tried in order; result kept in memory only.</summary>
    private async Task<ClaudeOAuthCredentials?> TryRefreshTokenAsync(
        ClaudeOAuthCredentials credentials, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(credentials.RefreshToken))
        {
            return null;
        }

        foreach (var endpoint in _options.TokenRefreshEndpoints)
        {
            var refreshed = await TryRefreshTokenAsync(endpoint, credentials, cancellationToken)
                .ConfigureAwait(false);
            if (refreshed is not null)
            {
                _refreshedCredentials = refreshed;
                return refreshed;
            }
        }

        return null;
    }

    private async Task<ClaudeOAuthCredentials?> TryRefreshTokenAsync(
        Uri endpoint, ClaudeOAuthCredentials credentials, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.UserAgent.ParseAdd(_options.UserAgent);
            var payload = JsonSerializer.Serialize(new RefreshRequestPayload(
                "refresh_token", credentials.RefreshToken!, _options.OAuthClientId));
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await ReadBoundedBodyAsync(response, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var accessToken = ReadStringProperty(root, "access_token");
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return null;
            }

            return new ClaudeOAuthCredentials
            {
                AccessToken = accessToken,
                RefreshToken = ReadStringProperty(root, "refresh_token") ?? credentials.RefreshToken,
                ExpiresAt = ComputeRefreshedExpiry(root),
                SubscriptionType = credentials.SubscriptionType,
            };
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private DateTimeOffset ComputeRefreshedExpiry(JsonElement root)
    {
        var now = _timeProvider.GetUtcNow();
        if (root.TryGetProperty("expires_in", out var expiresIn) &&
            expiresIn.ValueKind == JsonValueKind.Number &&
            expiresIn.TryGetInt64(out var seconds) &&
            seconds > 0 &&
            seconds <= (long)TimeSpan.FromDays(365).TotalSeconds)
        {
            return now + TimeSpan.FromSeconds(seconds);
        }

        // Missing/odd expires_in: treat the token as immediately stale so the next poll re-checks.
        return now;
    }

    /// <summary>§4.3 — defensive mapping of an undocumented API; null on any malformed body.</summary>
    private static async Task<ClaudeUsageSnapshot?> TryParseUsageResponseAsync(
        HttpResponseMessage response,
        ClaudeOAuthCredentials credentials,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        try
        {
            var body = await ReadBoundedBodyAsync(response, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var snapshot = new ClaudeUsageSnapshot
            {
                State = ClaudeProviderState.Ok,
                RetrievedAt = now,
                IsFromCache = false,
                FiveHour = ParseBucket(root, "five_hour"),
                SevenDay = ParseBucket(root, "seven_day"),
                SevenDayOpus = ParseBucket(root, "seven_day_opus"),
                SevenDaySonnet = ParseBucket(root, "seven_day_sonnet"),
                ExtraUsage = ParseExtraUsage(root),
                SubscriptionType = credentials.SubscriptionType,
            };
            return snapshot;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<string?> ReadBoundedBodyAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is { } length && length > MaxResponseChars)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return body.Length > MaxResponseChars ? null : body;
    }

    private static ClaudeUsageBucket? ParseBucket(JsonElement root, string propertyName)
    {
        // Bucket JSON null/missing, or missing/non-numeric utilization
        // => model bucket is null ("no limit reported"/unlimited).
        if (!root.TryGetProperty(propertyName, out var bucket) ||
            bucket.ValueKind != JsonValueKind.Object ||
            !bucket.TryGetProperty("utilization", out var utilization) ||
            utilization.ValueKind != JsonValueKind.Number ||
            !utilization.TryGetDouble(out var percentUsed))
        {
            return null;
        }

        DateTimeOffset? resetsAt = null;
        if (bucket.TryGetProperty("resets_at", out var resets) &&
            resets.ValueKind == JsonValueKind.String &&
            resets.TryGetDateTimeOffset(out var parsedResetsAt))
        {
            resetsAt = parsedResetsAt;
        }

        return new ClaudeUsageBucket
        {
            PercentUsed = percentUsed,
            ResetsAt = resetsAt,
        };
    }

    private static ClaudeExtraUsage? ParseExtraUsage(JsonElement root)
    {
        if (!root.TryGetProperty("extra_usage", out var extra) ||
            extra.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new ClaudeExtraUsage
        {
            IsEnabled = extra.TryGetProperty("is_enabled", out var isEnabled) &&
                        isEnabled.ValueKind == JsonValueKind.True,
            UsedCreditsCents = ReadDecimalProperty(extra, "used_credits"),
            MonthlyLimitCents = ReadDecimalProperty(extra, "monthly_limit"),
            Currency = ReadStringProperty(extra, "currency"),
        };
    }

    private static TimeSpan? ParseRetryAfter(HttpResponseMessage response, DateTimeOffset now)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta is { } delta)
        {
            return delta > TimeSpan.Zero ? delta : TimeSpan.Zero;
        }

        if (retryAfter.Date is { } date)
        {
            var until = date - now;
            return until > TimeSpan.Zero ? until : TimeSpan.Zero;
        }

        return null;
    }

    private static string? ReadStringProperty(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static decimal? ReadDecimalProperty(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetDecimal(out var result)
            ? result
            : null;

    /// <summary>Exponential backoff with jitter; no-op when RetryBaseDelay is zero (tests).</summary>
    private async Task BackoffAsync(int attempt, CancellationToken cancellationToken)
    {
        var baseDelay = _options.RetryBaseDelay;
        if (baseDelay <= TimeSpan.Zero)
        {
            return;
        }

        var delayMilliseconds = baseDelay.TotalMilliseconds * Math.Pow(2, attempt);

        // Crypto RNG keeps analyzers happy (CA5394); jitter adds 0–25 %.
        var jitterFactor = RandomNumberGenerator.GetInt32(0, 251) / 1000.0;
        delayMilliseconds += delayMilliseconds * jitterFactor;

        var delay = TimeSpan.FromMilliseconds(Math.Min(delayMilliseconds, 30_000));
        await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Refresh request body (§4.2). The client id is public, not a secret.</summary>
    private sealed record RefreshRequestPayload(
        [property: JsonPropertyName("grant_type")] string GrantType,
        [property: JsonPropertyName("refresh_token")] string RefreshToken,
        [property: JsonPropertyName("client_id")] string ClientId);
}
