using System.Text.Json;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

[TestFixture]
public sealed class MaxBatchUploadIntegrationTests
{
    #region Tests

    [Test]
    [Explicit("Requires installed 3ds Max Batch, a locally built 3ds Max plugin assembly, and real OmnibusCloud upload environment variables.")]
    [Category("Integration")]
    public void SmokeUploadLaunchPackageThrough3dsMaxBatchA01depthTest()
    {
        if (!MaxBatchSmokeTestUtils.TryCreateUploadSmokeEnvironment(out var environment, out var ignoreReason))
            Assert.Ignore(ignoreReason);

        Assert.That(environment, Is.Not.Null);
        var smokeEnvironment = environment!;
        var processResult = MaxBatchSmokeTestUtils.RunUploadSmoke(smokeEnvironment);
        var smokeResult = MaxBatchSmokeResult.Parse(processResult.ResultPath);

        Assert.Multiple(() =>
        {
            Assert.That(processResult.ExitCode, Is.EqualTo(0), $"3ds Max Batch exited with code {processResult.ExitCode}.{Environment.NewLine}STDOUT:{Environment.NewLine}{processResult.StandardOutput}{Environment.NewLine}STDERR:{Environment.NewLine}{processResult.StandardError}");
            Assert.That(smokeResult.Success, Is.True, $"Upload smoke result reported failure. Status='{smokeResult.StatusText}'.{Environment.NewLine}STDOUT:{Environment.NewLine}{processResult.StandardOutput}{Environment.NewLine}STDERR:{Environment.NewLine}{processResult.StandardError}");
            Assert.That(smokeResult.PackageArchivePath, Is.Not.Null.And.Not.Empty);
            Assert.That(smokeResult.UploadedBlobId, Is.Not.Null.And.Not.Empty);
            Assert.That(smokeResult.UploadReceiptPath, Is.Not.Null.And.Not.Empty);
            Assert.That(Guid.TryParse(smokeResult.GetRequiredValue("UploadedBlobId"), out _), Is.True);
            Assert.That(File.Exists(smokeResult.GetRequiredValue("PackageArchivePath")), Is.True);
            Assert.That(File.Exists(smokeResult.GetRequiredValue("UploadReceiptPath")), Is.True);
        });

        var uploadReceiptJson = File.ReadAllText(smokeResult.GetRequiredValue("UploadReceiptPath"));
        using var uploadReceiptDocument = JsonDocument.Parse(uploadReceiptJson);
        var root = uploadReceiptDocument.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("UploadedBlobId").GetGuid().ToString(), Is.EqualTo(smokeResult.GetRequiredValue("UploadedBlobId")).IgnoreCase);
            Assert.That(root.GetProperty("CloudUrl").GetString(), Is.EqualTo(smokeEnvironment.CloudUrl));
            Assert.That(root.GetProperty("IdentityUrl").GetString(), Is.EqualTo(smokeEnvironment.IdentityUrl));
            Assert.That(root.GetProperty("PackageArchivePath").GetString(), Is.EqualTo(smokeResult.GetRequiredValue("PackageArchivePath")));
        });
    }

    #endregion
}
