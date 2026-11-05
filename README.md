# Nukki Studio (누끼 스튜디오)

> **C# .NET 8 WPF 기반 고성능 영상(Video) & 이미지(Image) AI 배경 제거 전문 데스크톱 스튜디오**

[![CI Build & Test](https://github.com/kohojung/NukkiStudio/actions/workflows/build-and-release.yml/badge.svg)](https://github.com/kohojung/NukkiStudio/actions)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%2F11%20(x64)-blue.svg)](https://dotnet.microsoft.com/)
[![Target](https://img.shields.io/badge/.NET-8.0%20(LTS)-purple.svg)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

---

## 💡 프로젝트 소개 (Overview)

**Nukki Studio**는 크리에이터, 비디오 편집자, 모션 디자이너, 일반 사용자가 동영상과 사진의 배경을 손쉽고 완벽하게 분리(누끼 따기)할 수 있도록 설계된 프로페셔널 윈도우 데스크톱 애플리케이션입니다.

파이썬 환경 구축이나 복잡한 CLI 명령 없이, 순수 C#과 **Microsoft.ML.OnnxRuntime**, 그리고 **FFmpeg 스트리밍 파이프라인**을 결합하여 고품질의 투명 비디오(Apple ProRes 4444, WebM VP9) 및 투명 이미지(PNG, WebP)를 초고속으로 제작합니다.

---

## ✨ 핵심 기능 (Key Features)

### 1. 🎬 영상 & 이미지 완전 지원 (Video & Image Matting)
- **비디오**: MP4, MOV, WebM, MKV, AVI, WMV 등 주요 영상 포맷 지원.
- **이미지**: PNG, JPG, JPEG, WebP, BMP 등 주요 이미지 포맷 지원.
- **일괄 배치(Batch) 처리**: 여러 개의 영상과 이미지를 한 번에 대기열에 드래그 앤 드롭하여 연속 처리.

### 2. ⚡ 듀얼 매팅 엔진 (Dual Matting Engines)
- **🟩 초고속 크로마키 (Color Keying)**:
  - 녹색(Green), 청색(Blue) 스크린뿐만 아니라 **화면 클릭 스포이트(Eyedropper)**로 모든 배경색을 정밀 타겟팅.
  - **오차 허용도(Tolerance)**, **경계 부드러움(Smoothness)**, **녹색 반사광 억제(Spill Suppression)** 실시간 조절.
  - 비메모리 복사 포인터(unsafe) 연산으로 4K 프레임을 밀리초 단위로 처리.
- **🧠 AI 신경망 매팅 (ONNX Runtime Deep Learning)**:
  - **BRIA RMBG-1.4**: 인물, 사물, 동물, 제품을 아우르는 최신 고정밀 배경 제거 모델.
  - **MODNet**: 인물 영상에 특화된 실시간 웹캠/비디오 포트레이트 매팅 모델.
  - **U2Net-P**: 4MB 초경량 고속 매팅 모델.
  - 원클릭 자동 다운로드 및 사용자 로컬 `.onnx` 모델 즉시 연동 지원.

### 3. 🎞️ 프로페셔널 비디오 내보내기 (Export Formats)
- **Apple ProRes 4444 (`.mov`)**: Premiere Pro, After Effects, DaVinci Resolve, Final Cut Pro에서 즉시 쓸 수 있는 투명 알파 채널 포함 최고급 비디오.
- **WebM VP9 (`.webm`)**: 웹 브라우저 및 현대적 NLE 소프트웨어용 투명 비디오.
- **크로마키 그린스크린 (`.mp4`)**: 범용 편집을 위해 깨끗한 `#00FF00` 배경으로 대체된 H.264 MP4.
- **배경 교체 MP4 (`.mp4`)**: 원하는 단색 컬러나 사용자 지정 배경 이미지로 합성된 H.264 MP4.
- **알파 마스크 비디오 (`.mp4`)**: 그레이스케일 매트 비디오.
- **투명 PNG 시퀀스**: VFX / 3D 아티스트(Blender, Unreal Engine)를 위한 프레임별 PNG 폴더 출력.
- **오디오 트랙 보존**: 원본 영상의 오디오 스트림을 손실 없이 패스스루 muxing.

### 4. 🚀 제로-디스크 스트리밍 파이프라인 (Streaming Pipeline)
- 수만 장의 임시 이미지 파일을 디스크에 생성하여 하드웨어를 마모시키지 않습니다.
- FFmpeg 디코더의 `stdout` 파이프와 인코더의 `stdin` 파이프를 C# 메모리 상에서 직접 연결하여, **디스크 I/O 병목 없이 메모리 스트리밍 방식으로 렌더링**합니다.

### 5. 🎛️ 실시간 분할 비교 인터페이스 (Interactive Split View)
- 좌측(원본)과 우측(누끼 결과)을 하나의 화면에서 실시간 슬라이더로 대조하며 최적의 파라미터를 조율할 수 있습니다.
- 고대비 체커보드(Checkerboard) 캔버스를 통해 알파 투명도 경계면을 즉시 확인합니다.

---

## 🏛️ 시스템 아키텍처 (System Architecture)

```text
[User / Drag & Drop Media]
          │
          ▼
┌────────────────────────────────────────────────────────┐
│               WPF MVVM Presentation Layer              │
│  - MainWindow.xaml (Interactive Split View, Inspector) │
│  - MainViewModel.cs (Queue, Live Tuning, Progress)     │
└───────────────────────────┬────────────────────────────┘
                            │
          ┌─────────────────┴─────────────────┐
          ▼                                   ▼
┌──────────────────────┐            ┌──────────────────────┐
│  Image Matting Flow  │            │  Video Streaming Flow│
│  - BitmapSource      │            │  - FFmpegService.cs  │
│  - ImageProcessor.cs │            │  - RawVideo Pipe     │
└─────────┬────────────┘            └──────────┬───────────┘
          │                                    │
          └─────────────────┬──────────────────┘
                            ▼
        ┌──────────────────────────────────────┐
        │        IMattingEngine Interface      │
        ├──────────────────┬───────────────────┤
        │ ChromaKeyEngine  │  OnnxMattingEngine│
        │ - Tolerance/Edge │  - RMBG / MODNet  │
        │ - Spill Removal  │  - DirectML / CPU │
        └──────────────────┴───────────────────┘
                            │
                            ▼
┌────────────────────────────────────────────────────────┐
│                    Export Pipeline                     │
│  - Images: Transparent PNG / WebP / Composite JPG      │
│  - Video: ProRes 4444 MOV / WebM VP9 / H.264 MP4       │
│  - Direct Audio Muxing (-c:a copy)                     │
└────────────────────────────────────────────────────────┘
```

---

## 📁 프로젝트 폴더 구조 (File Structure)

```text
d:\영상 누끼\
├── .github/
│   └── workflows/
│       └── build-and-release.yml    # GitHub Actions CI/CD & 자동 배포
├── NukkiStudio/                     # WPF 메인 애플리케이션 (.NET 8-windows)
│   ├── Assets/                      # 애플리케이션 아이콘 및 그래픽
│   ├── Converters/                  # XAML 바인딩 컨버터
│   ├── Models/                      # 도메인 모델, 열거형, 메타데이터
│   ├── Services/                    # 핵심 비즈니스 로직
│   │   ├── IMattingEngine.cs        # 매팅 엔진 추상화
│   │   ├── ChromaKeyMattingEngine.cs# 초고속 크로마키 엔진
│   │   ├── OnnxMattingEngine.cs     # ONNX Runtime AI 딥러닝 엔진
│   │   ├── ModelManager.cs          # AI 모델 다운로더 및 관리자
│   │   ├── FFmpegService.cs         # FFmpeg 프로브 및 스트리밍 파이프라인
│   │   ├── ImageProcessor.cs        # 고속 픽셀 버퍼 및 포맷 인코더
│   │   └── DialogService.cs         # 파일/폴더 다이얼로그 서비스
│   ├── ViewModels/                  # MVVM 뷰모델 (CommunityToolkit.Mvvm)
│   │   └── MainViewModel.cs
│   ├── Views/                       # WPF 뷰
│   │   ├── MainWindow.xaml
│   │   └── MainWindow.xaml.cs
│   ├── App.xaml / App.xaml.cs
│   └── NukkiStudio.csproj
├── NukkiStudio.Tests/               # xUnit 단위 테스트 프로젝트
│   ├── ChromaKeyTests.cs            # 크로마키 알고리즘 검증
│   ├── FFmpegArgsTests.cs           # 코덱 인코딩 파라미터 검증
│   ├── ImageProcessorTests.cs       # 픽셀 변환 검증
│   └── NukkiStudio.Tests.csproj
├── web-preview/                     # GitHub Pages 배포용 웹 프리뷰 & 브랜딩
│   ├── index.html
│   ├── site.webmanifest
│   └── favicon.ico
├── NukkiStudio.sln                  # 표준 Visual Studio 솔루션
├── .gitignore                       # Git 제외 파일 설정
├── LICENSE                          # MIT 라이선스
├── README.md                        # 프로젝트 설명서
└── github-bootstrap.cmd             # 윈도우 원클릭 깃허브 배포 스크립트
```

---

## 🛠️ 개발 환경 및 요구사항 (Prerequisites)

1. **운영체제**: Windows 10 (Build 19041 이상) 또는 Windows 11 (x64)
2. **.NET SDK**: [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) 이상
3. **FFmpeg**: 비디오 처리를 위해 `ffmpeg.exe` 및 `ffprobe.exe`가 필요합니다.
   - 앱이 시스템 PATH, mise, scoop, winget 경로를 자동으로 탐색합니다.
   - Winget으로 간편 설치: `winget install Gyan.FFmpeg`
4. **Git & GitHub CLI** (저장소 생성 및 배포 시): `git`, `gh`

---

## 🚀 빌드 및 실행 방법 (Build & Run)

### 1. 솔루션 빌드
```bash
dotnet restore NukkiStudio.sln
dotnet build NukkiStudio.sln --configuration Release
```

### 2. 단위 테스트 실행
```bash
dotnet test NukkiStudio.sln
```

### 3. 애플리케이션 실행
```bash
dotnet run --project NukkiStudio/NukkiStudio.csproj
```

### 4. 독립 실행형 단일 실행 파일(Single-File Exe) 배포 빌드
```bash
dotnet publish NukkiStudio/NukkiStudio.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
```
`./publish/NukkiStudio.exe` 파일 하나만 배포하면 .NET 런타임이 설치되지 않은 PC에서도 바로 동작합니다.

---

## 📦 윈도우 원클릭 GitHub Bootstrap 스크립트

Windows 환경에서 Git 초기화, 커밋, 원격 Repository 생성, push, Pages 활성화, 태그 생성을 단 한 번의 더블 클릭으로 수행할 수 있는 `github-bootstrap.cmd`를 제공합니다.

### 실행 방법
```cmd
# cmd 또는 탐색기에서 더블클릭
github-bootstrap.cmd
```

- **스크립트 동작 순서**:
  1. `[CHECK]` Git, .NET SDK, GitHub CLI(`gh`) 설치 상태 확인
  2. `[CHECK]` `gh auth status` 로그인 상태 확인 (필요시 자동 로그인 안내)
  3. `[CHECK]` Git 사용자 환경 설정 확인
  4. `[BUILD]` `dotnet test NukkiStudio.sln`으로 자동 검증
  5. `[GIT]` `git init` 및 `main` 브랜치 설정
  6. `[REPO]` GitHub에 저장소가 없으면 자동 생성 (`gh repo create`)
  7. `[COMMIT]` Conventional Commits 규격의 첫 커밋 생성
  8. `[PUSH]` 원격 저장소로 push 및 GitHub Pages 설정
  9. `[RELEASE]` `v1.0.0` 태그 생성 및 배포 완료 확인

---

## 📄 라이선스 (License)

이 프로젝트는 [MIT License](LICENSE)에 따라 배포됩니다. 자유롭게 수정하고 상업적/비상업적 목적으로 활용하실 수 있습니다.
