@echo off
setlocal enabledelayedexpansion
chcp 65001 >nul

:: ============================================================================
:: NukkiStudio - Windows One-Click GitHub Bootstrap & Provisioning Script
:: ============================================================================

set "REPO_NAME=NukkiStudio"
set "REPO_DESC=C# .NET 8 WPF Studio for Video and Image Background Removal (ChromaKey & ONNX AI Matting)"
set "VISIBILITY=public"
set "DEFAULT_BRANCH=main"
set "INITIAL_TAG=v1.0.0"
set "REPO_TOPICS=csharp,wpf,video-matting,background-removal,chromakey,onnx-runtime,ffmpeg"

echo ============================================================================
echo   NukkiStudio - GitHub One-Click Bootstrap & Provisioning
echo ============================================================================
echo.

:: ----------------------------------------------------------------------------
:: 1. Check Git
:: ----------------------------------------------------------------------------
echo [CHECK] Git 설치 여부 확인 중...
where git >nul 2>nul
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Git이 설치되어 있지 않습니다.
    echo         https://git-scm.com/ 에서 Git을 설치한 후 다시 실행해주세요.
    goto :FAIL
)
for /f "tokens=*" %%i in ('git --version') do set "GIT_VER=%%i"
echo [OK] %GIT_VER% 감지됨.

:: ----------------------------------------------------------------------------
:: 2. Check .NET SDK
:: ----------------------------------------------------------------------------
echo [CHECK] .NET SDK 설치 여부 확인 중...
where dotnet >nul 2>nul
if %ERRORLEVEL% neq 0 (
    echo [ERROR] .NET SDK가 설치되어 있지 않습니다.
    echo         https://dotnet.microsoft.com/ 에서 .NET 8.0 SDK 이상을 설치해주세요.
    goto :FAIL
)
for /f "tokens=*" %%i in ('dotnet --version') do set "DOTNET_VER=%%i"
echo [OK] .NET SDK %DOTNET_VER% 감지됨.

:: ----------------------------------------------------------------------------
:: 3. Check GitHub CLI (gh)
:: ----------------------------------------------------------------------------
echo [CHECK] GitHub CLI (gh) 설치 여부 확인 중...
where gh >nul 2>nul
if %ERRORLEVEL% neq 0 (
    echo [ERROR] GitHub CLI (gh)가 설치되어 있지 않습니다.
    echo         winget install --id GitHub.cli 로 설치하거나
    echo         https://cli.github.com/ 에서 다운로드하세요.
    goto :FAIL
)
for /f "tokens=*" %%i in ('gh --version 2^>nul') do (
    set "GH_VER=%%i"
    goto :GH_FOUND
)
:GH_FOUND
echo [OK] %GH_VER% 감지됨.

:: ----------------------------------------------------------------------------
:: 4. Check GitHub Auth Status
:: ----------------------------------------------------------------------------
echo [CHECK] GitHub 로그인 상태 확인 중...
gh auth status >nul 2>nul
if %ERRORLEVEL% neq 0 (
    echo [WARN] GitHub CLI에 로그인되어 있지 않습니다.
    echo        'gh auth login' 명령을 시작합니다. 지시에 따라 로그인해주세요.
    gh auth login
    if %ERRORLEVEL% neq 0 (
        echo [ERROR] GitHub 로그인이 완료되지 않았습니다.
        goto :FAIL
    )
)
echo [OK] GitHub 로그인 확인 완료.

:: ----------------------------------------------------------------------------
:: 5. Check Git User Config
:: ----------------------------------------------------------------------------
echo [CHECK] Git 사용자 정보 확인 중...
for /f "tokens=*" %%i in ('git config user.name 2^>nul') do set "GIT_USER_NAME=%%i"
for /f "tokens=*" %%i in ('git config user.email 2^>nul') do set "GIT_USER_EMAIL=%%i"

if "%GIT_USER_NAME%"=="" (
    echo [WARN] Git user.name이 설정되지 않았습니다. 기본값을 설정합니다.
    git config --global user.name "NukkiStudio Developer"
)
if "%GIT_USER_EMAIL%"=="" (
    echo [WARN] Git user.email이 설정되지 않았습니다. 기본값을 설정합니다.
    git config --global user.email "dev@nukkistudio.local"
)
echo [OK] Git 커밋 작성자: %GIT_USER_NAME% ^<%GIT_USER_EMAIL%^>

:: ----------------------------------------------------------------------------
:: 6. Build & Test Verification
:: ----------------------------------------------------------------------------
echo.
echo [CHECK] 솔루션 빌드 및 단위 테스트 실행 중...
dotnet test NukkiStudio.sln --configuration Release --verbosity minimal
if %ERRORLEVEL% neq 0 (
    echo [ERROR] 단위 테스트 또는 빌드에 실패했습니다. 코드를 확인해주세요.
    goto :FAIL
)
echo [OK] 빌드 및 모든 단위 테스트 통과 완료!

:: ----------------------------------------------------------------------------
:: 7. Initialize Git Repository
:: ----------------------------------------------------------------------------
echo.
echo [GIT] 로컬 Git Repository 초기화 확인 중...
if not exist ".git" (
    git init -b %DEFAULT_BRANCH%
    echo [OK] Git 저장소가 %DEFAULT_BRANCH% 브랜치로 초기화되었습니다.
) else (
    echo [OK] 기존 Git 저장소가 이미 존재합니다.
    git branch -M %DEFAULT_BRANCH% 2>nul
)

:: ----------------------------------------------------------------------------
:: 8. Stage and Commit
:: ----------------------------------------------------------------------------
echo [GIT] 파일 스테이징 및 커밋 생성 중...
git add .

git rev-parse --verify HEAD >nul 2>nul
if %ERRORLEVEL% neq 0 (
    git commit -m "feat: initial release of NukkiStudio v1.0.0 (Video & Image AI Matting Studio)"
    echo [OK] 초기 커밋이 생성되었습니다.
) else (
    git diff --cached --quiet
    if %ERRORLEVEL% neq 0 (
        git commit -m "chore: update project assets, workflows, and documentation"
        echo [OK] 변경 사항이 커밋되었습니다.
    ) else (
        echo [OK] 커밋할 새로운 변경 사항이 없습니다.
    )
)

:: ----------------------------------------------------------------------------
:: 9. GitHub Remote & Repository Creation (Idempotent)
:: ----------------------------------------------------------------------------
echo.
echo [REPO] GitHub 저장소(%REPO_NAME%) 확인 및 생성...

:: Check if repository already exists on GitHub
gh repo view %REPO_NAME% >nul 2>nul
if %ERRORLEVEL% neq 0 (
    echo [REPO] 원격 저장소가 없으므로 신규 생성합니다: %REPO_NAME% (%VISIBILITY%)
    gh repo create %REPO_NAME% --%VISIBILITY% --description "%REPO_DESC%" --source=. --remote=origin --push
    if %ERRORLEVEL% neq 0 (
        echo [ERROR] GitHub 저장소 생성에 실패했습니다.
        goto :FAIL
    )
    echo [OK] 저장소 생성 및 초기 푸시 완료.
) else (
    echo [OK] GitHub 저장소가 이미 존재합니다.
    :: Ensure origin remote is linked
    git remote get-url origin >nul 2>nul
    if %ERRORLEVEL% neq 0 (
        for /f "tokens=*" %%u in ('gh repo view %REPO_NAME% --json url -q .url') do set "REMOTE_URL=%%u"
        git remote add origin !REMOTE_URL!
        echo [OK] remote 'origin' 연결됨: !REMOTE_URL!
    )
    echo [GIT] %DEFAULT_BRANCH% 브랜치로 푸시 중...
    git push -u origin %DEFAULT_BRANCH%
)

:: ----------------------------------------------------------------------------
:: 10. Update Repo Metadata (Topics & Description)
:: ----------------------------------------------------------------------------
echo.
echo [META] GitHub Repository 설정 및 토픽 적용 중...
gh repo edit %REPO_NAME% --description "%REPO_DESC%" --add-topic "%REPO_TOPICS%" 2>nul
echo [OK] Repository Description 및 Topics 적용 완료.

:: ----------------------------------------------------------------------------
:: 11. Create & Push Initial Tag (v1.0.0)
:: ----------------------------------------------------------------------------
echo.
echo [TAG] 초기 릴리즈 태그(%INITIAL_TAG%) 확인 중...
git rev-parse %INITIAL_TAG% >nul 2>nul
if %ERRORLEVEL% neq 0 (
    git tag -a %INITIAL_TAG% -m "Release %INITIAL_TAG%: Video & Image AI Background Removal Studio"
    git push origin %INITIAL_TAG%
    echo [OK] 태그 %INITIAL_TAG% 생성 및 푸시 완료.
) else (
    echo [OK] 태그 %INITIAL_TAG%가 이미 로컬에 존재합니다.
)

:: ----------------------------------------------------------------------------
:: 12. Retrieve & Display Final URLs
:: ----------------------------------------------------------------------------
for /f "tokens=*" %%u in ('gh repo view %REPO_NAME% --json url -q .url 2^>nul') do set "FINAL_REPO_URL=%%u"

echo.
echo ============================================================================
echo   [SUCCESS] NukkiStudio 배포 및 동기화가 성공적으로 완료되었습니다!
echo ============================================================================
echo.
echo   - GitHub Repository: %FINAL_REPO_URL%
echo   - Actions CI/CD:    %FINAL_REPO_URL%/actions
echo   - GitHub Pages:     https://ko9ma7.github.io/%REPO_NAME%/
echo.
echo   다음 명령으로 언제든지 애플리케이션을 즉시 실행할 수 있습니다:
echo     dotnet run --project NukkiStudio/NukkiStudio.csproj
echo.
pause
exit /b 0

:FAIL
echo.
echo ============================================================================
echo   [FAIL] 배포 중 오류가 발생했습니다. 위의 메시지를 확인해주세요.
echo ============================================================================
echo.
pause
exit /b 1
