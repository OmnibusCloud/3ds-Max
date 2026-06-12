namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

internal sealed class MaxBatchSmokeResult
{
    #region Constructors

    private MaxBatchSmokeResult(IReadOnlyDictionary<string, string> values)
    {
        Values = values;
    }

    #endregion

    #region Functions

    public static MaxBatchSmokeResult Parse(string resultPath)
    {
        if (string.IsNullOrWhiteSpace(resultPath))
            throw new InvalidOperationException("Smoke result path is required.");

        if (!File.Exists(resultPath))
            throw new FileNotFoundException("Smoke result file was not found.", resultPath);

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in File.ReadAllLines(resultPath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
                continue;

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();

            if (string.IsNullOrWhiteSpace(key))
                continue;

            values[key] = value;
        }

        return new MaxBatchSmokeResult(values);
    }

    public string GetRequiredValue(string key)
    {
        if (Values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;

        throw new InvalidOperationException($"Smoke result value '{key}' is required.");
    }

    public string? GetValueOrDefault(string key)
    {
        return Values.TryGetValue(key, out var value) ? value : null;
    }

    #endregion

    #region Properties

    public IReadOnlyDictionary<string, string> Values { get; }

    public bool Success
    {
        get
        {
            return bool.TryParse(GetValueOrDefault("Success"), out var success) && success;
        }
    }

    public string? StatusText => GetValueOrDefault("StatusText");

    public string? JsonOutputPath => GetValueOrDefault("JsonOutputPath");

    public string? MemoryPackOutputPath => GetValueOrDefault("MemoryPackOutputPath");

    public string? JsonGzipOutputPath => GetValueOrDefault("JsonGzipOutputPath");

    public string? MemoryPackGzipOutputPath => GetValueOrDefault("MemoryPackGzipOutputPath");

    public string? PackageId => GetValueOrDefault("PackageId");

    public string? PackageFolderPath => GetValueOrDefault("PackageFolderPath");

    public string? ManifestPath => GetValueOrDefault("ManifestPath");

    public string? PackageArchivePath => GetValueOrDefault("PackageArchivePath");

    public string? PrimaryArtifactPath => GetValueOrDefault("PrimaryArtifactPath");

    public string? UploadedBlobId => GetValueOrDefault("UploadedBlobId");

    public string? UploadReceiptPath => GetValueOrDefault("UploadReceiptPath");

    public string? JobId => GetValueOrDefault("JobId");

    public string? FinalJobStatus => GetValueOrDefault("FinalJobStatus");

    public string? OverallProgress => GetValueOrDefault("OverallProgress");

    public string? ResultBlobId => GetValueOrDefault("ResultBlobId");

    public string? DownloadedFilePath => GetValueOrDefault("DownloadedFilePath");

    public string? TraceLogPath => GetValueOrDefault("TraceLogPath");

    public string? ErrorMessage => GetValueOrDefault("ErrorMessage");

    #endregion
}
