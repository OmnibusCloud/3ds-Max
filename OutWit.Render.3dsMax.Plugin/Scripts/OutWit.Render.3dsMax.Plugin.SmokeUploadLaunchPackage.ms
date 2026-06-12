fn outwitUploadSmokeFail message resultPath =
(
    format "OutWit upload smoke failed: %\n" message

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
cloudUrl = maxOps.mxsCmdLineArgs[#cloudUrl]
identityUrl = maxOps.mxsCmdLineArgs[#identityUrl]
apiKey = maxOps.mxsCmdLineArgs[#apiKey]

format "OutWit upload smoke args | pluginAssembly=% | scenePath=% | outputFolder=% | resultPath=% | cloudUrl=% | identityUrl=%\n" pluginAssemblyPath scenePath outputFolder resultPath cloudUrl identityUrl

if pluginAssemblyPath == undefined or pluginAssemblyPath == "" do outwitUploadSmokeFail "MAXScript command line argument 'pluginAssembly' is required." resultPath
if outputFolder == undefined or outputFolder == "" do outwitUploadSmokeFail "MAXScript command line argument 'outputFolder' is required." resultPath
if resultPath == undefined or resultPath == "" do outwitUploadSmokeFail "MAXScript command line argument 'resultPath' is required." resultPath
if scenePath == undefined or scenePath == "" do outwitUploadSmokeFail "MAXScript command line argument 'scenePath' is required." resultPath
if cloudUrl == undefined or cloudUrl == "" do outwitUploadSmokeFail "MAXScript command line argument 'cloudUrl' is required." resultPath
if identityUrl == undefined or identityUrl == "" do outwitUploadSmokeFail "MAXScript command line argument 'identityUrl' is required." resultPath
if apiKey == undefined or apiKey == "" do outwitUploadSmokeFail "MAXScript command line argument 'apiKey' is required." resultPath
if doesFileExist pluginAssemblyPath == false do outwitUploadSmokeFail ("Plugin assembly was not found: " + pluginAssemblyPath) resultPath
if doesFileExist scenePath == false do outwitUploadSmokeFail ("Scene file was not found: " + scenePath) resultPath

makeDir outputFolder all:true

try
(
    loadMaxFile scenePath quiet:true useFileUnits:true
    dotNet.loadAssembly pluginAssemblyPath

    local commandService = dotNetObject "OutWit.Render.ThreeDsMax.Plugin.Services.MaxPluginCommandService"
    local launchPreparationService = commandService.CreateLaunchPreparationService()
    local uploadService = commandService.CreateConnectedRenderPackageUploadService()

    local request = dotNetObject "OutWit.Render.ThreeDsMax.Plugin.Export.Models.MaxSceneLaunchPackageRequest"
    request.CloudUrl = cloudUrl
    request.IdentityUrl = identityUrl
    request.RenderMode = "RenderStill"
    request.ResolutionX = 1920
    request.ResolutionY = 1080
    request.FrameStart = 1
    request.FrameEnd = 1
    request.Samples = 64
    request.SelectedGroupName = "Artists"
    request.OutputFolder = outputFolder

    local packageResult = launchPreparationService.Prepare request
    if packageResult.IsSuccess == false do
        throw packageResult.StatusText

    local jobState = dotNetObject "OutWit.Render.ThreeDsMax.Plugin.Export.Models.MaxConnectedRenderJobState"
    jobState.JobId = packageResult.PackageId
    jobState.StatusText = packageResult.StatusText
    jobState.PackageFolderPath = packageResult.PackageFolderPath
    jobState.ManifestPath = packageResult.ManifestPath
    jobState.PackageArchivePath = packageResult.PackageArchivePath
    jobState.PrimaryArtifactPath = packageResult.PrimaryArtifactPath
    jobState.SubmittedUtc = (dotNetClass "System.DateTime").UtcNow
    jobState.UpdatedUtc = jobState.SubmittedUtc

    local uploadRequest = dotNetObject "OutWit.Render.ThreeDsMax.Plugin.Export.Models.MaxConnectedRenderUploadRequest"
    uploadRequest.CloudUrl = cloudUrl
    uploadRequest.IdentityUrl = identityUrl
    uploadRequest.ApiKey = apiKey
    uploadRequest.PackageArchivePath = packageResult.PackageArchivePath

    local uploadResult = uploadService.Upload jobState uploadRequest
    local writer = createFile resultPath
    format "Success=%\n" uploadResult.IsSuccess to:writer
    format "StatusText=%\n" uploadResult.StatusText to:writer
    format "PackageId=%\n" packageResult.PackageId to:writer
    format "PackageFolderPath=%\n" packageResult.PackageFolderPath to:writer
    format "ManifestPath=%\n" packageResult.ManifestPath to:writer
    format "PackageArchivePath=%\n" packageResult.PackageArchivePath to:writer
    format "PrimaryArtifactPath=%\n" packageResult.PrimaryArtifactPath to:writer
    format "UploadedBlobId=%\n" uploadResult.UploadedBlobId to:writer
    format "UploadReceiptPath=%\n" uploadResult.UploadReceiptPath to:writer
    format "DiagnosticsCount=%\n" uploadResult.Diagnostics.Count to:writer

    for i = 0 to (uploadResult.Diagnostics.Count - 1) do
    (
        local diagnostic = uploadResult.Diagnostics.Item[i]
        format "Diagnostic[%].Severity=%\n" i diagnostic.Severity to:writer
        format "Diagnostic[%].Message=%\n" i diagnostic.Message to:writer
    )

    close writer

    if uploadResult.IsSuccess == false do
        throw uploadResult.StatusText
)
catch
(
    outwitUploadSmokeFail (getCurrentException()) resultPath
)
