@echo off
setlocal EnableExtensions DisableDelayedExpansion
echo [IEDExplorer FAT] Windows build and local installation
echo Workspace: "%GITHUB_WORKSPACE%"
echo Runner: "%RUNNER_NAME%"
echo Commit: "%GITHUB_SHA%"

if not defined GITHUB_WORKSPACE (
  echo ERROR: This script must run inside the GitHub Actions self-hosted Windows runner.
  exit /b 2
)
if not defined GITHUB_SHA (
  echo ERROR: GitHub commit metadata missing.
  exit /b 2
)
cd /d "%GITHUB_WORKSPACE%"
if errorlevel 1 exit /b 2

set "MSBUILD="
where MSBuild.exe >nul 2>&1
if not errorlevel 1 set "MSBUILD=MSBuild.exe"
if not defined MSBUILD (
  set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
  if exist "%VSWHERE%" (
    "%VSWHERE%" -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" > "%TEMP%\IEDExplorer-msbuild-path-%GITHUB_RUN_ID%.txt"
    if exist "%TEMP%\IEDExplorer-msbuild-path-%GITHUB_RUN_ID%.txt" (
      set /p "MSBUILD="<"%TEMP%\IEDExplorer-msbuild-path-%GITHUB_RUN_ID%.txt"
      del /q "%TEMP%\IEDExplorer-msbuild-path-%GITHUB_RUN_ID%.txt"
    )
  )
)
if not defined MSBUILD (
  echo ERROR: Visual Studio MSBuild not installed or not found.
  echo Install Visual Studio Build Tools and .NET Framework 4.5 targeting pack or compatible reference assemblies.
  exit /b 3
)

echo Building with "%MSBUILD%"
call "%MSBUILD%" "IEDExplorer.sln" /t:Build /p:Configuration=Release "/p:Platform=Any CPU" /m:1 /v:minimal /nologo
if errorlevel 1 (
  echo ERROR: MSBuild failed. No installation files changed.
  exit /b 4
)
if not exist "bin\Release\IEDExplorer.exe" (
  echo ERROR: Build reported success but bin\Release\IEDExplorer.exe was not produced.
  exit /b 5
)

rem GITHUB_WORKSPACE: C:\actions-runner\_work\iedexplorer\iedexplorer
rem Resolve the runner's own root; deploy outside _work where checkout may clean files.
for %%I in ("%GITHUB_WORKSPACE%\..\..\..") do set "RUNNER_ROOT=%%~fI"
if not exist "%RUNNER_ROOT%\_work" (
  echo ERROR: Unexpected runner workspace layout. No installation attempted.
  exit /b 6
)
set "DESTINATION=%RUNNER_ROOT%\IEDExplorer-FAT"
set "BUILD_SHA=%GITHUB_SHA:~0,12%"
set "BUILD_FOLDER=%DESTINATION%\builds\%BUILD_SHA%"

if not exist "%BUILD_FOLDER%" mkdir "%BUILD_FOLDER%"
if errorlevel 1 exit /b 7
xcopy "bin\Release\*" "%BUILD_FOLDER%\" /E /I /Y /Q >nul
if errorlevel 1 (
  echo ERROR: Could not stage local build to "%BUILD_FOLDER%".
  exit /b 8
)
if not exist "%BUILD_FOLDER%\IEDExplorer.exe" (
  echo ERROR: Local staging did not contain an executable.
  exit /b 9
)

rem The launcher is updated only after a successful build and complete file copy.
rem A failed build will leave the previously installed version untouched.
> "%DESTINATION%\Start-IEDExplorer-FAT.cmd" echo @echo off
>> "%DESTINATION%\Start-IEDExplorer-FAT.cmd" echo start "" "%%~dp0builds\%BUILD_SHA%\IEDExplorer.exe"
if errorlevel 1 (
  echo ERROR: Could not update local launcher.
  exit /b 10
)
> "%DESTINATION%\BUILD-INFO.txt" echo Build SHA: %GITHUB_SHA%
>> "%DESTINATION%\BUILD-INFO.txt" echo Build run: %GITHUB_SERVER_URL%/%GITHUB_REPOSITORY%/actions/runs/%GITHUB_RUN_ID%
>> "%DESTINATION%\BUILD-INFO.txt" echo Executable: %BUILD_FOLDER%\IEDExplorer.exe
echo INSTALL OK
echo Executable: "%BUILD_FOLDER%\IEDExplorer.exe"
echo Double-click: "%DESTINATION%\Start-IEDExplorer-FAT.cmd"
echo Latest build: "%DESTINATION%\BUILD-INFO.txt"
exit /b 0
