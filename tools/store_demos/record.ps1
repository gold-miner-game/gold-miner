param(
    [Parameter(Mandatory = $true)][string]$UnityEditor,
    [string]$CaptureProject
)
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (!$CaptureProject) {
    $CaptureProject = Join-Path ([IO.Path]::GetTempPath()) ('GoldMinerStoreDemos-' + [guid]::NewGuid().ToString('N'))
}
if (Test-Path -LiteralPath $CaptureProject) { throw 'Choose a new, empty capture-project path.' }
New-Item -ItemType Directory -Path $CaptureProject | Out-Null
foreach ($taskFolder in @('Assets', 'Packages', 'ProjectSettings')) {
    Copy-Item -LiteralPath (Join-Path $taskRoot $taskFolder) -Destination $CaptureProject -Recurse
}
$taskSettings = Join-Path $CaptureProject 'ProjectSettings/ProjectSettings.asset'
$taskText = Get-Content -LiteralPath $taskSettings -Raw
$taskText = $taskText -replace '(?m)^  productName:.*$', '  productName: GoldMinerStoreDemoCapture'
$taskText = $taskText -replace '(?m)^  companyName:.*$', '  companyName: GoldMinerCaptureOnly'
Set-Content -LiteralPath $taskSettings -Value $taskText
$taskAnalytics = Join-Path $CaptureProject 'Assets/Scripts/PlayAnalytics.cs'
$taskText = Get-Content -LiteralPath $taskAnalytics -Raw
$taskText = $taskText -replace 'const string FormUrl = "[^"]*";', 'const string FormUrl = "";'
Set-Content -LiteralPath $taskAnalytics -Value $taskText
$env:STORE_DEMO_OUTPUT = Join-Path $taskRoot 'Temp/StoreDemoCaptures'
$taskLog = Join-Path $CaptureProject 'capture.log'
$taskArgs = @('-batchmode', '-projectPath', ('"' + $CaptureProject + '"'),
    '-executeMethod', 'StoreDemoCaptureDriver.Begin', '-logFile', ('"' + $taskLog + '"'))
$taskProcess = Start-Process -FilePath $UnityEditor -ArgumentList $taskArgs -WindowStyle Hidden -PassThru -Wait
if ($taskProcess.ExitCode -ne 0) { throw "Capture failed; inspect $taskLog" }
Write-Output "Recorded clips: $env:STORE_DEMO_OUTPUT"
Write-Output "Capture project: $CaptureProject"
