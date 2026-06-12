namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

[TestFixture]
public sealed class MaxBatchSmokeIntegrationTests
{
    #region Tests

    [TestCaseSource(typeof(MaxBatchSmokeTestUtils), nameof(MaxBatchSmokeTestUtils.GetCanonicalSmokeSceneCases))]
    [Category("Integration")]
    public void SmokeValidateCurrentSceneThrough3dsMaxBatchCanonicalSceneTest(string sceneFileName)
    {
        if (!MaxBatchSmokeTestUtils.TryCreateCurrentSceneSmokeEnvironment(sceneFileName, out var environment, out var ignoreReason))
            Assert.Ignore(ignoreReason);

        Assert.That(environment, Is.Not.Null);
        var smokeEnvironment = environment!;
        var processResult = MaxBatchSmokeTestUtils.RunCurrentSceneSmoke(smokeEnvironment);
        var smokeResult = MaxBatchSmokeResult.Parse(processResult.ResultPath);

        Assert.Multiple(() =>
        {
            Assert.That(processResult.ExitCode, Is.EqualTo(0), $"3ds Max Batch exited with code {processResult.ExitCode}.\nSTDOUT:\n{processResult.StandardOutput}\nSTDERR:\n{processResult.StandardError}");
            Assert.That(smokeResult.Success, Is.True, $"Smoke result reported failure. Status='{smokeResult.StatusText}'.\nSTDOUT:\n{processResult.StandardOutput}\nSTDERR:\n{processResult.StandardError}");
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

    [TestCaseSource(typeof(MaxBatchSmokeTestUtils), nameof(MaxBatchSmokeTestUtils.GetRealisticValidationSmokeSceneCases))]
    [Explicit("Requires installed 3ds Max Batch and a locally built 3ds Max plugin assembly.")]
    [Category("Integration")]
    public void SmokeValidateCurrentSceneThrough3dsMaxBatchRealisticSceneTest(string scenePath)
    {
        if (!MaxBatchSmokeTestUtils.TryCreateCurrentSceneSmokeEnvironment(scenePath, out var environment, out var ignoreReason))
            Assert.Ignore(ignoreReason);

        Assert.That(environment, Is.Not.Null);
        var smokeEnvironment = environment!;
        var processResult = MaxBatchSmokeTestUtils.RunCurrentSceneSmoke(smokeEnvironment);
        var smokeResult = MaxBatchSmokeResult.Parse(processResult.ResultPath);

        Assert.Multiple(() =>
        {
            Assert.That(processResult.ExitCode, Is.EqualTo(0), $"3ds Max Batch exited with code {processResult.ExitCode}.\nSTDOUT:\n{processResult.StandardOutput}\nSTDERR:\n{processResult.StandardError}");
            Assert.That(smokeResult.Success, Is.True, $"Smoke result reported failure. Status='{smokeResult.StatusText}'.\nSTDOUT:\n{processResult.StandardOutput}\nSTDERR:\n{processResult.StandardError}");
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

    #endregion
}
