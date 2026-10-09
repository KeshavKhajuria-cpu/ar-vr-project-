$ErrorActionPreference = "Stop"

$unityExe = "C:\Program Files\Unity\Hub\Editor\6000.5.5f1\Editor\Unity.exe"
$projectDir = "C:\Users\K6006\OneDrive\Documents\desktop\ar vr project\ar projext\ar project"

# Setup short virtual drive P: to eliminate Windows MAX_PATH (260 chars) limitation in Gradle
if (!(Test-Path "P:\")) {
    subst P: $projectDir
}

if (Test-Path "P:\Temp\UnityLockfile") {
    Remove-Item -Force "P:\Temp\UnityLockfile" -ErrorAction SilentlyContinue
}

Write-Host "Starting Unity Build with projectPath 'P:\'..."
$argsList = @(
    "-batchmode",
    "-quit",
    "-projectPath", "P:\",
    "-executeMethod", "ARProject.EditorTools.ARProjectBuilder.BuildAndroidAPK",
    "-logFile", "P:\unity_build.log"
)

$process = Start-Process -FilePath $unityExe -ArgumentList $argsList -PassThru -NoNewWindow
Write-Host "Unity process started with PID: $($process.Id)"
$process.WaitForExit()
Write-Host "Unity exited with code: $($process.ExitCode)"

if (Test-Path "P:\Builds\ARProject.apk") {
    $apk = Get-Item "P:\Builds\ARProject.apk"
    Write-Host "SUCCESS: APK Generated at $($apk.FullName)"
    Write-Host "APK LastWriteTime: $($apk.LastWriteTime)"
    Write-Host "APK Size: $([Math]::Round($apk.Length / 1MB, 2)) MB"
}
