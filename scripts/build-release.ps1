<#
.SYNOPSIS
  Builds a shareable release: tests, a self-contained publish, a portable zip, the installer,
  and SHA-256 checksums, all in .\artifacts. Local builds are unsigned; official signed builds
  come from the GitHub release workflow via SignPath.

.EXAMPLE
  ./scripts/build-release.ps1 -Version 1.0.0
  ./scripts/build-release.ps1 -Version 1.0.0 -Stage Package   # installer + zip from an existing (e.g. signed) publish
#>
param(
    [Parameter(Mandatory)] [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')] [string] $Version,
    [ValidateSet('All', 'Publish', 'Package')] [string] $Stage = 'All',
    [string] $Runtime = 'win-x64',
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $root 'artifacts'
$publishDir = Join-Path $artifacts "publish\$Runtime"
$platform = if ($Runtime -eq 'win-arm64') { 'ARM64' } else { 'x64' }

function Step($text) { Write-Host "`n==> $text" -ForegroundColor Cyan }

if ($Stage -in 'All', 'Publish') {
    if (-not $SkipTests) {
        Step 'Running tests'
        dotnet test (Join-Path $root 'tests\Aos.Core.Tests') -c Release --nologo
        if ($LASTEXITCODE) { throw 'Tests failed.' }
    }

    Step "Publishing $Runtime ($Version)"
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
    # MSBuild's publish copy step breaks when the output path contains an apostrophe (this repo's
    # local folder is "Almighty's ..."), so publish to a temp folder first and copy it over.
    $tempOut = Join-Path ([IO.Path]::GetTempPath()) "aos-publish-$Runtime-$PID"
    if (Test-Path $tempOut) { Remove-Item $tempOut -Recurse -Force }
    dotnet publish (Join-Path $root 'src\Aos.App') -c Release -r $Runtime --self-contained `
        -p:Platform=$platform -p:Version=$Version -p:PublishReadyToRun=true -o $tempOut --nologo
    if ($LASTEXITCODE) { throw 'Publish failed.' }
    New-Item -ItemType Directory -Force (Split-Path $publishDir) | Out-Null
    Move-Item $tempOut $publishDir

    # Licence files travel with the portable build too.
    Copy-Item (Join-Path $root 'LICENSE') (Join-Path $publishDir 'LICENSE.txt')
    Copy-Item (Join-Path $root 'NOTICE.md'), (Join-Path $root 'THIRD-PARTY-NOTICES.md') $publishDir
    Copy-Item (Join-Path $root 'licenses') (Join-Path $publishDir 'licenses') -Recurse -Force
}

if ($Stage -in 'All', 'Package') {
    if (-not (Test-Path (Join-Path $publishDir 'AlmightysOverwatchSelector.exe'))) { throw "Nothing published in $publishDir." }

    Step 'Creating portable zip'
    $zip = Join-Path $artifacts "AlmightysOverwatchSelector-$Version-$Runtime-portable.zip"
    if (Test-Path $zip) { Remove-Item $zip }
    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zip -CompressionLevel Optimal

    Step 'Building installer'
    $iscc = @(
        (Get-Command iscc.exe -ErrorAction SilentlyContinue)?.Source,
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $iscc) { throw 'Inno Setup 6 not found. Install it: winget install JRSoftware.InnoSetup' }
    & $iscc /Q "/DAppVersion=$Version" "/DSourceDir=$publishDir" "/DOutputDir=$artifacts" (Join-Path $root 'installer\setup.iss')
    if ($LASTEXITCODE) { throw 'Installer build failed.' }

    Step 'Writing checksums'
    $files = Get-ChildItem $artifacts -File | Where-Object { $_.Name -like "*$Version*" -and $_.Extension -in '.exe', '.zip' }
    $sums = $files | ForEach-Object { "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.Name }
    $sums | Set-Content (Join-Path $artifacts 'SHA256SUMS.txt') -Encoding ascii
    $sums | Write-Host
}

Step "Done. Output in $artifacts"
