@echo off
setlocal

echo ======================================================================
echo    ConfigManager - Single-File Portable Executable Builder (.NET 9)
echo ======================================================================
echo.

set "OUTPUT_DIR=%~dp0publish\ConfigManager-Portable"

echo [1/3] Menyiapkan direktori publish...
if exist "%OUTPUT_DIR%" (
    rmdir /s /q "%OUTPUT_DIR%" >nul 2>&1
)
mkdir "%OUTPUT_DIR%" >nul 2>&1

echo [2/3] Membangun single-file executable portable (win-x64)...
dotnet publish "%~dp0ConfigManager.csproj" ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:DebugType=None ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:IncludeAllContentForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -o "%OUTPUT_DIR%"

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] Proses publish gagal dengan kode: %ERRORLEVEL%
    pause
    exit /b %ERRORLEVEL%
)

echo [3/3] Membersihkan file build tambahan (PDB dan JSON manifest)...
if exist "%OUTPUT_DIR%\ConfigManager.staticwebassets.endpoints.json" del /f /q "%OUTPUT_DIR%\ConfigManager.staticwebassets.endpoints.json" >nul 2>&1
if exist "%OUTPUT_DIR%\ConfigManager.pdb" del /f /q "%OUTPUT_DIR%\ConfigManager.pdb" >nul 2>&1
if exist "%OUTPUT_DIR%\nuget.config" del /f /q "%OUTPUT_DIR%\nuget.config" >nul 2>&1

echo.
echo ======================================================================
echo  PUBLISH BERHASIL! (100%% Portable Single-File)
echo ======================================================================

if not exist "%OUTPUT_DIR%\ConfigManager.exe" (
    echo [WARN] File ConfigManager.exe tidak ditemukan di folder output.
    pause
    exit /b 1
)

for %%F in ("%OUTPUT_DIR%\ConfigManager.exe") do set "SIZE_BYTES=%%~zF"

powershell -NoProfile -Command "$s = [math]::Round(%SIZE_BYTES% / 1MB, 2); Write-Host '  File Output : %OUTPUT_DIR%\ConfigManager.exe' -ForegroundColor Green; Write-Host ('  Ukuran File : ' + $s + ' MB (Single-File Mandiri)') -ForegroundColor Cyan"

echo.
echo  * File ini mandiri (tidak butuh .NET SDK / Runtime di PC tujuan).
echo  * Anda cukup copy file ConfigManager.exe ini saja ke PC mana pun!
echo ======================================================================
echo.

set /p OPEN_EXPLORER="Buka folder output di Windows Explorer? (Y/N) [Y]: "
if "%OPEN_EXPLORER%"=="" set "OPEN_EXPLORER=Y"
if /i "%OPEN_EXPLORER%"=="Y" (
    start "" explorer.exe "%OUTPUT_DIR%"
)

echo.
echo Selesai.
pause
