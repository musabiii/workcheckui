@echo off
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true --self-contained true
echo.
echo Done: bin\Release\net8.0-windows\win-x64\publish\WorkCheck.exe
pause
