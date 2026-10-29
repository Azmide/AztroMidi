# Builds AztroMidi into ./dist
#   .\build.ps1                -> small exe, needs the .NET 10 Desktop Runtime installed
#   .\build.ps1 -SelfContained -> bigger exe that runs on any Windows 10/11 x64 PC
param([switch]$SelfContained)

$ErrorActionPreference = "Stop"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"

$project = Join-Path $PSScriptRoot "src\AztroMidi\AztroMidi.csproj"
$out = Join-Path $PSScriptRoot "dist"

$publishArgs = @(
    "publish", $project,
    "-c", "Release",
    "-r", "win-x64",
    "-o", $out,
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true"
)
if ($SelfContained) {
    $publishArgs += @("--self-contained", "true", "-p:EnableCompressionInSingleFile=true")
} else {
    $publishArgs += @("--self-contained", "false")
}

dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

New-Item -ItemType Directory -Force (Join-Path $out "songs") | Out-Null
Write-Host ""
Write-Host "Done -> $out\AztroMidi.exe" -ForegroundColor Cyan
