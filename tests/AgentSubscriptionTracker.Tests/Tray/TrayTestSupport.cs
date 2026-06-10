// SPEC-0003 test support — fakes and helpers shared by the tray view-model test files.
// These tests reference contract types that do not exist yet (spec phase);
// the project intentionally does not compile until TASK-008 implements SPEC-0003.
//
// No window is shown, no network is touched, no real credential store is read.
// Fixtures are snapshot-shaped (post-service, token-free by SPEC-0001/0002 contract).

using System.Text.Json;
using System.Text.Json.Serialization;
using AgentSubscriptionTracker.App.Models;
using AgentSubscriptionTracker.App.Services;

namespace AgentSubscriptionTracker.Tests.Tray;

/// <summary>Shared instants for deterministic tests. T0 matches the fixtures' retrievedAt.</summary>
internal static class TrayTestData
{
    /// <summary>2026-06-10 12:00:00 UTC — the "now" used across SPEC-0003 fixtures.</summary>
    public static readonly DateTimeOffset T0 = new(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);
}

/// <summary>Reads golden-file snapshot fixtures copied to the test output directory.</summary>
internal static class TrayFixtures
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    public static string ReadRaw(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Tray", fileName));

    public static ClaudeUsageSnapshot LoadClaudeSnapshot(string fileName) =>
        JsonSerializer.Deserialize<ClaudeUsageSnapshot>(ReadRaw(fileName), Options)!;

    public static CopilotQuotaSnapshot LoadCopilotSnapshot(string fileName) =>
        JsonSerializer.Deserialize<CopilotQuotaSnapshot>(ReadRaw(fileName), Options)!;
}

/// <summary>
/// Scripted <see cref="IClaudeUsageService"/> fake: replays queued responses in order,
/// then the fallback; records call count. Never touches network or files.
/// </summary>
internal sealed class FakeClaudeUsageService : IClaudeUsageService
{
    private readonly Queue<Func<CancellationToken, Task<ClaudeUsageSnapshot>>> _script = new();
    private Func<CancellationToken, Task<ClaudeUsageSnapshot>>? _fallback;

    public int CallCount { get; private set; }

    public static FakeClaudeUsageService Returning(ClaudeUsageSnapshot snapshot)
    {
        var fake = new FakeClaudeUsageService();
        fake.FallbackTo(snapshot);
        return fake;
    }

    public FakeClaudeUsageService Enqueue(ClaudeUsageSnapshot snapshot)
    {
        _script.Enqueue(_ => Task.FromResult(snapshot));
        return this;
    }

    public FakeClaudeUsageService EnqueueHandler(Func<CancellationToken, Task<ClaudeUsageSnapshot>> handler)
    {
        _script.Enqueue(handler);
        return this;
    }

    public FakeClaudeUsageService FallbackTo(ClaudeUsageSnapshot snapshot)
    {
        _fallback = _ => Task.FromResult(snapshot);
        return this;
    }

    public Task<ClaudeUsageSnapshot> GetUsageAsync(CancellationToken cancellationToken = default)
    {
        CallCount++;
        var handler = _script.Count > 0
            ? _script.Dequeue()
            : _fallback ?? throw new InvalidOperationException(
                "FakeClaudeUsageService received an unexpected call — nothing scripted.");
        return handler(cancellationToken);
    }
}

/// <summary>
/// Scripted <see cref="ICopilotQuotaService"/> fake: replays queued responses in order,
/// then the fallback; records call count. Never touches network or files.
/// </summary>
internal sealed class FakeCopilotQuotaService : ICopilotQuotaService
{
    private readonly Queue<Func<CancellationToken, Task<CopilotQuotaSnapshot>>> _script = new();
    private Func<CancellationToken, Task<CopilotQuotaSnapshot>>? _fallback;

    public int CallCount { get; private set; }

    public static FakeCopilotQuotaService Returning(CopilotQuotaSnapshot snapshot)
    {
        var fake = new FakeCopilotQuotaService();
        fake.FallbackTo(snapshot);
        return fake;
    }

    public FakeCopilotQuotaService Enqueue(CopilotQuotaSnapshot snapshot)
    {
        _script.Enqueue(_ => Task.FromResult(snapshot));
        return this;
    }

    public FakeCopilotQuotaService EnqueueHandler(Func<CancellationToken, Task<CopilotQuotaSnapshot>> handler)
    {
        _script.Enqueue(handler);
        return this;
    }

    public FakeCopilotQuotaService FallbackTo(CopilotQuotaSnapshot snapshot)
    {
        _fallback = _ => Task.FromResult(snapshot);
        return this;
    }

    public Task<CopilotQuotaSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        CallCount++;
        var handler = _script.Count > 0
            ? _script.Dequeue()
            : _fallback ?? throw new InvalidOperationException(
                "FakeCopilotQuotaService received an unexpected call — nothing scripted.");
        return handler(cancellationToken);
    }
}
