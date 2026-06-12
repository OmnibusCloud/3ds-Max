namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

[TestFixture]
public sealed class MaxBatchInstalledPackageIntegrationTests
{
    #region Tests

    [Test]
    [Explicit("Requires installed 3ds Max Batch, writable ApplicationPlugins target root, and a locally built 3ds Max plugin assembly.")]
    [Category("Integration")]
    public void SmokeValidateInstalledPackageThrough3dsMaxBatchA01depthTest()
    {
        if (!MaxBatchSmokeTestUtils.TryCreateInstalledPackageSmokeEnvironment(out var environment, out var ignoreReason))
            Assert.Ignore(ignoreReason);

        Assert.That(environment, Is.Not.Null);
        var smokeEnvironment = environment!;

        try
        {
            MaxBatchSmokeTestUtils.InstallPackage(smokeEnvironment);
            var processResult = MaxBatchSmokeTestUtils.RunInstalledPackageSmoke(smokeEnvironment);
            var smokeResult = MaxBatchSmokeResult.Parse(processResult.ResultPath);

            Assert.Multiple(() =>
            {
                Assert.That(processResult.ExitCode, Is.EqualTo(0), $"3ds Max Batch exited with code {processResult.ExitCode}.{Environment.NewLine}STDOUT:{Environment.NewLine}{processResult.StandardOutput}{Environment.NewLine}STDERR:{Environment.NewLine}{processResult.StandardError}");
                Assert.That(smokeResult.Success, Is.True, $"Installed-package smoke result reported failure. Status='{smokeResult.StatusText}'.{Environment.NewLine}STDOUT:{Environment.NewLine}{processResult.StandardOutput}{Environment.NewLine}STDERR:{Environment.NewLine}{processResult.StandardError}");
                Assert.That(smokeResult.GetRequiredValue("PackageStartupLoaded"), Is.EqualTo("true").IgnoreCase);
                Assert.That(smokeResult.JsonOutputPath, Is.Not.Null.And.Not.Empty);
                Assert.That(smokeResult.MemoryPackOutputPath, Is.Not.Null.And.Not.Empty);
                Assert.That(smokeResult.JsonGzipOutputPath, Is.Not.Null.And.Not.Empty);
                Assert.That(smokeResult.MemoryPackGzipOutputPath, Is.Not.Null.And.Not.Empty);
                Assert.That(File.Exists(smokeResult.GetRequiredValue("JsonOutputPath")), Is.True);
                Assert.That(File.Exists(smokeResult.GetRequiredValue("MemoryPackOutputPath")), Is.True);
                Assert.That(File.Exists(smokeResult.GetRequiredValue("JsonGzipOutputPath")), Is.True);
                Assert.That(File.Exists(smokeResult.GetRequiredValue("MemoryPackGzipOutputPath")), Is.True);
                Assert.That(new FileInfo(smokeResult.GetRequiredValue("JsonOutputPath")).Length, Is.GreaterThan(0));
                Assert.That(new FileInfo(smokeResult.GetRequiredValue("MemoryPackOutputPath")).Length, Is.GreaterThan(0));
                Assert.That(new FileInfo(smokeResult.GetRequiredValue("JsonGzipOutputPath")).Length, Is.GreaterThan(0));
                Assert.That(new FileInfo(smokeResult.GetRequiredValue("MemoryPackGzipOutputPath")).Length, Is.GreaterThan(0));
            });
        }
        finally
        {
            MaxBatchSmokeTestUtils.UninstallPackage(smokeEnvironment);
        }
    }

    #endregion
}
