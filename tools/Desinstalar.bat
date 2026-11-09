@echo off
rem GeoPoint - remove o plugin do Revit 2027 (usuario atual)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1"
pause
