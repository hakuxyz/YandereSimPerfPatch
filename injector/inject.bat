@echo off
rem Yandere Simulator Performance Patch, double click entry point.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0inject.ps1" %*
set RC=%ERRORLEVEL%
echo.
pause
exit /b %RC%
