using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services.Auth;

[TestFixture]
public sealed class MaxConnectedRenderSubmissionTransportOmnibusCloudSessionTests
{
    #region Submit Tests

    [Test]
    public async Task SubmitFailsWhenPackageCarriesNoSceneTest()
    {
        var transport = CreateTransport(new FakeMaxCloudConnectionService());

        var result = await transport.SubmitAsync(CreateRequest(), new MaxSceneLaunchPackageResult
        {
            IsSuccess = true,
            Scene = null
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.JobId, Does.StartWith("failed-"));
            Assert.That(result.StatusText, Does.Contain("scene payload"));
        });
    }

    [Test]
    public async Task SubmitFailsWhenNoSignedInConnectionTest()
    {
        var transport = CreateTransport(new FakeMaxCloudConnectionService { Client = null });

        var result = await transport.SubmitAsync(CreateRequest(), new MaxSceneLaunchPackageResult
        {
            IsSuccess = true,
            Scene = new OutWit.Controller.Render.Dcc.Model.DccSceneData()
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.JobId, Does.StartWith("failed-"));
            Assert.That(result.StatusText, Does.Contain("No signed-in cloud connection"));
            Assert.That(result.Diagnostics.Any(me => me.Message.Contains("Sign in", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public async Task SubmitPropagatesFailedPackageStateTest()
    {
        var transport = CreateTransport(new FakeMaxCloudConnectionService());

        var result = await transport.SubmitAsync(CreateRequest(), new MaxSceneLaunchPackageResult
        {
            IsSuccess = false,
            StatusText = "Validation found blocking issues."
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.JobId, Does.StartWith("failed-"));
            Assert.That(result.StatusText, Is.EqualTo("Validation found blocking issues."));
        });
    }

    #endregion

    #region Refresh Tests

    [Test]
    public async Task RefreshSkipsJobsThatWereNeverSubmittedTest()
    {
        var transport = CreateTransport(new FakeMaxCloudConnectionService());

        var result = await transport.RefreshAsync(new MaxConnectedRenderJobState
        {
            JobId = "blocked-abc"
        });

        Assert.That(result.StatusText, Does.Contain("nothing to refresh"));
    }

    [Test]
    public async Task RefreshFailsWithoutSignedInConnectionTest()
    {
        var transport = CreateTransport(new FakeMaxCloudConnectionService { Client = null });

        var result = await transport.RefreshAsync(new MaxConnectedRenderJobState
        {
            JobId = Guid.NewGuid().ToString("D"),
            CloudUrl = "https://omnibuscloud.local"
        });

        Assert.That(result.StatusText, Does.Contain("No signed-in cloud connection"));
    }

    #endregion

    #region Tools

    private static MaxConnectedRenderSubmissionTransportOmnibusCloudSession CreateTransport(FakeMaxCloudConnectionService connectionService)
    {
        return new MaxConnectedRenderSubmissionTransportOmnibusCloudSession(connectionService, new MaxConnectedRenderSceneAttachmentService());
    }

    private static MaxSceneLaunchPackageRequest CreateRequest()
    {
        return new MaxSceneLaunchPackageRequest
        {
            CloudUrl = "https://omnibuscloud.local",
            IdentityUrl = "https://auth.omnibuscloud.local",
            RenderMode = "RenderStill",
            ResolutionX = 640,
            ResolutionY = 640,
            FrameStart = 1,
            FrameEnd = 1,
            Samples = 16,
            UseAllClients = true,
            OutputFolder = Path.GetTempPath()
        };
    }

    #endregion
}
