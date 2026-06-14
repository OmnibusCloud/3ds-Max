using OutWit.Cloud.Data.Access;
using OutWit.Cloud.Data.Processing;
using OutWit.Cloud.SDK;

namespace OutWit.Render.ThreeDsMax.Plugin.LocalTests.Live;

/// <summary>
/// Shared connection and helpers for live distributed integration tests that run against
/// the deployed OmnibusCloud instance with real connected node clients.
/// Ported from the WitEngine Cloud.Tests live suite, re-pointed at the canonical SaaS
/// deployment and the published OutWit.Cloud.SDK facet API.
/// </summary>
public abstract class LiveDistributedIntegrationTestBase
{
    #region Constants

    private static readonly TimeSpan CONNECT_TIMEOUT = TimeSpan.FromMinutes(10);

    private static readonly SemaphoreSlim SHARED_CLIENT_LOCK = new(1, 1);

    #endregion

    #region Fields

    private static WitCloudClient? s_sharedClient;

    private static bool s_scopeDiagnosticsWritten;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public async Task SetupAsync()
    {
        if (!LiveIntegrationSettings.IsConfigured)
            Assert.Ignore("Live integration skipped: set OMNIBUSCLOUD_API_KEY (and optionally OMNIBUSCLOUD_SERVER_URL / OMNIBUSCLOUD_IDENTITY_URL).");

        using var cts = new CancellationTokenSource(CONNECT_TIMEOUT);

        await SHARED_CLIENT_LOCK.WaitAsync(cts.Token);
        try
        {
            if (s_sharedClient == null)
            {
                var client = new WitCloudClient(
                    LiveIntegrationSettings.ServerUrl,
                    LiveIntegrationSettings.IdentityUrl,
                    LiveIntegrationSettings.ApiKey!);

                try
                {
                    await client.ConnectAsync(cts.Token);
                    s_sharedClient = client;
                }
                catch
                {
                    await client.DisposeAsync();
                    throw;
                }
            }

            Client = s_sharedClient;
            if (!s_scopeDiagnosticsWritten)
            {
                var scopeOptions = await Client.GetExecutionScopeOptionsAsync(cts.Token);
                WriteExecutionScopeDiagnostics(scopeOptions);
                s_scopeDiagnosticsWritten = true;
            }
        }
        finally
        {
            SHARED_CLIENT_LOCK.Release();
        }
    }

    [OneTimeTearDown]
    public Task TearDownAsync()
    {
        Client = null!;
        return Task.CompletedTask;
    }

    #endregion

    #region Tools

    protected static void WriteExecutionScopeDiagnostics(ExecutionScopeOptions scopeOptions)
    {
        Assert.That(scopeOptions, Is.Not.Null);

        var groups = scopeOptions.Groups.Length == 0
            ? "none"
            : string.Join(", ", scopeOptions.Groups.Select(me => $"{me.Name} ({me.GroupId})"));

        var projects = scopeOptions.Projects.Length == 0
            ? "none"
            : string.Join(", ", scopeOptions.Projects.Select(me => $"{me.Name} ({me.ProjectId})"));

        TestContext.Progress.WriteLine(
            $"Execution scope diagnostics: CanRunOnAllClients={scopeOptions.CanRunOnAllClients}; Groups={groups}; Projects={projects}");
    }

    /// <summary>
    /// Resolves the real client group to render on: an explicit <c>OMNIBUSCLOUD_GROUP_ID</c> if set,
    /// otherwise the first group in the API-key user's execution scope. Null means no named group was
    /// found — the caller decides whether to fall back to all-clients or skip. Mirrors the production
    /// submission transport and the Blender distribution tests.
    /// </summary>
    protected static async Task<Guid?> ResolveLiveGroupIdAsync(WitCloudClient client, CancellationToken cancellationToken)
    {
        var raw = Environment.GetEnvironmentVariable("OMNIBUSCLOUD_GROUP_ID");
        if (!string.IsNullOrWhiteSpace(raw) && Guid.TryParse(raw, out var fromEnv))
            return fromEnv;

        var scope = await client.GetExecutionScopeOptionsAsync(cancellationToken);
        var group = scope.Groups.FirstOrDefault();
        return group?.GroupId;
    }

    protected static string? FindSolutionRoot()
    {
        var directory = TestContext.CurrentContext.TestDirectory;
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory, "OutWit.slnx")))
                return directory;

            directory = Path.GetDirectoryName(directory);
        }

        return null;
    }

    protected static string GetPersistentOutputDirectory(string solutionRoot, string scriptName, Guid jobId, Guid resultBlobId)
    {
        var directory = Path.Combine(
            solutionRoot,
            "@Output",
            "LiveTestOutputs",
            scriptName,
            $"{DateTime.UtcNow:yyyy-MM-dd-HH-mm-ss}_{jobId:N}_{resultBlobId:N}");

        Directory.CreateDirectory(directory);
        return directory;
    }

    protected static void AssertCompletedOrIgnoreExternalCapacity<TResult>(WitJobResult<TResult> waitResult, string context, string? scriptName = null)
    {
        if (waitResult.Status != ProcessingJobStatus.Completed
            && (IsNoFallbackNodesAvailable(waitResult.ErrorMessage)
                || IsNodeResourceLimitReached(waitResult.ErrorMessage)))
        {
            Assert.Ignore($"Live external integration skipped because deployed render capacity is currently unavailable for {context}: {waitResult.ErrorMessage}");
        }

        Assert.That(waitResult.Status, Is.EqualTo(ProcessingJobStatus.Completed),
            $"{context} failed: {waitResult.ErrorMessage}");
    }

    protected static bool IsNoFallbackNodesAvailable(string? errorMessage)
    {
        return !string.IsNullOrWhiteSpace(errorMessage)
               && errorMessage.Contains("No fallback nodes available", StringComparison.OrdinalIgnoreCase);
    }

    protected static bool IsNodeResourceLimitReached(string? errorMessage)
    {
        return !string.IsNullOrWhiteSpace(errorMessage)
               && (errorMessage.Contains("inotify instances", StringComparison.OrdinalIgnoreCase)
                   || errorMessage.Contains("open file descriptors", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Properties

    protected WitCloudClient Client { get; private set; } = null!;

    #endregion
}
