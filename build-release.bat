@echo off
echo 🐀 Building RatPet Self-Contained Release 🐀
echo.

REM Clean previous builds
echo Cleaning previous builds...
dotnet clean
if exist "bin\Release" rmdir /s /q "bin\Release"
if exist "publish" rmdir /s /q "publish"

REM Build self-contained release
echo Building self-contained release...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish

REM Check if build was successful
if %ERRORLEVEL% neq 0 (
    echo ❌ Build failed!
    pause
    exit /b 1
)

echo.
echo ✅ Build successful!
echo.
echo 📁 Output location: publish\
echo 📄 Main executable: publish\RatPet.exe
echo.

REM Show file sizes
echo 📊 File sizes:
dir publish\RatPet.exe
echo.
pause
