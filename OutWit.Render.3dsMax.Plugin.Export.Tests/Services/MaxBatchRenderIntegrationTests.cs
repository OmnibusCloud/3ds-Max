namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

[TestFixture]
[NonParallelizable]
public sealed class MaxBatchRenderIntegrationTests
{
    #region Tests

    [TestCaseSource(typeof(MaxBatchSmokeTestUtils), nameof(MaxBatchSmokeTestUtils.GetCanonicalSmokeSceneCases))]
    [Explicit("Requires installed 3ds Max Batch, a locally built 3ds Max plugin assembly, and real OmnibusCloud render environment variables.")]
    [Category("Integration")]
    public void SmokeRenderDccSceneStillThrough3dsMaxBatchCanonicalSceneTest(string sceneFileName)
    {
        if (!MaxBatchSmokeTestUtils.TryCreateRenderSmokeEnvironment(sceneFileName, out var environment, out var ignoreReason))
            Assert.Ignore(ignoreReason);

        Assert.That(environment, Is.Not.Null);
        var smokeEnvironment = environment!;
        var processResult = MaxBatchSmokeTestUtils.RunRenderSmoke(smokeEnvironment);
        var smokeResult = MaxBatchSmokeResult.Parse(processResult.ResultPath);

        if (!smokeResult.Success
            && !string.IsNullOrWhiteSpace(smokeResult.ErrorMessage)
            && smokeResult.ErrorMessage.Contains("No fallback nodes available", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Ignore($"Real render smoke skipped because deployed render capacity is currently unavailable: {smokeResult.ErrorMessage}");
        }

        Assert.Multiple(() =>
        {
            Assert.That(processResult.ExitCode, Is.EqualTo(0), $"3ds Max Batch exited with code {processResult.ExitCode}.{Environment.NewLine}STDOUT:{Environment.NewLine}{processResult.StandardOutput}{Environment.NewLine}STDERR:{Environment.NewLine}{processResult.StandardError}");
            Assert.That(smokeResult.Success, Is.True, $"Render smoke result reported failure. Status='{smokeResult.StatusText}'. Error='{smokeResult.ErrorMessage}'.{Environment.NewLine}STDOUT:{Environment.NewLine}{processResult.StandardOutput}{Environment.NewLine}STDERR:{Environment.NewLine}{processResult.StandardError}");
            Assert.That(smokeResult.JobId, Is.Not.Null.And.Not.Empty);
            Assert.That(smokeResult.FinalJobStatus, Is.EqualTo("Completed").IgnoreCase);
            Assert.That(smokeResult.ResultBlobId, Is.Not.Null.And.Not.Empty);
            Assert.That(Guid.TryParse(smokeResult.GetRequiredValue("ResultBlobId"), out _), Is.True);
            Assert.That(smokeResult.DownloadedFilePath, Is.Not.Null.And.Not.Empty);
            Assert.That(smokeResult.TraceLogPath, Is.Not.Null.And.Not.Empty);
            Assert.That(File.Exists(smokeResult.GetRequiredValue("DownloadedFilePath")), Is.True);
            Assert.That(File.Exists(smokeResult.GetRequiredValue("TraceLogPath")), Is.True);
            Assert.That(new FileInfo(smokeResult.GetRequiredValue("DownloadedFilePath")).Length, Is.GreaterThan(0));
        });

        MaxBatchRenderAssertions.AssertImageIsReadableAndNotSolidBlack(
            smokeResult.GetRequiredValue("DownloadedFilePath"),
            "3ds Max connected render smoke");

        TestContext.Progress.WriteLine($"3ds Max render smoke output saved to: {Path.GetDirectoryName(smokeResult.GetRequiredValue("DownloadedFilePath"))}");
        TestContext.Progress.WriteLine($"3ds Max render smoke image saved to: {smokeResult.GetRequiredValue("DownloadedFilePath")}");
        TestContext.Progress.WriteLine($"3ds Max render smoke trace saved to: {smokeResult.GetRequiredValue("TraceLogPath")}");
    }

    [TestCaseSource(typeof(MaxBatchSmokeTestUtils), nameof(MaxBatchSmokeTestUtils.GetRealisticRenderSmokeSceneCases))]
    [Explicit("Requires installed 3ds Max Batch, a locally built 3ds Max plugin assembly, and real OmnibusCloud render environment variables.")]
    [Category("Integration")]
    public void SmokeRenderDccSceneStillThrough3dsMaxBatchRealisticSceneTest(string scenePath)
    {
        if (!MaxBatchSmokeTestUtils.TryCreateRenderSmokeEnvironment(scenePath, out var environment, out var ignoreReason))
            Assert.Ignore(ignoreReason);

        Assert.That(environment, Is.Not.Null);
        var smokeEnvironment = environment!;
        var processResult = MaxBatchSmokeTestUtils.RunRenderSmoke(smokeEnvironment);
        var smokeResult = MaxBatchSmokeResult.Parse(processResult.ResultPath);

        if (!smokeResult.Success
            && !string.IsNullOrWhiteSpace(smokeResult.ErrorMessage)
            && smokeResult.ErrorMessage.Contains("No fallback nodes available", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Ignore($"Real render smoke skipped because deployed render capacity is currently unavailable: {smokeResult.ErrorMessage}");
        }

        Assert.Multiple(() =>
        {
            Assert.That(processResult.ExitCode, Is.EqualTo(0), $"3ds Max Batch exited with code {processResult.ExitCode}.{Environment.NewLine}STDOUT:{Environment.NewLine}{processResult.StandardOutput}{Environment.NewLine}STDERR:{Environment.NewLine}{processResult.StandardError}");
            Assert.That(smokeResult.Success, Is.True, $"Render smoke result reported failure. Status='{smokeResult.StatusText}'. Error='{smokeResult.ErrorMessage}'.{Environment.NewLine}STDOUT:{Environment.NewLine}{processResult.StandardOutput}{Environment.NewLine}STDERR:{Environment.NewLine}{processResult.StandardError}");
            Assert.That(smokeResult.JobId, Is.Not.Null.And.Not.Empty);
            Assert.That(smokeResult.FinalJobStatus, Is.EqualTo("Completed").IgnoreCase);
            Assert.That(smokeResult.ResultBlobId, Is.Not.Null.And.Not.Empty);
            Assert.That(Guid.TryParse(smokeResult.GetRequiredValue("ResultBlobId"), out _), Is.True);
            Assert.That(smokeResult.DownloadedFilePath, Is.Not.Null.And.Not.Empty);
            Assert.That(smokeResult.TraceLogPath, Is.Not.Null.And.Not.Empty);
            Assert.That(File.Exists(smokeResult.GetRequiredValue("DownloadedFilePath")), Is.True);
            Assert.That(File.Exists(smokeResult.GetRequiredValue("TraceLogPath")), Is.True);
            Assert.That(new FileInfo(smokeResult.GetRequiredValue("DownloadedFilePath")).Length, Is.GreaterThan(0));
        });

        MaxBatchRenderAssertions.AssertImageIsReadableAndNotSolidBlack(
            smokeResult.GetRequiredValue("DownloadedFilePath"),
            "3ds Max connected render smoke");

        TestContext.Progress.WriteLine($"3ds Max realistic render smoke source scene: {scenePath}");
        TestContext.Progress.WriteLine($"3ds Max realistic render smoke output saved to: {Path.GetDirectoryName(smokeResult.GetRequiredValue("DownloadedFilePath"))}");
        TestContext.Progress.WriteLine($"3ds Max realistic render smoke image saved to: {smokeResult.GetRequiredValue("DownloadedFilePath")}");
        TestContext.Progress.WriteLine($"3ds Max realistic render smoke trace saved to: {smokeResult.GetRequiredValue("TraceLogPath")}");
    }

    [TestCaseSource(typeof(MaxBatchSmokeTestUtils), nameof(MaxBatchSmokeTestUtils.GetFeatureRenderSmokeSceneCases))]
    [Explicit("Requires installed 3ds Max Batch, a built 3ds Max plugin assembly, the @Data sample scenes, and real OmnibusCloud render environment variables.")]
    [Category("Integration")]
    public void SmokeRenderDccSceneStillThrough3dsMaxBatchFeatureSceneTest(string feature, string scenePath)
    {
        // Loads a real Autodesk sample scene that carries a specific Dcc 1.4 feature (deformation,
        // displacement, motion blur, vertex colours, HDRI environment), runs it through the installed
        // plugin's collector -> DccScene -> real OmnibusCloud render, and asserts a non-black image.
        // Proves the collector reads the real feature scene end-to-end (the per-frame visual proof for
        // deformation/animation is in the synthetic SDK live tests).
        if (!MaxBatchSmokeTestUtils.TryCreateRenderSmokeEnvironment(scenePath, out var environment, out var ignoreReason))
            Assert.Ignore(ignoreReason);

        Assert.That(environment, Is.Not.Null);
        var smokeEnvironment = environment!;
        var processResult = MaxBatchSmokeTestUtils.RunRenderSmoke(smokeEnvironment);
        var smokeResult = MaxBatchSmokeResult.Parse(processResult.ResultPath);

        if (!smokeResult.Success
            && !string.IsNullOrWhiteSpace(smokeResult.ErrorMessage)
            && smokeResult.ErrorMessage.Contains("No fallback nodes available", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Ignore($"Real render smoke skipped because deployed render capacity is currently unavailable: {smokeResult.ErrorMessage}");
        }

        Assert.Multiple(() =>
        {
            Assert.That(processResult.ExitCode, Is.EqualTo(0), $"3ds Max Batch exited with code {processResult.ExitCode}.{Environment.NewLine}STDOUT:{Environment.NewLine}{processResult.StandardOutput}{Environment.NewLine}STDERR:{Environment.NewLine}{processResult.StandardError}");
            Assert.That(smokeResult.Success, Is.True, $"Feature '{feature}' render smoke reported failure. Status='{smokeResult.StatusText}'. Error='{smokeResult.ErrorMessage}'.{Environment.NewLine}STDOUT:{Environment.NewLine}{processResult.StandardOutput}{Environment.NewLine}STDERR:{Environment.NewLine}{processResult.StandardError}");
            Assert.That(smokeResult.JobId, Is.Not.Null.And.Not.Empty);
            Assert.That(smokeResult.FinalJobStatus, Is.EqualTo("Completed").IgnoreCase);
            Assert.That(smokeResult.ResultBlobId, Is.Not.Null.And.Not.Empty);
            Assert.That(Guid.TryParse(smokeResult.GetRequiredValue("ResultBlobId"), out _), Is.True);
            Assert.That(File.Exists(smokeResult.GetRequiredValue("DownloadedFilePath")), Is.True);
            Assert.That(new FileInfo(smokeResult.GetRequiredValue("DownloadedFilePath")).Length, Is.GreaterThan(0));
        });

        MaxBatchRenderAssertions.AssertImageIsReadableAndNotSolidBlack(
            smokeResult.GetRequiredValue("DownloadedFilePath"),
            $"3ds Max {feature} feature render smoke");

        TestContext.Progress.WriteLine($"3ds Max feature '{feature}' source scene: {scenePath}");
        TestContext.Progress.WriteLine($"3ds Max feature '{feature}' render image saved to: {smokeResult.GetRequiredValue("DownloadedFilePath")}");
        TestContext.Progress.WriteLine($"3ds Max feature '{feature}' render trace saved to: {smokeResult.GetRequiredValue("TraceLogPath")}");
    }

    #endregion
}
