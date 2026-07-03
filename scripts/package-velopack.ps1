param(
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "ScreenForge\ScreenForge.csproj"
$publishDir = Join-Path $root "publish"
$releaseDir = Join-Path $root "Releases"

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$csproj = Get-Content -Encoding UTF8 $project
    $Version = $csproj.Project.PropertyGroup.Version | Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "Version bulunamadı. -Version 1.2.3 ile çalıştırın."
}

dotnet publish $project -c Release --self-contained false -r win-x64 -o $publishDir

dotnet vpk pack `
    --packId ScreenForge `
    --packTitle ScreenForge `
    --packAuthors ScreenForge `
    --packVersion $Version `
    --packDir $publishDir `
    --mainExe ScreenForge.exe `
    --icon (Join-Path $root "ScreenForge\Resources\app.ico") `
    --outputDir $releaseDir `
    --runtime win-x64
