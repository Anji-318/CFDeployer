@echo off
chcp 65001 >nul
title Git 推送工具（自动认证）
setlocal enabledelayedexpansion

REM ==================== 仓库地址配置 ====================
set "CUSTOM_REPO_URL=https://github.com/Anji-318/cloudflare-manager-android.git"
REM ==============================================================

echo.
echo    Git Push Tool
echo.

REM 保存原始目录
set "ORIGINAL_DIR=%CD%"

REM 从当前目录向上查找 .git 目录
set "PROJECT_DIR=%CD%"
:FIND_GIT
if exist "%PROJECT_DIR%\.git" (
    echo [信息] 找到仓库: %PROJECT_DIR%
    goto :FOUND_REPO
)

if "%PROJECT_DIR:~3,1%"=="" (
    echo [信息] 未找到 Git 仓库，准备自动初始化...
    goto :INIT_REPO
)

cd ..
set "PROJECT_DIR=%CD%"
goto :FIND_GIT

:INIT_REPO
cd /d "%ORIGINAL_DIR%"
echo.
echo ==========================================
echo           仓库初始化向导
echo ==========================================
echo.
echo [提示] 当前目录未关联 Git 仓库
choice /C YN /M "是否在此目录初始化新仓库并关联远程"
if errorlevel 2 (
    echo [退出] 未初始化仓库
    pause
    exit /b 1
)

echo [执行] git init ...
git init
if errorlevel 1 (
    echo [错误] git init 失败
    pause
    exit /b 1
)

set "PROJECT_DIR=%CD%"
echo [信息] 仓库已初始化: %PROJECT_DIR%

REM 配置远程仓库
set "REMOTE_NAME=origin"
set "REMOTE_URL=%CUSTOM_REPO_URL%"
echo [执行] git remote add %REMOTE_NAME% %REMOTE_URL% ...
git remote add %REMOTE_NAME% %REMOTE_URL%
if errorlevel 1 (
    echo [警告] 添加远程仓库失败，可能已存在
)

REM 检查是否有文件需要提交
if exist ".gitignore" (
    echo [信息] 发现 .gitignore，准备首次提交
) else (
    echo [信息] 创建默认 .gitignore ...
    echo .gradle/ > .gitignore
    echo /local.properties >> .gitignore
    echo /.idea/ >> .gitignore
    echo .DS_Store >> .gitignore
    echo /build/ >> .gitignore
    echo /app/build/ >> .gitignore
    echo *.apk >> .gitignore
    echo *.tar.gz >> .gitignore
    echo *.zip >> .gitignore
    echo *.bat >> .gitignore
    echo token.txt >> .gitignore
)

git add -A
git commit -m "Initial commit" >nul 2>&1
if errorlevel 1 (
    echo [提示] 无可提交内容或提交失败
) else (
    echo [完成] 首次提交已创建
)

echo [信息] 仓库初始化完成，准备进入主菜单
pause

cd /d "%PROJECT_DIR%"

:FOUND_REPO
cd /d "%PROJECT_DIR%"

REM 获取当前分支
for /f "tokens=*" %%a in ('git branch --show-current 2^>nul') do set "CURRENT_BRANCH=%%a"
if "%CURRENT_BRANCH%"=="" (
    echo [信息] 无分支，创建默认分支 main...
    git checkout -b main >nul 2>&1
    set "CURRENT_BRANCH=main"
    if errorlevel 1 (
        echo [错误] 无法创建分支
        cd /d "%ORIGINAL_DIR%"
        pause
        exit /b 1
    )
)

REM 检测远程仓库
echo [调试] 当前 git remote 配置:
git remote -v 2>nul || echo [无]

for /f "tokens=*" %%a in ('git remote 2^>nul') do set "REMOTE_NAME=%%a"

if "%REMOTE_NAME%"=="" (
    echo [警告] git remote 未配置，使用内置地址
    set "REMOTE_URL=%CUSTOM_REPO_URL%"
    set "REMOTE_NAME=origin"
) else (
    for /f "tokens=*" %%a in ('git remote get-url %REMOTE_NAME% 2^>nul') do set "REMOTE_URL=%%a"
    echo [信息] 使用远程: %REMOTE_NAME%
)

echo [仓库] %REMOTE_URL%
echo [分支] %CURRENT_BRANCH%

REM ==================== 读取认证信息 ====================
REM 从 git-config.txt 读取用户名和 Token，不存在则报错
set "CONFIG_FILE=%PROJECT_DIR%\git-config.txt"
if exist "%CONFIG_FILE%" (
    for /f "tokens=1,* delims==" %%a in ('type "%CONFIG_FILE%"') do (
        if "%%a"=="GITHUB_USERNAME" set "GITHUB_USERNAME=%%b"
        if "%%a"=="GITHUB_TOKEN" set "GITHUB_TOKEN=%%b"
    )
    echo [信息] 已从 git-config.txt 读取认证信息
) else (
    echo [错误] 未找到 git-config.txt 配置文件！
    echo [提示] 请在项目目录创建 git-config.txt，内容格式：
    echo   GITHUB_USERNAME=你的用户名
    echo   GITHUB_TOKEN=你的Token
    pause
    exit /b 1
)

if "%GITHUB_USERNAME%"=="" (
    echo [错误] git-config.txt 中未配置 GITHUB_USERNAME
    pause
    exit /b 1
)
if "%GITHUB_TOKEN%"=="" (
    echo [错误] git-config.txt 中未配置 GITHUB_TOKEN
    pause
    exit /b 1
)
REM =====================================================

REM 处理 HTTPS URL，添加认证
echo %REMOTE_URL% | findstr /I "https://" >nul
if not errorlevel 1 (
    set "TEMP_URL=!REMOTE_URL:https://=!"
    set "TEMP_URL=!TEMP_URL:*@=!"
    set "AUTH_URL=https://%GITHUB_USERNAME%:%GITHUB_TOKEN%@!TEMP_URL!"
) else (
    echo %REMOTE_URL% | findstr /I "git@" >nul
    if not errorlevel 1 (
        echo [错误] 检测到 SSH 地址，请改为 HTTPS
        cd /d "%ORIGINAL_DIR%"
        pause
        exit /b 1
    )
    echo [错误] 不支持的 URL 格式: %REMOTE_URL%
    cd /d "%ORIGINAL_DIR%"
    pause
    exit /b 1
)

REM 设置 Git 参数防止大文件推送超时
echo [配置] 优化 Git 推送参数...
git config --local http.postBuffer 524288000 >nul 2>&1
git config --local http.maxRequestBuffer 524288000 >nul 2>&1
git config --local http.lowSpeedLimit 0 >nul 2>&1
git config --local http.lowSpeedTime 999999 >nul 2>&1
git config --local core.compression 9 >nul 2>&1
git config --local pack.windowMemory 256m >nul 2>&1
git config --local pack.packSizeLimit 256m >nul 2>&1
echo [完成] 推送参数已优化

:MENU
cls
echo ==========================================
echo           Git 推送工具
echo ==========================================
echo  用户: %GITHUB_USERNAME%
echo  分支: %CURRENT_BRANCH%
echo  远程: %REMOTE_NAME%
echo  仓库: %REMOTE_URL%
echo  目录: %PROJECT_DIR%
echo ==========================================
echo.
echo  [1] 普通推送  (git push)
echo  [2] 强制覆盖推送 (git push --force)
echo  [3] 安全强制推送 (git push --force-with-lease)
echo  [4] 查看提交日志
echo  [5] 查看未提交更改
echo  [6] 测试连接
echo  [7] 删除配置文件
echo  [0] 退出
echo.
echo ==========================================
set /p choice="请选择操作 [0-7]: "

if "%choice%"=="1" goto :NORMAL_PUSH
if "%choice%"=="2" goto :FORCE_PUSH
if "%choice%"=="3" goto :FORCE_LEASE_PUSH
if "%choice%"=="4" goto :SHOW_LOG
if "%choice%"=="5" goto :SHOW_DIFF
if "%choice%"=="6" goto :TEST_CONNECTION
if "%choice%"=="7" goto :DELETE_TOKEN
if "%choice%"=="0" goto :EXIT

echo [错误] 无效选择，请重新输入
timeout /t 2 >nul
goto :MENU

:NORMAL_PUSH
cls
echo ==========================================
echo           普通推送
echo ==========================================
echo.

git diff-index --quiet HEAD -- >nul 2>&1
if errorlevel 1 (
    echo [警告] 检测到未提交的更改！
    echo.
    git status --short
    echo.
    choice /C YN /M "是否继续推送已提交的更改"
    if errorlevel 2 goto :MENU
)

echo [执行] git push %REMOTE_NAME% %CURRENT_BRANCH% ...
git push "%AUTH_URL%" %CURRENT_BRANCH%

if errorlevel 1 (
    echo.
    echo [失败] 推送被拒绝，可能需要强制推送
    echo [提示] 如果提示 403，请检查 Token 是否有 repo 权限
) else (
    echo.
    echo [成功] 推送完成！
)

pause
goto :MENU

:FORCE_PUSH
cls
echo ==========================================
echo        强制覆盖推送
echo ==========================================
echo.
echo [警告] 此操作会覆盖远程历史，可能导致数据丢失！
echo.

echo [信息] 本地提交:
git log --oneline -5
echo.
echo [信息] 远程提交:
call :SHOW_REMOTE_LOG 5

echo.
choice /C YN /M "确认强制覆盖远程仓库" /T 10 /D N
if errorlevel 2 goto :MENU

echo.
echo [执行] git push --force %REMOTE_NAME% %CURRENT_BRANCH% ...
git push --force "%AUTH_URL%" %CURRENT_BRANCH%

if errorlevel 1 (
    echo.
    echo [失败] 强制推送失败
    echo [提示] 如果提示 secrets detected，说明 GitHub 检测到了敏感信息
    echo [提示] 请检查提交中是否包含 token.txt 或脚本中的 Token
    echo [提示] 确保 token.txt 已加入 .gitignore 且未提交到仓库
) else (
    echo.
    echo [成功] 强制推送完成！
)

pause
goto :MENU

:FORCE_LEASE_PUSH
cls
echo ==========================================
echo        安全强制推送 (force-with-lease)
echo ==========================================
echo.
echo [说明] 仅当远程没有新提交时才会强制推送
echo.

echo [信息] 本地 vs 远程差异:
call :SHOW_REMOTE_DIFF

echo.
choice /C YN /M "确认安全强制推送" /T 10 /D N
if errorlevel 2 goto :MENU

echo.
echo [执行] git push --force-with-lease ...
git push --force-with-lease "%AUTH_URL%" %CURRENT_BRANCH%

if errorlevel 1 (
    echo.
    echo [失败] 推送被拒绝！远程可能有新提交
) else (
    echo.
    echo [成功] 安全强制推送完成！
)

pause
goto :MENU

:SHOW_LOG
cls
echo ==========================================
echo           提交日志
echo ==========================================
echo.
echo [本地提交] (最近10条):
git log --oneline -10
echo.
echo [远程提交] (最近5条):
call :SHOW_REMOTE_LOG 5
echo.
pause
goto :MENU

:SHOW_DIFF
cls
echo ==========================================
echo           未提交更改
echo ==========================================
echo.
git status
echo.
echo ==========================================
git diff --stat
echo.
pause
goto :MENU

:TEST_CONNECTION
cls
echo ==========================================
echo           测试连接
echo ==========================================
echo.
echo [测试] 验证认证信息...
echo [URL] %AUTH_URL:ghp_*@=***@%
echo.

git ls-remote --heads "%AUTH_URL%" %CURRENT_BRANCH% >nul 2>&1
if errorlevel 1 (
    echo [失败] 连接失败！请检查:
    echo   - 用户名和令牌是否正确
    echo   - 令牌是否有 repo 权限
    echo   - 仓库地址是否可访问
) else (
    echo [成功] 连接正常，认证有效！
    echo [信息] 远程分支存在，可以推送
)

echo.
pause
goto :MENU

:DELETE_TOKEN
cls
echo ==========================================
echo           删除配置文件
echo ==========================================
echo.
if exist "%CONFIG_FILE%" (
    del "%CONFIG_FILE%"
    echo [完成] git-config.txt 已删除
    echo [提示] 下次运行需要重新创建配置文件
) else (
    echo [提示] 未找到 git-config.txt
)
pause
goto :MENU

:EXIT
cd /d "%ORIGINAL_DIR%"
exit /b 0

REM ==================== 子程序 ====================

:SHOW_REMOTE_LOG
REM 参数: %1=显示条数
set "LOG_COUNT=%~1"
if "%LOG_COUNT%"=="" set "LOG_COUNT=5"

REM 先获取远程分支到临时引用
git fetch "%AUTH_URL%" %CURRENT_BRANCH%:refs/remotes/_temp_remote/%CURRENT_BRANCH% >nul 2>&1
if errorlevel 1 (
    echo [提示] 无法获取远程分支信息（可能为空仓库或网络问题）
    goto :EOF
)

REM 显示远程提交
git log --oneline -%LOG_COUNT% refs/remotes/_temp_remote/%CURRENT_BRANCH% 2>nul
if errorlevel 1 (
    echo [提示] 远程分支无提交记录
)

REM 清理临时引用
git update-ref -d refs/remotes/_temp_remote/%CURRENT_BRANCH% >nul 2>&1
goto :EOF

:SHOW_REMOTE_DIFF
REM 获取远程分支到临时引用
git fetch "%AUTH_URL%" %CURRENT_BRANCH%:refs/remotes/_temp_remote/%CURRENT_BRANCH% >nul 2>&1
if errorlevel 1 (
    echo [提示] 首次推送（无法获取远程分支）
    goto :EOF
)

REM 显示差异
git log --oneline refs/remotes/_temp_remote/%CURRENT_BRANCH%..HEAD 2>nul
if errorlevel 1 (
    echo [提示] 无法计算差异
)

REM 清理临时引用
git update-ref -d refs/remotes/_temp_remote/%CURRENT_BRANCH% >nul 2>&1
goto :EOF
