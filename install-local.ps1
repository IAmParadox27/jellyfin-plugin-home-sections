# Local Installation Script for Testing
# Adjust paths as needed for your Jellyfin installation

param(
    [string]$JellyfinPluginsPath = "C:\ProgramData\Jellyfin\Server\plugins"
)

Write-Host "Building plugin..." -ForegroundColor Cyan
dotnet build src/Jellyfin.Plugin.HomeScreenSections.sln --configuration Release

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "Build successful!" -ForegroundColor Green

# Get version from DLL
$buildOutputDir = "src\Jellyfin.Plugin.HomeScreenSections\bin\Release\net9.0"
$dllPath = Join-Path $buildOutputDir "Jellyfin.Plugin.HomeScreenSections.dll"

if (-Not (Test-Path $dllPath)) {
    Write-Host "Built DLL not found at $dllPath" -ForegroundColor Red
    exit 1
}

$version = (Get-Item $dllPath).VersionInfo.FileVersion

# Use the requested output directory name (space-separated friendly name)
$pluginFolderName = "Home Screen Sections_$version"
$pluginFolder = Join-Path $JellyfinPluginsPath $pluginFolderName

Write-Host "Installing to: $pluginFolder" -ForegroundColor Cyan

# Remove old version if exists
if (Test-Path $pluginFolder) {
    Write-Host "Removing old version..." -ForegroundColor Yellow
    Remove-Item $pluginFolder -Recurse -Force
}

# Create plugin directory
New-Item -ItemType Directory -Force -Path $pluginFolder | Out-Null

# Copy entire build output (dll, resources, views etc.) to the plugin folder
Write-Host "Copying build output from $buildOutputDir to $pluginFolder" -ForegroundColor Cyan
# Resolve full build output path so we can compute relative paths correctly
$buildOutputFull = (Get-Item $buildOutputDir).FullName

Get-ChildItem -Path $buildOutputFull -Recurse | ForEach-Object {
    # Compute relative path from build output root
    $relativePath = $_.FullName.Substring($buildOutputFull.Length).TrimStart('\')
    $dest = Join-Path $pluginFolder $relativePath

    if ($_.PSIsContainer) {
        New-Item -ItemType Directory -Force -Path $dest | Out-Null
    }
    else {
        $destDir = Split-Path $dest -Parent
        if (-Not (Test-Path $destDir)) {
            New-Item -ItemType Directory -Force -Path $destDir | Out-Null
        }
        Copy-Item $_.FullName -Destination $dest -Force
    }
}
Write-Host "Plugin installed successfully!" -ForegroundColor Green

Write-Host "`nNext steps:" -ForegroundColor Yellow
Write-Host "1. Restart Jellyfin" -ForegroundColor White
Write-Host "2. Go to Dashboard > Plugins > Home Screen Sections" -ForegroundColor White
Write-Host "3. Test the Custom Discover tab" -ForegroundColor White
