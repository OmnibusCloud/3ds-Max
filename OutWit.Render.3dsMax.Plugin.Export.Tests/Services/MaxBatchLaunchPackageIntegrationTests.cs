using System.Text.Json;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

[TestFixture]
public sealed class MaxBatchLaunchPackageIntegrationTests
{
    #region Tests

    [TestCaseSource(typeof(MaxBatchSmokeTestUtils), nameof(MaxBatchSmokeTestUtils.GetCanonicalSmokeSceneCases))]
    [Category("Integration")]
    public void SmokePrepareLaunchPackageThrough3dsMaxBatchCanonicalSceneTest(string sceneFileName)
    {
        if (!MaxBatchSmokeTestUtils.TryCreateLaunchPackageSmokeEnvironment(sceneFileName, out var environment, out var ignoreReason))
            Assert.Ignore(ignoreReason);

        Assert.That(environment, Is.Not.Null);
        var smokeEnvironment = environment!;
        var processResult = MaxBatchSmokeTestUtils.RunLaunchPackageSmoke(smokeEnvironment);
        var smokeResult = MaxBatchSmokeResult.Parse(processResult.ResultPath);

        Assert.Multiple(() =>
        {
            Assert.That(processResult.ExitCode, Is.EqualTo(0), $"3ds Max Batch exited with code {processResult.ExitCode}.{Environment.NewLine}STDOUT:{Environment.NewLine}{processResult.StandardOutput}{Environment.NewLine}STDERR:{Environment.NewLine}{processResult.StandardError}");
            Assert.That(smokeResult.Success, Is.True, $"Launch-package smoke result reported failure. Status='{smokeResult.StatusText}'.{Environment.NewLine}STDOUT:{Environment.NewLine}{processResult.StandardOutput}{Environment.NewLine}STDERR:{Environment.NewLine}{processResult.StandardError}");
            Assert.That(smokeResult.PackageId, Is.Not.Null.And.Not.Empty);
            Assert.That(smokeResult.PackageFolderPath, Is.Not.Null.And.Not.Empty);
            Assert.That(smokeResult.ManifestPath, Is.Not.Null.And.Not.Empty);
            Assert.That(smokeResult.PackageArchivePath, Is.Not.Null.And.Not.Empty);
            Assert.That(smokeResult.PrimaryArtifactPath, Is.Not.Null.And.Not.Empty);
            Assert.That(Directory.Exists(smokeResult.GetRequiredValue("PackageFolderPath")), Is.True);
            Assert.That(File.Exists(smokeResult.GetRequiredValue("ManifestPath")), Is.True);
            Assert.That(File.Exists(smokeResult.GetRequiredValue("PackageArchivePath")), Is.True);
            Assert.That(File.Exists(smokeResult.GetRequiredValue("PrimaryArtifactPath")), Is.True);
            Assert.That(smokeResult.GetRequiredValue("PackageArchivePath"), Does.EndWith(".zip"));
            Assert.That(new FileInfo(smokeResult.GetRequiredValue("PackageArchivePath")).Length, Is.GreaterThan(0));
        });

        var manifestJson = File.ReadAllText(smokeResult.GetRequiredValue("ManifestPath"));
        using var manifestDocument = JsonDocument.Parse(manifestJson);
        var root = manifestDocument.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("PackageId").GetString(), Is.EqualTo(smokeResult.GetRequiredValue("PackageId")));
            Assert.That(root.GetProperty("RenderMode").GetString(), Is.EqualTo("RenderStill"));
            Assert.That(root.GetProperty("SelectedGroupName").GetString(), Is.EqualTo("Artists"));
            Assert.That(root.GetProperty("JsonArtifactPath").GetString(), Is.Not.Null.And.Not.Empty);
            Assert.That(root.GetProperty("MemoryPackGzipArtifactPath").GetString(), Is.Not.Null.And.Not.Empty);
            Assert.That(root.GetProperty("PackageArchivePath").GetString(), Is.EqualTo(smokeResult.GetRequiredValue("PackageArchivePath")));
        });

        var jsonArtifactPath = root.GetProperty("JsonArtifactPath").GetString();
        var memoryPackGzipArtifactPath = root.GetProperty("MemoryPackGzipArtifactPath").GetString();

        Assert.Multiple(() =>
        {
            Assert.That(jsonArtifactPath, Is.Not.Null.And.Not.Empty);
            Assert.That(memoryPackGzipArtifactPath, Is.Not.Null.And.Not.Empty);
            Assert.That(File.Exists(jsonArtifactPath!), Is.True);
            Assert.That(File.Exists(memoryPackGzipArtifactPath!), Is.True);
            Assert.That(new FileInfo(jsonArtifactPath!).Length, Is.GreaterThan(0));
            Assert.That(new FileInfo(memoryPackGzipArtifactPath!).Length, Is.GreaterThan(0));
        });

        MaxBatchLaunchPackageAssertions.AssertArchiveContainsExpectedArtifacts(
            smokeResult.GetRequiredValue("PackageArchivePath"),
            smokeResult.GetRequiredValue("ManifestPath"));
    }

    #endregion
}
