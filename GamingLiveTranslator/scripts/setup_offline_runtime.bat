@echo off
setlocal enabledelayedexpansion

echo ======================================================================
echo Gaming Live Translator - Offline Translation Runtime Setup
echo Direct Argos Translate (MIT/CC0) Lightweight Bundling
echo ======================================================================
echo.

REM Determine target directory (default: PythonRuntime)
set "TARGET_DIR=%~dp0..\PythonRuntime"
if not "%~1"=="" (
    set "TARGET_DIR=%~1"
)

echo Target Runtime Directory: %TARGET_DIR%

REM Find Python executable
where python >nul 2>nul
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Python was not found in your system PATH.
    echo Please install Python 3.10, 3.11, or 3.12 (64-bit) from python.org,
    echo or ensure python.exe is accessible in PATH.
    exit /b 1
)

for /f "tokens=*" %%i in ('where python') do (
    set "SYSTEM_PYTHON=%%i"
    goto :found_python
)

:found_python
echo Using Host Python: %SYSTEM_PYTHON%
echo.

REM Create virtual environment in target directory if not present
if not exist "%TARGET_DIR%\Scripts\python.exe" (
    echo [1/3] Creating virtual environment at "%TARGET_DIR%"...
    "%SYSTEM_PYTHON%" -m venv "%TARGET_DIR%"
    if %ERRORLEVEL% neq 0 (
        echo [ERROR] Failed to create virtual environment.
        exit /b 1
    )
) else (
    echo [1/3] Existing runtime found at "%TARGET_DIR%".
)

echo.
echo [2/3] Installing lightweight Argos Translate dependencies...
echo Notice: Omitting PyTorch and SpaCy; using MiniSBD + ONNXRuntime (~125MB total disk footprint).
echo.

set "RUNTIME_PIP=%TARGET_DIR%\Scripts\pip.exe"
set "RUNTIME_PYTHON=%TARGET_DIR%\Scripts\python.exe"

"%RUNTIME_PIP%" install --upgrade pip
if %ERRORLEVEL% neq 0 (
    echo [WARNING] Failed to upgrade pip, continuing with existing pip...
)

"%RUNTIME_PIP%" install ctranslate2 sentencepiece numpy minisbd onnxruntime sacremoses packaging pyyaml
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Failed to install core NLP dependencies.
    exit /b 1
)

"%RUNTIME_PIP%" install argostranslate --no-deps
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Failed to install argostranslate.
    exit /b 1
)

echo.
echo [3/3] Verifying runtime integrity...
set "ARGOS_CHUNK_TYPE=MINISBD"
"%RUNTIME_PYTHON%" -c "import argostranslate.translate, ctranslate2, minisbd, onnxruntime; print(' Argostranslate and CTranslate2 runtime verified successfully.')"
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Verification failed.
    exit /b 1
)

echo.
echo ======================================================================
echo SUCCESS: Lightweight offline translation runtime is ready!
echo Location: %TARGET_DIR%
echo ======================================================================
exit /b 0
