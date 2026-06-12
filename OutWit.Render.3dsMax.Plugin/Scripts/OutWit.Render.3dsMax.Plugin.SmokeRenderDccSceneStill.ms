fn outwitRenderSmokeFail message resultPath =
(
    format "OutWit render smoke failed: %\n" message

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

format "OutWit render smoke args | pluginAssembly=% | scenePath=% | outputFolder=% | resultPath=% | cloudUrl=% | identityUrl=%\n" pluginAssemblyPath scenePath outputFolder resultPath cloudUrl identityUrl

if pluginAssemblyPath == undefined or pluginAssemblyPath == "" do outwitRenderSmokeFail "MAXScript command line argument 'pluginAssembly' is required." resultPath
if outputFolder == undefined or outputFolder == "" do outwitRenderSmokeFail "MAXScript command line argument 'outputFolder' is required." resultPath
if resultPath == undefined or resultPath == "" do outwitRenderSmokeFail "MAXScript command line argument 'resultPath' is required." resultPath
if scenePath == undefined or scenePath == "" do outwitRenderSmokeFail "MAXScript command line argument 'scenePath' is required." resultPath
if cloudUrl == undefined or cloudUrl == "" do outwitRenderSmokeFail "MAXScript command line argument 'cloudUrl' is required." resultPath
if identityUrl == undefined or identityUrl == "" do outwitRenderSmokeFail "MAXScript command line argument 'identityUrl' is required." resultPath
if apiKey == undefined or apiKey == "" do outwitRenderSmokeFail "MAXScript command line argument 'apiKey' is required." resultPath
if doesFileExist pluginAssemblyPath == false do outwitRenderSmokeFail ("Plugin assembly was not found: " + pluginAssemblyPath) resultPath
if doesFileExist scenePath == false do outwitRenderSmokeFail ("Scene file was not found: " + scenePath) resultPath

makeDir outputFolder all:true

try
(
    loadMaxFile scenePath quiet:true useFileUnits:true
    dotNet.loadAssembly pluginAssemblyPath

    local commandService = dotNetObject "OutWit.Render.ThreeDsMax.Plugin.Services.MaxPluginCommandService"
    local smokeService = commandService.CreateConnectedRenderLiveSmokeService()
    local smokeResult = smokeService.RunRenderDccSceneStill cloudUrl identityUrl apiKey outputFolder

    local writer = createFile resultPath
    format "Success=%\n" smokeResult.IsSuccess to:writer
    format "StatusText=%\n" smokeResult.StatusText to:writer
    format "JobId=%\n" smokeResult.JobId to:writer
    format "FinalJobStatus=%\n" smokeResult.FinalJobStatus to:writer
    format "OverallProgress=%\n" smokeResult.OverallProgress to:writer
    format "ResultBlobId=%\n" (smokeResult.ResultBlobId.ToString()) to:writer
    format "DownloadedFilePath=%\n" smokeResult.DownloadedFilePath to:writer
    format "TraceLogPath=%\n" smokeResult.TraceLogPath to:writer
    format "ErrorMessage=%\n" smokeResult.ErrorMessage to:writer
    format "DiagnosticsCount=%\n" smokeResult.Diagnostics.Count to:writer

    for i = 0 to (smokeResult.Diagnostics.Count - 1) do
    (
        local diagnostic = smokeResult.Diagnostics.Item[i]
        format "Diagnostic[%].Severity=%\n" i (diagnostic.Severity.ToString()) to:writer
        format "Diagnostic[%].Message=%\n" i diagnostic.Message to:writer
    )

    close writer

    if smokeResult.IsSuccess == false do
        throw smokeResult.StatusText
)
catch
(
    outwitRenderSmokeFail (getCurrentException()) resultPath
)
