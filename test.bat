@echo off
rem Runs the checks against real saves, then has the separate Python decoder read what the editor wrote.
rem   test.bat [folder with saves]
setlocal
set "ROOT=%~dp0"
set "SAVES=%~1"
if "%SAVES%"=="" set "SAVES=%ROOT%samples\original"
set "OUT=%TEMP%\sr5test"
"%ROOT%build\Tests.exe" "%SAVES%" "%OUT%"
if errorlevel 1 exit /b 1
python -I "%ROOT%tools\sr5save.py" check "%OUT%"\unlocked_*.sf_pc
if errorlevel 1 exit /b 1
echo All checks passed.
