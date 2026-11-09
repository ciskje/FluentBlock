@echo off
setlocal
set EXE=C:\Temp\opencode\FluentBlock-artifacts\bin\FluentBlock\debug\FluentBlock.exe
if not exist "%EXE%" (
  echo [FluentBlock] Debug exe not found: %EXE%
  echo Build first: dotnet build src\FluentBlock\FluentBlock.csproj --artifacts-path C:/Temp/opencode/FluentBlock-artifacts -c Debug
  exit /b 1
)
start "" "%EXE%" %*
