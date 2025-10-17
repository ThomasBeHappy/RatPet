# RatPet Self-Contained Build Script
Write-Host "🐀 Building RatPet Self-Contained Release 🐀" -ForegroundColor Green
Write-Host ""

# Clean previous builds
Write-Host "Cleaning previous builds..." -ForegroundColor Yellow
dotnet clean
if (Test-Path "bin\Release") { Remove-Item "bin\Release" -Recurse -Force }
if (Test-Path "publish") { Remove-Item "publish" -Recurse -Force }

# Build self-contained release
Write-Host "Building self-contained release..." -ForegroundColor Yellow
$buildArgs = @(
    "publish",
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:PublishReadyToRun=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-o", "publish"
)

dotnet @buildArgs

# Check if build was successful
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Build failed!" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host ""
Write-Host "✅ Build successful!" -ForegroundColor Green
Write-Host ""
Write-Host "📁 Output location: publish\" -ForegroundColor Cyan
Write-Host "📄 Main executable: publish\RatPet.exe" -ForegroundColor Cyan
Write-Host ""

# Show file sizes
Write-Host "📊 File sizes:" -ForegroundColor Yellow
Get-ChildItem "publish\RatPet.exe" | Format-Table Name, @{Name="Size (MB)"; Expression={[math]::Round($_.Length / 1MB, 2)}} -AutoSize
Write-Host ""
Read-Host "Press Enter to exit"
