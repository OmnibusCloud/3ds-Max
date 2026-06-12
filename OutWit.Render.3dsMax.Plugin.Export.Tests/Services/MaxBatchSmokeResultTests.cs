namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

[TestFixture]
public sealed class MaxBatchSmokeResultTests
{
    #region Tests

    [Test]
    public void ParseReadsExpectedSmokeKeyValuePairsTest()
    {
        var resultPath = Path.Combine(Path.GetTempPath(), $"OutWit.3dsMax.Smoke.Result.Tests.{Guid.NewGuid():N}.txt");

        try
        {
            File.WriteAllLines(resultPath,
            [
                "Success=true",
                "StatusText=Scene summary collected and neutral DCC payload validated successfully.",
                "JsonOutputPath=C:/Temp/dcc-scene.json",
                "MemoryPackOutputPath=C:/Temp/dcc-scene.mpack",
                "JsonGzipOutputPath=C:/Temp/dcc-scene.json.gz",
                "MemoryPackGzipOutputPath=C:/Temp/dcc-scene.mpack.gz"
            ]);

            var result = MaxBatchSmokeResult.Parse(resultPath);

            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.True);
                Assert.That(result.StatusText, Is.EqualTo("Scene summary collected and neutral DCC payload validated successfully."));
                Assert.That(result.JsonOutputPath, Is.EqualTo("C:/Temp/dcc-scene.json"));
                Assert.That(result.MemoryPackOutputPath, Is.EqualTo("C:/Temp/dcc-scene.mpack"));
                Assert.That(result.JsonGzipOutputPath, Is.EqualTo("C:/Temp/dcc-scene.json.gz"));
                Assert.That(result.MemoryPackGzipOutputPath, Is.EqualTo("C:/Temp/dcc-scene.mpack.gz"));
            });
        }
        finally
        {
            if (File.Exists(resultPath))
                File.Delete(resultPath);
        }
    }

    #endregion
}
