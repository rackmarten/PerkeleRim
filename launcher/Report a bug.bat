@echo off
rem Zips the PerkeleRim logs and versions to its Bug reports folder and opens the GitHub bug form. Sends nothing anywhere. See PerkeleRim.ps1.
set PSModulePath=
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0PerkeleRim.ps1" -ReportBug %*
pause
