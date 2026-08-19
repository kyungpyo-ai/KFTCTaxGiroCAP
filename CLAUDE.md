# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 프로젝트 개요

기존 MFC(Win32)로 개발된 `KFTCOneCAP` 가맹점 결제 단말 관리 프로그램의 **홈 화면**과 **리더기 설정 화면**을 C# WPF로 동일한 UI/UX와 비즈니스 로직으로 재구현하는 프로젝트. 1차 범위는 이 두 화면이며, 나머지 화면(가맹점 설정, 결제, 전표 설정 등)은 범위 밖이다.

**현재 상태: 1차 범위(홈 화면 + 리더기 설정 화면 UX/UI 재구현, Phase 0~6) 완료.** 1차 범위 문서는 `docs/home_reader_setup/`(PRD_WPF.md, ROADMAP.md, screenshots/)에 모아 관리한다.

**2차 범위(결제 중계 기능, Phase 7~, 준비 단계): `docs/payment_relay/PRD.md`가 요구사항 정본.** 소켓 서버(`localhost:8002`)로 POS 결제 요청을 받아 `ReaderSerial.dll`로 카드를 리딩하고 `KFTC_GIRO.dll`(`FNAISCRDVAN`)로 VAN 서버에 결제를 요청하는 기능을 **같은 `KFTCOneCAP.Wpf` 앱에 통합**한다(별도 실행 파일 아님). Phase 진행은 `docs/payment_relay/ROADMAP.md`에 Phase 7부터 이어서 기록한다.

## 빌드 / 실행

- 타겟 프레임워크: **`net48` (.NET Framework 4.8)** — Windows 7(SP1) 지원 요구사항 때문. .NET 6/8 등 최신 .NET(Core 계열)은 Windows 7에서 실행 자체가 불가하므로 사용 금지. 상세: `docs/home_reader_setup/PRD_WPF.md` 1.4장.
- 솔루션: `KFTCOneCAP.Wpf.sln` (루트) → 프로젝트: `src/KFTCOneCAP.Wpf/KFTCOneCAP.Wpf.csproj`
- 빌드: `dotnet build` (루트에서 실행)
- 실행: `dotnet run --project src/KFTCOneCAP.Wpf/KFTCOneCAP.Wpf.csproj`
- MVVM: `CommunityToolkit.Mvvm` 사용 (소스 제너레이터 기반 `[ObservableProperty]`/`[RelayCommand]`, .NET Framework 4.6.2+ 호환 확인됨). **1차 범위(Phase 0~6)는 패키지만 넣고 실제로는 코드비하인드로 구현됐다** — Phase 7(`docs/payment_relay/ROADMAP.md`)에서 두 화면을 ViewModel 기반으로 전환하며, **그 이후 모든 화면 작업은 MVVM으로 한다**(새 화면을 코드비하인드로 만들지 않는다). 단 트레이 아이콘·DWM 타이틀바처럼 창 핸들/OS에 직접 묶인 코드는 코드비하인드에 남긴다.
- **Windows 10 1809+ 전용 API(DWM 이머시브 타이틀바 등) 사용 시 반드시 OS 버전 체크 후 조건부 적용** — Win7에서 no-op 처리 필요 (원본 MFC 앱도 동일 패턴).

## 반드시 먼저 읽을 문서

- **`docs/home_reader_setup/PRD_WPF.md`** — 요구사항 정의서. 색상/폰트/레이아웃 수치, 화면별 상세 동작(버튼 로직, 토글, AOP 제약, 레지스트리 매핑)의 단일 진실 공급원(source of truth). 구현 중 애매한 부분이 있으면 추측하지 말고 이 문서를 먼저 확인한다.
- **`docs/home_reader_setup/ROADMAP.md`** — 1차 범위 개발 계획(Phase 0~6, 완료). 반드시 순서를 따르고, 각 Phase의 "완료 기준"(빌드+실행+스크린샷 대조)을 통과한 뒤에만 다음 Phase로 넘어간다. PRD와 실제 구현이 어긋나면 코드보다 먼저 PRD를 갱신한다.
- **`docs/home_reader_setup/screenshots/home_screen.png`, `docs/home_reader_setup/screenshots/reader_setup.png`** — 원본 MFC 앱을 실행해 캡처한 실측 화면. 텍스트/색상/레이아웃을 소스 코드 리터럴보다 우선하는 근거로 삼는다(빌드된 실행 파일과 소스가 일부 어긋나는 것이 이미 확인됨 — PRD 6장 미확정 사항 #7 참고).
- **`docs/payment_relay/PRD.md`** — 2차 범위(결제 중계 기능) 요구사항 정본. `KFTC_GIRO.dll`(VAN 연동)은 별도 SPEC 문서가 없어 이 PRD의 §2.3이 유일한 계약 정보다 — 임의로 필드를 추측하지 않는다.

**2차 범위는 `docs/payment_relay/` 한 폴더에 3단 문서 구성(PRD → ROADMAP → 실행계획서)으로 모아 관리한다**: `PRD.md`(무엇을) → `ROADMAP.md`(어떤 순서로 — Phase 7~18 + 계층 구조 설계 원칙) → `development_plan.md`(Task 단위 작업 지시/완료 조건, 각 Phase 착수 직전 작성). 같은 폴더에 `dll/`(KFTC_GIRO.dll)과 `images/`(결제 알림창 자산)도 있다. 코드 작성은 해당 Phase의 실행계획서가 준비된 뒤에 시작한다.

**Phase 번호는 두 ROADMAP에 걸쳐 이어진다** — 1차 0~6(`docs/home_reader_setup/ROADMAP.md`), 2차 7~(`docs/payment_relay/ROADMAP.md`). 같은 앱을 계속 확장하는 것이므로 번호를 새로 시작하지 않는다.

## 원본 MFC 소스 (참고용, 이 저장소 밖)

`C:\Project\MerchantSetup_OnPaintIcons_Clean_CP949\` — 재구현 대상 원본. 화면 동작/레이아웃/문구를 확인할 때 이 소스를 직접 열어 대조할 수 있다.
- 소스 파일은 **CP949 인코딩**이다. 이 저장소(WPF/UTF-8)에는 해당하지 않지만, 원본 소스를 읽거나 실행 중인 원본 앱과 비교할 때는 인코딩 차이를 주의한다.
- 원본은 시스템 트레이에 상주하며 시작 시 자동으로 최소화된다. 화면을 스크린샷으로 확인하려면 트레이 아이콘을 통해 창을 복원해야 한다(자동화된 `SetForegroundWindow` 호출은 Windows 포커스 잠금 정책으로 신뢰성이 낮았음 — 필요하면 사용자에게 직접 창을 띄워달라고 요청하는 편이 안전하고 빠르다).

## 서브에이전트

- `.claude/agents/csharp-wpf-developer.md` — 이 프로젝트의 WPF 개발 전담 에이전트. UX/UI(XAML)와 비즈니스 로직(ViewModel/서비스)을 통합해서 다룬다. `mcp__windows__*` 도구(스크린샷/클릭/스냅샷 등)를 갖추고 있어 빌드 후 실제 화면을 캡처해 원본과 대조하는 검증까지 책임진다 — 코드 작성만 하고 검증을 생략하지 않는다.
- `.claude/agents/reader-pinpad-spec-expert.md` — 리더기/핀패드 SPEC 원문 및 `ReaderSerial.dll` API 계약 확인 전담(아래 "리더기 연동 DLL" 절 참고).
- `.claude/agents/reader-dll-integration-developer.md` — `ReaderSerial.dll` P/Invoke 연동 개발 전담(아래 "리더기 연동 DLL" 절 참고).

## 리더기 연동 DLL (2차 개발, 준비 단계)

리더기 설정/결제 화면에 실제 하드웨어를 연동할 `ReaderSerial.dll`은 별도 저장소 `C:\Project\KFTCReaderDLL`에서
이미 완성되어 있다(Win32/x86 전용, 공개 API 5종 + CALLBACK 2종). 이 저장소는 그 DLL을 **소비하는** 입장이며,
연동에 필요한 참조 자료를 `docs/reader_dll/`(연동 가이드·API 명세·오류 코드·SPEC PDF)와
`vendor/ReaderSerial/`(DLL/헤더/lib, 검증된 C# P/Invoke 예제)에 스냅샷으로 가져와 뒀다 — 먼저
`docs/reader_dll/00_OVERVIEW.md`를 읽는다. DLL 연동 SPEC 확인은 `reader-pinpad-spec-expert`, 실제 P/Invoke
구현은 `reader-dll-integration-developer` 서브에이전트에 위임한다. 이 DLL 자체의 소스 수정은 이 저장소의
범위 밖이다(원본 저장소에서 진행). 연동 요구사항은 `docs/payment_relay/PRD.md`에 정리돼 있으며, 실제 P/Invoke
연동은 Phase 9부터 시작한다(`docs/payment_relay/ROADMAP.md`).
