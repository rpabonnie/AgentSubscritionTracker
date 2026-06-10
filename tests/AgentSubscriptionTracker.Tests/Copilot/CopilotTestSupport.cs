// SPEC-0002 test support — fakes and helpers shared by the Copilot quota test files.
// These tests reference contract types that do not exist yet (spec phase);
// the project intentionally does not compile until TASK-007 implements SPEC-0002.

using System.Net;
using System.Net.Http;
using AgentSubscriptionTracker.App.Services;

namespace AgentSubscriptionTracker.Tests.Copilot;

/// <summary>Deterministic <see cref="TimeProvider"/> for debounce/reset-math tests.</summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public FakeTimeProvider(DateTimeOffset start) => _utcNow = start;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan delta) => _utcNow += delta;
}

/// <summary>
/// Scripted <see cref="HttpMessageHandler"/>: returns queued responses in order and
/// records every request. Guarantees tests never touch the live network.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses = new();

    public List<HttpRequestMessage> Requests { get; } = [];

    public int CallCount => Requests.Count;

    public void EnqueueJson(HttpStatusCode statusCode, string jsonBody, Action<HttpResponseMessage>? configure = null)
    {
        _responses.Enqueue(() =>
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json"),
            };
            configure?.Invoke(response);
            return response;
        });
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("StubHttpMessageHandler received an unexpected request — no response queued.");
        }

        return Task.FromResult(_responses.Dequeue()());
    }

    protected override void Dispose(bool disposing) => base.Dispose(disposing);
}

/// <summary>Token provider stub returning a fixed (or no) token.</summary>
internal sealed class FakeCopilotTokenProvider(CopilotToken? token) : ICopilotTokenProvider
{
    public int CallCount { get; private set; }

    public Task<CopilotToken?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(token);
    }
}

/// <summary>In-memory stand-in for Windows Credential Manager (never touches the real store).</summary>
internal sealed class FakeCopilotCredentialStore : ICopilotCredentialStore
{
    public Dictionary<string, string> Secrets { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> ReadServiceNames { get; } = [];

    public string? ReadSecret(string serviceName)
    {
        ReadServiceNames.Add(serviceName);
        return Secrets.TryGetValue(serviceName, out var value) ? value : null;
    }
}

/// <summary>Reads golden-file JSON fixtures copied to the test output directory.</summary>
internal static class CopilotFixtures
{
    public static string Read(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Copilot", fileName));
}

/// <summary>
/// Self-cleaning temp directory used to simulate %LOCALAPPDATA% / %USERPROFILE% roots
/// for the token discovery chain (tests never read real credential files).
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Root = Path.Combine(Path.GetTempPath(), "astracker-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string WriteFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup of our own temp folder.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup of our own temp folder.
        }
    }
}
