fn outwitLaunchPackageFail message resultPath =
(
    format "OutWit launch-package smoke failed: %\n" message

    if resultPath != undefined and resultPath != "" and doesFileExist resultPath == false do
    (
        local writer = createFile resultPath
        format "Success=false\n" to:writer
        format "Message=%\n" message to:writer
        close writer
    )

    throw message
)

pluginAssemblyPath = maxOps.mxsCmdLineArgs[#pluginAssembly]
scenePath = maxOps.mxsCmdLineArgs[#scenePath]
outputFolder = maxOps.mxsCmdLineArgs[#outputFolder]
resultPath = maxOps.mxsCmdLineArgs[#resultPath]

format "OutWit launch-package smoke args | pluginAssembly=% | scenePath=% | outputFolder=% | resultPath=%\n" pluginAssemblyPath scenePath outputFolder resultPath

if pluginAssemblyPath == undefined or pluginAssemblyPath == "" do outwitLaunchPackageFail "MAXScript command line argument 'pluginAssembly' is required." resultPath
if outputFolder == undefined or outputFolder == "" do outwitLaunchPackageFail "MAXScript command line argument 'outputFolder' is required." resultPath
if resultPath == undefined or resultPath == "" do outwitLaunchPackageFail "MAXScript command line argument 'resultPath' is required." resultPath
if scenePath == undefined or scenePath == "" do outwitLaunchPackageFail "MAXScript command line argument 'scenePath' is required." resultPath
if doesFileExist pluginAssemblyPath == false do outwitLaunchPackageFail ("Plugin assembly was not found: " + pluginAssemblyPath) resultPath
if doesFileExist scenePath == false do outwitLaunchPackageFail ("Scene file was not found: " + scenePath) resultPath

makeDir outputFolder all:true

try
(
    loadMaxFile scenePath quiet:true useFileUnits:true
    dotNet.loadAssembly pluginAssemblyPath

    local commandService = dotNetObject "OutWit.Render.ThreeDsMax.Plugin.Services.MaxPluginCommandService"
    local launchPreparationService = commandService.CreateLaunchPreparationService()
    local request = dotNetObject "OutWit.Render.ThreeDsMax.Plugin.Export.Models.MaxSceneLaunchPackageRequest"
    request.CloudUrl = "https://omnibuscloud.local"
    request.IdentityUrl = "https://identity.omnibuscloud.local"
    request.RenderMode = "RenderStill"
    request.ResolutionX = 1920
    request.ResolutionY = 1080
    request.FrameStart = 1
    request.FrameEnd = 1
    request.Samples = 64
    request.SelectedGroupName = "Artists"
    request.OutputFolder = outputFolder

    local packageResult = launchPreparationService.Prepare request
    local writer = createFile resultPath
    format "Success=%\n" packageResult.IsSuccess to:writer
    format "StatusText=%\n" packageResult.StatusText to:writer
    format "PackageId=%\n" packageResult.PackageId to:writer
    format "PackageFolderPath=%\n" packageResult.PackageFolderPath to:writer
    format "ManifestPath=%\n" packageResult.ManifestPath to:writer
    format "PackageArchivePath=%\n" packageResult.PackageArchivePath to:writer
    format "PrimaryArtifactPath=%\n" packageResult.PrimaryArtifactPath to:writer
    format "DiagnosticsCount=%\n" packageResult.Diagnostics.Count to:writer

    for i = 0 to (packageResult.Diagnostics.Count - 1) do
    (
        local diagnostic = packageResult.Diagnostics.Item[i]
        format "Diagnostic[%].Severity=%\n" i diagnostic.Severity to:writer
        format "Diagnostic[%].Message=%\n" i diagnostic.Message to:writer
    )

    close writer

    if packageResult.IsSuccess == false do
        throw packageResult.StatusText
)
catch
(
    outwitLaunchPackageFail (getCurrentException()) resultPath
)
