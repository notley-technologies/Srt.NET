@echo off
rem This Source Code Form is subject to the terms of the Mozilla Public
rem License, v. 2.0. If a copy of the MPL was not distributed with this
rem file, You can obtain one at https://mozilla.org/MPL/2.0/.
rem
rem Copyright (c) 2026 Noah Davis
rem
rem Builds the win-x64 runtime: libsrt with statically linked OpenSSL, staged in artifacts\.
rem
rem Usage: native\build-windows.cmd [extra CMake configure arguments...]
rem   Example: native\build-windows.cmd -DSRTNET_TEST_OPENSSL=ON
rem
rem Runs from any command prompt. Unless the prompt is already an x64 Visual Studio developer prompt, it finds the newest
rem Visual Studio 2022 or 2026 with the x64 C++ tools through vswhere and enters its developer environment. It builds with
rem the CMake Visual Studio generator for that version. It then downloads the pinned Perl and NASM
rem (windows\PrepareWindows.cmake) and builds in artifacts\native-build\win-x64. CI runs this script; its native cache key
rem combines a hash of native\ with the arguments the workflow passes.
setlocal

if /i not "%VSCMD_ARG_TGT_ARCH%"=="x64" call :enter_developer_environment || exit /b 1

rem The developer environment sets VisualStudioVersion. CMake added the Visual Studio 18 2026 generator in 4.2; the CMake
rem bundled with Visual Studio 2026 has it too, but a standalone CMake earlier on PATH takes precedence.
set "GENERATOR="
if "%VisualStudioVersion%"=="17.0" set "GENERATOR=Visual Studio 17 2022"
if "%VisualStudioVersion%"=="18.0" set "GENERATOR=Visual Studio 18 2026"
if not defined GENERATOR (
    echo error: Visual Studio version "%VisualStudioVersion%" is not supported. Use Visual Studio 2022 or 2026. 1>&2
    exit /b 1
)
cmake --help | findstr /c:"%GENERATOR%" >nul || (
    echo error: The first CMake on PATH does not support the "%GENERATOR%" generator. Update it, or remove it from PATH 1>&2
    echo so that the CMake bundled with Visual Studio is used. 1>&2
    exit /b 1
)

cd /d "%~dp0.." || exit /b 1
cmake -P native/windows/PrepareWindows.cmake || exit /b 1

rem OpenSSL's build scripts need native Windows Perl and NASM, and the C locale.
set "LC_ALL=C"
set "PATH=%CD%\artifacts\build-tools\perl\perl\bin;%CD%\artifacts\build-tools\nasm\nasm-3.01;%PATH%"
cmake -S native -B artifacts/native-build/win-x64 -G "%GENERATOR%" -A x64 -DSRTNET_RID=win-x64 %* || exit /b 1
cmake --build artifacts/native-build/win-x64 --config Release --parallel || exit /b 1
exit /b 0

:enter_developer_environment
rem VsDevCmd.bat also runs vswhere, which it expects on PATH.
set "PATH=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer;%PATH%"
set "VSINSTALL="
for /f "usebackq delims=" %%i in (`vswhere.exe -latest -products * -version "[17.0,)" -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSINSTALL=%%i"

if not defined VSINSTALL (
    echo error: Visual Studio 2022 or later with the x64 C++ build tools was not found. 1>&2
    exit /b 1
)

call "%VSINSTALL%\Common7\Tools\VsDevCmd.bat" -arch=amd64 -host_arch=amd64 -no_logo || exit /b 1
exit /b 0
