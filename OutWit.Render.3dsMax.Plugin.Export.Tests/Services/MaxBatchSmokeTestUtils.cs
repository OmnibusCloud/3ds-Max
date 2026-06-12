using System.Diagnostics;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

internal static class MaxBatchSmokeTestUtils
{
    #region Constants

    private static readonly IReadOnlyList<int> SUPPORTED_3DS_MAX_VERSIONS = [2027, 2026, 2025, 2024, 2023];

    private static readonly IReadOnlyList<string> CANONICAL_SMOKE_SCENE_FILE_NAMES = ["A01depth.max", "A03Metal.max", "A07cglas.max"];

    private static readonly IReadOnlyList<string> REALISTIC_RENDER_SMOKE_SCENE_PATHS =
    [
        @"Scenes\Characters\Complete\TarrasqueTextured.max",
        @"Scenes\Mixamo\house_dancing_413.max"
    ];

    private static readonly IReadOnlyList<string> REALISTIC_VALIDATION_SMOKE_SCENE_PATHS =
    [
        @"Scenes\ViewportRendering\robby_vs_fly.max",
        @"Scenes\Crowd\MotionClips\Eagles.max",
        @"Scenes\Characters\Complete\TarrasqueTextured.max",
        @"Scenes\Mixamo\house_dancing_413.max"
    ];

    private const string BATCH_EXECUTABLE_PATH_ENVIRONMENT_VARIABLE_NAME = "OUTWIT_3DSMAX_BATCH_PATH";

    private const string PLUGIN_ASSEMBLY_PATH_ENVIRONMENT_VARIABLE_NAME = "OUTWIT_3DSMAX_PLUGIN_ASSEMBLY_PATH";

    private const string APPLICATION_PLUGINS_ROOT_ENVIRONMENT_VARIABLE_NAME = "OUTWIT_3DSMAX_APPLICATION_PLUGINS_ROOT";

    private const string OMNIBUSCLOUD_URL_ENVIRONMENT_VARIABLE_NAME = "OUTWIT_3DSMAX_OMNIBUSCLOUD_URL";

    private const string OMNIBUSCLOUD_IDENTITY_URL_ENVIRONMENT_VARIABLE_NAME = "OUTWIT_3DSMAX_OMNIBUSCLOUD_IDENTITY_URL";

    private const string OMNIBUSCLOUD_API_KEY_ENVIRONMENT_VARIABLE_NAME = "OUTWIT_3DSMAX_OMNIBUSCLOUD_API_KEY";

    private const string DEFAULT_OMNIBUSCLOUD_URL = "http://engine.omnibuscloud.com";

    private const string DEFAULT_OMNIBUSCLOUD_IDENTITY_URL = "https://id.waveslogic.com";

    private const string DEFAULT_OMNIBUSCLOUD_API_KEY = "wit_sk_u1hYCrcoanPomhlMoXaQ8CfNGl6wXXVGdqDEilCI";

    private const string SMOKE_SCENE_ROOT_RELATIVE_PATH = "@Data\\3ds_max\\Scenes\\Raytrace\\AdvancedExamples";

    #endregion

    #region Functions

    public static bool TryCreateCurrentSceneSmokeEnvironment(out MaxBatchSmokeEnvironment? environment, out string ignoreReason)
    {
        return TryCreateCurrentSceneSmokeEnvironment(CANONICAL_SMOKE_SCENE_FILE_NAMES[0], out environment, out ignoreReason);
    }

    public static bool TryCreateCurrentSceneSmokeEnvironment(string sceneFileName, out MaxBatchSmokeEnvironment? environment, out string ignoreReason)
    {
        environment = null;
        ignoreReason = string.Empty;

        var repoRoot = ResolveRepositoryRoot();
        if (repoRoot is null)
        {
            ignoreReason = "Repository root could not be resolved from the current test run location.";
            return false;
        }

        var batchExecutablePath = ResolveBatchExecutablePath();
        if (string.IsNullOrWhiteSpace(batchExecutablePath))
        {
            ignoreReason = $"3ds Max Batch executable was not found. Set {BATCH_EXECUTABLE_PATH_ENVIRONMENT_VARIABLE_NAME} or install 3ds Max locally.";
            return false;
        }

        var scriptPath = Path.Combine(repoRoot, "OutWit.Render.3dsMax.Plugin", "Scripts", "OutWit.Render.3dsMax.Plugin.SmokeValidateCurrentScene.ms");
        if (!File.Exists(scriptPath))
        {
            ignoreReason = $"Smoke MAXScript was not found: '{scriptPath}'.";
            return false;
        }

        var scenePath = ResolveSmokeScenePath(repoRoot, sceneFileName);
        if (!File.Exists(scenePath))
        {
            ignoreReason = $"3ds Max smoke scene was not found: '{scenePath}'.";
            return false;
        }

        var pluginAssemblyPath = ResolvePluginAssemblyPath(repoRoot);
        if (string.IsNullOrWhiteSpace(pluginAssemblyPath))
        {
            ignoreReason = $"3ds Max plugin assembly was not found. Build OutWit.Render.3dsMax.Plugin or set {PLUGIN_ASSEMBLY_PATH_ENVIRONMENT_VARIABLE_NAME}.";
            return false;
        }

        environment = new MaxBatchSmokeEnvironment
        {
            BatchExecutablePath = batchExecutablePath,
            WorkingDirectoryPath = Path.GetDirectoryName(batchExecutablePath) ?? throw new InvalidOperationException("3ds Max Batch working directory could not be resolved."),
            ScriptPath = scriptPath,
            ScenePath = scenePath,
            PluginAssemblyPath = pluginAssemblyPath
        };

        return true;
    }

    public static IEnumerable<TestCaseData> GetCanonicalSmokeSceneCases()
    {
        foreach (var sceneFileName in CANONICAL_SMOKE_SCENE_FILE_NAMES)
        {
            yield return new TestCaseData(sceneFileName)
                .SetName($"{{m}}_{Path.GetFileNameWithoutExtension(sceneFileName)}");
        }
    }

    public static IEnumerable<TestCaseData> GetRealisticRenderSmokeSceneCases()
    {
        foreach (var scenePath in REALISTIC_RENDER_SMOKE_SCENE_PATHS)
        {
            yield return new TestCaseData(scenePath)
                .SetName($"{{m}}_{Path.GetFileNameWithoutExtension(scenePath)}");
        }
    }

    public static IEnumerable<TestCaseData> GetRealisticValidationSmokeSceneCases()
    {
        foreach (var scenePath in REALISTIC_VALIDATION_SMOKE_SCENE_PATHS)
        {
            yield return new TestCaseData(scenePath)
                .SetName($"{{m}}_{Path.GetFileNameWithoutExtension(scenePath)}");
        }
    }

    public static bool TryCreateRenderSmokeEnvironment(out MaxBatchUploadEnvironment? environment, out string ignoreReason)
    {
        return TryCreateRenderSmokeEnvironment(CANONICAL_SMOKE_SCENE_FILE_NAMES[0], out environment, out ignoreReason);
    }

    public static bool TryCreateRenderSmokeEnvironment(string sceneFileName, out MaxBatchUploadEnvironment? environment, out string ignoreReason)
    {
        environment = null;
        ignoreReason = string.Empty;

        var repoRoot = ResolveRepositoryRoot();
        if (repoRoot is null)
        {
            ignoreReason = "Repository root could not be resolved from the current test run location.";
            return false;
        }

        var batchExecutablePath = ResolveBatchExecutablePath();
        if (string.IsNullOrWhiteSpace(batchExecutablePath))
        {
            ignoreReason = $"3ds Max Batch executable was not found. Set {BATCH_EXECUTABLE_PATH_ENVIRONMENT_VARIABLE_NAME} or install 3ds Max locally.";
            return false;
        }

        var scriptPath = Path.Combine(repoRoot, "OutWit.Render.3dsMax.Plugin", "Scripts", "OutWit.Render.3dsMax.Plugin.SmokeRenderDccSceneStill.ms");
        if (!File.Exists(scriptPath))
        {
            ignoreReason = $"Render smoke MAXScript was not found: '{scriptPath}'.";
            return false;
        }

        var scenePath = ResolveSmokeScenePath(repoRoot, sceneFileName);
        if (!File.Exists(scenePath))
        {
            ignoreReason = $"3ds Max smoke scene was not found: '{scenePath}'.";
            return false;
        }

        var pluginAssemblyPath = ResolvePluginAssemblyPath(repoRoot);
        if (string.IsNullOrWhiteSpace(pluginAssemblyPath))
        {
            ignoreReason = $"3ds Max plugin assembly was not found. Build OutWit.Render.3dsMax.Plugin or set {PLUGIN_ASSEMBLY_PATH_ENVIRONMENT_VARIABLE_NAME}.";
            return false;
        }

        var cloudUrl = ResolveOmnibusCloudUrl();
        if (string.IsNullOrWhiteSpace(cloudUrl))
        {
            ignoreReason = $"Set {OMNIBUSCLOUD_URL_ENVIRONMENT_VARIABLE_NAME} to run the real OmnibusCloud render smoke test.";
            return false;
        }

        var identityUrl = ResolveOmnibusCloudIdentityUrl();
        if (string.IsNullOrWhiteSpace(identityUrl))
        {
            ignoreReason = $"Set {OMNIBUSCLOUD_IDENTITY_URL_ENVIRONMENT_VARIABLE_NAME} to run the real OmnibusCloud render smoke test.";
            return false;
        }

        var apiKey = ResolveOmnibusCloudApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            ignoreReason = $"Set {OMNIBUSCLOUD_API_KEY_ENVIRONMENT_VARIABLE_NAME} to run the real OmnibusCloud render smoke test.";
            return false;
        }

        environment = new MaxBatchUploadEnvironment
        {
            BatchExecutablePath = batchExecutablePath,
            WorkingDirectoryPath = Path.GetDirectoryName(batchExecutablePath) ?? throw new InvalidOperationException("3ds Max Batch working directory could not be resolved."),
            ScriptPath = scriptPath,
            ScenePath = scenePath,
            PluginAssemblyPath = pluginAssemblyPath,
            RepositoryRootPath = repoRoot,
            CloudUrl = cloudUrl,
            IdentityUrl = identityUrl,
            ApiKey = apiKey
        };

        return true;
    }

    public static bool TryCreateUploadSmokeEnvironment(out MaxBatchUploadEnvironment? environment, out string ignoreReason)
    {
        environment = null;
        ignoreReason = string.Empty;

        var repoRoot = ResolveRepositoryRoot();
        if (repoRoot is null)
        {
            ignoreReason = "Repository root could not be resolved from the current test run location.";
            return false;
        }

        var batchExecutablePath = ResolveBatchExecutablePath();
        if (string.IsNullOrWhiteSpace(batchExecutablePath))
        {
            ignoreReason = $"3ds Max Batch executable was not found. Set {BATCH_EXECUTABLE_PATH_ENVIRONMENT_VARIABLE_NAME} or install 3ds Max locally.";
            return false;
        }

        var scriptPath = Path.Combine(repoRoot, "OutWit.Render.3dsMax.Plugin", "Scripts", "OutWit.Render.3dsMax.Plugin.SmokeUploadLaunchPackage.ms");
        if (!File.Exists(scriptPath))
        {
            ignoreReason = $"Upload smoke MAXScript was not found: '{scriptPath}'.";
            return false;
        }

        var scenePath = Path.Combine(repoRoot, "@Data", "3ds_max", "Scenes", "Raytrace", "AdvancedExamples", "A01depth.max");
        if (!File.Exists(scenePath))
        {
            ignoreReason = $"3ds Max smoke scene was not found: '{scenePath}'.";
            return false;
        }

        var pluginAssemblyPath = ResolvePluginAssemblyPath(repoRoot);
        if (string.IsNullOrWhiteSpace(pluginAssemblyPath))
        {
            ignoreReason = $"3ds Max plugin assembly was not found. Build OutWit.Render.3dsMax.Plugin or set {PLUGIN_ASSEMBLY_PATH_ENVIRONMENT_VARIABLE_NAME}.";
            return false;
        }

        var cloudUrl = ResolveOmnibusCloudUrl();
        if (string.IsNullOrWhiteSpace(cloudUrl))
        {
            ignoreReason = $"Set {OMNIBUSCLOUD_URL_ENVIRONMENT_VARIABLE_NAME} to run the real OmnibusCloud upload smoke test.";
            return false;
        }

        var identityUrl = ResolveOmnibusCloudIdentityUrl();
        if (string.IsNullOrWhiteSpace(identityUrl))
        {
            ignoreReason = $"Set {OMNIBUSCLOUD_IDENTITY_URL_ENVIRONMENT_VARIABLE_NAME} to run the real OmnibusCloud upload smoke test.";
            return false;
        }

        var apiKey = ResolveOmnibusCloudApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            ignoreReason = $"Set {OMNIBUSCLOUD_API_KEY_ENVIRONMENT_VARIABLE_NAME} to run the real OmnibusCloud upload smoke test.";
            return false;
        }

        environment = new MaxBatchUploadEnvironment
        {
            BatchExecutablePath = batchExecutablePath,
            WorkingDirectoryPath = Path.GetDirectoryName(batchExecutablePath) ?? throw new InvalidOperationException("3ds Max Batch working directory could not be resolved."),
            ScriptPath = scriptPath,
            ScenePath = scenePath,
            PluginAssemblyPath = pluginAssemblyPath,
            RepositoryRootPath = repoRoot,
            CloudUrl = cloudUrl,
            IdentityUrl = identityUrl,
            ApiKey = apiKey
        };

        return true;
    }

    public static bool TryCreateLaunchPackageSmokeEnvironment(out MaxBatchSmokeEnvironment? environment, out string ignoreReason)
    {
        return TryCreateLaunchPackageSmokeEnvironment(CANONICAL_SMOKE_SCENE_FILE_NAMES[0], out environment, out ignoreReason);
    }

    public static bool TryCreateLaunchPackageSmokeEnvironment(string sceneFileName, out MaxBatchSmokeEnvironment? environment, out string ignoreReason)
    {
        environment = null;
        ignoreReason = string.Empty;

        var repoRoot = ResolveRepositoryRoot();
        if (repoRoot is null)
        {
            ignoreReason = "Repository root could not be resolved from the current test run location.";
            return false;
        }

        var batchExecutablePath = ResolveBatchExecutablePath();
        if (string.IsNullOrWhiteSpace(batchExecutablePath))
        {
            ignoreReason = $"3ds Max Batch executable was not found. Set {BATCH_EXECUTABLE_PATH_ENVIRONMENT_VARIABLE_NAME} or install 3ds Max locally.";
            return false;
        }

        var scriptPath = Path.Combine(repoRoot, "OutWit.Render.3dsMax.Plugin", "Scripts", "OutWit.Render.3dsMax.Plugin.SmokePrepareLaunchPackage.ms");
        if (!File.Exists(scriptPath))
        {
            ignoreReason = $"Launch-package smoke MAXScript was not found: '{scriptPath}'.";
            return false;
        }

        var scenePath = ResolveSmokeScenePath(repoRoot, sceneFileName);
        if (!File.Exists(scenePath))
        {
            ignoreReason = $"3ds Max smoke scene was not found: '{scenePath}'.";
            return false;
        }

        var pluginAssemblyPath = ResolvePluginAssemblyPath(repoRoot);
        if (string.IsNullOrWhiteSpace(pluginAssemblyPath))
        {
            ignoreReason = $"3ds Max plugin assembly was not found. Build OutWit.Render.3dsMax.Plugin or set {PLUGIN_ASSEMBLY_PATH_ENVIRONMENT_VARIABLE_NAME}.";
            return false;
        }

        environment = new MaxBatchSmokeEnvironment
        {
            BatchExecutablePath = batchExecutablePath,
            WorkingDirectoryPath = Path.GetDirectoryName(batchExecutablePath) ?? throw new InvalidOperationException("3ds Max Batch working directory could not be resolved."),
            ScriptPath = scriptPath,
            ScenePath = scenePath,
            PluginAssemblyPath = pluginAssemblyPath
        };

        return true;
    }

    public static bool TryCreateInstalledPackageSmokeEnvironment(out MaxBatchInstalledPackageEnvironment? environment, out string ignoreReason)
    {
        environment = null;
        ignoreReason = string.Empty;

        var repoRoot = ResolveRepositoryRoot();
        if (repoRoot is null)
        {
            ignoreReason = "Repository root could not be resolved from the current test run location.";
            return false;
        }

        var batchExecutablePath = ResolveBatchExecutablePath();
        if (string.IsNullOrWhiteSpace(batchExecutablePath))
        {
            ignoreReason = $"3ds Max Batch executable was not found. Set {BATCH_EXECUTABLE_PATH_ENVIRONMENT_VARIABLE_NAME} or install 3ds Max locally.";
            return false;
        }

        var pluginAssemblyPath = ResolvePluginAssemblyPath(repoRoot);
        if (string.IsNullOrWhiteSpace(pluginAssemblyPath))
        {
            ignoreReason = $"3ds Max plugin assembly was not found. Build OutWit.Render.3dsMax.Plugin or set {PLUGIN_ASSEMBLY_PATH_ENVIRONMENT_VARIABLE_NAME}.";
            return false;
        }

        var installedPackageSmokeScriptPath = Path.Combine(repoRoot, "OutWit.Render.3dsMax.Plugin", "Scripts", "OutWit.Render.3dsMax.Plugin.SmokeValidateInstalledPackage.ms");
        if (!File.Exists(installedPackageSmokeScriptPath))
        {
            ignoreReason = $"Installed-package smoke MAXScript was not found: '{installedPackageSmokeScriptPath}'.";
            return false;
        }

        var installScriptPath = Path.Combine(repoRoot, "OutWit.Render.3dsMax.Plugin", "Scripts", "Install-OutWit.Render.3dsMax.Plugin.ps1");
        if (!File.Exists(installScriptPath))
        {
            ignoreReason = $"3ds Max install script was not found: '{installScriptPath}'.";
            return false;
        }

        var uninstallScriptPath = Path.Combine(repoRoot, "OutWit.Render.3dsMax.Plugin", "Scripts", "Uninstall-OutWit.Render.3dsMax.Plugin.ps1");
        if (!File.Exists(uninstallScriptPath))
        {
            ignoreReason = $"3ds Max uninstall script was not found: '{uninstallScriptPath}'.";
            return false;
        }

        var scenePath = Path.Combine(repoRoot, "@Data", "3ds_max", "Scenes", "Raytrace", "AdvancedExamples", "A01depth.max");
        if (!File.Exists(scenePath))
        {
            ignoreReason = $"3ds Max smoke scene was not found: '{scenePath}'.";
            return false;
        }

        var applicationPluginsRootPath = ResolveApplicationPluginsRootPath();

        environment = new MaxBatchInstalledPackageEnvironment
        {
            BatchExecutablePath = batchExecutablePath,
            WorkingDirectoryPath = Path.GetDirectoryName(batchExecutablePath) ?? throw new InvalidOperationException("3ds Max Batch working directory could not be resolved."),
            ScriptPath = installedPackageSmokeScriptPath,
            ScenePath = scenePath,
            InstallScriptPath = installScriptPath,
            UninstallScriptPath = uninstallScriptPath,
            ApplicationPluginsRootPath = applicationPluginsRootPath,
            Configuration = ResolveConfiguration(pluginAssemblyPath)
        };

        return true;
    }

    public static MaxBatchProcessRunResult RunCurrentSceneSmoke(MaxBatchSmokeEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var rootFolderPath = Path.Combine(Path.GetTempPath(), $"OutWit.3dsMax.Smoke.{Guid.NewGuid():N}");
        var outputFolderPath = Path.Combine(rootFolderPath, "output");
        var resultPath = Path.Combine(rootFolderPath, "result.txt");
        var listenerLogPath = Path.Combine(rootFolderPath, "listener.log");
        var maxLogPath = Path.Combine(rootFolderPath, "max.log");
        Directory.CreateDirectory(rootFolderPath);
        Directory.CreateDirectory(outputFolderPath);

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = environment.BatchExecutablePath,
            Arguments = BuildCommandLine(environment, outputFolderPath, resultPath, listenerLogPath, maxLogPath),
            WorkingDirectory = environment.WorkingDirectoryPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        process.Start();
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();

        if (!process.WaitForExit((int)TimeSpan.FromMinutes(10).TotalMilliseconds))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            throw new TimeoutException("3ds Max Batch smoke process timed out after 10 minutes.");
        }

        return new MaxBatchProcessRunResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = standardOutput,
            StandardError = standardError,
            ResultPath = resultPath,
            OutputFolderPath = outputFolderPath,
            ListenerLogPath = listenerLogPath,
            MaxLogPath = maxLogPath
        };
    }

    public static MaxBatchProcessRunResult RunRenderSmoke(MaxBatchUploadEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var rootFolderPath = Path.Combine(
            environment.RepositoryRootPath,
            "@Publish",
            "LiveTestOutputs",
            "3dsMax",
            "RenderDccSceneStill",
            $"{DateTime.UtcNow:yyyy-MM-dd-HH-mm-ss}_{Guid.NewGuid():N}");
        var outputFolderPath = Path.Combine(rootFolderPath, "output");
        var resultPath = Path.Combine(rootFolderPath, "result.txt");
        var listenerLogPath = Path.Combine(rootFolderPath, "listener.log");
        var maxLogPath = Path.Combine(rootFolderPath, "max.log");
        Directory.CreateDirectory(rootFolderPath);
        Directory.CreateDirectory(outputFolderPath);

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = environment.BatchExecutablePath,
            Arguments = BuildUploadCommandLine(environment, outputFolderPath, resultPath, listenerLogPath, maxLogPath),
            WorkingDirectory = environment.WorkingDirectoryPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        process.Start();
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();

        if (!process.WaitForExit((int)TimeSpan.FromMinutes(10).TotalMilliseconds))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            throw new TimeoutException("3ds Max render smoke process timed out after 10 minutes.");
        }

        return new MaxBatchProcessRunResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = standardOutput,
            StandardError = standardError,
            ResultPath = resultPath,
            OutputFolderPath = outputFolderPath,
            ListenerLogPath = listenerLogPath,
            MaxLogPath = maxLogPath
        };
    }

    public static MaxBatchProcessRunResult RunUploadSmoke(MaxBatchUploadEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var rootFolderPath = Path.Combine(Path.GetTempPath(), $"OutWit.3dsMax.UploadSmoke.{Guid.NewGuid():N}");
        var outputFolderPath = Path.Combine(rootFolderPath, "output");
        var resultPath = Path.Combine(rootFolderPath, "result.txt");
        var listenerLogPath = Path.Combine(rootFolderPath, "listener.log");
        var maxLogPath = Path.Combine(rootFolderPath, "max.log");
        Directory.CreateDirectory(rootFolderPath);
        Directory.CreateDirectory(outputFolderPath);

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = environment.BatchExecutablePath,
            Arguments = BuildUploadCommandLine(environment, outputFolderPath, resultPath, listenerLogPath, maxLogPath),
            WorkingDirectory = environment.WorkingDirectoryPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        process.Start();
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();

        if (!process.WaitForExit((int)TimeSpan.FromMinutes(10).TotalMilliseconds))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            throw new TimeoutException("3ds Max upload smoke process timed out after 10 minutes.");
        }

        return new MaxBatchProcessRunResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = standardOutput,
            StandardError = standardError,
            ResultPath = resultPath,
            OutputFolderPath = outputFolderPath,
            ListenerLogPath = listenerLogPath,
            MaxLogPath = maxLogPath
        };
    }

    public static MaxBatchProcessRunResult RunLaunchPackageSmoke(MaxBatchSmokeEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var rootFolderPath = Path.Combine(Path.GetTempPath(), $"OutWit.3dsMax.LaunchPackageSmoke.{Guid.NewGuid():N}");
        var outputFolderPath = Path.Combine(rootFolderPath, "output");
        var resultPath = Path.Combine(rootFolderPath, "result.txt");
        var listenerLogPath = Path.Combine(rootFolderPath, "listener.log");
        var maxLogPath = Path.Combine(rootFolderPath, "max.log");
        Directory.CreateDirectory(rootFolderPath);
        Directory.CreateDirectory(outputFolderPath);

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = environment.BatchExecutablePath,
            Arguments = BuildCommandLine(environment, outputFolderPath, resultPath, listenerLogPath, maxLogPath),
            WorkingDirectory = environment.WorkingDirectoryPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        process.Start();
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();

        if (!process.WaitForExit((int)TimeSpan.FromMinutes(10).TotalMilliseconds))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            throw new TimeoutException("3ds Max launch-package smoke process timed out after 10 minutes.");
        }

        return new MaxBatchProcessRunResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = standardOutput,
            StandardError = standardError,
            ResultPath = resultPath,
            OutputFolderPath = outputFolderPath,
            ListenerLogPath = listenerLogPath,
            MaxLogPath = maxLogPath
        };
    }

    public static MaxBatchProcessRunResult RunInstalledPackageSmoke(MaxBatchInstalledPackageEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var rootFolderPath = Path.Combine(Path.GetTempPath(), $"OutWit.3dsMax.InstalledSmoke.{Guid.NewGuid():N}");
        var outputFolderPath = Path.Combine(rootFolderPath, "output");
        var resultPath = Path.Combine(rootFolderPath, "result.txt");
        var listenerLogPath = Path.Combine(rootFolderPath, "listener.log");
        var maxLogPath = Path.Combine(rootFolderPath, "max.log");
        Directory.CreateDirectory(rootFolderPath);
        Directory.CreateDirectory(outputFolderPath);

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = environment.BatchExecutablePath,
            Arguments = BuildInstalledPackageCommandLine(environment, outputFolderPath, resultPath, listenerLogPath, maxLogPath),
            WorkingDirectory = environment.WorkingDirectoryPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        process.Start();
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();

        if (!process.WaitForExit((int)TimeSpan.FromMinutes(10).TotalMilliseconds))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            throw new TimeoutException("3ds Max installed-package smoke process timed out after 10 minutes.");
        }

        return new MaxBatchProcessRunResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = standardOutput,
            StandardError = standardError,
            ResultPath = resultPath,
            OutputFolderPath = outputFolderPath,
            ListenerLogPath = listenerLogPath,
            MaxLogPath = maxLogPath
        };
    }

    public static void InstallPackage(MaxBatchInstalledPackageEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        RunPowerShellScript(environment.InstallScriptPath, $"-Configuration {QuotePowerShellArgument(environment.Configuration)} -TargetRoot {QuotePowerShellArgument(environment.ApplicationPluginsRootPath)}");
    }

    public static void UninstallPackage(MaxBatchInstalledPackageEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        RunPowerShellScript(environment.UninstallScriptPath, $"-TargetRoot {QuotePowerShellArgument(environment.ApplicationPluginsRootPath)}");
    }

    private static string BuildCommandLine(MaxBatchSmokeEnvironment environment, string outputFolderPath, string resultPath, string listenerLogPath, string maxLogPath)
    {
        return string.Join(' ',
        [
            Quote(NormalizeForMax(environment.ScriptPath)),
            "-sceneFile",
            Quote(NormalizeForMax(environment.ScenePath)),
            "-mxsString",
            Quote($"pluginAssembly:{NormalizeForMax(environment.PluginAssemblyPath)}"),
            "-mxsString",
            Quote($"scenePath:{NormalizeForMax(environment.ScenePath)}"),
            "-mxsString",
            Quote($"outputFolder:{NormalizeForMax(outputFolderPath)}"),
            "-mxsString",
            Quote($"resultPath:{NormalizeForMax(resultPath)}"),
            "-listenerlog",
            Quote(NormalizeForMax(listenerLogPath)),
            "-log",
            Quote(NormalizeForMax(maxLogPath)),
            "-v",
            "5"
        ]);
    }

    private static string BuildUploadCommandLine(MaxBatchUploadEnvironment environment, string outputFolderPath, string resultPath, string listenerLogPath, string maxLogPath)
    {
        return string.Join(' ',
        [
            Quote(NormalizeForMax(environment.ScriptPath)),
            "-sceneFile",
            Quote(NormalizeForMax(environment.ScenePath)),
            "-mxsString",
            Quote($"pluginAssembly:{NormalizeForMax(environment.PluginAssemblyPath)}"),
            "-mxsString",
            Quote($"scenePath:{NormalizeForMax(environment.ScenePath)}"),
            "-mxsString",
            Quote($"outputFolder:{NormalizeForMax(outputFolderPath)}"),
            "-mxsString",
            Quote($"resultPath:{NormalizeForMax(resultPath)}"),
            "-mxsString",
            Quote($"cloudUrl:{NormalizeForMax(environment.CloudUrl)}"),
            "-mxsString",
            Quote($"identityUrl:{NormalizeForMax(environment.IdentityUrl)}"),
            "-mxsString",
            Quote($"apiKey:{environment.ApiKey}"),
            "-listenerlog",
            Quote(NormalizeForMax(listenerLogPath)),
            "-log",
            Quote(NormalizeForMax(maxLogPath)),
            "-v",
            "5"
        ]);
    }

    private static string BuildInstalledPackageCommandLine(MaxBatchInstalledPackageEnvironment environment, string outputFolderPath, string resultPath, string listenerLogPath, string maxLogPath)
    {
        return string.Join(' ',
        [
            Quote(NormalizeForMax(environment.ScriptPath)),
            "-sceneFile",
            Quote(NormalizeForMax(environment.ScenePath)),
            "-mxsString",
            Quote($"scenePath:{NormalizeForMax(environment.ScenePath)}"),
            "-mxsString",
            Quote($"outputFolder:{NormalizeForMax(outputFolderPath)}"),
            "-mxsString",
            Quote($"resultPath:{NormalizeForMax(resultPath)}"),
            "-listenerlog",
            Quote(NormalizeForMax(listenerLogPath)),
            "-log",
            Quote(NormalizeForMax(maxLogPath)),
            "-v",
            "5"
        ]);
    }

    private static string? ResolveRepositoryRoot()
    {
        var currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);

        while (currentDirectory is not null)
        {
            if (File.Exists(Path.Combine(currentDirectory.FullName, "OutWit.slnx")))
                return currentDirectory.FullName;

            currentDirectory = currentDirectory.Parent;
        }

        return null;
    }

    private static string? ResolveBatchExecutablePath()
    {
        var overridePath = Environment.GetEnvironmentVariable(BATCH_EXECUTABLE_PATH_ENVIRONMENT_VARIABLE_NAME);
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
            return overridePath;

        foreach (var environmentVariableName in Environment.GetEnvironmentVariables().Keys.Cast<string>().Where(me => me.StartsWith("ADSK_3DSMAX_x64_", StringComparison.OrdinalIgnoreCase)).OrderByDescending(me => me, StringComparer.OrdinalIgnoreCase))
        {
            var environmentVariableValue = Environment.GetEnvironmentVariable(environmentVariableName);
            if (string.IsNullOrWhiteSpace(environmentVariableValue))
                continue;

            foreach (var candidatePath in new[]
            {
                Path.Combine(environmentVariableValue, "3dsmaxbatch.exe"),
                Path.Combine(environmentVariableValue, "3dsmaxcmd.exe"),
                Path.Combine(environmentVariableValue, "..", "3dsmaxbatch.exe"),
                Path.Combine(environmentVariableValue, "..", "3dsmaxcmd.exe")
            }.Select(Path.GetFullPath))
            {
                if (File.Exists(candidatePath))
                    return candidatePath;
            }
        }

        foreach (var version in SUPPORTED_3DS_MAX_VERSIONS)
        {
            var batchPath = Path.Combine(@"C:\Program Files\Autodesk", $"3ds Max {version}", "3dsmaxbatch.exe");
            if (File.Exists(batchPath))
                return batchPath;

            var cmdPath = Path.Combine(@"C:\Program Files\Autodesk", $"3ds Max {version}", "3dsmaxcmd.exe");
            if (File.Exists(cmdPath))
                return cmdPath;
        }

        return null;
    }

    private static string? ResolvePluginAssemblyPath(string repoRoot)
    {
        var overridePath = Environment.GetEnvironmentVariable(PLUGIN_ASSEMBLY_PATH_ENVIRONMENT_VARIABLE_NAME);
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
            return overridePath;

        var testDirectoryPluginPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "3dsmax-plugin", "OutWit.Render.3dsMax.Plugin.dll");
        if (File.Exists(testDirectoryPluginPath))
            return testDirectoryPluginPath;

        foreach (var candidatePath in new[]
        {
            Path.Combine(repoRoot, "OutWit.Render.3dsMax.Plugin", "bin", "x64", "Debug", "net10.0-windows", "OutWit.Render.3dsMax.Plugin.dll"),
            Path.Combine(repoRoot, "OutWit.Render.3dsMax.Plugin", "bin", "x64", "Release", "net10.0-windows", "OutWit.Render.3dsMax.Plugin.dll")
        })
        {
            if (File.Exists(candidatePath))
                return candidatePath;
        }

        return null;
    }

    private static string ResolveSmokeScenePath(string repoRoot, string sceneFileName)
    {
        if (string.IsNullOrWhiteSpace(sceneFileName))
            throw new InvalidOperationException("Smoke scene file name is required.");

        if (Path.IsPathRooted(sceneFileName))
            return sceneFileName;

        if (sceneFileName.Contains(Path.DirectorySeparatorChar) || sceneFileName.Contains(Path.AltDirectorySeparatorChar))
            return Path.Combine(repoRoot, "@Data", "3ds_max", sceneFileName);

        return Path.Combine(repoRoot, SMOKE_SCENE_ROOT_RELATIVE_PATH, sceneFileName);
    }

    private static string ResolveApplicationPluginsRootPath()
    {
        var overridePath = Environment.GetEnvironmentVariable(APPLICATION_PLUGINS_ROOT_ENVIRONMENT_VARIABLE_NAME);
        if (!string.IsNullOrWhiteSpace(overridePath))
            return overridePath;

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Autodesk", "ApplicationPlugins");
    }

    private static string ResolveOmnibusCloudUrl()
    {
        return Environment.GetEnvironmentVariable(OMNIBUSCLOUD_URL_ENVIRONMENT_VARIABLE_NAME) ?? DEFAULT_OMNIBUSCLOUD_URL;
    }

    private static string ResolveOmnibusCloudIdentityUrl()
    {
        return Environment.GetEnvironmentVariable(OMNIBUSCLOUD_IDENTITY_URL_ENVIRONMENT_VARIABLE_NAME) ?? DEFAULT_OMNIBUSCLOUD_IDENTITY_URL;
    }

    private static string ResolveOmnibusCloudApiKey()
    {
        return Environment.GetEnvironmentVariable(OMNIBUSCLOUD_API_KEY_ENVIRONMENT_VARIABLE_NAME) ?? DEFAULT_OMNIBUSCLOUD_API_KEY;
    }

    private static string ResolveConfiguration(string pluginAssemblyPath)
    {
        if (pluginAssemblyPath.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || pluginAssemblyPath.Contains(Path.AltDirectorySeparatorChar + "Release" + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return "Release";
        }

        return "Debug";
    }

    private static void RunPowerShellScript(string scriptPath, string arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File {QuotePowerShellArgument(scriptPath)} {arguments}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        process.Start();
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"PowerShell script '{scriptPath}' failed with exit code {process.ExitCode}.{Environment.NewLine}STDOUT:{Environment.NewLine}{standardOutput}{Environment.NewLine}STDERR:{Environment.NewLine}{standardError}");
        }
    }

    private static string NormalizeForMax(string path)
    {
        return path.Replace('\\', '/');
    }

    private static string Quote(string value)
    {
        return $"\"{value}\"";
    }

    private static string QuotePowerShellArgument(string value)
    {
        return $"\"{value.Replace("\"", "`\"")}\"";
    }

    #endregion
}
