param (
    [string]$Configuration = "Release",
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   Reeled Custom WinUI 3 Installer Build Pipeline         " -ForegroundColor Cyan
Write-Host "   Configuration: $Configuration | Version: $Version      " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$RootDir = Split-Path -Parent $PSScriptRoot
$ArtifactsDir = Join-Path $RootDir "artifacts"
$PublishDir = Join-Path $ArtifactsDir "publish"
$InstallerPublishDir = Join-Path $ArtifactsDir "installer"
$PayloadZip = Join-Path $ArtifactsDir "payload.zip"

# Ensure output directories exist
if (Test-Path $ArtifactsDir) { Remove-Item -Path $ArtifactsDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $ArtifactsDir | Out-Null
New-Item -ItemType Directory -Force -Path $PublishDir | Out-Null

Write-Host "`n[1/4] Publishing Reeled application binaries ($Configuration)..." -ForegroundColor Yellow
dotnet publish "$RootDir\Reeled\Reeled.csproj" `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishReadyToRun=true `
    -p:PublishTrimmed=false `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to publish Reeled application binaries."
}

Write-Host "`n[2/4] Pruning unused architectures and compressing payload into payload.zip..." -ForegroundColor Yellow

# Prune redundant 32-bit and ARM64 LibVLC binaries from win-x64 build (saves ~180MB uncompressed)
$vlcArm64 = Join-Path $PublishDir "libvlc\win-arm64"
$vlcX86 = Join-Path $PublishDir "libvlc\win-x86"
if (Test-Path $vlcArm64) { Remove-Item $vlcArm64 -Recurse -Force }
if (Test-Path $vlcX86) { Remove-Item $vlcX86 -Recurse -Force }

# Ensure all WinUI 3 XBF compiled files and PRI resource indexes are included in publish dir
$PossibleBinDirs = @(
    (Join-Path $RootDir "Reeled\bin\$Configuration\net8.0-windows10.0.26100.0\win-x64"),
    (Join-Path $RootDir "Reeled\bin\x64\$Configuration\net8.0-windows10.0.26100.0\win-x64")
)
foreach ($bd in $PossibleBinDirs) {
    if (Test-Path $bd) {
        Write-Host "Verifying WinUI 3 compiled XAML resources and PRI indexes from $bd..." -ForegroundColor Cyan
        Get-ChildItem -Path $bd -Include "*.xbf" -Recurse | Where-Object { $_.FullName -notlike "*\publish\*" } | ForEach-Object {
            $rel = [System.IO.Path]::GetRelativePath($bd, $_.FullName)
            $dest = Join-Path $PublishDir $rel
            $destDir = Split-Path -Parent $dest
            if (!(Test-Path $destDir)) { New-Item -ItemType Directory -Force -Path $destDir | Out-Null }
            Copy-Item -Path $_.FullName -Destination $dest -Force
        }
        Get-ChildItem -Path $bd -Filter "*.pri" | Where-Object { $_.FullName -notlike "*\publish\*" } | ForEach-Object {
            $dest = Join-Path $PublishDir $_.Name
            Copy-Item -Path $_.FullName -Destination $dest -Force
        }
    }
}

# Remove any accidental nested publish folder
$nestedPub = Join-Path $PublishDir "publish"
if (Test-Path $nestedPub) { Remove-Item $nestedPub -Recurse -Force }

# Verify critical files exist in payload
$AppXbf = Join-Path $PublishDir "App.xbf"
$ReeledPri = Join-Path $PublishDir "Reeled.pri"
if (!(Test-Path $AppXbf) -or !(Test-Path $ReeledPri)) {
    Write-Error "CRITICAL: WinUI 3 App.xbf or Reeled.pri was not found in $PublishDir! Build cannot proceed."
}

if (Test-Path $PayloadZip) { Remove-Item $PayloadZip -Force }

# Use .NET ZipFile for fast and optimal compression
[System.Reflection.Assembly]::LoadWithPartialName("System.IO.Compression.FileSystem") | Out-Null
[System.IO.Compression.ZipFile]::CreateFromDirectory($PublishDir, $PayloadZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

$PayloadSizeMB = [math]::Round((Get-Item $PayloadZip).Length / 1MB, 2)
Write-Host "Payload archive created: $PayloadZip ($PayloadSizeMB MB)" -ForegroundColor Green

Write-Host "`n[3/4] Publishing Reeled.Installer (ReeledSetup.exe)..." -ForegroundColor Yellow
dotnet publish "$RootDir\Reeled.Installer\Reeled.Installer.csproj" `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $InstallerPublishDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to publish Reeled.Installer."
}

$SetupExe = Join-Path $InstallerPublishDir "ReeledSetup.exe"
if (!(Test-Path $SetupExe)) {
    Write-Error "ReeledSetup.exe was not found in $InstallerPublishDir"
}

Write-Host "`n[4/5] Signing installer binaries (Authenticode)..." -ForegroundColor Yellow
$FinalSetupExe = Join-Path $ArtifactsDir "ReeledSetup.exe"
$VersionedSetupExe = Join-Path $ArtifactsDir "ReeledSetup-v$Version.exe"

Copy-Item $SetupExe $FinalSetupExe -Force
Copy-Item $SetupExe $VersionedSetupExe -Force

try {
    $CertSubject = "CN=k25jura, O=k25jura"
    $Cert = Get-ChildItem -Path Cert:\CurrentUser\My -CodeSigningCert | Where-Object { $_.Subject -like "*$CertSubject*" } | Select-Object -First 1

    if (-not $Cert) {
        Write-Host "Creating local code signing certificate for $CertSubject..." -ForegroundColor Cyan
        $Cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $CertSubject -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(5)
    }

    if ($Cert) {
        Write-Host "Signing binaries with certificate $($Cert.Thumbprint)..." -ForegroundColor Green
        Set-AuthenticodeSignature -FilePath $FinalSetupExe -Certificate $Cert -HashAlgorithm SHA256 -TimestampServer "http://timestamp.digicert.com" | Out-Null
        Set-AuthenticodeSignature -FilePath $VersionedSetupExe -Certificate $Cert -HashAlgorithm SHA256 -TimestampServer "http://timestamp.digicert.com" | Out-Null
    }
} catch {
    Write-Warning "Code signing warning: $_"
}

Write-Host "`n[5/5] Finalizing release artifacts..." -ForegroundColor Yellow

$SetupSizeMB = [math]::Round((Get-Item $FinalSetupExe).Length / 1MB, 2)
$Hash = (Get-FileHash -Path $FinalSetupExe -Algorithm SHA256).Hash

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host "   BUILD SUCCEEDED!                                       " -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "Installer: $FinalSetupExe ($SetupSizeMB MB)" -ForegroundColor White
Write-Host "SHA-256:   $Hash" -ForegroundColor White
Write-Host "Versioned: $VersionedSetupExe" -ForegroundColor White
Write-Host "==========================================================" -ForegroundColor Green
