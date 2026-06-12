using System.IO.Compression;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

internal static class MaxBatchLaunchPackageAssertions
{
    #region Functions

    public static void AssertArchiveContainsExpectedArtifacts(string packageArchivePath, string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(packageArchivePath))
            throw new InvalidOperationException("Package archive path is required.");

        if (string.IsNullOrWhiteSpace(manifestPath))
            throw new InvalidOperationException("Manifest path is required.");

        using var archive = ZipFile.OpenRead(packageArchivePath);
        var entriesByName = archive.Entries.ToDictionary(me => me.FullName, StringComparer.OrdinalIgnoreCase);
        var expectedEntryNames = new[]
        {
            "launch-request.json",
            "dcc-scene.json",
            "dcc-scene.mpack.gz"
        };

        Assert.Multiple(() =>
        {
            foreach (var expectedEntryName in expectedEntryNames)
            {
                Assert.That(entriesByName.ContainsKey(expectedEntryName), Is.True,
                    $"Launch package archive '{packageArchivePath}' does not contain expected entry '{expectedEntryName}'.");
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(entriesByName["launch-request.json"].Length, Is.GreaterThan(0));
            Assert.That(entriesByName["dcc-scene.json"].Length, Is.GreaterThan(0));
            Assert.That(entriesByName["dcc-scene.mpack.gz"].Length, Is.GreaterThan(0));
        });

        using var manifestEntryStream = entriesByName["launch-request.json"].Open();
        using var manifestEntryReader = new StreamReader(manifestEntryStream);
        var archivedManifestJson = manifestEntryReader.ReadToEnd();
        var manifestJson = File.ReadAllText(manifestPath);

        Assert.That(archivedManifestJson, Is.EqualTo(manifestJson),
            $"Launch package archive manifest entry does not match disk manifest '{manifestPath}'.");
    }

    #endregion
}
