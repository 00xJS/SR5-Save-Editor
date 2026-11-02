@echo off
rem Builds SR5-Save-Editor.exe and build\Tests.exe with the C# compiler that comes with Windows.
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "ROOT=%~dp0"
set "RES=/resource:"%ROOT%data\names.txt",names.txt /resource:"%ROOT%data\weapons.txt",weapons.txt /resource:"%ROOT%data\vehicles.txt",vehicles.txt /resource:"%ROOT%data\vehicle_variants.txt",vehicle_variants.txt /resource:"%ROOT%data\garage_options.txt",garage_options.txt /resource:"%ROOT%data\custom_items.txt",custom_items.txt"
if exist "%ROOT%data\catalog.bin" set "RES=%RES% /resource:"%ROOT%data\catalog.bin",catalog.bin"
if not exist "%ROOT%build" mkdir "%ROOT%build"

"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /out:"%ROOT%SR5-Save-Editor.exe" ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll %RES% ^
  "%ROOT%Core.cs" "%ROOT%Model.cs" "%ROOT%Inventory.cs" "%ROOT%Options.cs" "%ROOT%Editor.cs" "%ROOT%Screens.cs"
if errorlevel 1 exit /b 1

"%CSC%" /nologo /target:exe /optimize+ /platform:anycpu /out:"%ROOT%build\Tests.exe" ^
  /r:System.dll /r:System.Core.dll %RES% ^
  "%ROOT%Core.cs" "%ROOT%Model.cs" "%ROOT%Inventory.cs" "%ROOT%Options.cs" "%ROOT%Tests.cs"
if errorlevel 1 exit /b 1
