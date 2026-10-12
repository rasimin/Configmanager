@echo off
setlocal
echo ==========================================================
echo  ConfigManager - Single-File Portable Executable Builder
echo ==========================================================
echo.
echo Publishing Self-Contained Single File Portable Executable (win-x64)...
dotnet publish ConfigManager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish\ConfigManager-Portable-SelfContained

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ==========================================================
    echo  PUBLISH SUCCESSFUL!
    echo  Portable Executable: publish\ConfigManager-Portable-SelfContained\ConfigManager.exe
    echo ==========================================================
) else (
    echo.
    echo Publish failed with error code %ERRORLEVEL%.
)
pause
