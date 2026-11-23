# Screenshots of real game screens for art review, without touching the real project's saves
# or sending analytics: mirrors the project into a separate capture copy (product name changed,
# analytics FormUrl emptied), builds a Windows player there, and runs it with ArtCaptureRunner.
#   powershell -File tools/art_capture/capture.ps1 -Scenes "Menu,Select,level_01"
param(
    [string]$UnityEditor = 'E:\unity\6000.3.22f1\Editor\Unity.exe',
    [string]$Work = (Join-Path $PSScriptRoot '..\..\..\_art_capture'),
    [string]$Scenes = 'Menu,world_map,Select,Store,Survival,level_01,level_02,level_04',
    [string]$Out,
    [double]$Wait = 3,
    [int]$Frames = 0,          # also save this many 12 fps frames per scene (motion check)
    [switch]$Peel,             # press the menu's main button before the Menu frames
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
New-Item -ItemType Directory -Force -Path $Work | Out-Null
$Work = (Resolve-Path $Work).Path
$project = Join-Path $Work 'project'
$exe = Join-Path $Work 'build\GoldMinerArtCapture.exe'
if (!$Out) { $Out = Join-Path $Work ('shots\' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }

if (!$SkipBuild) {
    foreach ($folder in 'Assets', 'Packages', 'ProjectSettings') {
        robocopy (Join-Path $root $folder) (Join-Path $project $folder) /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "robocopy $folder failed ($LASTEXITCODE)" }
    }
    $capture = Join-Path $project 'Assets\ArtCapture'
    New-Item -ItemType Directory -Force -Path (Join-Path $capture 'Editor') | Out-Null
    Copy-Item (Join-Path $PSScriptRoot 'ArtCaptureRunner.cs') $capture
    Copy-Item (Join-Path $PSScriptRoot 'ArtCaptureBuild.cs') (Join-Path $capture 'Editor')

    $settings = Join-Path $project 'ProjectSettings\ProjectSettings.asset'
    $text = Get-Content -LiteralPath $settings -Raw
    $text = $text -replace '(?m)^  productName:.*$', '  productName: GoldMinerArtCapture'
    $text = $text -replace '(?m)^  companyName:.*$', '  companyName: GoldMinerCaptureOnly'
    Set-Content -LiteralPath $settings -Value $text -NoNewline
    $analytics = Join-Path $project 'Assets\Scripts\PlayAnalytics.cs'
    $text = Get-Content -LiteralPath $analytics -Raw
    $text = $text -replace 'const string FormUrl = "[^"]*";', 'const string FormUrl = "";'
    Set-Content -LiteralPath $analytics -Value $text -NoNewline

    $env:ART_CAPTURE_EXE = $exe
    $log = Join-Path $Work 'build.log'
    $p = Start-Process -FilePath $UnityEditor -ArgumentList @('-batchmode', '-quit', '-projectPath', ('"' + $project + '"'),
        '-executeMethod', 'ArtCaptureBuild.Build', '-logFile', ('"' + $log + '"')) -WindowStyle Hidden -PassThru
    $p.WaitForExit()   # not -Wait: that also waits for Unity's helper processes, which outlive it
    if ($p.ExitCode -ne 0) { throw "Build failed; see $log" }
}

New-Item -ItemType Directory -Force -Path $Out | Out-Null
$playerLog = Join-Path $Out 'player.log'
$playerArgs = @('-screen-fullscreen', '0', '-screen-width', '1600', '-screen-height', '900',
    '-artcapture', ('"' + $Out + '"'), '-artscenes', $Scenes, '-artwait', $Wait, '-artframes', $Frames,
    '-logFile', ('"' + $playerLog + '"'))
if ($Peel) { $playerArgs += '-artpeel' }
$p = Start-Process -FilePath $exe -ArgumentList $playerArgs -PassThru
if (!$p.WaitForExit(600000)) { $p.Kill(); throw "Player timed out; see $playerLog" }
Write-Output "Shots: $Out (exit $($p.ExitCode))"
