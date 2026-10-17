@echo off
rem Starts My Summer Car minimized, then Skyrim through SKSE. See PerkeleRim.ps1.
rem Windows PowerShell rebuilds its own module path; one inherited from PowerShell 7 breaks it.
set PSModulePath=
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0PerkeleRim.ps1" %*
if errorlevel 1 pause
