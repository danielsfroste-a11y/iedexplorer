@echo off
setlocal EnableExtensions DisableDelayedExpansion
echo [IEDExplorer FAT] Installing tested Windows build onto the self-hosted runner

if not defined GITHUB_WORKSPACE (
  echo ERROR: Must be run by the self-hosted GitHub Actions runner.
  exit /b 2
)
if not defined GITHUB_SHA (
  echo ERROR: GitHub commit SHA missing.
  exit /b 2
)
if not exist "%GITHUB_WORKSPACE%\local-package\IEDExplorer.exe" (
  echo ERROR: Downloaded build artifact does not include IEDExplorer.exe.
  exit /b 3
)

rem GITHUB_WORKSPACE is normally C:\actions-runner\_work\iedexplorer\iedexplorer.
rem Store successful builds outside the cleaned GitHub checkout directory.
for %%I in ("%GITHUB_WORKSPACE%\..\..\..") do set "RUNNER_ROOT=%%~fI"
if not exist "%RUNNER_ROOT%\_work" (
  echo ERROR: Cannot verify runner's persistent installation directory.
  exit /b 4
)
set "DESTINATION=%RUNNER_ROOT%\IEDExplorer-FAT"
set "BUILD_SHA=%GITHUB_SHA:~0,12%"
set "BUILD_FOLDER=%DESTINATION%\builds\%BUILD_SHA%"

if not exist "%BUILD_FOLDER%" mkdir "%BUILD_FOLDER%"
if errorlevel 1 exit /b 5
xcopy "%GITHUB_WORKSPACE%\local-package\*" "%BUILD_FOLDER%\" /E /I /Y /Q >nul
if errorlevel 1 (
  echo ERROR: Could not copy package to "%BUILD_FOLDER%".
  exit /b 6
)
if not exist "%BUILD_FOLDER%\IEDExplorer.exe" (
  echo ERROR: Installation did not produce a runnable executable.
  exit /b 7
)

rem Update the launcher only after the package is fully copied and checked.
> "%DESTINATION%\Start-IEDExplorer-FAT.cmd" echo @echo off
>> "%DESTINATION%\Start-IEDExplorer-FAT.cmd" echo start "" "%%~dp0builds\%BUILD_SHA%\IEDExplorer.exe"
if errorlevel 1 (
  echo ERROR: Could not update the latest-build launcher.
  exit /b 8
)
> "%DESTINATION%\BUILD-INFO.txt" echo Build SHA: %GITHUB_SHA%
>> "%DESTINATION%\BUILD-INFO.txt" echo Build run: %GITHUB_SERVER_URL%/%GITHUB_REPOSITORY%/actions/runs/%GITHUB_RUN_ID%
>> "%DESTINATION%\BUILD-INFO.txt" echo Executable: %BUILD_FOLDER%\IEDExplorer.exe

echo ==========================================
echo LOCAL INSTALLATION SUCCESS
echo Executable: "%BUILD_FOLDER%\IEDExplorer.exe"
echo Launcher: "%DESTINATION%\Start-IEDExplorer-FAT.cmd"
echo Build information: "%DESTINATION%\BUILD-INFO.txt"
echo ==========================================
exit /b 0
