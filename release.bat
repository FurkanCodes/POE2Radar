@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0release.ps1" -NoPause %*
set EXITCODE=%ERRORLEVEL%
echo.
if %EXITCODE% neq 0 (
    echo Release FAILED with exit code %EXITCODE%.
) else (
    echo Release finished successfully.
)
pause
exit /b %EXITCODE%
