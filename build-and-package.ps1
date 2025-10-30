# Build and Package WgWrap Portable Release
# This script builds the application in Release mode, publishes it as a self-contained portable app,
# and creates a ZIP file excluding appsettings.json and the data directory.

param(
    [string]$OutputZip = "WgWrap-Portable.zip"
)

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $projectRoot

# Get version from csproj
$csprojPath = "$projectRoot\WgWrap\WgWrap.csproj"
[xml]$csproj = Get-Content $csprojPath
$version = $csproj.Project.PropertyGroup.Version

# If default name, append version
if ($OutputZip -eq "WgWrap-Portable.zip") {
    $OutputZip = "WgWrap-Portable-v$version.zip"
}

# Create Publish folder
$publishFolder = Join-Path $projectRoot "Publish"
if (!(Test-Path $publishFolder)) {
    New-Item -ItemType Directory -Path $publishFolder -Force | Out-Null
}

Write-Host "Building WgWrap Release..." -ForegroundColor Green

# Clean previous builds
if (Test-Path "bin") {
    Remove-Item "bin" -Recurse -Force
}

# Build the project
dotnet build -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed!"
    exit 1
}

# Publish as self-contained portable app
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed!"
    exit 1
}

$publishDir = "$projectRoot\WgWrap\bin\Release\net9.0-windows\win-x64\publish"
$tempDir = Join-Path $projectRoot "temp_publish"

Write-Host "Creating portable ZIP package..." -ForegroundColor Green

# Check if publish directory exists
if (!(Test-Path $publishDir)) {
    Write-Error "Publish directory not found: $publishDir"
    exit 1
}

# Create temp directory for filtered files
if (Test-Path $tempDir) {
    Remove-Item $tempDir -Recurse -Force
}
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

# Copy all files from publish directory, excluding appsettings.json and data directory
Get-ChildItem $publishDir -Recurse | Where-Object {
    $_.FullName -notlike "*\appsettings.json" -and
    $_.FullName -notlike "*\data\*"
} | ForEach-Object {
    $relativePath = $_.FullName.Substring($publishDir.Length + 1)
    $destPath = Join-Path $tempDir $relativePath

    if ($_.PSIsContainer) {
        # Create directory
        if (!(Test-Path $destPath)) {
            New-Item -ItemType Directory -Path $destPath -Force | Out-Null
        }
    } else {
        # Copy file
        $destDir = Split-Path $destPath -Parent
        if (!(Test-Path $destDir)) {
            New-Item -ItemType Directory -Path $destDir -Force | Out-Null
        }
        Copy-Item $_.FullName $destPath -Force
    }
}

# Create ZIP file
$outputZipPath = Join-Path $publishFolder $OutputZip
if (Test-Path $outputZipPath) {
    Remove-Item $outputZipPath -Force
}
Compress-Archive -Path "$tempDir\*" -DestinationPath $outputZipPath

# Clean up temp directory
Remove-Item $tempDir -Recurse -Force

Write-Host "Portable ZIP package created: $outputZipPath" -ForegroundColor Green
Write-Host "Package excludes: appsettings.json and data directory contents" -ForegroundColor Yellow
Write-Host "ZIP size: $((Get-Item $outputZipPath).Length / 1MB) MB" -ForegroundColor Cyan
