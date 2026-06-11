// SPEC-0002 §4–§6 — Copilot quota service: verified copilot_internal/user request shape,
// tolerant response mapping, provider-state table, >=30 s debounce, Retry-After honoring,
// transient-only retry with TimeProvider-driven backoff, last-good fallback, and full
// token redaction. GetSnapshotAsync never throws except for caller cancellation/disposal.

using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using AgentSubscriptionTracker.App.Models;

namespace AgentSubscriptionTracker.App.Services;

/// <summary>
/// Fetches the signed-in user's GitHub Copilot quota from the (undocumented, verified)
/// internal endpoint <c>GET https://api.github.com/copilot_internal/user</c> and maps it
/// to a <see cref="CopilotQuotaSnapshot"/>.
/// </summary>
public sealed class CopilotQuotaService : ICopilotQuotaService, IDisposable
{
    private const string AllowedHost = "api.github.com";
    private const string EndpointPath = "copilot_internal/user";
    private static readonly TimeSpan MaxHttpTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>Responses are untrusted input; bound the payload read to 1 MiB.</summary>
    private const int MaxResponseChars = 1024 * 1024;

    // Fixed, friendly, token-free status texts (SPEC-0002 §6 redaction: error response
    // bodies are never echoed; NotSignedIn guidance must mention "Copilot CLI").
    private const string NotSignedInMessage =
        "Not signed in to GitHub Copilot. Run 'gh auth login' (GitHub CLI) or sign in with the Copilot CLI / JetBrains plugin.";
    private const string TokenRejectedMessage =
        "The stored GitHub token was rejected. Run 'gh auth login' again, or re-sign-in with the Copilot CLI (or the JetBrains/Neovim plugin).";
    private const string ForbiddenMessage =
        "Access to Copilot quota was denied. The stored token may be stale - run 'gh auth login' again or re-sign-in with the Copilot CLI.";
    private const string RateLimitedMessage =
        "GitHub API rate limit reached. Quota will refresh automatically later.";
    private const string UnavailableMessage =
        "Copilot quota is currently unavailable.";

    private readonly ICopilotTokenProvider _tokenProvider;
    private readonly TimeProvider _timeProvider;
    private readonly CopilotQuotaServiceOptions _options;
    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;
    private readonly SemaphoreSlim _pollGate = new(1, 1);

    private CopilotQuotaSnapshot? _lastSnapshot;
    private CopilotQuotaSnapshot? _lastGood;
    private DateTimeOffset? _lastAttemptAt;
    private DateTimeOffset? _rateLimitDeadline;
    private bool _disposed;

    /// <summary>
    /// Creates the service. The handler is injected for testability and not disposed here.
    /// Throws <see cref="ArgumentException"/> when the base address is not the allowlisted
    /// https://api.github.com or when the HTTP timeout exceeds the 10 s CLAUDE.md cap.
    /// </summary>
    public CopilotQuotaService(
        ICopilotTokenProvider tokenProvider,
        HttpMessageHandler httpMessageHandler,
        TimeProvider timeProvider,
        CopilotQuotaServiceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(tokenProvider);
        ArgumentNullException.ThrowIfNull(httpMessageHandler);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _tokenProvider = tokenProvider;
        _timeProvider = timeProvider;
        _options = options ?? new CopilotQuotaServiceOptions();

        if (!Uri.UriSchemeHttps.Equals(_options.BaseAddress.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !AllowedHost.Equals(_options.BaseAddress.Host, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"BaseAddress must be https://{AllowedHost}/ (allowlisted host).", nameof(options));
        }

        if (_options.HttpTimeout <= TimeSpan.Zero || _options.HttpTimeout > MaxHttpTimeout)
        {
            throw new ArgumentException(
                "HttpTimeout must be positive and at most 10 seconds.", nameof(options));
        }

        _endpoint = new Uri(_options.BaseAddress, EndpointPath);
        _httpClient = new HttpClient(httpMessageHandler, disposeHandler: false)
        {
            Timeout = _options.HttpTimeout,
        };
    }

    /// <inheritdoc />
    public async Task<CopilotQuotaSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Coalesce concurrent callers onto one in-flight poll (SPEC-0002 §5 concurrency).
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

    private async Task<CopilotQuotaSnapshot> PollAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();

        // §5 debounce — within the window (extended by a pending Retry-After deadline),
        // serve the cache: no HTTP request and no credential-chain walk.
        if (TryGetGatedSnapshot(now) is { } gated)
        {
            return gated;
        }

        var token = await _tokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false);

        var snapshot = token is null
            ? BuildFailureSnapshot(CopilotProviderState.NotSignedIn, NotSignedInMessage, now)
            : await FetchAsync(token, now, cancellationToken).ConfigureAwait(false);

        _lastAttemptAt = now;
        _lastSnapshot = snapshot;
        if (snapshot is { State: CopilotProviderState.Ok, IsFromCache: false })
        {
            _lastGood = snapshot;
        }

        return snapshot;
    }

    private CopilotQuotaSnapshot? TryGetGatedSnapshot(DateTimeOffset now)
    {
        if (_lastAttemptAt is not { } lastAttemptAt || _lastSnapshot is not { } lastSnapshot)
        {
            return null;
        }

        var window = _options.MinRefreshInterval;
        if (lastSnapshot.State == CopilotProviderState.RateLimited &&
            _rateLimitDeadline is { } deadline &&
            deadline - lastAttemptAt > window)
        {
            window = deadline - lastAttemptAt;
        }

        return now - lastAttemptAt < window
            ? lastSnapshot with { IsFromCache = true }
            : null;
    }

    /// <summary>
    /// §5 last-good fallback — failures after a previous Ok fetch keep the last good
    /// plan/reset-date/buckets (and their RetrievedAt, so the UI can show data age) with
    /// the new error state and IsFromCache = true.
    /// </summary>
    private CopilotQuotaSnapshot BuildFailureSnapshot(
        CopilotProviderState state, string statusMessage, DateTimeOffset now)
    {
        if (_lastGood is { } lastGood)
        {
            return lastGood with
            {
                State = state,
                StatusMessage = statusMessage,
                IsFromCache = true,
            };
        }

        return new CopilotQuotaSnapshot
        {
            State = state,
            StatusMessage = statusMessage,
            RetrievedAt = now,
            IsFromCache = false,
        };
    }

    private async Task<CopilotQuotaSnapshot> FetchAsync(
        CopilotToken token, DateTimeOffset now, CancellationToken cancellationToken)
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
                    using var request = CreateQuotaRequest(token.Value);
                    response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException)
                {
                    if (transientAttempts >= _options.MaxRetries)
                    {
                        return BuildFailureSnapshot(CopilotProviderState.Unavailable, UnavailableMessage, now);
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
                        return BuildFailureSnapshot(CopilotProviderState.Unavailable, UnavailableMessage, now);
                    }

                    await BackoffAsync(transientAttempts, cancellationToken).ConfigureAwait(false);
                    transientAttempts++;
                    continue;
                }

                switch (response.StatusCode)
                {
                    case HttpStatusCode.OK:
                    {
                        var snapshot = await TryParseQuotaResponseAsync(response, now, cancellationToken)
                            .ConfigureAwait(false);
                        return snapshot
                            ?? BuildFailureSnapshot(CopilotProviderState.Unavailable, UnavailableMessage, now);
                    }

                    case HttpStatusCode.Unauthorized:
                        // Expired/revoked token — never retried within a call (§4).
                        return BuildFailureSnapshot(CopilotProviderState.NotSignedIn, TokenRejectedMessage, now);

                    case HttpStatusCode.Forbidden:
                        // Known for stale Neovim hosts.json tokens (research) — never retried.
                        return BuildFailureSnapshot(CopilotProviderState.Forbidden, ForbiddenMessage, now);

                    case HttpStatusCode.TooManyRequests:
                    {
                        var retryAfter = ParseRetryAfter(response, now) ?? DefaultRetryAfter;
                        _rateLimitDeadline = now + retryAfter;
                        return BuildFailureSnapshot(CopilotProviderState.RateLimited, RateLimitedMessage, now);
                    }

                    default:
                    {
                        if ((int)response.StatusCode >= 500)
                        {
                            if (transientAttempts >= _options.MaxRetries)
                            {
                                return BuildFailureSnapshot(
                                    CopilotProviderState.Unavailable, UnavailableMessage, now);
                            }

                            await BackoffAsync(transientAttempts, cancellationToken).ConfigureAwait(false);
                            transientAttempts++;
                            continue;
                        }

                        // 404 and other non-transient statuses: unversioned internal API.
                        return BuildFailureSnapshot(CopilotProviderState.Unavailable, UnavailableMessage, now);
                    }
                }
            }
            finally
            {
                response?.Dispose();
            }
        }
    }

    /// <summary>§4 — verified request shape: 'token' scheme (not Bearer), the four editor
    /// headers, Accept: application/json, and no X-GitHub-Api-Version.</summary>
    private HttpRequestMessage CreateQuotaRequest(string tokenValue)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, _endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("token", tokenValue);
        request.Headers.TryAddWithoutValidation("Editor-Version", "vscode/1.104.1");
        request.Headers.TryAddWithoutValidation("Editor-Plugin-Version", "copilot-chat/0.26.7");
        request.Headers.TryAddWithoutValidation("Copilot-Integration-Id", "vscode-chat");
        request.Headers.UserAgent.ParseAdd("GitHubCopilotChat/0.26.7");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    /// <summary>§4 — strict-but-tolerant mapping of an unversioned API; null on any
    /// malformed/oversized body (the caller maps that to Unavailable).</summary>
    private static async Task<CopilotQuotaSnapshot?> TryParseQuotaResponseAsync(
        HttpResponseMessage response, DateTimeOffset now, CancellationToken cancellationToken)
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

            var hasSnapshots = root.TryGetProperty("quota_snapshots", out var quotaSnapshots) &&
                               quotaSnapshots.ValueKind == JsonValueKind.Object;

            return new CopilotQuotaSnapshot
            {
                State = CopilotProviderState.Ok,
                Plan = ReadStringProperty(root, "copilot_plan"),
                QuotaResetDate = ParseQuotaResetDate(root),
                Chat = hasSnapshots ? ParseBucket(quotaSnapshots, "chat", "Chat") : null,
                Completions = hasSnapshots ? ParseBucket(quotaSnapshots, "completions", "Code completions") : null,
                PremiumInteractions = hasSnapshots
                    ? ParseBucket(quotaSnapshots, "premium_interactions", "Premium requests")
                    : null,
                RetrievedAt = now,
                IsFromCache = false,
                StatusMessage = null,
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

    private static DateOnly? ParseQuotaResetDate(JsonElement root) =>
        ReadStringProperty(root, "quota_reset_date") is { } text &&
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    /// <summary>Missing/non-object bucket → null; missing/null fields → null/defaults.</summary>
    private static CopilotQuotaBucket? ParseBucket(JsonElement quotaSnapshots, string key, string displayName)
    {
        if (!quotaSnapshots.TryGetProperty(key, out var bucket) ||
            bucket.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new CopilotQuotaBucket
        {
            Key = key,
            DisplayName = displayName,
            Entitlement = ReadInt32Property(bucket, "entitlement"),
            Remaining = ReadInt32Property(bucket, "remaining"),
            PercentRemaining = ReadDoubleProperty(bucket, "percent_remaining"),
            Unlimited = ReadBooleanProperty(bucket, "unlimited"),
            OverageCount = ReadInt32Property(bucket, "overage_count") ?? 0,
            OveragePermitted = ReadBooleanProperty(bucket, "overage_permitted"),
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

    private static int? ReadInt32Property(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var result)
            ? result
            : null;

    private static double? ReadDoubleProperty(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out var result)
            ? result
            : null;

    private static bool ReadBooleanProperty(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.True;

    /// <summary>§5 — exponential backoff (base 500 ms ×2) with 0–25 % jitter, driven by the
    /// injected TimeProvider. Transient failures only; tests disable via MaxRetries = 0.</summary>
    private async Task BackoffAsync(int attempt, CancellationToken cancellationToken)
    {
        var delayMilliseconds = RetryBaseDelay.TotalMilliseconds * Math.Pow(2, attempt);

        // Crypto RNG keeps analyzers happy (CA5394); jitter adds 0–25 %.
        var jitterFactor = RandomNumberGenerator.GetInt32(0, 251) / 1000.0;
        delayMilliseconds += delayMilliseconds * jitterFactor;

        var delay = TimeSpan.FromMilliseconds(Math.Min(delayMilliseconds, 30_000));
        await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
    }
}
