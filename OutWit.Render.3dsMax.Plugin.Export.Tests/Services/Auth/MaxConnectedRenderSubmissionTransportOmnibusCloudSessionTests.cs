using OutWit.Cloud.Data.Processing;
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
    public async Task SubmitFailsWhenBothProjectAndGroupAreSelectedTest()
    {
        var transport = CreateTransport(new FakeMaxCloudConnectionService { Client = new FakeWitCloudClient() });
        var request = CreateRequest();
        request.UseAllClients = false;
        request.SelectedGroupName = "Artists";
        request.SelectedProjectName = "Town Asset";

        var result = await transport.SubmitAsync(request, CreatePackageWithScene());

        Assert.Multiple(() =>
        {
            Assert.That(result.JobId, Does.StartWith("failed-"));
            Assert.That(result.StatusText, Does.Contain("not both"));
        });
    }

    [Test]
    public async Task SubmitFailsWhenNoTargetIsSelectedTest()
    {
        // Launch-week req 4: no target must never silently degrade to an unscoped all-clients
        // submit (the engine rejects it for accounts without the global grant).
        var transport = CreateTransport(new FakeMaxCloudConnectionService { Client = new FakeWitCloudClient() });
        var request = CreateRequest();
        request.UseAllClients = false;

        var result = await transport.SubmitAsync(request, CreatePackageWithScene());

        Assert.Multiple(() =>
        {
            Assert.That(result.JobId, Does.StartWith("failed-"));
            Assert.That(result.StatusText, Does.Contain("Select a project or a render group"));
        });
    }

    [Test]
    public async Task SubmitFailsWhenSelectedProjectIsNotInScopeTest()
    {
        var client = new FakeWitCloudClient();
        client.ScopeOptions = new OutWit.Cloud.Data.Access.ExecutionScopeOptions
        {
            Projects = [new OutWit.Cloud.Data.Access.ExecutionProjectOption { ProjectId = Guid.NewGuid(), Name = "Spring Rig" }]
        };
        var transport = CreateTransport(new FakeMaxCloudConnectionService { Client = client });
        var request = CreateRequest();
        request.UseAllClients = false;
        request.SelectedProjectName = "Town Asset";

        var result = await transport.SubmitAsync(request, CreatePackageWithScene());

        Assert.Multiple(() =>
        {
            Assert.That(result.JobId, Does.StartWith("failed-"));
            Assert.That(result.StatusText, Does.Contain("Project 'Town Asset' was not found"));
        });
    }

    [Test]
    public async Task SubmitFailsWhenSelectedGroupIsNotInScopeTest()
    {
        // Pre-project behavior preserved — but now the vanished group fails BEFORE any upload.
        var transport = CreateTransport(new FakeMaxCloudConnectionService { Client = new FakeWitCloudClient() });
        var request = CreateRequest();
        request.UseAllClients = false;
        request.SelectedGroupName = "Artists";

        var result = await transport.SubmitAsync(request, CreatePackageWithScene());

        Assert.Multiple(() =>
        {
            Assert.That(result.JobId, Does.StartWith("failed-"));
            Assert.That(result.StatusText, Does.Contain("Group 'Artists' was not found"));
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

    [Test]
    public async Task RefreshDownloadsFrameSequenceResultsTest()
    {
        var client = new FakeWitCloudClient();
        client.FakeJobs.Status = ProcessingJobStatus.Completed;
        client.FakeJobs.OverallProgress = 1d;
        client.FakeJobs.Result = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        var transport = CreateTransport(new FakeMaxCloudConnectionService { Client = client });
        var jobId = Guid.NewGuid();
        var jobState = new MaxConnectedRenderJobState
        {
            JobId = jobId.ToString("D"),
            CloudUrl = "https://omnibuscloud.local",
            RenderMode = "RenderFrames",
            FrameStart = 5,
            FrameEnd = 7
        };

        try
        {
            var result = await transport.RefreshAsync(jobState);

            Assert.Multiple(() =>
            {
                Assert.That(result.IsCompleted, Is.True);
                Assert.That(result.ResultFrameBlobIds, Has.Count.EqualTo(3));
                Assert.That(client.FakeBlobs.DownloadedBlobs, Has.Count.EqualTo(3));
                Assert.That(result.PrimaryArtifactPath, Does.EndWith("frame_0005.png"));
                Assert.That(File.Exists(result.PrimaryArtifactPath), Is.True);
                Assert.That(client.FakeBlobs.DownloadedBlobs.Any(me => me.LocalPath.EndsWith("frame_0007.png")), Is.True);
            });
        }
        finally
        {
            var resultFolder = Path.Combine(Path.GetTempPath(), "OmnibusCloudResults", jobId.ToString("D").Replace('-', '_'));
            if (Directory.Exists(resultFolder))
                Directory.Delete(resultFolder, true);
        }
    }

    [Test]
    public async Task RefreshMarksJobCancelledWhenFarmReportsCancelledTest()
    {
        var client = new FakeWitCloudClient();
        client.FakeJobs.Status = ProcessingJobStatus.Cancelled;

        var transport = CreateTransport(new FakeMaxCloudConnectionService { Client = client });

        var result = await transport.RefreshAsync(new MaxConnectedRenderJobState
        {
            JobId = Guid.NewGuid().ToString("D"),
            CloudUrl = "https://omnibuscloud.local",
            RenderMode = "RenderStill"
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.IsCancelled, Is.True);
            Assert.That(result.IsCompleted, Is.False);
        });
    }

    #endregion

    #region Cancel Tests

    [Test]
    public async Task CancelRequestsServerSideCancellationTest()
    {
        var client = new FakeWitCloudClient();
        var transport = CreateTransport(new FakeMaxCloudConnectionService { Client = client });
        var jobId = Guid.NewGuid();

        var result = await transport.CancelAsync(new MaxConnectedRenderJobState
        {
            JobId = jobId.ToString("D"),
            CloudUrl = "https://omnibuscloud.local"
        });

        Assert.Multiple(() =>
        {
            Assert.That(client.FakeJobs.CancelledJobIds, Is.EqualTo(new[] { jobId }));
            Assert.That(result.StatusText, Does.Contain("Cancel requested"));
            Assert.That(result.IsCancelled, Is.False, "The job stays active until a refresh observes the terminal cancelled status.");
        });
    }

    [Test]
    public async Task CancelMarksNeverSubmittedJobCancelledLocallyTest()
    {
        var transport = CreateTransport(new FakeMaxCloudConnectionService());

        var result = await transport.CancelAsync(new MaxConnectedRenderJobState
        {
            JobId = "blocked-abc"
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.IsCancelled, Is.True);
            Assert.That(result.StatusText, Does.Contain("Cancelled before submission"));
        });
    }

    [Test]
    public async Task CancelFailsWithoutSignedInConnectionTest()
    {
        var transport = CreateTransport(new FakeMaxCloudConnectionService { Client = null });

        var result = await transport.CancelAsync(new MaxConnectedRenderJobState
        {
            JobId = Guid.NewGuid().ToString("D"),
            CloudUrl = "https://omnibuscloud.local"
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.IsCancelled, Is.False);
            Assert.That(result.StatusText, Does.Contain("No signed-in cloud connection"));
        });
    }

    #endregion

    #region Tools

    private static MaxConnectedRenderSubmissionTransportOmnibusCloudSession CreateTransport(FakeMaxCloudConnectionService connectionService)
    {
        return new MaxConnectedRenderSubmissionTransportOmnibusCloudSession(connectionService, new MaxConnectedRenderSceneAttachmentService());
    }

    private static MaxSceneLaunchPackageResult CreatePackageWithScene()
    {
        return new MaxSceneLaunchPackageResult
        {
            IsSuccess = true,
            Scene = new OutWit.Controller.Render.Dcc.Model.DccSceneData()
        };
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
