# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 프로젝트 개요

기존 MFC(Win32)로 개발된 `KFTCOneCAP` 가맹점 결제 단말 관리 프로그램의 **홈 화면**과 **리더기 설정 화면**을 C# WPF로 동일한 UI/UX와 비즈니스 로직으로 재구현하는 프로젝트. 1차 범위는 이 두 화면이며, 나머지 화면(가맹점 설정, 결제, 전표 설정 등)은 범위 밖이다.

**현재 상태: 1차 범위(홈 화면 + 리더기 설정 화면 UX/UI 재구현, Phase 0~6) 완료**. 문서는 `docs/home_reader_setup/`(PRD_WPF.md, ROADMAP.md, screenshots/)에 모아 관리한다. 다음 기능 추가는 새 PRD 문서로 시작하되, `docs/home_reader_setup/ROADMAP.md`는 계속 이어서 사용한다(Phase 7부터 추가).

## 빌드 / 실행

- 타겟 프레임워크: **`net48` (.NET Framework 4.8)** — Windows 7(SP1) 지원 요구사항 때문. .NET 6/8 등 최신 .NET(Core 계열)은 Windows 7에서 실행 자체가 불가하므로 사용 금지. 상세: `docs/home_reader_setup/PRD_WPF.md` 1.4장.
- 솔루션: `KFTCOneCAP.Wpf.sln` (루트) → 프로젝트: `src/KFTCOneCAP.Wpf/KFTCOneCAP.Wpf.csproj`
- 빌드: `dotnet build` (루트에서 실행)
- 실행: `dotnet run --project src/KFTCOneCAP.Wpf/KFTCOneCAP.Wpf.csproj`
- MVVM: `CommunityToolkit.Mvvm` 사용 (소스 제너레이터 기반 `[ObservableProperty]`/`[RelayCommand]`, .NET Framework 4.6.2+ 호환 확인됨)
- **Windows 10 1809+ 전용 API(DWM 이머시브 타이틀바 등) 사용 시 반드시 OS 버전 체크 후 조건부 적용** — Win7에서 no-op 처리 필요 (원본 MFC 앱도 동일 패턴).

## 반드시 먼저 읽을 문서

- **`docs/home_reader_setup/PRD_WPF.md`** — 요구사항 정의서. 색상/폰트/레이아웃 수치, 화면별 상세 동작(버튼 로직, 토글, AOP 제약, 레지스트리 매핑)의 단일 진실 공급원(source of truth). 구현 중 애매한 부분이 있으면 추측하지 말고 이 문서를 먼저 확인한다.
- **`docs/home_reader_setup/ROADMAP.md`** — Phase 0~8 순서대로 진행되는 개발 계획. 반드시 이 순서를 따르고, 각 Phase의 "완료 기준"(빌드+실행+스크린샷 대조)을 통과한 뒤에만 다음 Phase로 넘어간다. PRD와 실제 구현이 어긋나면 코드보다 먼저 PRD를 갱신한다.
- **`docs/home_reader_setup/screenshots/home_screen.png`, `docs/home_reader_setup/screenshots/reader_setup.png`** — 원본 MFC 앱을 실행해 캡처한 실측 화면. 텍스트/색상/레이아웃을 소스 코드 리터럴보다 우선하는 근거로 삼는다(빌드된 실행 파일과 소스가 일부 어긋나는 것이 이미 확인됨 — PRD 6장 미확정 사항 #7 참고).

## 원본 MFC 소스 (참고용, 이 저장소 밖)

`C:\Project\MerchantSetup_OnPaintIcons_Clean_CP949\` — 재구현 대상 원본. 화면 동작/레이아웃/문구를 확인할 때 이 소스를 직접 열어 대조할 수 있다.
- 소스 파일은 **CP949 인코딩**이다. 이 저장소(WPF/UTF-8)에는 해당하지 않지만, 원본 소스를 읽거나 실행 중인 원본 앱과 비교할 때는 인코딩 차이를 주의한다.
- 원본은 시스템 트레이에 상주하며 시작 시 자동으로 최소화된다. 화면을 스크린샷으로 확인하려면 트레이 아이콘을 통해 창을 복원해야 한다(자동화된 `SetForegroundWindow` 호출은 Windows 포커스 잠금 정책으로 신뢰성이 낮았음 — 필요하면 사용자에게 직접 창을 띄워달라고 요청하는 편이 안전하고 빠르다).

## 서브에이전트

`.claude/agents/csharp-wpf-developer.md` — 이 프로젝트의 WPF 개발 전담 에이전트. UX/UI(XAML)와 비즈니스 로직(ViewModel/서비스)을 통합해서 다룬다. `mcp__windows__*` 도구(스크린샷/클릭/스냅샷 등)를 갖추고 있어 빌드 후 실제 화면을 캡처해 원본과 대조하는 검증까지 책임진다 — 코드 작성만 하고 검증을 생략하지 않는다.
