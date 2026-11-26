@echo off
REM Builds EchoKey (Release, win-x64). Requires the .NET 10 SDK: https://dotnet.microsoft.com/download
setlocal
cd /d "%~dp0"

echo === Framework-dependent single file (small; needs the .NET 10 Desktop Runtime) ===
dotnet publish EchoKey.csproj -c Release -r win-x64 --self-contained false ^
  -p:PublishSingleFile=true -p:PublishReadyToRun=true -o publish\framework-dependent || goto :err

echo === Self-contained single file (bigger; runs without installing .NET) ===
dotnet publish EchoKey.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=true ^
  -o publish\self-contained || goto :err

echo.
echo Done:
echo   publish\framework-dependent\EchoKey.exe
echo   publish\self-contained\EchoKey.exe
exit /b 0

:err
echo Build failed.
exit /b 1
