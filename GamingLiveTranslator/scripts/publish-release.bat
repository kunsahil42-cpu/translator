@echo off
setlocal enabledelayedexpansion

echo ======================================================================
echo Gaming Live Translator - Release Packaging
echo Self-Contained Single-File Executable (Ttranslator.exe)
echo ======================================================================
echo.

set "SCRIPT_DIR=%~dp0"
set "PROJECT_DIR=%SCRIPT_DIR%.."
set "CSPROJ=%PROJECT_DIR%\GamingLiveTranslator.csproj"
set "PUBLISH_DIR=%PROJECT_DIR%\bin\Release\net10.0-windows\win-x64\publish"

echo [1/4] Publishing self-contained single-file win-x64 executable...
dotnet publish "%CSPROJ%" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=false
if %ERRORLEVEL% neq 0 (
    echo [ERROR] dotnet publish failed.
    exit /b 1
)

echo.
echo [2/4] Verifying/bundling Python runtime companion files...
set "RES_PYTHON=%PUBLISH_DIR%\Resources\PythonRuntime"
if not exist "%RES_PYTHON%" mkdir "%RES_PYTHON%"

REM Copy virtual environment or PythonRuntime if present in workspace
if exist "%PROJECT_DIR%\PythonRuntime" (
    echo Bundling PythonRuntime from %PROJECT_DIR%\PythonRuntime...
    robocopy "%PROJECT_DIR%\PythonRuntime" "%RES_PYTHON%" /E /MT:8 /NFL /NDL /NJH /NJS /nc /ns /np >nul
) else if exist "%PROJECT_DIR%\test_env" (
    echo Bundling Python environment from %PROJECT_DIR%\test_env...
    robocopy "%PROJECT_DIR%\test_env" "%RES_PYTHON%" /E /MT:8 /NFL /NDL /NJH /NJS /nc /ns /np >nul
)


echo.
echo [3/4] Bundling voice models...
set "MODELS_DEST=%PUBLISH_DIR%\Resources\models\piper"
if not exist "%MODELS_DEST%" mkdir "%MODELS_DEST%"
set "LOCAL_MODELS=%LOCALAPPDATA%\GamingLiveTranslator\models\piper"
if exist "%LOCAL_MODELS%" (
    echo Copying local voice models from %LOCAL_MODELS%...
    copy /y "%LOCAL_MODELS%\*" "%MODELS_DEST%\" >nul 2>nul
)

echo.
echo [4/4] Package Verification...
if exist "%PUBLISH_DIR%\Ttranslator.exe" (
    echo [SUCCESS] Launcher binary: %PUBLISH_DIR%\Ttranslator.exe
) else (
    echo [ERROR] Ttranslator.exe was not found in %PUBLISH_DIR%
    exit /b 1
)

echo ======================================================================
echo Packaging complete!
echo Output folder: %PUBLISH_DIR%
echo Distributable: Ttranslator.exe + Resources/ directory
echo ======================================================================
exit /b 0
