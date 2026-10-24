<#
.SYNOPSIS
    Builds OpIlGen as a single .exe (framework-dependent) ready for a GitHub release.

.DESCRIPTION
    Publishes the project as a single-file executable WITHOUT the bundled .NET runtime.
    Users who do not have the .NET 10 Desktop Runtime installed must download it separately.
    The output goes to ./publish: OpIlGen.exe and a zip archive with it.
    Optionally uploads the archive to GitHub Releases through the `gh` CLI.

.PARAMETER Version
    Version used for the archive name, the assembly version and the release tag (v<Version>).
    Default: 1.0.0

.PARAMETER Runtime
    Target runtime identifier. Default: win-x64 (use win-arm64 for ARM).

.PARAMETER Release
    After building, create a GitHub release v<Version> and upload the zip (requires `gh auth login`).

.PARAMETER Draft
    With -Release: create the release as a draft so you can review it on GitHub first.

.EXAMPLE
    .\deploy.ps1
.EXAMPLE
    .\deploy.ps1 -Version 1.2.0 -Release -Draft
#>
[CmdletBinding()]
param(
    [string]$Version = '1.0.0',
    [string]$Runtime = 'win-x64',
    [switch]$Release,
    [switch]$Draft
)

$ErrorActionPreference = 'Stop'
Set-Location -Path $PSScriptRoot

$project    = Join-Path $PSScriptRoot 'OpIlGen.csproj'
$publishDir = Join-Path $PSScriptRoot 'publish'
$zipName    = "OpIlGen-$Version-$Runtime.zip"
$zipPath    = Join-Path $publishDir $zipName

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK was not found. Install the .NET 10 SDK: https://dotnet.microsoft.com/download'
}

Write-Host "==> Cleaning $publishDir" -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

Write-Host "==> Publishing OpIlGen $Version ($Runtime, single file, framework-dependent)" -ForegroundColor Cyan
dotnet publish $project `
    --configuration Release `
    --runtime $Runtime `
    --self-contained false `
    --output $publishDir `
    -p:PublishSingleFile=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -p:Version=$Version
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

$exe = Join-Path $publishDir 'OpIlGen.exe'
if (-not (Test-Path $exe)) { throw "OpIlGen.exe was not produced in $publishDir" }

Write-Host "==> Creating archive $zipName" -ForegroundColor Cyan
Compress-Archive -Path $exe -DestinationPath $zipPath -Force

$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 2)
Write-Host ''
Write-Host "Done." -ForegroundColor Green
Write-Host "  EXE: $exe ($sizeMb MB)"
Write-Host "  ZIP: $zipPath"
Write-Host '  Requires the .NET 10 Desktop Runtime on the user machine: https://dotnet.microsoft.com/download/dotnet/10.0'

if ($Release) {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw 'GitHub CLI (gh) was not found. Install it from https://cli.github.com and run `gh auth login`.'
    }

    $tag  = "v$Version"
    $ghArgs = @('release', 'create', $tag, $zipPath, $exe,
              '--title', "OpIlGen $Version",
              '--notes', 'Requires the .NET 10 Desktop Runtime: https://dotnet.microsoft.com/download/dotnet/10.0')
    if ($Draft) { $ghArgs += '--draft' }

    Write-Host "==> Creating GitHub release $tag" -ForegroundColor Cyan
    & gh @ghArgs
    if ($LASTEXITCODE -ne 0) { throw "gh release create failed with exit code $LASTEXITCODE" }
}
else {
    Write-Host ''
    Write-Host "To upload manually: GitHub -> Releases -> Draft a new release -> attach $zipName (or OpIlGen.exe)." -ForegroundColor DarkGray
    Write-Host 'Or re-run with:  .\deploy.ps1 -Release [-Draft]' -ForegroundColor DarkGray
}
