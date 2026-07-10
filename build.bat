@echo off
chcp 65001 >nul
title CFDeployer 编译工具 - .NET 9 WPF 单文件版

echo ========================================
echo       CFDeployer 编译工具 (单文件版)
echo ========================================
echo [信息] 工作目录: %CD%
echo [信息] 时间: %date% %time%
echo.

echo [信息] 正在清理旧文件...
dotnet clean -c Release >nul 2>&1
rmdir /s /q bin obj publish 2>nul

echo [信息] 正在还原 NuGet 包...
dotnet restore
if errorlevel 1 (
    echo [错误] NuGet 包还原失败！
    echo [提示] 请检查网络或 NuGet 源
    pause
    exit /b 1
)
echo [成功] NuGet 包还原完成
echo.

echo [信息] 正在编译 Release 版本...
dotnet build -c Release --no-restore
if errorlevel 1 (
    echo [错误] 编译失败！
    pause
    exit /b 1
)
echo [成功] 编译完成
echo.

echo [信息] 正在发布单文件可执行程序...
echo [提示] 平台: win-x64   模式: 单文件 + 自包含
dotnet publish -c Release -r win-x64 --self-contained true ^
    /p:PublishSingleFile=true ^
    /p:IncludeNativeLibrariesForSelfExtract=true ^
    /p:PublishTrimmed=false ^
    /p:DebugType=embedded ^
    -o ./publish

if errorlevel 1 (
    echo [错误] 发布失败！
    pause
    exit /b 1
)

echo.
echo ========================================
echo [成功] 全部编译完成！
echo ========================================
echo [信息] 输出目录: %CD%\publish
echo [信息] 生成文件:
dir /b .\publish\*.exe 2>nul || echo (未找到 exe)
echo.
echo [提示] 可直接双击运行:
echo     .\publish\CFDeployer.exe
echo.
echo 按任意键退出...
pause >nul