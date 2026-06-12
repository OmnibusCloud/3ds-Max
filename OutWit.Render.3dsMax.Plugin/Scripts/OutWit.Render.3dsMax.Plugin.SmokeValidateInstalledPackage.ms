fn outwitInstalledFail message resultPath =
(
    format "OutWit installed-package smoke failed: %\n" message

    if resultPath != undefined and resultPath != "" and doesFileExist resultPath == false do
    (
        local writer = createFile resultPath
        format "Success=false\n" to:writer
        format "Message=%\n" message to:writer
        close writer
    )

    throw message
)

scenePath = maxOps.mxsCmdLineArgs[#scenePath]
outputFolder = maxOps.mxsCmdLineArgs[#outputFolder]
resultPath = maxOps.mxsCmdLineArgs[#resultPath]

format "OutWit installed-package smoke args | scenePath=% | outputFolder=% | resultPath=%\n" scenePath outputFolder resultPath

if scenePath == undefined or scenePath == "" do outwitInstalledFail "MAXScript command line argument 'scenePath' is required." resultPath
if outputFolder == undefined or outputFolder == "" do outwitInstalledFail "MAXScript command line argument 'outputFolder' is required." resultPath
if resultPath == undefined or resultPath == "" do outwitInstalledFail "MAXScript command line argument 'resultPath' is required." resultPath
if doesFileExist scenePath == false do outwitInstalledFail ("Scene file was not found: " + scenePath) resultPath
if OutWit_3dsMax_EnsurePluginLoaded == undefined do outwitInstalledFail "OutWit package startup script is not loaded. Verify the ApplicationPlugins package is installed and 3ds Max was restarted." resultPath

makeDir outputFolder all:true

try
(
    loadMaxFile scenePath quiet:true useFileUnits:true
    OutWit_3dsMax_EnsurePluginLoaded()

    local commandService = dotNetObject "OutWit.Render.ThreeDsMax.Plugin.Services.MaxPluginCommandService"
    local exportService = commandService.CreateExportService()
    local exportFormat = dotNetClass "OutWit.Render.ThreeDsMax.Plugin.Export.Models.MaxSceneExportOutputFormat"
    local jsonResult = exportService.ExportCurrentScene outputFolder exportFormat.Json
    local memoryPackResult = exportService.ExportCurrentScene outputFolder exportFormat.MemoryPack
    local jsonGzipResult = exportService.ExportCurrentScene outputFolder exportFormat.JsonGzip
    local memoryPackGzipResult = exportService.ExportCurrentScene outputFolder exportFormat.MemoryPackGzip
    local jsonSize = if jsonResult.OutputPath != undefined and doesFileExist jsonResult.OutputPath then getFileSize jsonResult.OutputPath else 0
    local memoryPackSize = if memoryPackResult.OutputPath != undefined and doesFileExist memoryPackResult.OutputPath then getFileSize memoryPackResult.OutputPath else 0
    local jsonGzipSize = if jsonGzipResult.OutputPath != undefined and doesFileExist jsonGzipResult.OutputPath then getFileSize jsonGzipResult.OutputPath else 0
    local memoryPackGzipSize = if memoryPackGzipResult.OutputPath != undefined and doesFileExist memoryPackGzipResult.OutputPath then getFileSize memoryPackGzipResult.OutputPath else 0
    local memoryPackPercent = if jsonSize > 0 then (100.0 * memoryPackSize / jsonSize) else 0
    local memoryPackGzipPercent = if jsonGzipSize > 0 then (100.0 * memoryPackGzipSize / jsonGzipSize) else 0

    local writer = createFile resultPath
    format "Success=%\n" (jsonResult.IsSuccess and memoryPackResult.IsSuccess and jsonGzipResult.IsSuccess and memoryPackGzipResult.IsSuccess) to:writer
    format "StatusText=%\n" jsonResult.StatusText to:writer
    format "PackageStartupLoaded=true\n" to:writer

    if jsonResult.OutputPath != undefined do
        format "JsonOutputPath=%\n" jsonResult.OutputPath to:writer

    if memoryPackResult.OutputPath != undefined do
        format "MemoryPackOutputPath=%\n" memoryPackResult.OutputPath to:writer

    if jsonGzipResult.OutputPath != undefined do
        format "JsonGzipOutputPath=%\n" jsonGzipResult.OutputPath to:writer

    if memoryPackGzipResult.OutputPath != undefined do
        format "MemoryPackGzipOutputPath=%\n" memoryPackGzipResult.OutputPath to:writer

    format "JsonBytes=%\n" jsonSize to:writer
    format "MemoryPackBytes=%\n" memoryPackSize to:writer
    format "MemoryPackPercentOfJson=%\n" memoryPackPercent to:writer
    format "SizeSummary=MemoryPack is % percent of JSON size (% bytes vs % bytes).\n" memoryPackPercent memoryPackSize jsonSize to:writer
    format "JsonGzipBytes=%\n" jsonGzipSize to:writer
    format "MemoryPackGzipBytes=%\n" memoryPackGzipSize to:writer
    format "MemoryPackGzipPercentOfJsonGzip=%\n" memoryPackGzipPercent to:writer
    format "CompressedSizeSummary=MemoryPack.gz is % percent of JSON.gz size (% bytes vs % bytes).\n" memoryPackGzipPercent memoryPackGzipSize jsonGzipSize to:writer
    format "DiagnosticsCount=%\n" jsonResult.Diagnostics.Count to:writer

    for i = 0 to (jsonResult.Diagnostics.Count - 1) do
    (
        local diagnostic = jsonResult.Diagnostics.Item[i]
        format "Diagnostic[%].Severity=%\n" i diagnostic.Severity to:writer
        format "Diagnostic[%].Message=%\n" i diagnostic.Message to:writer
    )

    close writer

    if jsonResult.IsSuccess == false do
        throw jsonResult.StatusText

    if memoryPackResult.IsSuccess == false do
        throw memoryPackResult.StatusText

    if jsonGzipResult.IsSuccess == false do
        throw jsonGzipResult.StatusText

    if memoryPackGzipResult.IsSuccess == false do
        throw memoryPackGzipResult.StatusText
)
catch
(
    outwitInstalledFail (getCurrentException()) resultPath
)
