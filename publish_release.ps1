# Simple IP Scanner — Velopack Release Script
# Usage: .\publish_release.ps1 -Version "1.4.0" -GitHubToken "ghp_xxxx"
#
# Prerequisites (one-time setup):
#   dotnet tool install -g vpk

param(
    [Parameter(Mandatory=$true)]
    [string]$Version,       # e.g. "1.4.0"

    [Parameter(Mandatory=$true)]
    [string]$GitHubToken    # GitHub PAT with repo scope
)

$ProjectDir = $PSScriptRoot
$PublishDir = "$ProjectDir\bin\Release\Publish"
$PackageDir = "$ProjectDir\bin\Release\VelopackOutput"
$RepoUrl    = "https://github.com/ChaseCorbin/SimpleIPScanner"

Write-Host "--- Cleaning previous output ---" -ForegroundColor Cyan
if (Test-Path $PublishDir) { Remove-Item -Recurse -Force $PublishDir }
if (Test-Path $PackageDir) { Remove-Item -Recurse -Force $PackageDir }

Write-Host "--- Building and publishing ---" -ForegroundColor Cyan
dotnet publish "$ProjectDir\SimpleIPScanner.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained `
    -p:PublishSingleFile=false `
    -p:Version=$Version `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) { Write-Error "dotnet publish failed"; exit 1 }

Write-Host "--- Packaging with vpk ---" -ForegroundColor Cyan
vpk pack `
    --packId SimpleIPScanner `
    --packVersion $Version `
    --packDir $PublishDir `
    --mainExe SimpleIPScanner.exe `
    --outputDir $PackageDir

if ($LASTEXITCODE -ne 0) { Write-Error "vpk pack failed"; exit 1 }

Write-Host "--- Uploading to GitHub Releases ---" -ForegroundColor Cyan
vpk upload github `
    --repoUrl $RepoUrl `
    --publish `
    --releaseName "v$Version" `
    --tag "v$Version" `
    --token $GitHubToken `
    --outputDir $PackageDir

if ($LASTEXITCODE -ne 0) { Write-Error "GitHub upload failed"; exit 1 }

Write-Host "`nRelease v$Version published successfully." -ForegroundColor Green
Write-Host "Users can install from SimpleIPScanner-Setup.exe in the GitHub release." -ForegroundColor Gray
