@echo off
setlocal
rem HeartRateMonitor build launcher.
rem Usage:
rem   build.bat --standalone | --releases | --debug
rem   build.bat -SkipWeb                            (skip vue build)
rem PE icons come from Windows/Release.json (icons section) - no icon args.
rem Interactive (no arg) also picks branch by number.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
exit /b %ERRORLEVEL%
