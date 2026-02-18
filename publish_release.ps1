# Simple IP Scanner - Release Script
# This script builds the app, generates a security hash, and prepares a release folder.

$ProjectDir = $PSScriptRoot
$PublishDir = "$ProjectDir\bin\Release\Publish"
$ExeName = "SimpleIPScanner.exe"

Write-Host "--- Starting Build Process ---" -ForegroundColor Cyan

# 1. Clean previous publish
if (Test-Path $PublishDir) { Remove-Item -Recurse -Force $PublishDir }

# 2. Build and Publish as Single File
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $PublishDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed!"
    exit $LASTEXITCODE
}

# 3. Generate SHA-256 Hash for security
Write-Host "--- Generating Security Hash ---" -ForegroundColor Cyan
$ExePath = "$PublishDir\$ExeName"
$HashResult = Get-FileHash -Path $ExePath -Algorithm SHA256
$Hash = $HashResult.Hash

# 4. Save Hash to file
$HashFile = "$PublishDir\SHA256SUM.txt"
Set-Content -Path $HashFile -Value "$Hash  $ExeName"

Write-Host "`nSuccessfully Published to: $PublishDir" -ForegroundColor Green
Write-Host "Executable SHA-256 Hash:" -ForegroundColor White
Write-Host $Hash -ForegroundColor Yellow
Write-Host "`nYou can now upload both '$ExeName' and 'SHA256SUM.txt' to GitHub Releases." -ForegroundColor Cyan
