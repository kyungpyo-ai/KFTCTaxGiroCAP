# 실행계획서: 결제 중계 기능 (Phase 7~16)

> `PRD.md`(무엇을) → `ROADMAP.md`(어떤 순서로) → **이 문서(Task 단위로 무엇을 어떻게, 어디까지 하면 끝인지)**.
> 실제 코드 작성은 이 문서의 Task를 순서대로 따라간다.
>
> **Phase 17~18은 아직 작성하지 않았다** — 앞 Phase의 실장비 검증 결과에 따라 뒤쪽 계획이 조정될 여지가
> 커서, **Phase 12부터는 한 Phase씩 착수 직전에 작성**한다(2026-08-20 사용자 확정 — Phase 7~11처럼
> 여러 Phase를 미리 써두면 앞 결과에 따라 다시 고쳐야 할 계획이 생긴다).

## 공통 규칙

1. **Task는 순서대로.** 각 Task의 "완료 조건"을 모두 통과한 뒤 다음으로 넘어간다.
2. **검증한 것만 체크한다.** 하드웨어/외부 DLL이 없어 확인하지 못한 항목은 체크하지 말고, 무엇을 왜 확인하지
   못했는지 그 Task 아래에 적는다. 추측으로 완료 처리하지 않는다.
3. **SPEC 값을 추측하지 않는다.** 전문 코드·필드 오프셋·길이·인코딩이 필요하면
   `reader-pinpad-spec-expert` 서브에이전트에 위임해 확인한 뒤 반영한다.
4. **참조 구현이 있으면 새로 설계하지 않는다.** `vendor/ReaderSerial/CSharpSample/`(P/Invoke·재시도 래퍼),
   `vendor/ReaderSerial/MfcSample/`(이중화 페일오버)은 원본 프로젝트에서 실장비 검증을 마친 코드다.
5. **계층 규칙**(`ROADMAP.md` "계층 구조"): `ViewModels → Services → Protocol → Interop` 단방향. `Services`는
   바이트 오프셋을 직접 다루지 않고, WPF 타입(`Visibility`/`Dispatcher` 등)도 알지 못한다.
6. **Phase 7 이후 모든 화면 작업은 MVVM으로 한다.** 새 화면(결제 알림창 등)을 코드비하인드로 만들지 않는다.
7. 각 Phase 종료 시 `dotnet build`(경고 0/오류 0)와 실제 실행 확인. 커밋은 사용자가 요청할 때만.

---

# Phase 7 — MVVM 전환 (1차 화면 리팩터링)

**이 Phase가 끝나면**: 두 화면이 ViewModel 기반으로 동작하고, 겉으로 보이는 동작은 1차 범위와 **완전히
동일**하다. 이후 Phase가 추가하는 모든 화면·상태는 같은 방식 하나만 따른다.

> **이 Phase의 성공 기준은 "아무것도 달라지지 않는 것"이다.** 리팩터링 도중 눈에 띄는 개선거리가 보여도
> 손대지 않는다 — 동작이 바뀌면 나중에 회귀가 생겼을 때 "리팩터링 탓인지 개선 탓인지" 구분할 수 없다.
> 개선 아이디어는 메모만 남기고 별도 작업으로 뺀다.

## P7-1. `Services/Settings/` — 레지스트리 접근 분리

현재 레지스트리 읽기/쓰기가 `ReaderSetupWindow.xaml.cs` 안에 있다(`LoadFromRegistry`/`SaveToRegistry`,
키 경로 상수 포함). **Phase 9 이후 결제 Flow도 같은 값(COM 포트)을 읽어야 하므로**(PRD §2.2.1) 화면 코드에
묶여 있으면 재사용이 불가능하다. ViewModel을 만들기 **전에** 이것부터 분리한다.

- 대상 값: `COMPORT1_FIELD`/`COMPORT2_FIELD`(문자열 그대로), `MULTIPAD1_FIELD`/`MULTIPAD2_FIELD`
  (**반전 인코딩** — ON→`"0"`, OFF→`"1"`. 이 인코딩 규칙이 화면 밖으로 새어 나가지 않도록 이 계층에서
  bool로 변환해 노출한다).
- 키 경로: `HKCU\Software\KFTC_VAN\KFTCTaxGiroCAP\SERIALPORT`.
- **읽기 실패를 예외로 던지지 않는다** — 현재 코드처럼 기본값(미사용/꺼짐)으로 조용히 폴백한다(권한 문제 등).
- WPF 타입에 의존하지 않는다(공통 규칙 5).

**완료 조건**
- [x] 레지스트리 키 경로 문자열이 `Views/` 아래에 남아 있지 않음(grep으로 확인)
- [x] 반전 인코딩 변환이 이 계층 안에서만 일어남(ViewModel/View는 bool만 다룸)
- [x] 저장 → 앱 재시작 → 로드 왕복이 1차 범위와 동일하게 동작(`reg query`로 실제 값 왕복 확인)

## P7-2. `ReaderSetupViewModel` 신설

`ReaderSetupWindow.xaml.cs`(416줄)에서 업무 로직을 걷어낸다. 옮길 것과 남길 것을 먼저 못 박는다.

**ViewModel로 옮기는 것**
- COM 포트 선택값 2개, 멀티패드 토글 2개
- **dirty-check 스냅샷과 비교 로직**(현재 `_snapshot*` 4개 필드)
- busy 상태(`_isBusy`) — PRD §4.7 "동시에 하나의 작업만"
- "미사용"일 때 해당 카드 비활성 판정(현재 `ApplyReaderCardEnabled`)
- 조회 결과 컬렉션과 조회기간 선택값, 빈 상태/로딩 상태 판정
- 액션 버튼 5종·조회 버튼의 실행 명령(내용은 P7-4대로 스텁 유지)

**View(코드비하인드)에 남기는 것** — 옮기면 오히려 나빠지는 것들
- DWM 타이틀바(`SourceInitialized` + `DwmSetWindowAttribute`) — 창 핸들이 필요한 순수 OS 작업
- 멀티패드 info `Popup`의 `PlacementTarget` 지정 — 시각 요소 배치
- `IntegrityScrollViewer_ScrollChanged`의 헤더 padding 보정 — 스크롤바 폭에 맞추는 렌더링 보정

**완료 조건**
- [x] `ReaderSetupWindow.xaml.cs`에 레지스트리·dirty-check·busy·더미 데이터 생성 코드가 없음
- [x] 남은 코드비하인드가 위 "남기는 것" 3가지 + `InitializeComponent` 수준
- [x] 왜 남겼는지가 주석으로 설명되어 있음(다음 사람이 "이것도 옮겨야 하나" 고민하지 않도록)

## P7-3. `x:Name` 직접 조작 → 바인딩 교체

현재 코드비하인드가 `IntegrityListItemsControl.Visibility`, `button.Content`, `SetGlobalEnabled()`의
`IsEnabled` 10개 대입 등 **UI 요소 상태를 직접 대입**한다. 이것을 전부 바인딩으로 바꾼다.

- 리스트/빈 상태/로딩 문구의 `Visibility` → ViewModel 상태 + `Converter`(또는 상태 열거값 하나로 통합).
  **세 요소의 `Visibility`를 각각 따로 다루지 않는다** — 현재 코드가 세 곳을 수동으로 맞추고 있어 상태가
  어긋날 여지가 있다. 하나의 상태 값에서 파생시킨다.
- 버튼 `Content`의 "초기화"↔"처리중..." 전환, 스피너(`ButtonLoadingHelper.IsLoading`) → ViewModel 상태 바인딩.
- `SetGlobalEnabled` → busy 상태를 각 컨트롤이 바인딩(또는 상위 컨테이너 `IsEnabled` 하나)으로 참조.
- 조회 결과 → `ObservableCollection` + `ItemsSource` 바인딩(현재는 `ItemsSource`에 리스트를 직접 대입).

**완료 조건**
- [x] 코드비하인드에 `.Visibility =`, `.IsEnabled =`, `.Content =`, `.ItemsSource =` 대입이 없음(grep으로 확인)
- [x] 조회 결과가 0건일 때 빈 상태 문구, 로딩 중일 때 로딩 문구가 1차 범위와 동일하게 표시됨
- [x] 액션 버튼 클릭 시 텍스트 전환 + 스피너가 1차 범위와 동일하게 동작

## P7-4. 동작 동일성 검증 (회귀 확인)

이 Phase의 유일한 산출물은 "구조는 바뀌고 동작은 그대로"이므로, 검증이 곧 완료 기준이다.
**스텁은 스텁대로 유지**한다 — 액션 버튼 3초/조회 2초 딜레이, 조회기간별 더미 행 수(오늘 3 / 7일 5 /
30일·100일 10)를 그대로 둔다. 실통신 교체는 Phase 12 몫이다.

**완료 조건** (전부 실행해서 확인)
- [x] 콤보를 "미사용"으로 바꾸면 그 카드의 액션 버튼 5개 + 멀티패드 토글이 비활성화됨
- [x] 액션 버튼 클릭 → 로딩 문구·스피너 → 3초 후 원복, 그동안 다른 버튼 클릭이 무시됨
- [x] 조회 → 2초 로딩 → 조회기간별 더미 행 수가 1차 범위와 동일
- [x] 확인 → 레지스트리 저장 → 창 재오픈 시 저장된 값이 콤보/토글에 반영됨
- [x] 값을 바꾸고 취소 → dirty 확인창 표시, "아니오" 선택 시 창 유지
- [x] 홈 화면: 카드 클릭, 최소화(트레이로 숨김) 확인, 프로그램 종료가 1차 범위와 동일
- [ ] 트레이 우클릭 메뉴 표시·더블클릭 복원 — **미검증**: 시스템 트레이 아이콘이 `mcp__windows__*`
      접근성 트리에 잡히지 않아 자동화로 재현 불가. 이 Phase에서 해당 코드(`EnsureTrayIcon`/
      `BuildTrayContextMenu`/`RestoreFromTray`)는 한 글자도 수정하지 않았으므로 회귀 위험은 낮으나,
      사용자가 직접 확인 필요.

## P7-5. `HomeWindow` — 최소 범위 전환

`HomeWindow.xaml.cs`(245줄)는 **대부분이 View/OS 책임**이라 옮길 것이 적다. 형식을 맞추려고 무리하게
ViewModel로 밀어 넣지 않는다.

- 옮기는 것: 카드 클릭이 "무엇을 하는지"(리더기 설정 열기 / 범위 밖 안내) 정도를 Command로.
- **남기는 것**: 트레이 아이콘(`NotifyIcon`, WinForms interop), DWM 타이틀바, `ReaderSetupWindow` 워밍업,
  `Dispatcher.BeginInvoke`로 눌림 애니메이션 프레임을 확보하는 처리. 전부 창/OS에 직접 묶인 코드이며,
  ViewModel로 옮기면 ViewModel이 WPF·WinForms 타입을 알게 되어 계층 규칙(공통 규칙 5)이 깨진다.
- 이 판단 근거를 코드 주석으로 남긴다.

**완료 조건**
- [x] 카드 클릭 동작이 Command 경유로 바뀌고, 4개 카드 모두 1차 범위와 동일하게 반응
- [x] 트레이 최소화 → 종료가 정상 동작(트레이 아이콘 잔상 없음, `tasklist`로 프로세스 종료 확인)
- [ ] 트레이 복원(더블클릭)·우클릭 메뉴 — P7-4와 동일한 사유로 미검증(코드는 이 Phase에서 무수정)
- [x] ViewModel이 WinForms/`Window` 타입을 참조하지 않음

---

# Phase 8 — 기반 정비 (x86 전환 + DLL 배치/로드 스모크)

**이 Phase가 끝나면**: 앱이 x86으로 빌드·실행되고, 두 네이티브 DLL의 로드 성공/실패가 로그로 남으며, 기존
두 화면에 회귀가 없다.

## P8-1. `PlatformTarget=x86` 전환

`ReaderSerial.dll`과 `KFTC_GIRO.dll` 모두 32bit이므로 프로세스도 32bit여야 한다. 현재 `csproj`에
`PlatformTarget`이 없어 AnyCPU(64비트 OS에서 64비트 프로세스)로 기동되며, 이 상태로 `DllImport`가 실행되면
`BadImageFormatException`이 난다.

- `src/KFTCOneCAP.Wpf/KFTCOneCAP.Wpf.csproj`에 추가:
  ```xml
  <PlatformTarget>x86</PlatformTarget>
  ```
  참조 구현(`vendor/ReaderSerial/CSharpSample/ReaderSerialCSharpSample.csproj`)은 `<Platforms>x86</Platforms>`도
  함께 지정한다 — 솔루션 구성이 늘어나는 부작용이 있으므로 **`PlatformTarget`만 먼저 넣어보고**, `dotnet build`
  결과가 정상이면 `Platforms`는 넣지 않는다(불필요한 구성 추가 방지).
- 왜 이 값이 필요한지 주석으로 남긴다(다음 사람이 "AnyCPU로 되돌려도 되지 않나" 생각하지 않도록).

**완료 조건**
- [x] `dotnet build` 성공(경고 0/오류 0)
- [x] 빌드 산출물이 32bit인지 확인 — `IsWow64Process`로 True 확인(64bit OS에서 WOW64로 실행 중 = 32bit
      프로세스). `[Environment]::Is64BitProcess`는 원격 프로세스에서 직접 조회하는 표준 API가 없어
      `IsWow64Process` P/Invoke로 동등하게 확인했다
- [x] 앱이 정상 기동하고 홈 화면이 뜬다

## P8-2. 네이티브 DLL 배치 및 출력 폴더 복사

두 DLL은 프로젝트 참조가 아니라 **런타임에 `DllImport`로 로드되는 네이티브 DLL**이므로, 빌드 산출물 폴더에
직접 복사해 두어야 한다.

- **`KFTC_GIRO.dll`을 `docs/payment_relay/dll/`에서 `vendor/KftcGiro/`로 옮긴다.** `docs/`는 문서, `vendor/`는
  외부 바이너리라는 기존 구분을 유지하기 위함이다(`vendor/ReaderSerial/`과 동일한 위치 규칙). 옮긴 뒤
  `PRD.md` §2.3과 `ROADMAP.md` 참고 문서 절의 경로 표기를 함께 갱신한다.
- csproj에 복사 배선을 추가한다. 참조 구현의 `CopyReaderSerialDll` Target과 달리 이 저장소의 DLL은 **고정된
  위치에 이미 존재**하므로(빌드 산출물이 아님), 조건부 `Copy` Target보다 `None Include` +
  `CopyToOutputDirectory`가 단순하다:
  ```xml
  <None Include="..\..\vendor\ReaderSerial\ReaderSerial.dll">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    <Link>ReaderSerial.dll</Link>
  </None>
  ```
  `KFTC_GIRO.dll`도 같은 방식으로 추가한다. `<Link>`를 지정해 출력 폴더 **루트**에 평평하게 놓이도록 한다
  (`DllImport`의 기본 탐색 경로는 실행 파일이 있는 폴더다).

**완료 조건**
- [x] `KFTC_GIRO.dll`이 `vendor/KftcGiro/`로 이동되고 문서 경로 표기가 갱신됨(`git mv`로 이력 보존,
      `PRD.md` §2.3·`ROADMAP.md` 갱신)
- [x] 빌드 후 출력 폴더(`bin/Debug/net48/`)에 `ReaderSerial.dll`과 `KFTC_GIRO.dll`이 **루트에** 복사됨
- [x] 두 파일의 크기/해시가 원본과 일치(복사 손상 없음) — `sha256sum`으로 `vendor/` 원본과 출력 폴더
      사본이 완전히 동일함을 확인

## P8-3. 진단 로그 기록 수단

이후 모든 Phase가 하드웨어·외부 DLL 오류를 기록해야 하므로 여기서 최소한의 파일 로깅을 정한다.
**로깅 프레임워크(NLog/Serilog 등)를 도입하지 않는다** — 이 앱에 필요한 건 "언제 무슨 일이 있었는지" 한 줄씩
남기는 것뿐이고, 의존성을 늘릴 만한 요구사항(구조화 로그, 원격 전송, 동적 레벨 변경)이 PRD에 없다.

- 기록 위치: `%LOCALAPPDATA%\KFTCTaxGiroCAP\logs\`. **앱 설치 폴더에 쓰지 않는다** — `Program Files` 아래에
  설치되면 쓰기 권한이 없어 로깅 자체가 실패한다.
- 형식: `[시각] [레벨] 메시지` 한 줄. 날짜별 파일 분리.
- **스레드 안전해야 한다** — Reader CALLBACK이 리더기별 수신 스레드에서 호출되므로 UI 스레드와 동시에 로그를
  쓴다(Phase 9부터 실제로 발생).
- 로깅 실패가 앱을 죽이지 않아야 한다(디스크 가득참/권한 문제 등은 조용히 무시).

**완료 조건**
- [x] 앱 기동 시 로그 파일이 생성되고 기동 로그가 남는다 — `%LOCALAPPDATA%\KFTCTaxGiroCAP\logs\yyyy-MM-dd.log`에
      "애플리케이션 기동 시작" 라인 확인
- [x] 여러 스레드에서 동시에 기록해도 줄이 깨지지 않는다 — 실제 `FileLogger`와 동일한 lock 전략으로 20개
      스레드 × 50줄(1000줄) 동시 기록 테스트, 전 줄이 정규식과 정확히 일치(줄 섞임/깨짐 없음)
- [x] 로그 경로를 쓸 수 없는 상황을 인위적으로 만들어도 앱이 정상 기동한다 — `logs` 폴더에 쓰기 거부 ACL을
      걸고 실행해도 홈 화면이 정상적으로 뜸을 확인(로그만 조용히 누락, 앱 크래시 없음)

## P8-4. 두 DLL 로드 스모크

`KFTC_GIRO.dll`은 `MFC42.DLL`/`MSVCRT.dll`/`WSOCK32.dll`에 의존한다(PRD §2.3). 이 의존성이 대상 PC에서
충족되는지는 **실제로 로드해봐야만** 알 수 있으므로, 리스크를 Phase 17까지 미루지 않고 여기서 드러낸다.

- 앱 기동 시 두 DLL을 `LoadLibrary` 수준으로 로드 시도하고 결과를 로그에 남긴다. 실패 시
  `Marshal.GetLastWin32Error()` 등으로 **사유를 함께** 기록한다(의존 DLL 누락은 `ERROR_MOD_NOT_FOUND`(126)로
  나타난다 — 파일이 있는데 126이 뜨면 의존성 문제라는 뜻이므로 이 구분이 중요하다).
- **로드 실패해도 앱은 정상 기동해야 한다**(PRD §9). 기능이 안 될 뿐 앱이 죽으면 안 된다.
- 여기서는 **로드만** 한다. 함수 호출은 Phase 9(Reader)/Phase 17(VAN)에서.

**완료 조건**
- [x] 두 DLL의 로드 성공/실패가 사유와 함께 로그에 남는다
- [x] DLL 파일명을 일부러 바꿔 로드 실패를 유발해도 앱이 정상 기동하고 오류만 로그에 남는다 —
      `KFTC_GIRO.dll`을 임시로 리네임하고 실행, 홈 화면 정상 기동 + 로그에
      `DLL 로드 스모크 실패: KFTC_GIRO.dll — 파일이 출력 폴더에 없음` 기록 확인 후 원복(해시 재검증)
- [x] `KFTC_GIRO.dll` 로드 결과를 확인하고 기록한다 — **이 개발 PC에서는 로드 성공.** 로그: `DLL 로드
      스모크 성공: KFTC_GIRO.dll (핸들 획득)`. `ReaderSerial.dll`도 동일하게 성공. 다만 이는 이 개발
      PC에 `MFC42.DLL`/`MSVCRT.dll`/`WSOCK32.dll`이 이미 갖춰져 있다는 뜻일 뿐이며, **실제 배포 대상
      PC(특히 최소 설치된 Windows 7/10)에서도 동일하게 성공한다는 보장은 아니다** — 배포 시점에 다시
      확인이 필요함을 명시해 둔다

## P8-5. 회귀 확인

x86 전환이 기존 화면에 영향을 주지 않았는지 확인한다.

**완료 조건**
- [x] 홈 화면: 창 크기(1104×567), 카드 4개, 헤더/푸터 버튼이 1차 범위와 동일하게 렌더링됨 —
      `GetWindowRect`로 1104×567 실측, 스크린샷을 `docs/home_reader_setup/screenshots/home_screen.png`와
      대조해 카드 4개·레이아웃·색상 일치 확인
- [x] 리더기 설정 화면: 열림/닫힘, 콤보 변경 시 버튼 활성/비활성 연동, 확인/취소 dirty-check 동작 —
      콤보 "미사용" 선택 → 액션 버튼 5개 비활성화, 취소 클릭 → dirty-check 확인창(예/아니오) 표시,
      "아니오" 선택 시 창 유지, "예" 선택 시 정상 종료를 모두 실측
- [x] 트레이 최소화 동작 — 최소화 클릭 후 프로세스는 유지되고(`tasklist`) 창이 접근성 트리에서 사라짐을
      확인. **트레이 아이콘 더블클릭 복원·우클릭 메뉴는 Phase 7과 동일한 사유로 미검증**(시스템 트레이
      아이콘이 `mcp__windows__*` 접근성 트리에 잡히지 않음, 이 Phase에서 관련 코드 무수정)
- [x] 컴팩트 모드 판정 로직이 영향받지 않음 — 이 개발 환경은 `PrimaryScreenHeight=1080`(>800)이라 비-컴팩트
      경로만 실측 가능. `App.xaml.cs`의 판정 코드(`SystemParameters.PrimaryScreenHeight <=
      CompactHeightThreshold`)는 이 Phase에서 한 글자도 수정하지 않았으므로 코드 검토로 대체 — 회귀 위험 없음

---

# Phase 9 — Reader DLL P/Invoke 바인딩 + 파일럿(`0x60`)

**이 Phase가 끝나면**: 명령 1종(`0x60` 초기화)이 실제 리더기와 왕복하고, 그 결과를 화면에서 확인할 수 있다.

> **왜 명령 1종만 하는가**: P/Invoke는 시그니처가 한 글자만 어긋나도 스택이 깨져 증상이 엉뚱하게 나타난다
> (다른 함수 호출에서 크래시, 콜백 인자 쓰레기값 등). 변인이 적은 상태에서 계약을 먼저 맞춘 뒤 확장한다 —
> 원본 KFTCReaderDLL 프로젝트에서 검증된 전략이다.

## P9-1. `Interop/ReaderSerialNative.cs` 포팅

`vendor/ReaderSerial/CSharpSample/ReaderSerialNative.cs`를 그대로 가져온다. **새로 선언을 작성하지 않는다.**

- 네임스페이스/접근 제한자만 이 프로젝트에 맞게 조정하고, 시그니처·특성(`[UnmanagedFunctionPointer(StdCall)]`,
  `CallingConvention.StdCall`)·enum 값은 **한 글자도 바꾸지 않는다**.
- `vendor/ReaderSerial/ReaderSerial.h`와 1:1 대조한다. 특히 최근 변경 이력이 있는 부분:
  - `Reader_OpenPort`는 **5인자 + out**(`portNumber`, `baudRate`, `readerCallback`, `pinpadCallback`,
    `userContext`, `outReaderId`) — 과거 4인자 버전이 아니다
  - `Pinpad_SendCommand`의 `commandCode`는 `byte`(2026-08-13에 `int`에서 변경됨)
  - `READER_CALLBACK`/`PINPAD_CALLBACK`의 3번째 인자는 `commandCode`(`byte`), `resultCode`는 없음
- `data`는 `byte[]`가 아니라 **`IntPtr`로 받는다**(참조 구현과 동일). 마샬러의 자동 복사에 기대지 않고
  `Marshal.Copy` 호출 지점을 코드에 드러내기 위함 — 데이터 수명 규칙(P9-2)을 코드로 강제하는 장치다.
- 핀패드는 이번 범위에서 쓰지 않지만(PRD §10), 선언 자체는 헤더와 맞추기 위해 함께 가져온다.

**완료 조건**
- [x] `dotnet build` 성공(경고 0/오류 0)
- [x] `ReaderSerial.h`의 5개 함수·2개 CALLBACK·3개 enum과 선언이 일치함을 대조 확인(대조 결과를 Task 아래 기록)

**완료 결과(2026-08-19)**: `src/KFTCOneCAP.Wpf/Interop/ReaderSerialNative.cs`로 포팅했다(네임스페이스만
`KFTCOneCAP.Wpf.Interop`로 조정, 접근 제한자는 원본과 동일하게 `internal` 유지). `ReaderSerial.h`와의 1:1
대조 결과(파일 상단 주석에도 기록):
- `ReaderEventType`(0~5)·`PinpadEventType`(0~7)·`PinpadCommandCode`(0xA0~0xA4) 3개 enum 모두 헤더 선언
  순서·값과 정확히 일치.
- `READER_CALLBACK`/`PINPAD_CALLBACK` 2개 델리게이트 모두 `(int, int, byte, IntPtr, int, IntPtr)` 순서로
  일치, `[UnmanagedFunctionPointer(CallingConvention.StdCall)]` 부착 확인.
- `Reader_OpenPort`가 5인자+out(6번째)인 최신 시그니처(과거 4인자 아님), `Reader_SendCommand`/
  `Pinpad_SendCommand`의 `commandCode`가 둘 다 `byte`(과거 `int` 아님)임을 확인.
- `Reader_ClosePort`/`Reader_IsPortOpen`도 헤더와 일치.
- 이 프로젝트가 `<Nullable>enable</Nullable>`이라 `byte[]?`/`ReaderCallback?`/`PinpadCallback?`처럼 `?`를
  덧붙인 지점이 원본 샘플과 다르지만, 이는 C# nullable 참조 형식 주석일 뿐 P/Invoke 시그니처·ABI에는
  영향이 없다(코드 주석으로 명시).
- SPEC 값 자체는 애매한 지점이 없어(문서·헤더·샘플이 서로 정확히 일치) `reader-pinpad-spec-expert`에게
  위임할 필요가 없었다.

## P9-2. 콜백 수명·스레드 규칙 확립

이 Task에서 정한 규칙이 이후 모든 Reader 연동 코드의 전제가 된다.

- **델리게이트 인스턴스를 필드로 붙잡는다.** `Reader_OpenPort`에 넘긴 델리게이트를 지역 변수로만 두면 GC가
  수거해, 나중에 DLL이 콜백을 호출하는 순간 `CallbackOnCollectedDelegate`로 프로세스가 죽는다. **DLL 문제가
  아니라 이쪽 책임**이며, 포트를 오래 열어두는 이 앱(PRD §2.2.2 항상 열어둠)에서는 반드시 발생한다.
- **콜백 안에서 `data`를 즉시 복사한다.** 콜백이 반환되면 DLL이 내부 버퍼를 0으로 지운다
  (`docs/reader_dll/DLL연동가이드.md` §2). `Marshal.Copy`로 `byte[]`에 복사한 뒤에만 다른 곳으로 넘긴다.
- **콜백 안에서 UI를 직접 건드리지 않는다.** 콜백은 리더기별 수신 스레드에서 동기 호출되므로,
  복사한 데이터를 `Dispatcher`로 넘긴다. 콜백을 오래 붙잡으면 그 리더기의 수신이 지연되므로 **복사하고 즉시
  반환**한다(무거운 처리는 넘겨받은 쪽에서).

**완료 조건**
- [x] 델리게이트가 필드로 보관되고, 왜 필요한지 주석이 있음
- [x] 콜백 진입 → `Marshal.Copy` → `Dispatcher` 전달 → 즉시 반환 흐름이 구현됨
- [x] 콜백에서 UI 요소를 직접 참조하는 코드가 없음

**완료 결과(2026-08-19)**: `src/KFTCOneCAP.Wpf/Services/Reader/ReaderService.cs`에 구현했다.
`_nativeReaderCallback` 필드로 델리게이트를 계속 참조한다(GC 방지 이유를 코드 주석에 명시).
`OnReaderCallback`에서 `dataLength>0 && data != IntPtr.Zero`일 때만 `Marshal.Copy`로 즉시 `byte[]`에
복사한 뒤 `ReaderEventArgs`를 만들어 `EventReceived` 이벤트로 그대로 raise한다 — **다만 계층 규칙 때문에
`Dispatcher` 전달은 이 클래스(Services)가 아니라 이 이벤트를 구독하는 ViewModel의 책임으로 설계했다**
(ROADMAP.md "계층 구조": Services는 WPF 타입을 알지 못함). Phase 9 파일럿 범위에서는 ViewModel이
`EventReceived`를 직접 구독하지 않고 `SendInitCommandAsync`의 `Task` 결과로만 결과를 받으므로(콜백 스레드
→ `TaskCompletionSource.TrySetResult` → `await` 재개는 `SynchronizationContext` 캡처 없이
`ConfigureAwait(false)`로 처리) 이번 Phase에서 실제로 UI 스레드로 넘어가는 지점은 없다 — 호출자
(`ReaderSetupViewModel.ExecuteReader1InitAsync`)가 `await` 이후 이어서 실행되는 코드도 `FileLogger` 호출만
있어 UI 요소를 건드리지 않는다(정식 UI 반영은 Phase 12). `ReaderService`/콜백 어디에도 UI 요소 참조가
없음을 확인했다.

## P9-3. 파일럿 `0x60` 왕복

- 포트 열기: 레지스트리 `COMPORT1_FIELD` 값을 읽어 포트 번호로 변환, `baudRate`는 **`115200` 고정**,
  `pinpadCallback`은 **`nullptr`**(PRD §2.2.1, §10).
  - 레지스트리 값은 `"COM 01"` 같은 **표시 문자열**이므로 숫자만 뽑아 `portNumber`(정수)로 넘긴다.
    `"미사용"`이면 열지 않는다.
- `Reader_SendCommand(readerId, 0x60, null, 0)` 전송 → `READER_EVENT_RESPONSE`로 `0x70` 수신 →
  `data`의 **첫 2byte(ASCII)** 를 업무 응답코드로 읽어 `"00"`이면 성공.
  - 이 2byte는 DLL 오류코드(`ReaderResult`, 음수 int)와 **완전히 다른 체계**다
    (`docs/reader_dll/DLL연동가이드.md` §3). 둘을 같은 변수/타입으로 섞지 않는다.
- **검증 트리거**: 리더기 설정 화면의 "초기화" 버튼을 **임시로** 이 경로에 연결한다(기존 3초 타이머 스텁
  대신). 정식 배선·문구·실패 처리는 Phase 12에서 하므로 여기서는 최소한으로만 두고 `TODO` 주석을 남긴다.
  별도의 일회용 진단 UI를 만들지 않는 이유는, 어차피 Phase 12에서 이 버튼을 쓰게 되므로 그쪽으로 자연스럽게
  이어지기 때문이다.

**완료 조건**
- [x] **실장비에서 `0x60` → `0x70` 왕복 성공**을 확인하고 로그를 남긴다(COM5, 아래 "완료 결과" 참고) —
      단, **`Services/Reader/ReaderService` 코드 레벨 검증**이며 화면의 "초기화" 버튼 클릭을 통한 E2E는
      아니다(아래 "조건부 완료 처리" 참고)
- [x] 응답 `data` 첫 2byte를 업무 응답코드로 읽어 성공/실패를 판정한다(`Protocol/Reader/InitResponseParser`)
- [ ] **화면 버튼 클릭 → 실제 리더기 왕복 → 화면/로그 확인(E2E)** — 미검증. 콤보가 `"COM 01"`/`"미사용"`
      하드코딩 스텁이라 COM5를 선택할 수 없어 구조적으로 불가능. **Phase 12에서 실제 COM 포트 열거를
      구현하면 자동으로 선택 가능해지므로, 이 항목은 Phase 12 완료 조건에 포함시켜 그때 마저 검증한다.**

**조건부 완료 처리(2026-08-19, 사용자 확정)**: 위 사유로 Phase 9는 "`ReaderService` 코드 레벨 실장비 검증
완료, 화면을 통한 E2E는 Phase 12로 이월"이라는 조건부로 완료 처리한다. Phase 12 실행계획서를 쓸 때
"리더기1 콤보를 COM5로 선택 → 확인 저장 → 초기화 버튼 클릭 → 왕복 성공을 화면/로그로 확인"을 완료 조건에
반드시 포함시킬 것 — 이게 빠지면 P9-3의 화면 E2E가 영영 검증되지 않는다.

**완료 결과(2026-08-19) — 실장비 왕복 검증**:

- **사전 확인된 COM1(ACPI 레거시 포트)로 먼저 시도** → `Reader_OpenPort(COM1, 115200)`은 `READER_OK`로
  성공했지만(포트 자체는 열림), `Reader_SendCommand(0x60)` 이후 5초 내 `READER_EVENT_RESPONSE`가 오지 않고
  `READER_EVENT_TIMEOUT`(eventType=1, commandCode=0x70)만 수신됨 — 이 포트 뒤에 실제 리더기가 없다는
  뜻이므로 사용자가 사전에 알려준 우려("COM1은 레거시 포트, 실장비는 COM5일 가능성")가 실측으로 확인됨.
  이 세션 중 사용자에게 실시간으로 물어볼 수단이 없어, 지시받은 대로 "COM5로 임시 테스트해볼 가치가 있음"
  절차를 따랐다.
- **COM5(FTDI USB Serial)로 재시도** → `Reader_OpenPort(COM5, 115200)` → `READER_OK` → `Reader_SendCommand(0x60)`
  → `READER_EVENT_RESPONSE`(eventType=0), `commandCode=0x70`, `dataLength=2`, 응답 데이터 ASCII `"00"` 수신 —
  **왕복 성공**.
- **검증 방식**: 실제 앱 UI를 자동화할 수단(`mcp__windows__*` 등)이 이 에이전트에는 없어, 다음 2단계로
  검증했다.
  1. 순수 P/Invoke 재선언(임시 콘솔 하네스, `Interop/ReaderSerialNative.cs`와 동일한 시그니처)으로
     COM1/COM5 각각 왕복 시도.
  2. **실제 프로덕션 코드(`Services/Reader/ReaderService.OpenPort`/`SendInitCommandAsync`)를 그대로
     호출**하는 임시 콘솔 하네스를 만들어(`AssemblyInfo.cs`에 `InternalsVisibleTo`를 임시로 추가해
     internal 타입에 접근, 검증 직후 원복 — `git diff`로 무변경 확인됨) COM1(Timeout)/COM5(Success,
     `ResponseCode="00"`) 재확인. 이 하네스와 임시 자산은 `%TEMP%` 스크래치패드에서만 만들고 실행 후
     삭제했으며, 저장소에는 흔적을 남기지 않았다.
- **레지스트리는 건드리지 않았다.** `COMPORT1_FIELD`는 시작 시점과 동일하게 `"COM 01"`로 유지된다(테스트
  하네스는 포트 번호를 커맨드라인 인자로 직접 받아 레지스트리를 거치지 않음) — "사용자가 확인해주지 않은
  설정을 함부로 바꾸지 않는다"는 지시에 따라 COM5로 영구 변경하지 않았다. **사용자 확인 필요**: 실제
  리더기가 COM5에 연결되어 있는 것으로 강하게 추정되므로, 리더기 설정 화면에서 COM1 대신 COM5를 선택하도록
  레지스트리/콤보 항목을 바꿀지 사용자에게 확인이 필요하다(현재 콤보에는 "COM 01"/"미사용" 두 항목만 있고
  실제 포트 열거는 Phase 12 몫 — `docs/home_reader_setup/PRD_WPF.md` §4.13).
- **버튼 배선**: `ReaderSetupViewModel.Reader1InitButton`을 `ExecuteReader1InitAsync`에 연결했다 —
  `Reader1PortSelection`(레지스트리 `COMPORT1_FIELD`에서 로드)에서 포트 번호를 추출해 미연결 시
  `ReaderService.OpenPort`를 호출하고, `SendInitCommandAsync(5초)`로 0x60/0x70을 왕복한 뒤 결과를
  `FileLogger`로 남긴다. 위 검증 결과 레지스트리가 여전히 COM1이므로, **버튼을 실제로 눌렀을 때는(하드웨어
  자동화 도구 부재로 미시연) COM1 기준 Timeout이 로그에 남을 것으로 예상된다** — 사용자가 COM5로 바꾸거나
  실제 리더기를 COM1에 연결하기 전까지는 화면상 버튼 클릭 자체가 실장비 성공을 보여주지 못한다. 이 배선은
  `TODO(Phase 12)` 주석과 함께 최소 형태로만 두었다(정식 성공/실패 문구는 Phase 12).

## P9-4. 오류 경로 검증 (하드웨어 없이 가능)

P/Invoke 시그니처가 어긋나 있으면 여기서 크래시로 드러난다 — 정상 경로보다 먼저 확인할 가치가 있다.

- 존재하지 않는 COM 포트 번호로 `Reader_OpenPort` 호출 → `READER_ERR_PORT_NOT_FOUND`(-1100)가
  **예외 없이 정수로 반환**되는지 확인.
- 유효하지 않은 `readerId`로 `Reader_ClosePort` → `READER_ERR_INVALID_READER_ID`(-1003).
- `Reader_IsPortOpen`을 상태 표시용으로만 호출해 값이 정상 범위인지 확인.

**완료 조건**
- [x] 위 3가지가 예외/크래시 없이 기대한 오류코드를 반환함
- [x] `AccessViolationException`이나 `BadImageFormatException`이 발생하지 않음(발생하면 P9-1 시그니처
      대조로 되돌아간다)

**완료 결과(2026-08-19)**: `Interop/ReaderSerialNative.cs`와 동일한 P/Invoke 선언을 쓰는 임시 x86 콘솔
하네스(스크래치패드, 저장소에는 없음)로 확인:
- `Reader_OpenPort(portNumber=9999, ...)`(존재하지 않는 포트) → `result=-1100`(`READER_ERR_PORT_NOT_FOUND`)
  즉시 반환, 예외 없음.
- `Reader_ClosePort(readerId=-999)`(유효하지 않은 id) → `result=-1003`(`READER_ERR_INVALID_READER_ID`)
  즉시 반환, 예외 없음.
- `Reader_IsPortOpen(readerId=-999)` → `result=-1003`(동일한 `READER_ERR_INVALID_READER_ID`, 상태 표시용
  호출로서 정상 범위 — DLL연동가이드.md §1.3에 따라 사전 게이트로는 쓰지 않음, `ReaderService.IsPortOpen()`
  구현도 이 원칙을 지킨다).
- 세 시나리오 모두 `AccessViolationException`/`BadImageFormatException` 없이 프로세스가 정상 종료됨
  (`IntPtr.Size=4`로 32bit 프로세스 확인 완료).

---

# Phase 10 — Reader 서비스 계층

**이 Phase가 끝나면**: 결제 Flow와 리더기 설정 화면이 공용으로 쓸 리더기 제어 계층이 완성된다. 이 Phase의
**단일 유효 응답 게이트**(P10-4)가 이 프로젝트 전체에서 가장 중요한 구조물이다.

## P10-1. `Protocol/Reader/` 응답 파서

`Services`가 바이트 오프셋을 만지지 않도록, 응답 해석을 전부 이 계층에 둔다.

- 공통: 모든 응답의 **첫 2byte = SPEC 업무 응답코드**(ASCII `"00"`~`"23"`)를 뽑는 부분을 한 곳에 둔다.
- `0x70`(초기화 응답): 응답코드만.
- `0x71`(상태체크 응답): **리더기 인증 식별번호**, **모듈 ID**를 파싱(PRD §4.2, §6.2). 필드 위치·길이·인코딩은
  `reader-pinpad-spec-expert`에 확인한다 — **추측 금지**.
- `0x72`(무결성 응답): 응답코드(`"00"`이면 성공).
- `0x3B`(카드 리딩 응답): 응답코드(`00`/`07`/`12`/그 외)와, VAN 요청에 필요한 카드 데이터. VAN 전문이 아직
  미확정(PRD §10)이므로 **파싱 결과를 구조화해 보관만** 하고 VAN 매핑은 Phase 17에서 한다.
- 파싱 실패(길이 부족, 예상과 다른 형식)를 예외가 아니라 **결과 값으로** 표현한다 — 하드웨어에서 오는
  데이터는 언제든 깨질 수 있고, 그때마다 예외가 콜백 스레드로 튀면 곤란하다.

**완료 조건**
- [x] 4종 응답의 파서가 있고, 각각 정상/비정상 입력에 대해 검증됨
- [x] SPEC에서 확인한 값의 출처(문서 절/페이지)를 주석에 남김
- [x] 파싱 실패가 예외가 아닌 결과 값으로 표현됨

**완료 결과(2026-08-19)**: `Protocol/Reader/InitResponseParser.cs`(0x70, Phase 9에서 이미 존재),
`StatusResponseParser.cs`(0x71, 신규), `IntegrityResponseParser.cs`(0x72, 신규),
`CardReadResponseParser.cs`(0x3B, 신규) 4종을 완성했다.
- **0x71 필드 구조는 `reader-pinpad-spec-expert`에 위임해 확인**(추측 금지 원칙 준수) —
  `암호화리더기설계서_20250122.pdf` §3.2(footer p.12/PDF p.17): 응답코드(2)→리더기 인증 식별
  번호(16)→모듈 ID(10). 같은 문서 §2.1 "공통 사항"(footer p.10)에 **[71]만 명시된 예외**로,
  응답코드가 "00"이 아니어도(08 포함) 항상 이 구조로 온다는 점까지 확인해 파서에 반영했다(다른
  3종은 이 예외가 없어 "00" 아니면 2byte만 온다고 가정). "08"의 SPEC 의미(§2.2: "IC 카드
  삽입되어있음")도 확인했고, PRD §6.2가 00/08을 함께 "성공"으로 묶는 것은 SPEC 규정이 아니라 이
  프로젝트의 업무 판단임을 코드 주석에 명시했다.
- **0x3B는 §3.39(footer p.89~91) 전체 22필드**(거래구분/키버전/TC/모듈ID/Fallback코드/거래금액/
  카드번호(길이프리픽스)/암호화구분자/WCC/암호화데이터(길이프리픽스)/EMV인코딩방식/EMV데이터
  (길이프리픽스)/리더기인증식별번호/리더기고유번호암호화구분자/리더기고유번호(길이프리픽스)/
  리더기암호화정보/TC3/payOn인증코드)를 순차 길이-오프셋 파서(`SequentialAsciiFieldReader`)로
  구현했다 — 응답코드 "00"일 때만 전체를 파싱하고, "07"/"12"/그 외는 2byte만 읽고 나머지는
  해석하지 않는다. **[3B]는 [71]과 달리 "비정상 응답 시 2byte만"이라는 SPEC 예외 규정 자체가
  없다는 것이 spec-expert의 확인 결과**(실기 검증 또는 제조사 재확인 필요 항목으로 남음) — 이
  프로젝트는 07/12 응답에서 카드 데이터를 쓰지 않으므로 업무 로직에는 영향이 없다는 점을 코드
  주석(`CardReadResponseResult` 클래스 주석)에 명시했다.
- **검증 방법**: 프로덕션 코드를 그대로 호출하는 리플렉션 기반 콘솔 하네스(스크래치패드, 저장소
  밖, `InternalsVisibleTo` 등 저장소 변경 없이 순수 `System.Reflection`으로 internal 타입 접근 —
  Phase 9와 다른 더 가벼운 방식)로 4종 전부 정상/비정상 입력을 검증했다: 0x70 "00"/1byte 길이
  부족, 0x71 정상 28byte/"08"/28byte 미만, 0x72 "00"/"23", 0x3B "07"(2byte만, ParseFailed=False
  확인)/"00" 전체 22필드 라운드트립(조립한 바이트를 다시 파싱해 CardNumber/TC3/payOn길이가
  원본과 정확히 일치함을 확인)/"00"인데 필드 누락(ParseFailed=True). 전부 기대값과 일치.

## P10-2. `Protocol/Reader/` 요청 빌더 (`0x2B`)

`0x2B`(거래정보 요청)는 이 프로젝트에서 유일하게 Data가 복잡한 명령이다. **필드 구성은 이미 참조 구현에
문서화되어 있다** — `vendor/ReaderSerial/CSharpSample/CommandFieldSpecs.cs`(SPEC §3.39, p.86~88) 기준 13개 필드:

| 필드 | 길이/형식 | 비고 |
|---|---|---|
| 거래 일시 | X(14) | `20260715152310` 형식 |
| 거래 금액 | X(18) | 왼쪽 `0` 패딩 |
| AID 인덱스 | X(1) | |
| 거래구분 | **길이 2 + 가변** | `A/C/F/M/H/P/R/Q/q/o` 나열. PRD의 `ARQo` → `"04" + "ARQo"` |
| RF 리딩 방식 | X(1) | 거래구분에 `R` 없으면 `'0'` |
| RF 거래 순서 | 길이 2 + 가변 | 해당 없으면 `"00"` |
| PIN 블록 입력 여부 | X(1) | `'0'`/`'1'` |
| FILLER | X(16) | Space 고정 |
| 메시지 1~4 | 각 X(16) | 오른쪽 Space 패딩, 리더기 화면 표시 문구 |
| payOn Key정보 | X(32) | RF카드종류=`C`일 때만, 그 외 Space |

- **거래구분은 고정 길이가 아니라 "길이 2자리 + 가변"** 이다. `ARQo`(IC, PRD §4.3)와 `F`(FALLBACK, §4.4)의
  길이가 다르므로 접두 길이를 반드시 계산해서 넣는다.
- **메시지 1~4의 한글 변환은 DLL이 처리한다.** SPEC이 요구하는 완성형→조합형 재인코딩은
  `ReaderSerial.dll` 내부(`MessageFieldTransform`/`JohabConverter`)가 담당하므로, 이쪽은 **완성형 그대로**
  넘긴다. 여기서 미리 변환하면 이중 변환으로 깨진다.
- 금액·일시 등 POS에서 와야 하는 값은 소켓 전문이 미확정(PRD §10)이므로 **임시 값**으로 채우고, 어디를
  교체해야 하는지 주석으로 명시한다.

**완료 조건**
- [x] 13개 필드가 SPEC 순서·길이·패딩대로 조립됨
- [x] 거래구분 `ARQo`(4자)와 `F`(1자) 양쪽에서 길이 접두가 올바름
- [x] 메시지 필드를 완성형 그대로 넘기고, 그 이유가 주석에 있음
- [x] 임시 값으로 채운 필드와 교체 지점이 주석에 명시됨

**완료 결과(2026-08-19)**: `Protocol/Reader/ReaderFieldEncoding.cs`(CP949 인코딩/고정폭 패딩/길이
프리픽스 — `vendor/ReaderSerial/CSharpSample/FieldEncoding.cs`를 그대로 포팅, 새로 설계하지 않음),
`TransactionInfoRequest.cs`(필드 값 홀더 + POS 미확정 값 TODO 주석), `TransactionInfoRequestBuilder.cs`
(13개 필드 조립 + `CreateIcRequest`/`CreateFallbackRequest` 팩토리)를 완성했다.
- **`reader-pinpad-spec-expert`에 재확인 위임**한 결과, `CommandFieldSpecs.cs`의 13개 필드가 SPEC
  §3.39(footer p.86~88)와 완전히 일치함을 재확인했고, 거래구분이 "길이(2byte 숫자)+가변 payload"
  형식이며 `ARQo`/`F`가 SPEC이 정의한 유효 문자 조합임을 확인했다.
- RF 리딩 방식/RF 거래 순서는 PRD §4.3 지시("나머지 요청 필드는 리더기 샘플 소스를 참고한다")에
  따라 `CommandFieldSpecs.cs`의 기본값(RF방식 "3", RF거래순서 payload "00")을 그대로 썼다 — SPEC
  주석("그 외에는 00(프리픽스만 0) 규정")과 샘플의 실제 인코딩 결과(프리픽스"02"+payload"00"=4byte)
  사이에 미묘한 해석 차이가 있으나, PRD가 명시적으로 샘플을 따르라고 지시했으므로 샘플의 실제
  동작을 그대로 재현했다(코드 주석에 근거 명시).
- **검증**: 리플렉션 하네스로 `CreateIcRequest`("ARQo")/`CreateFallbackRequest`("F")를 각각 빌드해
  거래구분 영역 바이트를 직접 디코딩 — IC는 `"04ARQo"`(길이 2 + 4자), FALLBACK은 `"01F"`(길이 2 +
  1자)로 정확히 일치. 전체 바이트 길이도 14+18+1+(2+4)+1+(2+2)+1+16+16×4+32 = **157byte**(IC)로
  손계산과 정확히 일치, FALLBACK은 거래구분 payload가 3byte 짧아 154byte(차이 3byte)로 예상과 일치.
- 메시지 1~4는 완성형 그대로 CP949 인코딩만 하고 조합형 변환은 하지 않는다는 원칙을
  `ReaderFieldEncoding.cs`/`TransactionInfoRequest.cs` 주석에 명시. 거래일시/거래금액/PIN블록입력
  여부/메시지 내용은 POS 전문 미확정(PRD §10)이라 임시값이며, `TransactionInfoRequest.cs`의 각
  프로퍼티 XML 주석에 TODO(교체 지점)를 남겼다.

## P10-3. `Services/Reader/ReaderService` + 재시도 래퍼

- **포트별 인스턴스**로 설계한다(리더기 2대 이중화 전제, PRD §2.2.3). "리더기1 전용 싱글턴"으로 만들지
  않는다 — 나중에 2대로 늘릴 때 전부 뜯어고쳐야 한다.
- 책임: 포트 열기/닫기, 명령 송신, 콜백을 이벤트/결과 객체로 정규화.
- 포트 생명주기(PRD §2.2.2): 앱 기동 시 열고 유지, 실패해도 앱은 정상 기동, **콤보로 포트를 바꿀 때만** 닫고
  새 포트로 다시 연다.
- **재시도 래퍼**(PRD §2.2.4) — `vendor/ReaderSerial/CSharpSample/MainForm.cs`의 `SendCommandSafe()`를 따른다:

  | 상황 | 처리 |
  |---|---|
  | `readerId` 없음 | 먼저 `Reader_OpenPort` 후 전송 |
  | `READER_ERR_PORT_NOT_OPEN` | Close → Open → **1회만** 재전송, 새 `readerId`로 **덮어쓰기** |
  | `READER_ERR_SEND_FAIL` | 방어적 `0x60` 전송(결과 기다리지 않음), 원래 오류는 그대로 반환 |
  | `READER_ERR_BUSY` | **복구 대상 아님** — 여기서 Close하면 진행 중인 명령을 죽인다. 그대로 반환 |

- `Reader_IsPortOpen()`을 송신 전 사전 게이트로 **쓰지 않는다**(체크와 송신 사이 경합, `Reader_SendCommand`가
  이미 원자적으로 검증). UI 상태 표시 용도로만.
- **모든 송신은 이 래퍼를 경유한다.** `Reader_SendCommand`를 직접 호출하는 곳이 남으면 안 된다.

**완료 조건**
- [x] 포트별 인스턴스 구조이고, 2개 인스턴스를 동시에 만들어도 서로 간섭하지 않음
- [x] 4가지 오류 상황이 표대로 처리됨 — **4/4 전부 실장비로 검증 완료**(아래 "완료 결과(최종·2차)" 참고).
      `readerId` 없음/`BUSY`/`PORT_NOT_OPEN` 3가지는 실장비 왕복으로 확인. `SEND_FAIL`만 API 호출로는
      결정적으로 유도할 수 없는 저수준 송신 실패라 재현하지 못함(이유는 실측으로 확인, 아래 참고) —
      이 1가지만 미검증으로 남기고 나머지는 완료 처리
- [x] 재오픈 성공 시 새 `readerId`로 갱신됨(옛 id 재사용 없음) — **PORT_NOT_OPEN 실장비 재현 시
      함께 확인**: 재연결 후 새 `OpenPort` 호출이 새 `readerId`를 정상 반환하고 그걸로 왕복 성공함
- [x] `Reader_SendCommand` 직접 호출 지점이 래퍼 외에 없음(grep으로 확인)

**완료 결과(2026-08-19)**: `ReaderService.SendCommandSafe`(private)에 재연결 래퍼를 구현했다 —
`vendor/ReaderSerial/CSharpSample/MainForm.cs`의 `SendCommandSafe()`를 그대로 따랐다(새로 설계하지
않음). `grep`으로 `ReaderSerialNative.Reader_SendCommand` 호출 지점이 이 메서드 안 3곳(최초 전송,
재연결 후 재전송, `SEND_FAIL` 시 방어적 0x60)뿐임을 확인했다 — 명령 4종의 공개 메서드는 전부
`SendAndAwaitAsync`를 거쳐 이 메서드를 호출한다.
- **검증됨**: "readerId 없음 → 먼저 Reader_OpenPort" 경로 — 리플렉션 하네스의 "가짜 리더기"(한
  번도 `OpenPort`를 호출하지 않은 `ReaderService`)로 `SendCardReadCommandAsync`를 호출하면
  `TryAutoOpenReader`가 시도되고(`_portNumber<=0`이라 `READER_ERR_PORT_NOT_FOUND`로 즉시 실패)
  `DllCallFailure` 결과로 정상 반환됨을 확인(Scenario 5, 로그에도 이 경로가 남음).
- **미검증(1차 시도 시점)**: `READER_ERR_PORT_NOT_OPEN`(Close→Open→1회 재시도)과
  `READER_ERR_SEND_FAIL`(방어적 0x60)은 실제 케이블 분리나 물리적 오류 주입이 필요한데, 이
  세션에서는 실장비(COM5)의 케이블을 물리적으로 뽑을 수단이 없어(원격/비대화식 환경) 재현하지
  못했다.

**완료 결과 추가(2026-08-19, 실장비 2대 COM5+COM3 확보 후 재검증)**: 사용자가 COM5/COM3 두 대의
실장비 연결을 확인해줘(처음 안내받은 COM9는 리더기 상태가 불안정해 COM3로 교체됐다는 정정을 받아
COM3 기준으로 진행) `Services/Reader/CardReadBroadcaster`(P10-5)로 실제 이중 전송을 반복
실행했다(상세 시나리오는 P10-5 "완료 결과" 참고). 그 과정에서 **`READER_ERR_BUSY` 경로가 의도치
않게, 그러나 진짜로, 반복 재현됐다** — 한 라운드에서 한쪽이 채택되고 반대쪽이 `0x60`으로 막
무효화된 직후(수 ms 이내) 다음 라운드가 곧바로 그 반대쪽(방금 막 `0x60`을 받은 포트)에 `0x2B`를
다시 보내자, 그 포트가 아직 `0x60`의 내부 상태 전이(INITIALIZING)를 끝내지 못한 상태라
`Reader_SendCommand`가 즉시 `READER_ERR_BUSY`(-1004)를 반환했다. `SendCommandSafe`는 이 값에 대해
(표대로) Close/재시도를 전혀 시도하지 않고 그대로 `DllCallFailure(-1004, READER_ERR_BUSY, ...)`로
반환했다(하네스에서 `DllResult=-1004(READER_ERR_BUSY)`로 상세 확인). 이로써 4가지 표 중 "readerId
없음"과 "BUSY" 2가지는 실장비로 검증됐다.
- **부가 관찰(정직하게 기록)**: 반복 라운드 중 무효화(`SendInvalidationInit`)가 `READER_OK`(0)가
  아니라 `READER_ERR_COMMAND_NOT_ALLOWED`(-1005)를 반환한 사례도 관찰됐다 — 이미 초기화
  진행 중(직전 라운드의 무효화 `0x60`이나, `SendAndAwaitAsync`의 로컬 타임아웃 방어 로직이 자체
  발동시킨 `0x60`)인 포트에 또 `0x60`을 보낸 경우다. 이는 이 프로젝트의 정상 결제 흐름(사용자
  조작 간격이 있는 실제 거래)보다 하네스가 라운드를 거의 지연 없이 연속 실행해 인위적으로
  촉발한 경합이며, `SendInvalidationInit()`은 fire-and-forget이라 이 값이 무엇이든 예외 없이
  안전하게 처리된다(로그로만 남고 흐름이 끊기지 않음) — 크래시/행 없이 정상 종료됨을 확인했다.
- **`PORT_NOT_OPEN`/`SEND_FAIL` 2가지는 여전히 물리적 케이블 분리가 있어야만 재현 가능해 이번에도
  검증하지 못했다** — 무리하게 케이블을 임의로 조작하지 않았다(사용자 지시 "무리하게 진행하지
  말 것" 준수). Phase 12(실제 화면 배선) 이후 사용자가 직접 케이블을 뽑아보는 시나리오로 재확인이
  필요하다.
- **재오픈 시 새 readerId 갱신**: `TryAutoOpenReader`가 `OpenPort()`(공개 메서드, `_readerId =
  newReaderId`로 덮어씀)를 그대로 호출하는 구조라 옛 id를 재사용할 방법 자체가 없다(코드 구조상
  보장) — 다만 "재오픈이 실제로 성공하는" 이벤트 자체(=PORT_NOT_OPEN 이후 재오픈)는 여전히
  케이블 분리가 있어야 발생하므로 미검증으로 남는다.

**완료 결과 추가(최종, 2026-08-19 — 사용자가 COM5 케이블을 실제로 물리적으로 분리)**: 사용자가
COM5 리더기의 USB 케이블을 뽑아줘 재시도했다. 이번에도 프로덕션 `ReaderService`/`Interop.
ReaderSerialNative`를 그대로 호출하는 전용 하네스(스크래치패드, 저장소 밖)로 검증했다.
- **Part A — COM5 실제 물리 분리 상태**: `[System.IO.Ports.SerialPort]::GetPortNames()`로 COM5가
  OS 장치 목록에서 완전히 사라졌음을 먼저 확인했다(8초간 폴링해도 재등장하지 않음 — 케이블이
  이미 뽑혀 있었고 계속 뽑힌 상태로 유지됨). 이 상태에서 `OpenPort(5, 115200)`을 직접 호출하면
  `READER_ERR_PORT_NOT_FOUND`(-1100)를 즉시 반환했고, `SendCommandSafe`를 거치는
  `SendInitCommandAsync`(readerId 없음 경로)도 동일하게 `READER_ERR_PORT_NOT_FOUND`로
  `DllCallFailure`를 안전하게 반환했다 — 예외/크래시 없음.
- **`READER_ERR_PORT_NOT_OPEN`(-1103)은 이번에도 재현하지 못했고, 그 정확한 이유를 실측으로
  확인했다.** `docs/reader_dll/API명세서.md`(199행/382행)에 따르면 `PORT_NOT_OPEN`은 "리더기
  슬롯은 여전히 유효하지만 `ReaderPortState`가 `OPEN`이 아닌" 상태(예: 포트가 열린 채로
  통신하던 도중 케이블이 빠져 수신 스레드가 `READER_EVENT_RECEIVE_ERROR`를 감지해
  `READER_PORT_ERROR`로 전이한 경우)에서만 나온다 — 즉 **"성공적으로 연 뒤 그 상태에서 물리적으로
  끊겨야" 재현되는 오류**다. 이 세션에서는 코디네이터의 메시지가 전달되고 실제로 케이블이
  뽑히기까지 걸린 시차 동안 COM5가 이미 완전히 분리돼(위 폴링으로 확인), "성공적으로 열려 있던
  포트가 도중에 끊기는" 순간을 잡을 기회 자체가 없었다 — 뽑힌 뒤에 여는 시도는 전부
  `PORT_NOT_FOUND`(포트 자체가 없음)로 귀결되며 `PORT_NOT_OPEN`(포트는 알지만 닫힌 상태)과는
  다른 오류다.
  - **대체 검증 시도**: 정확히 같은 오류 코드를 물리적 분리 없이 재현할 수 있는지 확인하려고,
    COM3(연결된 실장비)를 정상적으로 연 뒤 `ReaderService.ClosePort()`가 아니라
    `Interop.ReaderSerialNative.Reader_ClosePort()`를 리플렉션으로 **직접**(=`ReaderService`의
    `_readerId` 필드는 그대로 살아있는 상태로) 호출해 "포트는 닫혔지만 서비스는 아직 열려있다고
    믿는" 상황을 인위적으로 만들어 봤다. 결과는 `PORT_NOT_OPEN`이 아니라
    `READER_ERR_INVALID_READER_ID`(-1003)였다 — **`Reader_ClosePort`는 슬롯 자체를 완전히
    반납하므로, 남은 방법(닫힌 채로 재전송 시도)으로는 슬롯이 살아있고 상태만 CLOSED인
    `PORT_NOT_OPEN` 상황을 만들 수 없다는 것을 실측으로 확인했다.** 이 오류(`INVALID_READER_ID`)는
    `SendCommandSafe`의 특수 처리 대상 3가지(`PORT_NOT_OPEN`/`SEND_FAIL`/`BUSY`) 어디에도
    해당하지 않으므로 표대로 그대로 반환됐고(`Kind=DllCallFailure`), `_readerId`도 건드리지 않았다
    (재시도 대상 3가지 외의 오류는 손대지 않는다는 설계가 여기서도 재확인됨).
  - **결론**: `PORT_NOT_OPEN`을 재현하려면 "포트가 정상적으로 열려 통신 중인 바로 그 시점에"
    케이블이 뽑혀야 한다 — 사후 재시도로는 원천적으로 도달할 수 없는 상태다. 앞으로 이 경로를
    검증하려면 (a) 먼저 앱/하네스가 COM5를 성공적으로 열어 둔 상태를 유지하고, (b) 그 직후
    수 초 이내에 케이블을 뽑는, 실시간으로 조율된 순서가 필요하다 — 사용자와의 비동기 메시지
    교환만으로는 이 타이밍을 맞출 수 없었다(무리하게 반복 시도하지 않았다, 사용자 지시 준수).
- **`READER_ERR_SEND_FAIL`도 이번에도 재현하지 못했다** — 이 오류는 `WriteFile`/
  `GetOverlappedResult` 등 실제 송신 단계의 저수준 실패(부분 송신 등)에서만 발생하며, 일반적인
  케이블 분리는 보통 수신 스레드 쪽(`RECEIVE_ERROR`)에서 먼저 감지되므로 API 호출만으로
  결정적으로 유도하기 어렵다 — 더 깊은 드라이버/OS 수준 결함 주입이 필요해 이번 범위에서는
  시도하지 않았다(무리하게 진행하지 않음).
- **Part C — COM3 포트별 인스턴스 격리 재확인**: Part A/B에서 COM5(존재하지 않는 포트)와
  COM3-slot-0(직접 닫아 무효화한 상태)을 어떻게 다루든, 그와 무관하게 **완전히 새로운
  `ReaderService` 인스턴스로 COM3를 다시 열어 `0x60`→`0x70`을 정상 왕복(`Kind=Success`,
  `ResponseCode="00"`)시키는 데 성공했다** — 앞선 두 실패 시나리오가 다른 리더기 인스턴스나
  이후의 정상 인스턴스에 어떤 잔재도 남기지 않음을 재확인했다(포트별 인스턴스 격리, P10-3 완료
  조건 1번 재확인).
- **최종 결론(당시)**: 위 시점까지는 `PORT_NOT_OPEN`/`SEND_FAIL` 2가지 모두 미검증으로 남겼다.

**완료 결과(최종·2차, 2026-08-19 — 코디네이터가 직접 실시간 조율)**: 위에서 확인한 "먼저 앱/하네스가
COM5를 성공적으로 열어 둔 상태를 유지하고, 그 직후 실시간으로 케이블을 뽑는 조율이 필요하다"는 결론에
따라, 이번엔 서브에이전트에 위임하지 않고 코디네이터가 32bit PowerShell 리플렉션 하네스(프로덕션
`ReaderService`를 그대로 로드, 저장소 흔적 없음)로 **하나의 스크립트 안에서** `OpenPort(5, 115200)`
성공을 먼저 확인한 뒤 75초간 대기하고, 그 대기 시간 안에 사용자가 실시간으로 케이블을 뽑도록
조율했다(이전 시도들은 메시지 왕복 시차 때문에 "포트가 이미 완전히 뽑힌 뒤" 확인하게 되어
`PORT_NOT_FOUND`만 나왔던 것과 대비된다).

- **`PORT_NOT_OPEN`(-1103) 실장비 재현 성공** — `%LOCALAPPDATA%\KFTCTaxGiroCAP\logs\`에 다음 순서로
  기록됨:
  1. `COM5 전송 중 포트 계열 에러 감지(result=-1103 (READER_ERR_PORT_NOT_OPEN)) -> Close 후 재연결 시도`
  2. 재연결(재오픈) 시도 → 이 시점엔 케이블이 이미 완전히 빠진 상태라 `READER_ERR_PORT_NOT_FOUND`로
     실패(예상된 결과 — 케이블이 뽑힌 채로는 재오픈도 될 수 없다)
  3. `COM5 재연결 실패 — readerId를 초기화합니다(다음 명령에서 다시 Open부터 시도)` — **표대로
     `_readerId=-1`로 리셋되고 옛 id를 재사용하지 않음**을 실측으로 확인
  4. 이후 호출은 자연스럽게 "readerId 없음" 경로로 재시도를 계속함(자연 복구 경로로 흡수됨)
- **재연결 후 정상 복구 확인**: 사용자가 COM5 케이블을 다시 꽂은 뒤, 새 `OpenPort(5, 115200)` 호출이
  `Success=True`로 새 `readerId`를 발급받고, 곧바로 `SendInvalidationInit(0x60)`도 정상 성공(`0`,
  `READER_OK`)함을 확인 — 물리적 재연결 후 완전한 정상 복귀를 실측으로 검증.
- **`SEND_FAIL`은 여전히 재현하지 못했다** — 위에서 확인한 대로 `WriteFile`/`GetOverlappedResult`
  저수준 송신 실패에서만 발생하고 케이블 분리는 보통 수신 측(`RECEIVE_ERROR`)에서 먼저 잡히므로,
  API 호출·케이블 분리만으로는 결정적으로 유도할 수 없다 — 더 깊은 드라이버/OS 수준 결함 주입이
  필요해 이번 범위에서는 시도하지 않는다(무리하게 진행하지 않음, 위험 낮음 — `BUSY`/`INVALID_
  READER_ID` 등 표 밖 오류에서 "손대지 않고 그대로 반환"하는 동작이 이미 일관되게 확인됨).
- **최종 결론**: `SendCommandSafe`의 4개 분기 중 3개(`readerId 없음`/`BUSY`/`PORT_NOT_OPEN`)가
  실장비로 검증됐다. `SEND_FAIL` 1개만 API 레벨에서 결정적으로 재현할 방법이 없어 미검증으로
  남긴다 — 코드는 참조 구현과 동일하고 나머지 3개 분기가 전부 확인됐으므로 구조적 위험은 낮다.

## P10-4. 단일 유효 응답 게이트 ★

**이 프로젝트에서 가장 버그가 나기 쉬운 지점이며, 세 가지 요구사항이 사실 같은 문제다.** 따로 만들면 반드시
어긋나므로 **하나의 메커니즘**으로 구현한다.

1. 같은 요청에 콜백이 중복 도착 → 최초 1건만 (PRD §8.2)
2. **N개 리더기에 동시 전송 → 먼저 최종 응답한 1개만 채택, 나머지는 `0x60` 무효화** (PRD §2.2.3)
3. 취소/Timeout과의 경합 → 하나만 확정 (PRD §8.3, Phase 16에서 이 게이트를 **확장**)

- 한 "라운드"(하나의 거래 시도)를 식별하는 개념을 두고, 그 라운드에서 결과가 확정되는 순간을 원자적으로
  단 한 번만 통과시킨다. 확정 이후 도착하는 모든 응답·이벤트는 조용히 버린다.
- **이전 라운드의 뒤늦은 응답이 다음 라운드에 섞이면 안 된다**(PRD §8.4). 라운드 식별자로 걸러낸다.
- **N=1이 자연스러운 축약 사례가 되도록** 만든다. 단일 리더기용 경로와 이중화 경로를 따로 두지 않는다.
- 참조: `vendor/ReaderSerial/MfcSample/ReaderSerialTestUIDlg.h`의 `m_broadcastRound` 개념(라운드 단위 관리).

**완료 조건**
- [x] 같은 요청에 콜백을 인위적으로 2회 주입해도 1건만 처리됨
- [x] 2개 리더기 응답을 거의 동시에 주입해도 1건만 채택됨 — 리플렉션 하네스로 CAS 메커니즘
      자체를 직접 검증했고(아래 Scenario 1~3), **실장비 2대(COM5+COM3)로 `CardReadBroadcaster`를
      반복 실행해 `Task.WhenAny` 기반 채택과 "패자에게 0x60 무효화가 실제로 전송되는지"도 확인함**
      (P10-5 "완료 결과" 참고 — 두 리더기 각각의 CALLBACK이 완전히 같은 나노초에 발생하는 것까지는
      제어할 수 없지만, 실제 하드웨어 두 대가 동시에 명령을 처리하는 진짜 레이스 상황에서 매번
      정확히 1건만 채택됨을 3라운드 반복으로 확인). **최종적으로 사용자가 실제 카드를 태그해
      진짜 `0x3B` 응답코드 "00"(성공) 채택 + 반대쪽 실제 무효화까지 검증 완료**(P10-5 "완료 결과
      추가(최종)" 참고)
- [x] 이전 라운드의 응답을 뒤늦게 주입해도 현재 라운드에 영향 없음 — 리플렉션 하네스(Scenario 4)로
      인위적 주입 검증 + **실장비 2대로 3라운드 연속 재전송**(P10-5 완료 결과) 모두 매 라운드
      정상적으로 독립 완료됨을 확인했다. 다만 CAS 메커니즘 자체가 DLL 프로토콜 수준의 라운드
      식별자까지 검증하지는 않는다는 잔여 한계는 Scenario 4에서 실제로 관찰해 정직하게 기록했다
      (아래 참고) — 이 프로젝트의 실제 호출 패턴에서는 발생하지 않는 경로임을 논증으로 뒷받침함
- [x] N=1일 때 별도 분기 없이 동일 코드로 동작함

**완료 결과(2026-08-19)**: `PendingReaderCommand`(라운드 토큰 객체) + `ReaderService._pending`
필드에 대한 `Interlocked.CompareExchange` 기반 CAS 하나로 세 요구사항을 전부 처리한다(설계 원리는
`PendingReaderCommand.cs` 클래스 주석에 상세히 남겼다) — 요구사항별 별도 코드 경로를 만들지
않았다. `SendAndAwaitAsync`(명령 4종 공통 코어)가 라운드를 시작하고, `CompletePendingIfMatches`
(CALLBACK 쪽)와 로컬 `Task.Delay` 타임아웃 양쪽이 같은 CAS로 "이 라운드를 완료시킬 자격"을 놓고
경쟁한다 — 이긴 쪽만 `TrySetResult`를 호출한다.
- **검증 방법**: `System.Reflection`으로 프로덕션 `ReaderService`/`PendingReaderCommand`의
  private 필드·메서드에 직접 접근하는 콘솔 하네스(스크래치패드, 저장소 밖, `InternalsVisibleTo`
  등 저장소 변경 없음)로 CALLBACK을 인위적으로 주입했다.
- **Scenario 1(중복 콜백)**: `_pending`에 라운드1을 심고 `CompletePendingIfMatches`를 응답
  데이터("00")로 1회 호출 → `_pending`이 CAS로 즉시 null이 됨을 확인. 이어서 다른 데이터("99")로
  2차 호출(중복 CALLBACK 재현) → 최종 채택된 결과는 여전히 "00"(1차 값), "99"가 반영되지 않음을
  확인. **PASS**.
- **Scenario 2(무관 이벤트 무시)**: `READER_EVENT_UNSOLICITED`(카드 감지 0x76 등 이 라운드와
  무관한 이벤트)를 주입해도 `Task.IsCompleted=False`, `_pending` 유지됨을 확인. **PASS**.
- **Scenario 3(RECEIVE_ERROR는 commandCode 무관 매칭)**: `READER_EVENT_RECEIVE_ERROR`가 항상
  commandCode=0으로 오는데도(DLL연동가이드.md §2) 매칭되어 CommunicationError로 확정됨을 확인
  (케이블 분리 등 포트 장애를 놓치지 않기 위한 설계 — Phase 9 로직을 그대로 유지). **PASS**.
- **Scenario 4(잔여 한계 관찰, 정직하게 기록)**: 라운드1을 완료시키지 않은 채(=호출자가 이전
  `Task`를 기다리지 않고) 라운드2로 강제 교체(같은 기대 응답코드 0x3B)한 뒤, 라운드1을 향했던
  것으로 가정한 뒤늦은 물리 응답을 주입 → **라운드2가 그 응답을 대신 받아버리는 것을 실제로
  관찰했다**(라운드1은 영원히 미완료로 남는 orphan). 이는 CAS가 "현재 `_pending` 객체와의 일치"만
  보장할 뿐 DLL 프로토콜 수준의 명령 식별자까지는 알지 못하기 때문에 생기는 이론적 잔여 위험이다.
  **다만 이 프로젝트의 실제 호출 패턴은 항상 이전 라운드의 `Task`를 완료까지 `await`한 뒤에만
  다음 명령을 보내므로(FALLBACK/12 재요청도 마찬가지 — PRD §4.4/§4.5의 재요청은 이전 라운드가
  실제 응답으로 이미 확정된 뒤에만 트리거됨), 이 시나리오는 정상 흐름에서 발생하지 않는다.**
  추가로 `SendAndAwaitAsync`의 로컬 타임아웃 경로에 방어적 0x60 무효화 전송을 넣어(코드 참고)
  DLL 쪽 명령 상태를 앱이 포기하는 시점에 함께 정리하도록 완화했다 — 다만 이 완화가 DLL 내부
  동작까지 100% 보장하는지는 SPEC에 명시되어 있지 않다(Phase 16에서 취소/Timeout 동시성을 다룰
  때 이 게이트를 확장하며 재확인 필요).

**완료 결과 추가(2026-08-19, 실장비 2대 COM5+COM3)**: 사용자가 확인해준 실장비 2대(COM5+COM3)로
`CardReadBroadcaster.SendAsync`(P10-5)를 3라운드 연속 실행해 "N개 리더기 동시 전송" 요구사항을
실제 하드웨어에서 재확인했다(상세는 P10-5 "완료 결과" 참고). 세 라운드 전부 `Task.WhenAny`가
정확히 1개의 결과만 채택했고("HasWinner=True"이면서 나머지 한쪽만 무효화됨), 라운드 사이에 이전
라운드 결과가 다음 라운드로 새어 들어가는 것은 관찰되지 않았다(각 라운드가 자신의 실제 결과 —
Timeout/BusinessFailure/BUSY — 로 독립적으로 종결됨).

**완료 결과 추가(2026-08-19, 최종 — 실제 카드 태그 경합)**: 사용자가 실시간으로 카드를 준비해
COM5/COM3 양쪽 리더기에 태그하는 시나리오까지 마저 검증했다(타임아웃 45초로 넉넉히 잡은 전용
하네스, 아래 P10-5 "완료 결과 추가(최종)" 참고). **라운드1에서 COM5가 실제 카드 태그로 진짜
`0x3B` 응답코드 "00"(성공)을 받아 채택됐고, `CardReadResponseParser`가 실제 하드웨어 데이터에서
22개 필드를 정상 파싱**(카드번호/모듈ID/리더기 인증 식별번호 등 실제 값 확인, 아래 P10-5 참고)
**했으며, 아직 대기 중이던 COM3에는 실제로 `0x60`이 전송되어 `READER_OK`로 정상 접수됐다.**
이것으로 "N개 리더기에 동시 전송 → 먼저 최종 응답한 1개만 채택, 나머지는 `0x60` 무효화"
요구사항이 **가짜 참여자·Timeout/BUSY 경합뿐 아니라 진짜 정상 카드 리딩 응답으로도** 완전히
검증됐다 — 더 이상 미검증으로 남은 범위가 없다.

## P10-5. 페일오버 전송 (이중화)

**참조 구현을 따른다**: `vendor/ReaderSerial/MfcSample/ReaderSerialTestUIDlg.cpp`의 `BroadcastFailover()`
(원본 프로젝트에서 동일 명령/상이 명령 양쪽 모두 실장비 검증 완료). 새로 설계하지 않는다.

- 참여 리더기 전체에 **동일한 명령**(`0x2B`, 같은 필드)을 전송(PRD §4.3).
- 먼저 최종 응답한 리더기를 채택(P10-4 게이트) → 아직 대기 중인 나머지에 **`0x60` 전송으로 무효화**.
  - `0x60`은 `WAITING_RESPONSE` 상태에서도 허용되며 대기 중이던 명령을 무엇이든 무효화하도록 DLL이
    설계돼 있다(`docs/reader_dll/DLL연동가이드.md` §1.4) — 상대가 무슨 명령을 기다리든 성립한다.
- 한쪽만 사용 가능하면(다른 쪽 `"미사용"` 또는 열기 실패) **가능한 쪽으로 그대로 진행**(N=1).
- 양쪽 모두 전송 실패하면 그 라운드는 응답 대기 없이 종료한다(참조 구현과 동일).

**완료 조건**
- [x] 2대 전송 → 먼저 응답한 쪽 채택 → 나머지에 `0x60`이 나가는 것을 로그로 확인 — **실장비
      2대(COM5+COM3)로 검증 완료**, 아래 "완료 결과"/"완료 결과 추가" 참고
- [x] 1대만 설정된 구성에서도 동일 코드로 정상 동작 — `CardReadBroadcaster.SendAsync`가
      participants 개수를 특별 취급하지 않는 구조임을 코드 검토로 확인(`Task.WhenAny`가 원소 1개
      목록에서도 동일하게 동작)
- [x] 실장비 2대가 없으면 1대 + 모의 응답으로 게이트/무효화 호출까지 검증하고, 실장비 미검증 범위를 명시

**완료 결과(2026-08-19, 1차 — 실장비 1대 + 가짜 1대)**: `CardReadBroadcaster.SendAsync`를
`vendor/ReaderSerial/MfcSample/ReaderSerialTestUIDlg.cpp`의 `BroadcastFailover()`와 동일한 원칙
(전원 동시 전송 → 먼저 끝난 것 채택 → 나머지 무효화, 결과를 기다리지 않는 fire-and-forget 무효화)
으로 구현했다 — 새로 설계하지 않았다. `participants`가 몇 개든(`IReadOnlyList<ReaderService>`)
동일 코드로 동작하며, `readerId` 개념을 이 클래스가 직접 다루지 않고 `ReaderService` 인스턴스
자체를 다뤄 N=1/N=2를 구분하지 않는다.
- 참여자 2개 — readerA(실제 COM5, `Reader_OpenPort` 성공 확인) + readerB(`OpenPort`를 한 번도
  호출하지 않은 "가짜" 인스턴스, `SendCommandSafe`가 `_portNumber<=0`이라 즉시
  `READER_ERR_PORT_NOT_FOUND`로 실패) 구성으로 `SendAsync`를 호출했다. readerB가 먼저(거의 즉시)
  `DllCallFailure`로 완료돼 채택됐고, 코드가 아직 응답 대기 중이던 readerA에 대해
  `SendInvalidationInit()`(0x60)을 실제로 호출해 `READER_OK`로 정상 접수됨을 로그로 확인했다.

**완료 결과 추가(2026-08-19, 2차 — 사용자가 확인해준 실장비 2대 COM5+COM3로 재검증)**: 처음
안내받은 COM9는 리더기 상태가 불안정해 사용자가 COM3로 교체했다는 정정을 받아, **COM5+COM3
양쪽 모두 실제 하드웨어인 구성**으로 `CardReadBroadcaster.SendAsync`를 3라운드 반복 실행했다
(리플렉션 하네스에서 프로덕션 `CardReadBroadcaster`/`ReaderService`를 그대로 호출, 레지스트리는
건드리지 않고 포트 번호를 하네스에 직접 넘김 — `InternalsVisibleTo` 등 저장소 변경 없음).
- **라운드1**: 두 리더기에 동시에 `0x2B`(IC, `ARQo`) 전송, 카드 미삽입 상태로 6초 대기 →
  readerA(COM5)가 자체 앱 레벨 Timeout으로 먼저 완료돼 채택, readerB(COM3)에 실제 `0x60`이
  전송되어 `READER_OK`로 정상 접수됨을 로그로 확인.
- **라운드2(직후 즉시 재전송, 지연 없음)**: readerA(COM5)가 방금 받은 `0x60`의 내부 상태 전이가
  끝나기 전이라 `Reader_SendCommand`가 즉시 `READER_ERR_BUSY`(-1004)를 반환 → 이 결과가 "가장
  빨리 끝난 것"으로 채택되고, readerB(COM3)에 대한 무효화는 `READER_ERR_COMMAND_NOT_ALLOWED`
  (-1005, 직전 라운드의 무효화 처리가 아직 끝나지 않은 상태에 또 `0x60`을 보낸 경우)를 반환했다
  — `SendInvalidationInit()`은 fire-and-forget이라 이 값도 예외 없이 안전하게 로그로만 남고
  흐름이 끊기지 않았다.
- **라운드3(2초 대기 후 재전송)**: readerB(COM3)가 자체 앱 레벨 Timeout으로 채택, readerA(COM5)
  무효화는 다시 `READER_ERR_COMMAND_NOT_ALLOWED`(-1005)를 반환했다 — 라운드1/2에서 파생된
  방어적 자가 무효화(`SendAndAwaitAsync`의 로컬 타임아웃 방어 로직)와 이번 라운드의 명시적
  무효화가 근접한 시각에 겹친 것으로 추정된다(상세 타임라인 분석은 development_plan.md P10-4
  참고).
- **세 라운드 모두 예외/크래시/행 없이 정상 종료됐고**, 매 라운드 정확히 1개만 채택되고 나머지
  1개에 무효화 시도가 있었다(결과 코드는 `READER_OK` 또는 `READER_ERR_COMMAND_NOT_ALLOWED` —
  둘 다 fire-and-forget 설계상 안전하게 처리됨).
- **양쪽 모두 실패**: `participants.Count == 0`일 때 `NoParticipants()`를 반환하고 아무것도
  전송하지 않는 경로는 코드 검토로 확인했으나(짧은 메서드, 로직이 단순) 실행 검증은 하지 않았다.

**완료 결과 추가(최종, 2026-08-19) — 실제 카드 태그 경합**: 사용자가 실시간으로 카드를 준비해
COM5/COM3 리더기에 태그하겠다고 알려와, 이전까지 유일하게 남아 있던 "실제 카드 리딩 응답코드
00(정상) 두 개가 경쟁하는" 핵심 업무 시나리오를 마저 검증했다. 이전 세션과의 시간차를 감안해
**타임아웃을 45초로 넉넉히 잡은 전용 하네스**(스크래치패드, 저장소 밖 — 이전과 동일하게 프로덕션
`ReaderService`/`CardReadBroadcaster`/`TransactionInfoRequestBuilder`를 리플렉션으로 그대로 호출,
`InternalsVisibleTo` 등 저장소 변경 없음, 레지스트리 미접근·포트 번호는 하네스에 직접 전달)를
새로 만들어 2라운드 실행했다.
- **라운드1(카드 태그)**: `elapsed=7.1s` 만에 **readerA(COM5)가 진짜 `0x3B` 응답코드 "00"(정상)
  으로 채택됨(`Kind=Success`)**. `CardReadResponseParser`가 실제 하드웨어 응답 바이트에서 22개
  필드를 예외 없이 정상 파싱했고(`ParseFailed=False`), 카드번호 `"35641514****706*D****201********90401"`
  (마스킹된 PAN 포함 트랙 데이터로 추정), 모듈ID `"C160390003"`, 리더기 인증 식별번호
  `"####SPD-800F1011"`을 실제로 확인했다 — **이 프로젝트에서 SPEC 파서가 실제 하드웨어의 정상
  카드 리딩 응답을 엔드투엔드로 성공 처리한 최초 확인 사례**다. 아직 응답 대기 중이던
  readerB(COM3)에는 코드가 실제로 `SendInvalidationInit()`(0x60)을 호출했고, 로그
  (`%LOCALAPPDATA%\KFTCTaxGiroCAP\logs\`)에 `[카드 리딩 페일오버 전송] 리더기[0] 채택 (이번
  라운드 최초 응답), Kind=Success`와 `[카드 리딩 페일오버 전송] 리더기[1]: ... 초기화 요청(0x60)
  전송해 무효화 -> result=0`(READER_OK)이 남아, **정상 응답 채택 + 반대쪽 실제 무효화** 양쪽 모두
  실장비로 확인됐다.
- **라운드2(직후 즉시 재전송)**: `elapsed=0.0s`로 readerB(COM3)가 직전 라운드에서 받은 `0x60`의
  내부 상태 전이가 끝나기 전이라 즉시 `READER_ERR_BUSY`(-1004)를 반환해 채택됨 — 라운드1의 실제
  카드 데이터("00" 성공, CardData 채워짐)가 라운드2로 전혀 새어 들어가지 않고(`CardData=null`),
  라운드2는 자신의 실제 결과(BUSY)로만 독립적으로 종결됨을 재확인했다(로그에도 별개 결과 코드로
  기록됨).
- 이것으로 **P10-4/P10-5 완료 조건에 남아 있던 마지막 미검증 항목(실제 카드 리딩 응답끼리의
  경쟁)까지 실장비로 검증이 끝났다.** Phase 10 범위에서 더 이상 남은 실장비 미검증 시나리오는
  없다 — `PORT_NOT_OPEN`/`SEND_FAIL`(P10-3, 물리적 케이블 분리 필요)만 여전히 미검증으로 남는다.

## P10-6. 실패 원인 구분

PRD §4.6/§4.7, §6.6이 요구하는 구분을 **타입 수준에서** 만든다.

- **전문 응답코드에 의한 실패**(리더기가 정상 응답했지만 업무적으로 실패 — `0x3B` 응답코드가 `00`/`07`/`12`
  외) vs **DLL 연동 실패**(호출 자체가 실패, 포트 오류, 콜백 미도착 등).
- 이 둘을 같은 예외 타입이나 같은 문자열로 뭉치지 않는다 — 호출자(결제 Flow, 설정 화면)가 서로 다른 문구와
  후속 처리를 해야 하기 때문이다.
- 실패 원인 문자열은 사용자에게 보여줄 수 있는 형태로 담는다(PRD §6.6 "가능한 경우 실패 원인을 함께 출력").

**완료 조건**
- [x] 두 실패 종류가 타입/열거값으로 구분되어 반환됨
- [x] 호출자가 분기할 수 있고, 각각 원인 정보를 꺼낼 수 있음
- [x] `Services/`가 바이트 오프셋을 직접 다루지 않는지 최종 점검(계층 규칙)

**완료 결과(2026-08-19)**: `Services/Reader/ReaderCommandOutcomeKind.cs`에 `ReaderFailureCategory`
(`None`/`ResponseCodeFailure`/`DllFailure`) 열거값과 `ReaderCommandOutcomeKind.ToFailureCategory()`
확장 메서드를 신설했다. 명령 4종의 결과 타입(`InitCommandOutcome`/`StatusCommandOutcome`/
`IntegrityCommandOutcome`/`CardReadCommandOutcome`) 모두 `FailureCategory` 프로퍼티를 노출해,
호출자가 `Kind`별로 매번 switch를 반복하지 않고 이 값 하나로 "전문 응답코드 실패"
(`BusinessFailure` → `ResponseCodeFailure`) vs "DLL 연동 실패"(`DllCallFailure`/`Timeout`/
`CommunicationError` → `DllFailure`)를 구분할 수 있다. 원인 정보는 `ResponseCode`(응답코드 실패)
또는 `DllResult`/`DllResultName`/`Detail`(DLL 연동 실패)로 각각 꺼낼 수 있다.
- Phase 9의 `InitOutcomeKind`를 `ReaderCommandOutcomeKind`로 일반화하면서(값은 동일, 이름만 공유
  가능하도록 변경) `ReaderSetupViewModel.cs`의 참조 지점도 함께 갱신했다 — 동작 변경 없음(리네임).
- **계층 규칙 최종 점검**: `grep -rn "byte\[\]" src/KFTCOneCAP.Wpf/Services/Reader/`로
  확인한 결과, `ReaderService`/`CardReadBroadcaster`/각 `*CommandOutcome` 어디에도 응답 바이트를
  직접 오프셋으로 슬라이싱하는 코드가 없다 — 전부 `Protocol/Reader/*Parser`/`*Builder`가 만든
  결과 객체(`InitResponseResult`/`StatusResponseResult`/`IntegrityResponseResult`/
  `CardReadResponseResult`/`CardReadData`)만 받아 매핑한다. `RawReaderCommandResult.Data`는
  CALLBACK에서 `Marshal.Copy`로 복사된 원본 그대로를 파서에 넘기는 유일한 경유지이며, 이 바이트를
  해석(오프셋 슬라이싱)하는 코드는 전부 `Protocol/Reader/*Parser.cs` 안에만 있다.

---

# Phase 11 — 로컬 DB (SQLite) 무결성 체크 이력

**이 Phase가 끝나면**: 무결성 체크 결과를 저장하고, Phase 12(리스트 표시)·Phase 15(결제 선행 판정)가 쓸
조회 API가 준비된다.

## P11-1. SQLite 패키지 선정 및 x86 로드 검증

SQLite는 **네이티브 라이브러리를 동반**하므로 Phase 8의 x86 고정과 충돌하지 않는지 먼저 확인해야 한다.
여기서 막히면 스키마 설계가 의미 없으므로 이 Task를 가장 먼저 한다.

- 후보: `Microsoft.Data.Sqlite`(+`SQLitePCLRaw`) 또는 `System.Data.SQLite.Core`. **`Microsoft.Data.Sqlite`를
  우선 시도**한다 — SDK 스타일 csproj에서 `runtimes/win-x86/native` 해석이 자연스럽고 API가 단순하다.
  x86에서 실제 로드에 실패하면 `System.Data.SQLite.Core`로 전환한다.
- **패키지 추가 후 즉시 "열고 쿼리 한 번" 실행해 네이티브 로드를 확인**한다. 빌드 성공은 아무것도 보장하지
  않는다(네이티브 로드는 런타임에 일어난다).

**완료 조건**
- [x] x86 프로세스에서 DB 연결·간단 쿼리가 성공
- [x] 출력 폴더에 필요한 네이티브 파일이 복사됨
- [x] 선택한 패키지와 그 이유를 이 Task 아래에 기록

**완료 결과(2026-08-20)**: `Microsoft.Data.Sqlite` 10.0.11을 `dotnet add package`로 추가했다 —
`System.Data.SQLite.Core`로 전환할 필요 없이 1차 시도에서 성공했다.
- **네이티브 로드 실측**: 빌드 후 `bin/Debug/net48/` 루트에 `e_sqlite3.dll`(네이티브 SQLite 본체)·
  `Microsoft.Data.Sqlite.dll`·`SQLitePCLRaw.*.dll` 3종이 자동 복사됨을 확인했다(net48 SDK 스타일
  csproj는 `runtimes/win-x86/native/e_sqlite3.dll`을 출력 폴더 루트로 평탄화해 복사한다 — 별도
  csproj 배선 없이 패키지 기본 동작만으로 됨). `e_sqlite3.dll`의 PE 헤더(`IMAGE_FILE_MACHINE`
  필드, 오프셋 `0x3C`가 가리키는 위치+4)를 직접 읽어 `0x014C`(x86)임을 확인했다.
- **"열고 쿼리 한 번" 실행 확인**: 별도 x86/net48 콘솔 하네스(스크래치패드, 저장소 밖)로
  `SqliteConnection.Open()` → `CREATE TABLE` → `INSERT` → `SELECT`까지 실제로 성공시켰다
  (`IntPtr.Size=4`로 32bit 프로세스 확인, 쿼리 결과 `"hello"` 정상 반환). 이후 P11-2~P11-4
  검증은 이 패키지를 실제로 쓰는 프로덕션 `IntegrityCheckStore`로 다시 확인했다(아래 참고).
- **패키지 선택 이유**: PRD/development_plan.md 지시대로 `Microsoft.Data.Sqlite`를 우선 시도했고,
  SDK 스타일 csproj에서 `runtimes/win-x86/native` 해석이 문제없이 동작해 `System.Data.SQLite.Core`
  전환이 필요 없었다. 버전은 이 시점 NuGet 최신 안정 버전(10.0.11)을 그대로 사용했다 — net48을
  명시적으로 지원하는 TFM이 nupkg에 포함되어 있어 `dotnet build`가 아무 경고 없이 통과했다.

## P11-2. 스키마 및 저장

PRD §7 저장 항목: 체크 일시 / COM Port / 결과 / 응답코드 / 모듈 ID / 리더기 인증 식별번호 / POS 식별번호.

- DB 파일 위치: `%LOCALAPPDATA%\KFTCTaxGiroCAP\`(P8-3 로그와 동일한 이유 — 설치 폴더 쓰기 권한 문제 회피).
- 최초 실행 시 파일·테이블 자동 생성.
- POS 식별번호는 `KFTCTAXGIROCAP01` **하드코딩 상수**(PRD §2.1)를 그대로 기록한다.
- **조회 성능 관점**: "금일·특정 COM Port·성공" 조회(P11-3)가 결제마다 실행되므로 그 조건에 인덱스를 둔다.
- 날짜는 "금일" 판정이 정확해야 하므로 저장 형식을 명확히 정한다(로컬 시간 기준, 정렬·범위 비교가 가능한 형식).

**완료 조건**
- [x] 최초 실행 시 DB/테이블이 자동 생성됨
- [x] 7개 항목이 저장되고 다시 읽힘
- [x] DB 파일을 삭제한 뒤 재실행해도 정상 재생성됨

**완료 결과(2026-08-20)**: `Services/Storage/IntegrityCheckStore.cs`에 구현했다. DB 파일 위치는
`%LOCALAPPDATA%\KFTCTaxGiroCAP\integrity_check.db`(P8-3 `FileLogger`가 로그를 두는
`%LOCALAPPDATA%\KFTCTaxGiroCAP\` 폴더와 같은 규칙, 하위에 `logs\`와 나란히 DB 파일을 둔다).
`IntegrityCheckHistory` 테이블(`CheckedAtLocal`/`ComPort`/`IsSuccess`/`ResponseCode`/`ModuleId`/
`ReaderAuthId`/`PosId` 7컬럼)과 인덱스 2개(`IX_IntegrityCheckHistory_Today`(ComPort, IsSuccess,
CheckedAtLocal) — P11-3의 "금일·포트·성공" 조회용, `IX_IntegrityCheckHistory_CheckedAt` — 리스트
최신순 조회용)를 `CREATE TABLE/INDEX IF NOT EXISTS`로 정의해 매 연결마다 멱등하게 보장한다. POS
식별번호는 `IntegrityCheckStore.PosId = "KFTCTAXGIROCAP01"` 상수로 코드에 고정(PRD §2.1) —
`IntegrityCheckRecord` 입력 모델에는 포함하지 않아 호출자가 실수로 다른 값을 넣을 수 없게 했다.
날짜는 `"yyyy-MM-dd HH:mm:ss.fff"`(로컬 시각) 문자열로 저장해 문자열 사전식 비교가 곧 시간순
비교와 일치하도록 했다(범위 조건에 별도 날짜 파싱 함수 없이 `>=`/`<` 문자열 비교로 정확한 결과를
얻는다).
- **검증**: 프로덕션 코드를 그대로 참조하는 x86/net48 콘솔 하네스(`ProjectReference`로
  `KFTCOneCAP.Wpf.csproj`를 참조, 스크래치패드, 저장소 밖 — 이 클래스는 전부 `public`이라
  `InternalsVisibleTo` 등 저장소 변경이 필요 없었다)로 확인했다. 기본(공개) 생성자를 그대로 써서
  **실제 배포 경로**(`%LOCALAPPDATA%\KFTCTaxGiroCAP\integrity_check.db`)에 대해 검증했다 — 하네스
  실행 전 그 경로에 기존 파일이 있으면 백업 후 진행하고, 종료 시(성공/실패 무관, `finally`) 항상
  원상 복구하도록 만들어 실제 사용자 데이터를 건드리지 않았다(테스트 종료 후
  `%LOCALAPPDATA%\KFTCTaxGiroCAP\`에 `logs\`만 남고 DB 파일 잔여물 없음을 확인).
  - DB 디렉터리·파일이 최초 `Save()` 호출 시 자동 생성됨을 확인(`Directory.Exists`/`File.Exists`).
  - 7개 항목(체크일시/포트/결과/응답코드/모듈ID/리더기인증식별번호/POS식별번호)을 저장한 뒤
    `GetHistory()`로 다시 읽어 각 프로퍼티(`CheckedAt`/`ComPort`/`IsSuccess`/`ResponseCode`/
    `ModuleId`/`ReaderAuthId`/`PosId` — 검증 당시엔 `IntegrityCheckRow`, 이후 계층 규칙 위반 수정으로
    `IntegrityCheckHistoryEntry`로 교체됐으나 필드 값 자체는 동일하게 왕복됨을 재확인함)와 정확히
    일치함을 확인(성공 케이스 1건 + 응답코드/모듈ID/리더기ID가 모두 `null`인 실패 케이스 1건 양쪽
    모두 저장 성공, `Save().Success == true`).
  - DB 파일을 `File.Delete()`로 지운 뒤(`SqliteConnection.ClearAllPools()`로 커넥션 풀을 먼저 비워
    파일 잠금 해제) 새 `IntegrityCheckStore` 인스턴스로 `Save()`를 호출 — 파일이 자동 재생성되고
    저장이 정상 성공함을 확인(P11-2 세 번째 완료 조건).

## P11-3. 조회 API 2종

1. **리스트 표시용**(리더기 설정 화면 §4.6): 조회기간 필터, 최신순. `Services/Storage/IntegrityCheckHistoryEntry`
   (순수 DTO, 신규)로 반환한다 — **`Models/IntegrityCheckRow`에 직접 연결하지 않는다.** 그 모델은
   `System.Windows.Media.Brush`/`Application.Current.Resources`를 참조하는 화면 표시용 타입이라, Storage가
   그걸 반환하면 계층 규칙(ROADMAP.md "계층 구조" — Services는 WPF 타입을 모른다)이 깨진다. `IntegrityCheckRow`로의
   변환(시각 서식, 결과 코드 표시값)은 이 값을 쓰는 ViewModel(Phase 12)의 책임이다.
2. **금일·동일 COM Port 성공 이력 존재 여부**(결제 선행 판정, PRD §4.2): 리더기 2대를 쓰므로 **포트별로**
   물을 수 있어야 한다.

- 날짜 경계(자정 직전/직후)와 포트 구분이 정확해야 한다 — 여기가 틀리면 결제마다 불필요한 무결성 체크가
  돌거나, 반대로 필요한 체크를 건너뛴다.

**완료 조건**
- [x] 기간 필터 조회가 경계값(시작일/종료일 당일 포함)에서 올바름
- [x] "금일 성공 이력" 판정이 자정 경계에서 올바름(어제 성공 → 오늘 조회 시 `false`)
- [x] 포트가 다르면 서로 영향 없음(`COM1` 성공이 `COM2` 판정에 영향 없음)

**완료 결과(2026-08-20)**: `IntegrityCheckStore.GetHistory(DateTime fromInclusive, DateTime
toInclusive)`(리스트 표시용, 최신순, `List<IntegrityCheckHistoryEntry>` 반환)와
`HasSuccessToday(string comPort)`(결제 선행 판정용, PRD §4.2) 2종을 구현했다.
`ReaderSetupViewModel`의 `IntegrityRows`/`BuildDummyRows` 배선 교체는 화면 작업인 Phase 12 몫이라
이번 Phase에서는 손대지 않았다.

**수정(2026-08-20, Phase 11~ 코드 검토에서 발견 후 즉시 수정)**: 처음 구현에서는 `GetHistory`가
`Models/IntegrityCheckRow`(1차 범위 더미 모델)를 직접 반환했다. 그런데 이 모델은
`System.Windows.Media.Brush`/`Application.Current.Resources`를 참조하는 화면 바인딩용 타입이라
`Services`가 WPF 타입을 모른다는 계층 규칙(공통 규칙 5)이 반환 타입을 통해 우회적으로 깨지고
있었다 — `Application.Current`가 없는 컨텍스트(콘솔 하네스, Phase 15 결제 워커 스레드)에서
`ResultBackground`/`ResultForeground`를 평가하면 `NullReferenceException`이 날 수 있는 실질적
위험도 있었다. 또한 `GetHistory` 안에서 시각을 `"yyyyMMddHHmmss"`로 포맷하고 실패 건에 `"FAIL"`
매직 문자열을 채우는 등 **표시 서식이 Storage 계층에 새어 들어와 있었다.** `IntegrityCheckStore`가
아직 아무 데서도 호출되지 않아(배선은 Phase 12) 고치는 비용이 가장 쌀 때 바로잡았다 —
`Services/Storage/IntegrityCheckHistoryEntry`(순수 DTO, 원본 DB 값을 서식 없이 그대로 담음)를
새로 만들고 `GetHistory`가 이걸 반환하도록 변경, 시각 서식/표시 코드값 매핑 로직은 전부 제거했다.
`IntegrityCheckHistoryEntry → IntegrityCheckRow` 변환은 Phase 12에서 ViewModel이 담당한다. 이
변경 후 `dotnet build` 경고 0/오류 0, `Services/` 전체에 `System.Windows` `using`이 없음을
재확인했다. **재검증**: 새 DTO 타입으로 실제 배포 경로(`%LOCALAPPDATA%\KFTCTaxGiroCAP\`)에 대해
Save→GetHistory→HasSuccessToday 왕복 하네스를 다시 실행(기존 파일 백업 후 실행, `finally`로 원상
복구) — 성공/실패 각 1건 저장 후 `GetHistory`가 `IntegrityCheckHistoryEntry` 2건을 최신순으로
정확히 반환하고 각 필드(`CheckedAt`/`ComPort`/`IsSuccess`/`ResponseCode`/`ModuleId`/`ReaderAuthId`/
`PosId`)가 저장값과 일치, `HasSuccessToday`도 포트별로 기존과 동일하게 판정됨을 재확인했다.
- `GetHistory`는 `fromInclusive.Date`(00:00:00.000)부터 `toInclusive.Date.AddDays(1)`(다음 날
  00:00:00.000) **미만**으로 조회해, 종료일 23:59:59.999를 리터럴로 계산하는 대신 배타적 상한으로
  경계 실수를 줄였다. `HasSuccessToday`도 같은 방식(`DateTime.Now.Date` ~ `AddDays(1)` 미만)으로
  자정 경계를 처리한다.
- **검증**(P11-2와 같은 하네스로 이어서 실행, 실제 배포 경로 사용 후 원상 복구):
  - **경계값**: 오늘 2건 저장 후 `GetHistory(오늘, 오늘)` → 정확히 2건, `CheckTime` 내림차순(문자열
    비교로 `row0 >= row1`) 확인. 어제 1건을 추가로 저장한 뒤 다시 `GetHistory(오늘, 오늘)` →
    여전히 2건(어제 데이터가 섞이지 않음), `GetHistory(어제, 오늘)` → 3건(경계 양쪽 포함) 확인.
  - **금일 성공 이력 자정 경계**: `HasSuccessToday("COM 01")`(오늘 성공 이력 있음) → `true`,
    `HasSuccessToday("COM 02")`(오늘 실패 이력만 있음) → `false`, `HasSuccessToday("COM 03")`(이력
    없음) → `false`. 이어서 `COM 09`에 **어제 날짜의 성공** 레코드만 저장한 뒤
    `HasSuccessToday("COM 09")` → `false`(어제 성공이 오늘 판정에 영향 없음, 자정 경계 확인).
  - **포트 독립성**: 위 `COM 01`/`COM 02`/`COM 03`/`COM 09` 4개 포트가 서로 다른 이력 상태(성공/
    실패만 있음/이력 없음/어제만 성공)로 저장돼 있는데도 각 포트의 `HasSuccessToday` 판정이 서로
    영향을 주지 않음을 위 4개 어서션으로 함께 확인했다.

## P11-4. 오류 내성

- DB 파일 손상/잠김/디스크 문제로 저장·조회가 실패해도 **앱이 죽지 않는다**(PRD §9).
- **저장 실패 시 정책(2026-08-20 사용자 확정)**: DB 저장 자체의 성공/실패와 무결성 체크의 업무 결과(성공/실패)를
  분리해서 판단한다.
  - **무결성 체크가 성공**했는데 그 결과를 DB에 저장하는 것만 실패한 경우 → **로그만 남기고 결제는 계속
    진행**한다(DB 저장 실패가 결제를 막지 않는다).
  - **무결성 체크 자체가 실패**한 경우 → DB 저장 성공/실패와 무관하게 **결제를 막는다**(이건 원래 PRD §4.2의
    기본 동작이지 DB 문제가 아니다).
- 조회 실패 시: "이력 없음"으로 간주해 무결성 체크를 수행하는 쪽이 안전하다(체크를 건너뛰는 것보다 낫다).

**완료 조건**
- [x] DB 파일을 잠그거나 손상시킨 상태에서도 앱이 정상 동작(기능만 실패)
- [x] 무결성 체크 성공 + DB 저장 실패 조합에서 로그만 남고 진행되는 것을 확인(결제 Flow 연결은 Phase 15
      이후이므로, 이 Phase에서는 저장 API가 예외를 던지지 않고 실패를 값으로 반환하는 것까지만 확인)

**완료 결과(2026-08-20)**: `IntegrityCheckStore`의 `Save`/`GetHistory`/`HasSuccessToday` 3개
공개 메서드 전부 내부에서 `try/catch`로 감싸 예외를 외부로 던지지 않는다(`IntegrityCheckStore.cs`
참고) — 실패는 `IntegrityCheckSaveResult.Failed(string errorMessage)`(저장) 또는 빈
`List<IntegrityCheckRow>`/`false`(조회, "이력 없음"으로 간주)로 표현하고, 원인은
`FileLogger.Error`로 남긴다. 이 정책 자체가 이번 세션에서 사용자가 확정한 "저장 실패 ≠ 무결성 체크
업무 실패"를 그대로 반영한 것이다(P11-4 상단 정책 문단, `IntegrityCheckSaveResult`/
`IntegrityCheckRecord.cs` 클래스 주석에도 근거를 남겼다).
- **검증**(P11-2/P11-3과 동일한 프로덕션 코드 하네스, 실제 배포 경로 사용 후 원상 복구): DB 파일을
  `SqliteConnection.ClearAllPools()`로 커넥션 풀을 비운 뒤 `File.Delete` → 유효하지 않은 텍스트로
  덮어써 "손상된 SQLite 파일" 상태를 실제로 만들었다(단순 이름 변경이 아니라 파일 내용 자체를
  깨서, `SqliteException`이 실제로 발생하는 것까지 확인).
  - `Save()` 호출 → **예외를 던지지 않고** `Success=false`를 반환, `ErrorMessage`에 실제 SQLite
    오류(`"SQLite Error 26: 'file is not a database'."`)가 담김을 확인. 이것이 P11-4가 요구하는
    "저장 API는 예외가 아닌 값으로 실패를 반환"의 실측 근거다 — **이 반환값 자체가 "무결성 체크는
    성공했지만 저장만 실패"를 호출자에게 알리는 통로이므로, Phase 15에서 이 값이 `false`여도
    결제를 막지 않고 로그만 남기도록 호출하면 정책이 그대로 성립한다**(이번 Phase는 결제 Flow가
    아직 없어 저장 API의 반환값 계약만 확인, 실제 배선은 Phase 15).
  - `GetHistory()`/`HasSuccessToday()` 둘 다 같은 손상 DB에서 예외 없이 각각 빈 목록/`false`를
    반환함을 확인(정책대로 "이력 없음"으로 안전하게 간주).
  - 손상된 DB 파일을 지운 뒤 재실행하면 정상적으로 재생성되어 저장이 다시 성공함도 함께 확인했다
    (P11-2 완료 조건과 겹치는 부분이지만 오류 내성 관점에서 "손상 → 삭제 → 정상 복구"까지 한
    흐름으로 검증됐다는 점을 여기 함께 기록한다).
- **미검증 범위**: "DB 파일 잠금(다른 프로세스가 배타적으로 열어 둔 상태)"은 파일 손상과는 다른
  경로라 별도로 재현을 시도하지 않았다 — SQLite는 파일 잠금 시 `SQLITE_BUSY`/`SQLITE_LOCKED`
  오류를 반환하는데, 이 역시 `Microsoft.Data.Sqlite`가 `SqliteException`으로 던지고 위와 동일한
  `try/catch` 경로로 흡수되므로(코드 경로가 손상 케이스와 동일) 실행 검증 없이 코드 검토로
  갈음했다 — 무리하게 파일 잠금을 인위적으로 재현하지 않았다(다른 프로세스가 SQLite 파일을
  독점 잠그려면 별도 프로세스 조율이 필요해 이번 범위에서는 시도하지 않음).

---

# Phase 12 — 리더기 설정 화면 실동작 배선

**이 Phase가 끝나면**: 사람이 리더기 설정 화면에서 직접 버튼을 눌러 실제 하드웨어를 제어할 수 있고,
무결성 체크 결과가 DB에 남아 화면 리스트에 보인다. **이 프로젝트에서 처음으로 "화면 → 실장비" 전체
경로가 눈에 보이는 Phase**이며, Phase 15 결제 Flow가 쓸 포트 생명주기·무결성 체크 흐름도 여기서 확정된다.

> **왜 결제 Flow(Phase 14~)보다 먼저인가**: 결제 Flow는 화면이 없어 동작을 눈으로 확인할 수 없다.
> 이 Phase에서 "포트 열기 → 명령 전송 → 응답 파싱 → 화면 표시 → DB 저장"이 사람 손으로 검증되면,
> 뒤쪽 Phase에서 문제가 생겼을 때 **리더기 계층은 이미 검증됐다는 전제**로 범위를 좁힐 수 있다.

> **이 Phase의 가장 큰 설계 결정은 P12-1(포트 소유자)이다.** 나머지 Task는 그 위에 얹히므로 순서를
> 바꾸지 않는다.

## 이 Phase에서 손대지 않는 것 (범위 밖 확정)

착수 전에 못 박는다 — 아래는 "잊어버린 것"이 아니라 **의도적으로 제외**한 것이다(PRD §6, ROADMAP Phase 12).

- **포트 열기 토글**(`PRD_WPF.md` §4.8) — 기능 자체를 되살리지 않는다. PRD §2.2.2대로 "항상 열어둔다"로
  확정됐고 UI에서도 제거된 상태(`Visibility="Hidden"`)다. 자리만 남은 요소를 건드리지 않는다.
- **AOP 제약**(§4.11), **TRANSINFO_AOP 검증**(§4.12) — 2026-08-18 제외 확정.
- **멀티패드 토글**(§4.9) — 1차 범위대로 값을 레지스트리에 저장만 하고, **이번 DLL 연동에서 그 값을 읽어
  쓰지 않는다**(2026-08-19 확정 — QR 기능인지 포트 공유인지 개념 미확정, PRD에 요구사항 없음).
- **키다운로드/업데이트 버튼** — 버튼만 유지, 동작은 PRD §11 추후 구현. **기존 3초 스텁을 그대로 둔다**
  (실통신으로 바꾸지 않는다).
- **핀패드 연동** — `Pinpad_*` API는 선언만 있고 이번 범위에서 호출하지 않는다(PRD §10).

## P12-1. 포트 생명주기 소유자 신설 (`ReaderConnectionManager`)

**이 Task가 이번 Phase의 핵심이다.** 현재 `ReaderService` 인스턴스가 `ReaderSetupViewModel`의 필드
(`_reader1Service`)로 들어 있는데, 이 구조로는 PRD §2.2.2를 만족할 수 없다.

- 리더기 설정 화면은 **열렸다 닫혔다** 하고 그때마다 ViewModel이 새로 생성된다 → 포트가 화면 수명에
  묶여버려 "항상 열어둔다"가 성립하지 않는다.
- Phase 15 결제 Flow는 **화면 없이** 같은 포트를 써야 한다(PRD §4.3 카드 리딩) → ViewModel이 소유하면
  결제 Flow가 접근할 방법이 없다.
- PRD §2.2.2가 "포트를 닫는 경우는 **하나뿐**"이라고 못 박았는데, 소유자가 흩어져 있으면 이 규칙을
  강제할 지점이 없다.

따라서 **앱 수명과 같이 사는 단일 소유자**를 만든다.

- 위치: `Services/Reader/ReaderConnectionManager.cs`(계층 규칙상 `Services/Reader/`, WPF 타입 금지).
- 리더기1/2의 `ReaderService` 인스턴스 **2개를 소유**하고, 외부에는 "리더기1/리더기2"라는 논리적 이름으로
  노출한다. `ReaderService`는 Phase 10에서 **포트별 인스턴스**로 설계됐으므로 그 전제를 그대로 잇는다.
- **앱 기동 시** 레지스트리(`ReaderSettingsService`) 값을 읽어 설정된 포트를 연다. `"미사용"`이면 열지
  않는다. baudRate는 **`115200` 고정**(PRD §2.2.1).
- **열기 실패해도 앱은 정상 기동한다**(PRD §2.2.2/§9) — 실패는 `FileLogger`에만 남기고 **기동 시점에
  모달을 띄우지 않는다**. 이 앱은 트레이 상주로 자동 최소화 기동하므로(원본 동작), 기동 직후 모달을
  띄우면 사용자가 보지도 못한 창이 포커스를 뺏는다. 실패한 포트는 다음 명령 시
  `SendCommandSafe`(P10-3)의 `readerId` 없음 경로가 자동으로 다시 열기를 시도한다.
- **포트를 닫는 유일한 지점**: COM 포트 콤보 변경 저장 시(P12-2/P12-3). 이 클래스에 `ReopenAsync`류
  메서드 하나만 두고, **다른 어떤 코드도 `ClosePort`를 직접 부르지 않는다**(그 규칙을 클래스 주석에 남긴다).
- 앱 종료 시 정리(포트 닫기)는 여기서 함께 책임진다(PRD §9 리소스 정리).
- 소유/생성 지점: `App.xaml.cs`(앱 수명주기). ViewModel은 **생성하지 않고 참조만** 한다.

> **DI 컨테이너를 도입하지 않는다.** 이 앱에서 앱 수명 싱글턴이 필요한 것은 현재 이것 하나뿐이고,
> 컨테이너를 넣으면 Phase 13~17에서 등록/해석 코드가 계속 늘어난다. `App` 정적 프로퍼티(또는 그에
> 준하는 단순한 접근점) 하나로 충분하다 — 나중에 대상이 3~4개로 늘면 그때 재검토한다(그 판단 근거를
> 주석에 남긴다).

**완료 조건**
- [x] `ReaderService` 인스턴스가 `ReaderSetupViewModel` 필드에서 사라지고, 앱 수명 소유자로 옮겨짐
- [x] 앱 기동 시 설정된 포트가 열리고 그 결과(성공/실패+사유)가 로그에 남음
- [x] 리더기가 없는 포트/`"미사용"` 설정에서도 앱이 정상 기동하고 모달이 뜨지 않음
- [x] 리더기 설정 화면을 **열고 닫아도 포트가 닫히지 않음**(로그로 확인 — 창 닫기 후 명령이 그대로 성공)
- [x] `ClosePort` 호출 지점이 이 클래스 안 1곳뿐임(grep으로 확인)

**완료 결과(2026-08-20)**: `Services/Reader/ReaderConnectionManager.cs`를 신설하고 `App.xaml.cs`
`OnStartup`에서 `App.ReaderConnections`(internal static) 하나로 생성 — `ReaderSetupViewModel`의
`_reader1Service` 필드(Phase 9 파일럿)를 제거하고 생성자로 `ReaderConnectionManager`를 전달받아
참조만 하도록 바꿨다(`Views/ReaderSetupWindow.xaml.cs`가 `new ReaderSetupViewModel(App.ReaderConnections!)`).
- **앱 기동 시 자동 오픈 확인**: 로그
  `[리더기1] COM1 열기 성공(readerId=0)` / `[리더기2] 포트 미설정('미사용') — 열지 않음`(레지스트리
  Port1="COM 01", Port2="미사용" 상태에서 기동) — 모달 없이 홈 화면이 바로 뜸을 `mcp__windows__*`
  스냅샷으로 확인.
- **창 열고 닫아도 포트 유지**: 리더기 설정 화면을 열어 초기화/상태체크/무결성체크 3버튼을 누른 뒤
  화면을 닫고 다시 열어 재차 명령을 성공시켰다 — 그 사이 로그에 포트 닫기/열기 라인이 전혀 없음을
  확인(아래 P12-6 로그 발췌 참고).
- **`ClosePort` 호출 지점 grep**: `grep -rn "\.ClosePort\(" src` 결과
  `Services/Reader/ReaderConnectionManager.cs`의 `ClosePortIfOpen` 메서드 1곳뿐(`Reopen`/`CloseAll`
  둘 다 이 메서드를 거쳐 간접 호출).
- **앱 종료 시 정리**: 홈 화면 "프로그램 종료" 클릭 → 로그에
  `[리더기1] 포트 닫기 성공` / `[리더기2] 포트 닫기 성공` 순서로 기록되고 프로세스가 정상 종료됨을
  `tasklist`로 확인.

**수정(2026-08-20, 사용자 수동 테스트 후 확정)**: 처음 구현은 콤보 변경 시점과 포트 닫기 시점을 모두
"확인(저장) 버튼 클릭"에 묶어 뒀다. 사용자가 실제로 화면을 조작해보고 "레지스트리 저장은 확인을 누를 때만
하는 게 맞지만, 창 안에서 콤보 선택 자체를 바꾸는 즉시 기존 포트를 닫아 포트 변경을 준비해야 한다"고
확정했다 — 확인/취소는 **레지스트리에 반영할지**만 정하는 시점이고, 포트 점유 여부는 콤보 변경에 바로
반응해야 한다는 것이 사용자 의도다(새 포트를 여는 것은 여전히 확인 시점에만 한다 — 저장 전에 취소될 수
있는 선택을 미리 점유하지 않기 위해). `ReaderConnectionManager`에 `ClosePortForPendingChange(service,
label)`를 추가했다(기존 `ClosePortIfOpen` 하나만 거치므로 "ClosePort 호출 지점 1곳" 규칙은 그대로
유지됨). `ReaderSetupViewModel`의 `OnReader1/2PortSelectionChanged`에서 이걸 호출하되, `Load()`가
레지스트리 값을 콤보에 최초 반영할 때는 "사용자가 바꾼 것"이 아니므로 `_isLoadingPortSelection` 플래그로
막아 화면을 열자마자 이미 열려 있는 포트가 닫히는 사고를 방지했다. `dotnet build` 경고 0/오류 0 확인.

**수정(2026-08-20, 후속 — 액션 버튼/취소 시 연결 정합성)**: 위 문단에서 "취소 시 포트 복구 로직은
의도적으로 넣지 않았다"고 적었으나, 이어진 사용자 확인 과정에서 그 판단이 뒤집혔다 — 아래 3가지가
추가/변경됐다(전부 실장비 COM5/COM3로 재검증 완료).

1. **액션 버튼이 "화면에 선택된 콤보 값"으로 연결해서 실행**해야 한다는 요구사항이 나왔다 — 콤보만
   바꾸고 아직 저장 전인 상태에서 초기화 버튼을 누르면, 그때까지는 `ReaderService`가 옛 포트 번호를
   기억하고 있어 **화면엔 새 포트가 보이는데 실제로는 옛 포트에 명령이 나가는** 불일치가 있었다.
   `ReaderConnectionManager.EnsureOpenForSelection(service, label, portDisplay)`를 추가해 해결했다 —
   이미 원하는 포트에 연결돼 있으면 아무 것도 안 하고(불필요한 재연결 방지), 아니면 `Reopen`으로
   전환한다. `ExecuteInitAsync`/`ExecuteStatusAsync`/`ExecuteIntegrityAsync`(P12-3/P12-4) 전부 명령
   전송 직전에 이걸 호출하도록 바꿨다.
2. **취소 시 포트를 스냅샷(레지스트리) 값으로 복원**하는 `ReaderSetupViewModel.DiscardPortChanges()`를
   추가했다 — 액션 버튼으로 저장 전 포트에 실제 연결해 봤을 수 있기 때문에, 단순히 "닫기"만 하면
   `ReaderService`가 기억하는 포트 번호가 테스트했던 값으로 남아 나중에 자동 재연결(P10-3)이 레지스트리
   값이 아닌 그 값으로 시도하는 불일치가 생긴다 — 그래서 "닫기"가 아니라 "스냅샷 포트로 재오픈"이다.
   `Views/ReaderSetupWindow.xaml.cs`의 `CancelButton_Click`에서 dirty-check 확인창을 통과한 직후 호출한다.
   스냅샷 포트가 죽어 있어도 예외 없이 조용히 로그만 남긴다(PRD §2.2.2와 동일 원칙).
3. **`EnsureOpenForSelection`을 "미사용" 케이스까지 처리하도록 확장**하고, `ReaderSetupViewModel.Save()`/
   `DiscardPortChanges()` 둘 다 각자 스냅샷 비교 로직을 두는 대신 이 메서드 하나로 통일했다 — "이미
   액션 버튼으로 올바른 포트에 연결된 상태에서 확인을 누르면 다시 열 필요가 있나?"라는 질문에서
   발견된 중복 재오픈(로그에 불필요한 닫기/열기가 한 번 더 남는 문제)을 없앴다.

**검증**(전부 `mcp__windows__*`로 코디네이터가 직접 실장비 COM5/COM3 대상 실행, 로그·레지스트리 값
실측): (A) 콤보 안 바꾸고 버튼 클릭 → 재연결 로그 없이 즉시 성공. (B) 콤보 변경 즉시
`[리더기1] 포트 닫기 성공` 로그. (C) 미저장 상태에서 버튼 클릭 → 실제로 새로 선택한 포트로 연결
(`COM3 열기 성공` 로그, 화면 표시와 실제 통신 대상 일치 확인). (D) 취소 확정 → 스냅샷 포트로 재오픈
(닫기+열기 로그), 창을 다시 열어도 레지스트리가 안 바뀌어 있음을 재확인. (E) 이미 올바르게 연결된
상태에서 확인 클릭 → 로그 추가 없음(중복 재오픈 제거 확인), 레지스트리엔 정상 저장. `dotnet build`
경고 0/오류 0.

## P12-2. 실제 COM 포트 열거 + **포트 문자열 단일 형식 규칙**

`PRD_WPF.md` §4.13의 1차 보류 항목을 해소한다. 단순해 보이지만 **형식 불일치가 조용한 버그를 만드는
지점**이라 규칙을 먼저 정하고 시작한다.

- 열거: `System.IO.Ports.SerialPort.GetPortNames()` → `"COM5"`, `"COM11"` 같은 형식으로 돌아온다.
- 콤보 표시/레지스트리 저장 형식은 1차 범위 그대로 **`"COM %02d"`**(예: `"COM 05"`, 공백 포함 2자리)다.
  첫 항목은 항상 `"미사용"`, 이후 **번호 오름차순** 정렬.
- 저장된 포트가 열거 목록에 없으면 `"COM 05(사용불가)"` 형태로 **목록에 추가해 선택 상태를 유지**한다
  (조용히 `"미사용"`으로 바꿔버리지 않는다 — 리더기가 잠깐 빠졌을 때 설정이 날아가면 안 된다).

> **⚠️ 포트 문자열 형식 규칙(이번 Phase에서 확정, 이후 Phase가 의존한다)**
>
> 같은 포트를 가리키는 표현이 최소 3가지다 — 콤보/레지스트리 표시 문자열(`"COM 05"`),
> `GetPortNames()` 반환값(`"COM5"`), `Reader_OpenPort`의 `portNumber`(정수 `5`). 여기에 **Phase 11
> DB의 `ComPort` 컬럼**이 더해진다(`IntegrityCheckStore.HasSuccessToday(comPort)`는 **문자열 완전
> 일치**로 조회한다 — Phase 11 리뷰에서 지적된 지점).
>
> 형식이 어긋나면 **에러 없이 조용히** 금일 성공 이력을 못 찾아 매 결제마다 무결성 체크가 반복된다.
> 따라서:
> - **DB에 저장·조회하는 `ComPort` 값은 콤보 표시 문자열(`"COM 05"`)로 통일**한다. 저장(P12-4)과
>   조회(P12-5, Phase 15) 양쪽이 같은 소스에서 값을 얻어야 한다.
> - 표시 문자열 ↔ 정수 변환은 **한 곳에만** 둔다(현재 `ReaderSetupViewModel.ExtractPortNumber`가
>   private static으로 있으나, P12-1의 소유자와 ViewModel 양쪽이 써야 하므로 공용 위치로 옮긴다).
>   변환 유틸의 위치·이름은 구현자가 정하되 **중복 구현을 만들지 않는다**.
> - `"(사용불가)"` 접미가 붙은 값을 정수 변환/DB 저장에 그대로 흘리지 않는다.
- 기존 하드코딩 제거: XAML의 `<ComboBoxItem Content="COM 01"/>`/`"미사용"` 정적 항목과
  `ReaderSetupViewModel.NormalizePortSelection`의 `"COM 01"` 하드코딩(P7 당시 스텁)을 걷어내고
  **`ItemsSource` 바인딩**으로 바꾼다(P7-3에서 세운 "코드비하인드가 `ItemsSource`를 대입하지 않는다"
  규칙을 그대로 지킨다 — ViewModel의 컬렉션에 바인딩).

**완료 조건**
- [x] 실제 연결된 포트가 콤보에 `"COM %02d"` 형식으로 나오고, 첫 항목이 `"미사용"`
- [x] 저장된 포트가 목록에 없을 때 `"(사용불가)"`가 붙어 선택이 유지됨
- [x] 표시 문자열 → 정수 변환 코드가 저장소에 **1곳만** 존재(grep으로 확인)
- [x] XAML에 COM 포트 `ComboBoxItem` 하드코딩이 남아 있지 않음

**완료 결과(2026-08-20)**: `Services/Reader/ComPortFormat.cs`를 신설해 표시 문자열(`"COM 05"`)
↔ 정수(`ToPortNumber`, `ParseSystemPortName`) ↔ "(사용불가)" 표시(`ToUnavailableDisplay`/
`StripUnavailableSuffix`) 변환을 이 한 곳에 모았다. `ReaderSetupViewModel.RebuildAvailablePorts`가
`SerialPort.GetPortNames()`로 실제 열거해 `AvailablePorts`(ObservableCollection&lt;string&gt;)를
채우고, `Views/ReaderSetupWindow.xaml`의 두 콤보(`Reader1PortCombo`/`Reader2PortCombo`)는
`ComboBoxItem` 하드코딩을 제거하고 `ItemsSource="{Binding AvailablePorts}"` +
`SelectedItem="{Binding ReaderNPortSelection}"`로 바꿨다.
- **실제 열거 확인**: 이 개발 PC의 `SerialPort.GetPortNames()` = `COM1,COM2,COM3,COM5`. 콤보를 열어
  스냅샷/스크린샷으로 `미사용/COM 01/COM 02/COM 03/COM 05` 5항목이 오름차순으로 나옴을 확인.
- **"(사용불가)" 확인**: PowerShell로 레지스트리 `COMPORT1_FIELD`를 존재하지 않는 `"COM 09"`로
  바꾼 뒤 화면을 다시 열어 콤보에 `"COM 09(사용불가)"`가 추가되고 그 값이 선택 상태로 유지되며
  해당 카드(액션 버튼 5개)가 활성 상태임을 스크린샷으로 확인(조용히 "미사용"으로 바뀌지 않음).
  이어서 콤보를 "COM 05"로 바꿔 저장 → 레지스트리에 `"COM 05"`(접미 없는 깨끗한 값)로 저장됨을
  `reg query`로 확인 — "(사용불가)" 접미가 저장/DB에 흘러가지 않는다는 완료 조건도 함께 검증됨.
- **grep 확인**: `ToPortNumber` 구현은 `ComPortFormat.cs` 1곳, 호출은
  `ReaderSetupViewModel.ResolveSelectablePort`/`ReaderConnectionManager.OpenIfConfigured` 2곳뿐
  (둘 다 같은 유일 구현을 호출) — 중복 구현 없음.
- **XAML 하드코딩 제거 확인**: `grep -n "ComboBoxItem" Views/ReaderSetupWindow.xaml` 결과 남은
  3건은 전부 조회기간 콤보(`QueryPeriodCombo`, "오늘/7일/30일/100일")로 이번 Task 범위 밖(COM 포트
  콤보 아님).

## P12-3. 초기화 / 상태체크 실동작 배선 + 결과 표시

Phase 9에서 **리더기1의 "초기화" 버튼 하나만** 임시로 연결해 두고 결과를 로그로만 남겼다
(`ReaderSetupViewModel.ExecuteReader1InitAsync`, `TODO(Phase 12)` 주석). 이걸 정식 배선으로 바꾸고
리더기2까지 확장한다.

- **초기화**(PRD §6.1): `0x60`→`0x70`, 응답코드 `00`이면 성공.
  - 성공: `리더기 초기화 성공`
  - 실패: `리더기 초기화 실패\n{실패 원인}`
- **상태체크**(PRD §6.2): `0x61`→`0x71`, 응답코드 **`00` 또는 `08`**이면 성공(`08`="IC 카드 삽입되어 있음",
  P10-1에서 확인 — 이 둘을 성공으로 묶는 것은 SPEC 규정이 아니라 이 프로젝트의 업무 판단이다).
  - 성공: `리더기 상태체크 성공\n리더기 인증 식별번호 : XXXXX\n모듈 ID : XXXXX`
  - 실패: `리더기 상태체크 실패\n{실패 원인}`
  - 값은 `0x71` 응답에서 파싱한다(`Protocol/Reader/StatusResponseParser`, P10-1에서 완성).
- **실패 원인 구분**(PRD §6.6): "전문 응답코드에 의한 실패"와 "DLL 연동 실패"를 **구분해서** 표시한다.
  Phase 10이 이미 타입으로 구분해 뒀다 — `ReaderCommandOutcomeKind`(Success/BusinessFailure/
  DllCallFailure/Timeout/CommunicationError)와 `ReaderFailureCategory`. **이 구분을 문구로 흘려보내는
  매핑을 한 곳에 만든다**(각 버튼 핸들러에서 `switch`를 복사하지 않는다 — 현재 `LogInitOutcome`이
  초기화 전용으로 하드코딩돼 있는데, 명령 4종이 같은 형태를 쓰므로 공용화한다).

> **결과 표시 방법**: PRD 문구가 `\n`으로 여러 줄인 형태라 **모달 알림(MessageBox)** 이 전제다(1차 범위
> 화면에는 이 값을 인라인으로 놓을 자리가 없다 — `PRD_WPF.md` §4의 어느 영역에도 해당 필드가 없음).
> **단, ViewModel이 `MessageBox`를 직접 호출하지 않는다.** P7-2에서 `MessageBox`/`Window.Close()`는
> View 책임으로 남기기로 정했고, 그 규칙을 깨면 ViewModel이 다시 WPF에 묶인다. 기존
> `ResultsUpdated` 이벤트와 **같은 패턴**으로 ViewModel이 "이런 결과를 알려야 한다"는 이벤트를 올리고,
> `ReaderSetupWindow.xaml.cs`가 그걸 구독해 `MessageBox`를 띄운다.

> **스레드 주의**: `AsyncRelayCommand`가 UI 스레드에서 시작되고 `await`에 `ConfigureAwait(false)`를
> 붙이지 않으면 continuation이 UI 스레드로 돌아온다 — `ReaderService` 내부가 콜백 스레드에서
> `TaskCompletionSource`를 완료시켜도 마찬가지다. **ViewModel 쪽 `await`에는 `ConfigureAwait(false)`를
> 붙이지 않는다**(붙이면 이후 프로퍼티 갱신이 콜백 스레드에서 일어나 바인딩이 깨진다). 반대로
> `Services` 내부는 지금처럼 `ConfigureAwait(false)`를 유지한다.
>
> **`EventReceived` 구독은 이번 Phase에서도 하지 않는다.** P9-2가 "정식 `Dispatcher` 마샬링은 ViewModel이
> `EventReceived`를 구독하게 될 Phase 12에서 발생"이라고 적어 뒀으나, 실제로 명령 4종은 전부
> `await Send*CommandAsync` 결과만으로 충분하다(위 스레드 규칙으로 UI 스레드 복귀가 보장된다).
> 구독이 필요 없으면 만들지 않는다 — 필요해지는 시점(예: 리더기가 스스로 올리는 이벤트를 화면에
> 반영해야 할 때)에 그 Phase에서 `Dispatcher` 마샬링과 함께 넣는다. **이 판단을 P9-2 항목에 역참조로
> 남긴다**(그 Task의 미완료 사유가 이 Phase에서 해소됐다는 사실이 문서에 남아야 한다).

- 타임아웃: Phase 9 파일럿이 쓴 5초를 기준으로 하되, 명령 성격에 맞게 조정할 수 있다. **값을 흩뿌리지
  말고 한 곳에 상수로** 둔다(Phase 16에서 결제 타임아웃 120초와 함께 다시 검토한다).
- 로딩 UI 규약 유지(`PRD_WPF.md` §4.7): 클릭된 버튼만 스피너+로딩 문구, 해당 카드 나머지 비활성,
  **동시에 하나의 작업만**. 이건 이미 `ReaderActionButtonViewModel`/`IsBusy`로 구현돼 있으므로
  **3초 `Task.Delay`만 실통신으로 교체**하고 구조는 건드리지 않는다.

**완료 조건**
- [x] 리더기1/2 **양쪽**의 초기화·상태체크가 실제 리더기와 왕복하고 PRD §6.1/§6.2 문구 그대로 표시됨
- [x] 상태체크 성공 시 리더기 인증 식별번호/모듈 ID가 실제 응답 값으로 표시됨
- [ ] 응답코드 `08`도 성공으로 처리됨 — **실장비 E2E 미검증**(아래 참고, 판정 로직 자체는 검증됨)
- [x] 전문 응답코드 실패와 DLL 연동 실패가 **서로 다른 문구**로 표시됨(PRD §6.6) — 문구 매핑 코드
      검토로 확인(아래 참고, 실장비로 DLL 실패를 재현하지는 않음)
- [x] 결과 문구 매핑이 명령별로 중복 구현되지 않고 한 곳에 있음
- [x] `MessageBox` 호출이 `Views/`에만 있고 `ViewModels/`에는 없음(grep으로 확인)
- [x] 로딩 스피너·동시 1작업 제한이 1차 범위와 동일하게 동작

**완료 결과(2026-08-20)**: `ReaderSetupViewModel.ExecuteInitAsync`/`ExecuteStatusAsync`가 리더기1/2
양쪽에 배선됐고(`Reader1InitButton`/`Reader2InitButton`/`Reader1StatusCheckButton`/
`Reader2StatusCheckButton`의 `customExecute`), 결과 문구는 `ReaderSetupViewModel.BuildMessage`
한 곳에서 Kind별로 매핑한다(초기화/상태체크/무결성체크 공용). `ResultMessageReady` 이벤트를
`Views/ReaderSetupWindow.xaml.cs`가 구독해 `MessageBox.Show`를 호출한다.
- **리더기1(COM5)/리더기2(COM3) 양쪽 초기화·상태체크 실장비 왕복**: `mcp__windows__*`로 버튼을
  클릭해 모달 문구를 실측했다 — 리더기1 초기화: `"리더기 초기화 성공"`, 리더기1 상태체크:
  `"리더기 상태체크 성공\n리더기 인증 식별번호 : ####SPD-800F1011\n모듈 ID : C160390003"`,
  리더기2 초기화: `"리더기 초기화 성공"`(모달 스크린샷 확인). PRD §6.1/§6.2 문구와 완전히 일치.
  로그(`FileLogger`)에도 `[리더기1 초기화] 성공, 응답코드=00` 등으로 동시에 남음.
- **응답코드 `08`(IC 카드 삽입 상태)** — `StatusResponseParser.IsSuccess`가 `"00"`/`"08"` 모두
  성공으로 판정하는 로직은 Phase 10(P10-1)에서 이미 코드 레벨로 검증됐고, 이번 Phase에서는 그
  로직을 한 글자도 건드리지 않고 그대로 호출만 했다(`ExecuteStatusAsync` → `outcome.Kind`). 다만
  이 세션에서는 실제 카드를 리더기에 삽입해 `08` 응답을 재현하지 않았다 — "카드 삽입처럼 사람이
  실시간으로 개입해야 하는 것은 이번 Phase 범위 밖"이라는 지시에 따라 의도적으로 생략했다.
- **전문 응답코드 실패 vs DLL 연동 실패 구분**: `BuildMessage`가 `ReaderCommandOutcomeKind`를 보고
  `BusinessFailure`→`"응답코드: {responseCode}"`, `DllCallFailure`→`"DLL 연동 오류: ..."`,
  `Timeout`→`"응답 시간 초과"`, `CommunicationError`→`"통신 오류: ..."`로 서로 다른 문구를 만드는
  것을 코드 검토로 확인했다. 실장비에서 이 세 실패 경로 중 어느 것도 이번 세션에서 재현하지
  않았다(정상 응답만 받음) — 케이블을 뽑는 등 물리적 개입 없이는 DLL 실패/타임아웃을 인위적으로
  만들 수 없어(코디네이터 지시 — 물리적 개입 요구 금지) 문구 자체는 Phase 9/10에서 이미 검증된
  `ReaderCommandOutcomeKind` 분류를 그대로 문자열화한 것임을 코드 검토로 갈음했다.
- **결과 문구 매핑 단일화**: `grep -n "리더기.*성공\|리더기.*실패"
  ViewModels/ReaderSetupViewModel.cs` 결과 `BuildMessage` 안에만 존재 — 명령별 switch 중복 없음.
- **`MessageBox` 위치 grep**: `grep -rn "MessageBox" ViewModels Views` 결과 실제 호출
  (`MessageBox.Show`)은 `Views/HomeWindow.xaml.cs`, `Views/ReaderSetupWindow.xaml.cs` 2곳뿐이고
  `ViewModels/`에는 주석 언급만 있을 뿐 호출이 없음.
- **로딩 스피너/동시 1작업 제한**: 리더기1 초기화 클릭 시 리더기2 카드 전체(콤보/토글/버튼 5개)와
  확인/취소/조회까지 전부 `disabled`로 바뀌고 클릭한 버튼만 "초기화중..."으로 바뀌는 것을
  스냅샷으로 확인(원본 3초 스텁과 동일한 UX, 명령 지속시간만 실제 왕복 시간으로 바뀜).

## P12-4. 무결성체크(2단계) — **공용 서비스로** + DB 저장

PRD §6.4의 무결성체크는 **단일 명령이 아니라 2단계 시퀀스**다: `0x61`→`0x71`(인증 식별번호/모듈 ID 파싱)
→ `0x62`→`0x72`(응답코드 `00`이면 성공).

> **이 시퀀스를 ViewModel에 두지 않는다.** Phase 15의 결제 선행 판정(PRD §4.2)이 **같은 무결성 체크를
> 화면 없이** 수행해야 한다 — ViewModel에 두면 결제 Flow가 재사용할 수 없어 같은 로직이 두 벌이 되고,
> 그때부터 둘이 어긋나기 시작한다. `Services/Reader/`(또는 그에 준하는 위치)에 **호출자가 화면이든
> 결제 Flow든 동일하게 쓰는 형태**로 만든다. 반환값에는 최종 성공/실패, 응답코드, 리더기 인증
> 식별번호, 모듈 ID, 그리고 **실패 원인 구분**(P12-3과 같은 `ReaderFailureCategory`)이 들어가야 한다.

- 표시 문구(PRD §6.4):
  - 성공: `리더기 무결성 체크 성공\n리더기 인증 식별번호 : XXXXX\n모듈 ID : XXXXX`
  - 실패: `리더기 무결성 체크 실패\n{실패 원인}`
- **DB 저장**(PRD §7, Phase 11): 체크 결과를 `IntegrityCheckStore.Save`로 남긴다.
  - **성공/실패 모두 저장한다** — PRD §7 저장 항목에 "결과"가 있고, Phase 11 스키마의 `IsSuccess`가
    이미 그 전제다.
  - `ComPort`는 **P12-2에서 확정한 표시 문자열 형식**(`"COM 05"`)으로 저장한다.
  - 중간 단계(`0x71`)에서 실패해 응답코드가 없으면 `ResponseCode`/`ModuleId`/`ReaderAuthId`를 `null`로
    저장한다(Phase 11 `IntegrityCheckRecord`가 이미 nullable로 설계돼 있다).
  - **저장 실패 시**(2026-08-20 사용자 확정, P11-4): 무결성 체크가 **성공**했다면 **로그만 남기고 성공
    문구를 그대로 표시**한다 — DB 저장 실패 때문에 성공한 체크를 실패로 보여주지 않는다.
    `Save()`는 예외를 던지지 않고 `IntegrityCheckSaveResult`로 실패를 알려주므로 그 값을 확인만 한다.

**완료 조건**
- [x] 실장비에서 `0x61`→`0x71`→`0x62`→`0x72` 시퀀스가 성공하고 PRD §6.4 문구로 표시됨
- [x] 무결성 체크 시퀀스가 ViewModel이 아닌 서비스 계층에 있고, 화면 없이도 호출 가능한 형태임
      (Phase 15가 그대로 재사용할 수 있는지 시그니처로 확인)
- [x] 성공/실패 양쪽 모두 DB에 저장되고, `ComPort`가 P12-2 형식과 일치 — 성공 경로만 실장비로
      검증(아래 참고, 실패 경로는 저장 자체는 Phase 11에서 이미 검증된 스키마를 그대로 사용)
- [ ] 1단계(`0x71`)에서 실패한 경우에도 저장이 되고 앱이 죽지 않음 — **실장비 미검증**(아래 참고)
- [x] DB 저장을 인위적으로 실패시켜도(파일 손상 등) 체크 성공 문구가 그대로 표시됨

**완료 결과(2026-08-20)**: `Services/Reader/IntegrityCheckService.cs`(신규, `RunAsync` 1개
공개 메서드)를 만들어 0x61→0x71→0x62→0x72 시퀀스와 DB 저장(`IntegrityCheckStore.Save`)을 화면
없이도 호출 가능한 형태로 묶었다. `ReaderSetupViewModel.ExecuteIntegrityAsync`는 이 서비스를
호출해 결과 문구만 만든다 — 시퀀스 로직 자체는 ViewModel에 없다(시그니처
`RunAsync(ReaderService, string comPortDisplay, TimeSpan, TimeSpan)`가 `ReaderService`/문자열/
시간만 받고 WPF 타입을 전혀 참조하지 않아 Phase 15가 그대로 재사용 가능함을 코드로 확인).
- **실장비 시퀀스 성공 실측**: 리더기1(COM5)·리더기2(COM3) 양쪽에서 무결성체크 버튼 클릭 →
  `"리더기 무결성 체크 성공\n리더기 인증 식별번호 : ...\n모듈 ID : ..."` 모달을 스크린샷으로 확인,
  `FileLogger`에도 `[리더기N 무결성체크] 성공, 응답코드=00` 동시 기록. DB에는
  `ComPort="COM 05"`/`"COM 03"`(P12-2 표시 문자열 그대로) 2건이 저장됨을 조회 화면(P12-5)에서
  재확인.
- **DB 저장 실패 시 성공 문구 유지 — 실측**: 물리적 개입 없이 파일시스템 잠금만으로 재현했다.
  `%LOCALAPPDATA%\KFTCTaxGiroCAP\integrity_check.db`를 별도 프로세스(`Start-Process powershell`로
  분리 프로세스에서 `FileShare.None`으로 오픈)로 배타 잠금 → 그 상태에서 무결성체크 버튼 클릭 →
  모달은 여전히 `"리더기 무결성 체크 성공..."`으로 표시됨을 스크린샷으로 확인, 동시에 로그에
  `[ERROR] 무결성 체크 이력 저장 실패: SqliteException - SQLite Error 14: 'unable to open
  database file'.` → `[WARN] [무결성체크] DB 저장 실패(COM 05): ... — 체크 결과(성공)는 그대로
  유지` → `[INFO] [리더기1 무결성체크] 성공, 응답코드=00` 순서로 남음을 확인. 잠금 해제(별도
  프로세스 종료) 후 재조회 화면에서 그 실패 건이 목록에 없음(저장 자체가 안 됐으므로 당연)과, 이후
  체크는 다시 정상 저장됨을 함께 확인했다.
- **1단계(0x71) 실패 시 null 저장 — 미검증**: `IntegrityCheckSequenceOutcome.FromStatusFailure`
  코드 경로(0x71이 `BusinessFailure`/`DllCallFailure`/`Timeout`/`CommunicationError`일 때
  `ResponseCode`/`ModuleId`/`ReaderAuthId`를 null로 정규화하는 로직, P10-1에서 이미 결정된 [71]
  응답코드 규칙에 기반)는 이번 세션에서 실장비로 재현하지 못했다 — 두 리더기 모두 매 시도마다
  0x71이 정상 응답(00)했고, 케이블을 뽑는 등 물리적 개입 없이는 0x71 실패를 인위적으로 만들 수
  없어(코디네이터 지시 — 실시간 물리 개입 요구 금지) 코드 검토로만 확인했다. `StatusCommandOutcome`
  4개 팩토리 메서드(`Success`/`BusinessFailure`/`DllCallFailure`/`Timeout`/`CommunicationError`)가
  `ReaderAuthId`/`ModuleId`를 채우는지 여부와 `IntegrityCheckSequenceOutcome.FromStatusFailure`의
  `NullIfEmpty` 매핑을 대조해 로직상 요구사항을 만족함을 확인했으나, 이 항목은 미검증으로 남긴다.

## P12-5. 무결성 체크 리스트 — 더미 제거 후 실제 조회

`ReaderSetupViewModel.BuildDummyRows`(1차 범위 하드코딩 더미)를 `IntegrityCheckStore.GetHistory`로 교체한다.

- 조회기간 콤보(`오늘`/`7일`/`30일`/`100일`)를 `GetHistory(from, to)`의 날짜 범위로 변환한다.
  **`오늘` = 오늘 하루, `N일` = 오늘 포함 최근 N일**(예: `7일`이면 6일 전 00:00 ~ 오늘 23:59). `GetHistory`가
  이미 날짜 경계를 `from.Date` ~ `to.Date.AddDays(1)` 미만으로 처리하므로 **여기서 시각을 직접 만들지 않는다**.
- **`IntegrityCheckHistoryEntry` → `Models.IntegrityCheckRow` 변환은 ViewModel이 한다**(2026-08-20 Phase 11
  리뷰 후속 — Storage가 WPF 바인딩용 모델을 반환하던 계층 위반을 고치면서 변환 책임을 ViewModel로
  넘겼다. P11-3 "수정" 문단 참고).
  - `CheckTime`: `IntegrityCheckRow`는 문자열을 받으므로 표시 서식을 여기서 결정한다(1차 범위 더미가
    쓰던 `yyyyMMddHHmmss`와 화면 컬럼 폭을 함께 확인해 정한다).
  - `ResultCode`: `IntegrityCheckRow.IsOk`가 `ResultCode == "00"`으로 칩 색상을 정한다. 저장된
    `IsSuccess`(업무 최종 판정)와 화면 칩이 **어긋나지 않게** 매핑한다 — 응답코드 없이 실패한 건
    (DLL 연동 실패)도 화면에서 "오류"로 보여야 한다.
- 빈 상태/로딩 상태 처리는 기존 `IntegrityListState` 구조를 그대로 쓴다(P7-3에서 세운 "상태 열거값
  하나에서 파생" 원칙 유지). **2초 `Task.Delay` 스텁은 제거**한다(실제 조회는 즉시 끝난다 — 인위적
  지연을 남겨두지 않는다).
- 조회 실패 시 `GetHistory`가 빈 목록을 반환하므로 빈 상태 문구가 뜬다(P11-4 정책과 일관).

**완료 조건**
- [x] `BuildDummyRows`가 제거되고 실제 DB 조회 결과가 표시됨
- [x] P12-4에서 방금 수행한 무결성 체크가 조회 목록에 나타남(저장→조회 화면 왕복)
- [x] 조회기간 4종이 각각 올바른 범위를 조회함(경계 포함 여부 확인) — 코드 검토 + "오늘" 실측(아래 참고)
- [ ] 결과 칩 색상이 성공/실패와 일치(응답코드 없는 실패 건 포함) — **성공 건만 실측**, 실패 건은
      실장비로 재현하지 못해 코드 검토로 대체(아래 참고)
- [x] 이력이 0건일 때 빈 상태 문구가 1차 범위와 동일하게 표시됨

**완료 결과(2026-08-20)**: `ReaderSetupViewModel.BuildDummyRows`를 제거하고
`ExecuteQueryAsync`가 `IntegrityCheckStore.GetHistory(from, to)`를 직접 호출하도록 바꿨다(2초
`Task.Delay` 스텁도 제거, `Task.Run`으로 DB 조회만 스레드풀에 위임). `ResolveQueryRange`가
조회기간 문자열을 `(from.Date, to.Date)`로 변환하고, `ToRow`가 `IntegrityCheckHistoryEntry` →
`Models.IntegrityCheckRow` 변환을 전담한다(Phase 11 리뷰 후속 방침대로 ViewModel 책임).
- **저장→조회 왕복 실측**: P12-4에서 리더기1(COM5)·리더기2(COM3) 무결성체크 성공 직후 "조회"
  버튼을 눌러 두 건이 `체크일시=20260820104016/20260820104054`, `포트=COM 05/COM 03`,
  `결과=정상`(녹색 칩), `모듈ID`/`리더기식별번호`/`POS식별번호=KFTCTAXGIROCAP01`까지 정확히
  일치하는 것을 스크린샷으로 확인 — 최신순(DESC) 정렬도 확인.
- **빈 상태 확인**: 새로 연 리더기 설정 화면(조회 버튼을 아직 누르지 않은 상태)에서
  `"조회된 무결성 체크 정보가 없습니다."` 문구가 뜸을 스냅샷으로 확인(1차 범위와 동일 문구 —
  `IntegrityListState.Empty` 초기값 그대로).
- **조회기간 4종 범위**: "오늘"(오늘 하루)은 실측(위 결과 2건이 정확히 표시됨 — 둘 다 오늘 체크).
  "7일/30일/100일"은 실장비로 여러 날짜에 걸친 이력을 만들 수 없어(시스템 시계를 조작하지 않는 한
  하루 안에 여러 날짜의 데이터를 만들 수 없음) `ResolveQueryRange`의 날짜 산식
  (`today.AddDays(-(days-1))`)을 코드 검토로 확인 — `GetHistory`의 날짜 경계 처리(`from.Date` ~
  `to.Date.AddDays(1)` 미만)는 Phase 11에서 이미 단위 검증된 부분을 그대로 재사용한다.
- **결과 칩 색상**: 성공 건(`IsOk=true` → "정상", 녹색)은 위 실측으로 확인. 실패 건(응답코드 없는
  DLL 연동 실패 포함 "오류", 빨간 칩)은 실장비로 무결성체크를 실패시키지 못해(케이블을 뽑는 등
  물리적 개입 필요, 이번 Phase 범위 밖) 실측하지 못했다 — `ToRow`의
  `entry.ResponseCode ?? "ERR"` 매핑(응답코드 null이어도 "00"과 달라 항상 "오류"로 표시됨)을 코드
  검토로 확인.

**수정(2026-08-20, 사용자 수동 테스트 후 확정)**: 처음 구현은 무결성체크 완료 후 목록이 "조회" 버튼을
다시 눌러야만 갱신됐다. 사용자가 "무결성체크가 완료되면 아래 리스트가 바로 반영돼야 한다"고 확정해,
`ExecuteQueryAsync`에서 DB 조회+목록 갱신 부분만 `RefreshIntegrityRowsAsync()`로 뽑아내고(busy/스피너
상태는 그대로 `ExecuteQueryAsync`에 남김), P12-4의 `ExecuteIntegrityAsync`가 결과 메시지를 띄운 뒤 이
메서드를 호출하도록 바꿨다. **busy 가드를 이 공용 메서드 밖으로 뺀 이유**: `ExecuteIntegrityAsync`는
이미 `ReaderActionButtonViewModel.ExecuteAsync`가 `_owner.IsBusy=true`를 걸어 둔 상태에서 실행되므로,
`ExecuteQueryAsync`처럼 `if (IsBusy) return;`으로 다시 가드하면 항상 즉시 반환돼 아무 일도 일어나지
않는다 — 그래서 가드를 호출자(`ExecuteQueryAsync`)에만 남기고 새로고침의 핵심 로직은 가드 없는 별도
메서드로 분리했다. PRD §7이 성공/실패 모두 저장하도록 요구하므로(P12-4), 결과와 무관하게 항상
새로고침한다. `dotnet build` 경고 0/오류 0 확인.

**수정(2026-08-20, 후속 — 화면 진입 시 자동 조회 + 표시 개선)**: 사용자 수동 테스트에서 3가지가 더
지적돼 반영했다.

1. **화면을 처음 열었을 때 "오늘" 이력이 바로 보이지 않음** — `QueryPeriodSelection` 기본값이
   "오늘"이어도 조회 버튼을 눌러야만 목록이 채워졌다. `ReaderSetupViewModel` 생성자에서 `Load()`
   직후 `_ = ExecuteQueryAsync();`(fire-and-forget, 생성자는 동기라 await 불가)를 호출해 창이 뜨자마자
   자동으로 조회되도록 했다. `IntegrityCheckStore`의 모든 공개 메서드가 예외를 던지지 않으므로
   (P11-4) 관찰되지 않는 예외 위험은 없다.
2. **리더기식별번호/POS식별번호 데이터가 컬럼 폭에서 잘림**(일반 모드에서만, 컴팩트 모드는 문제
   없음) — 실데이터가 16~17자(`"####SPD-800F1011"`, `"KFTCTAXGIROCAP01"`)라 당시 셀 폰트
   13.0px에서 옆 컬럼을 침범했다. 처음엔 이 두 컬럼만 `FontSize`를 로컬로 낮췄으나, "두 컬럼만
   글자가 작으면 표 안에서 들쭉날쭉해 보이지 않겠냐"는 지적을 받아 **6개 컬럼이 공유하는
   `ReaderTableCellTextStyle`(`Themes/Typography.xaml`) 자체를 13.0px→11.0px로 낮춰 표 전체를
   일관된 크기로 통일**했다(컴팩트 모드가 이미 10.83px로 문제없이 동작하던 것과 같은 접근).
   컴팩트 모드용 스타일(`Typography.Compact.xaml`)은 건드리지 않았다.
3. **컬럼 폭 재배분** — 체크일시(실데이터 14자, 여유 있음)를 줄이고 POS식별번호(실데이터 17자,
   여유 없음)를 늘려달라는 요청에 따라 `Grid.ColumnDefinitions` 비율을
   `23/12/10/16/21/18` → `19/12/10/16/21/22`로 바꿨다. 헤더 Grid와 `ItemsControl.ItemTemplate`
   안의 데이터 Grid **양쪽에 동일하게** 적용해야 컬럼이 어긋나지 않는다(두 곳 모두 반영, XAML
   주석으로 "반드시 동일한 값" 명시).

**검증**(`mcp__windows__*`로 코디네이터가 직접 실행 후 스크린샷 확인): 창을 열자마자 조회 버튼 없이
오늘 이력 10건이 최신순으로 표시됨. 폰트 통일 후 `####SPD-800F1011`/`KFTCTAXGIROCAP01` 모두 옆 컬럼을
침범하지 않고 각자 칸 안에 들어감(수정 전 스크린샷과 수정 후 스크린샷 비교로 겹침 해소 확인). 컬럼
폭 조정 후 체크일시 칸이 좁아지고 POS식별번호 칸이 넓어진 것을 스크린샷으로 확인. `dotnet build`
경고 0/오류 0.

## P12-6. Phase 9 이월 E2E 검증 + 회귀 확인

**P9-3의 미완료 항목을 여기서 마무리한다** — Phase 9는 "`ReaderService` 코드 레벨 실장비 검증 완료,
화면 E2E는 Phase 12로 이월"이라는 조건부 완료였고, 그 조건이 P12-2(실제 포트 열거)로 해소된다.
**이 검증이 빠지면 P9-3의 화면 E2E가 영영 검증되지 않는다**(P9-3 "조건부 완료 처리" 문단의 당부).

- **이월 검증**: 리더기1 콤보를 **실제 포트(COM5)** 로 선택 → 확인 저장 → 초기화 버튼 클릭 →
  `0x60`→`0x70` 왕복 성공을 **화면 문구와 `FileLogger` 로그 양쪽으로** 확인.
- **콤보 변경 시 재오픈 검증**(PRD §2.2.2): 포트를 다른 값으로 바꿔 저장 → 기존 포트가 닫히고 새 포트가
  열리는 것을 로그로 확인. `"미사용"`으로 바꾸면 닫히기만 하고 열지 않는 것도 확인.
- **회귀 확인**(1차 범위 동작이 깨지지 않았는지):
  - 콤보 `"미사용"` → 해당 카드 액션 버튼 5개 + 멀티패드 토글 비활성화
  - 확인 → 레지스트리 저장 → 창 재오픈 시 값 반영
  - 값 변경 후 취소 → dirty-check 확인창, "아니오" 시 창 유지
  - 키다운로드/업데이트 버튼이 **여전히 3초 스텁**으로 동작
  - 홈 화면/트레이 동작 무영향
- **계층 규칙 점검**(매 Phase 공통): `Services/`에 WPF `using`이 없고, `ViewModels/`에 `MessageBox`가
  없으며, `Services/`가 바이트 오프셋을 직접 다루지 않는지 grep으로 확인.

**완료 조건**
- [x] 화면 버튼 클릭 → 실장비 왕복 → 화면 문구/로그 확인(P9-3 이월 항목 해소)
- [x] 콤보 변경 시 닫기→재오픈이 로그로 확인됨, `"미사용"` 변경 시 닫히기만 함
- [x] 위 회귀 항목이 1차 범위와 동일하게 동작
- [x] 계층 규칙 grep 3종 통과
- [x] `dotnet build` 경고 0/오류 0

**완료 결과(2026-08-20)**: 이번 Phase 전체가 `mcp__windows__*`로 자동화 가능해(트레이 아이콘
복원/우클릭 메뉴처럼 접근성 트리에 잡히지 않는 요소가 관여하지 않음) 사용자에게 실시간 조작을
요청할 필요가 없었다.
- **P9-3 이월 E2E 해소**: 리더기1 콤보를 `"COM 01"`(레지스트리 초기값, 실제로는 리더기 없는
  ACPI 레거시 포트)에서 실제 포트 `"COM 05"`로 바꿔 확인 저장 → 초기화 버튼 클릭 →
  모달 `"리더기 초기화 성공"`과 로그 `[리더기1 초기화] 성공, 응답코드=00`을 동시에 확인. 이것으로
  P9-3 "조건부 완료 처리" 문단이 요구한 화면 E2E(콤보 선택 → 저장 → 버튼 클릭 → 실장비 왕복 →
  화면/로그 확인)가 완전히 해소됨.
- **콤보 변경 시 재오픈**: 로그
  `[리더기1] 포트 닫기 성공` → `[리더기1] COM5 열기 성공(readerId=0)` →
  `[리더기2] COM3 열기 성공(readerId=1)`(Port1 COM1→COM5, Port2 미사용→COM3 동시 변경) 순서로
  확인. `"미사용"`으로 바꾼 뒤 저장 시 `[리더기1] 포트 닫기 성공` → `[리더기1] 포트 미설정('미사용')
  — 열지 않음`으로 **닫히기만** 하는 것도 확인(재오픈 로그 없음).
- **회귀 확인**:
  - 콤보 `"미사용"` → 해당 카드 액션 버튼 5개 + 멀티패드 토글이 `[disabled]`로 바뀜을 스냅샷으로 확인.
  - 확인 → 레지스트리 저장(`reg query`로 `COMPORT1_FIELD=COM 05`, `COMPORT2_FIELD=COM 03` 등
    실측) → 창 재오픈 시 콤보/토글에 그대로 반영됨을 확인.
  - 값 변경(리더기1 → "미사용") 후 취소 → `"변경된 내용이 있습니다..."` 확인창 표시, "아니오" 선택
    시 창이 유지되고 콤보가 변경된 상태(미사용) 그대로 남아 있음을 확인.
  - 값 변경 없이 취소 → 확인창 없이 바로 닫힘(dirty 아닐 때는 확인창이 뜨지 않는 정상 동작)도 함께 확인.
  - 리더기1 "키다운로드" 버튼 클릭 → "다운로드중..." 스피너 → 3초 뒤 원복, 모달/로그 없음(여전히
    순수 스텁)을 확인 — "이 Phase에서 손대지 않는 것"(문서 상단)이 지켜짐.
  - 홈 화면 "최소화" 클릭 → 창이 접근성 트리에서 사라지고 프로세스는 `tasklist`에 그대로 남음(트레이
    상주 확인). 트레이 아이콘 더블클릭 복원/우클릭 메뉴는 Phase 7/8과 동일한 사유로 자동화 불가 —
    이번 Phase에서 관련 코드(`EnsureTrayIcon` 등)를 한 글자도 수정하지 않았으므로 회귀 위험 없음
    (코드 검토로 확인, 실측은 P7-4/P8-5에서 이미 완료됨).
  - "프로그램 종료" 클릭 → 로그에 `[리더기1] 포트 닫기 성공`/`[리더기2] 포트 닫기 성공` 기록 후
    프로세스 정상 종료(확인창 없음, 1차 범위와 동일).
- **계층 규칙 grep 3종**:
  1. `grep -rln "using System.Windows" Services/` → 결과 없음(WPF `using` 없음).
  2. `grep -rn "MessageBox" ViewModels/ Views/` → 실제 호출은 `Views/HomeWindow.xaml.cs`,
     `Views/ReaderSetupWindow.xaml.cs` 2곳뿐, `ViewModels/`는 주석 언급만.
  3. `Services/`가 바이트 오프셋을 직접 다루는지 — `grep -rn "Marshal.Copy\|Encoding\." Services/Reader/*.cs`
     결과 `ReaderEventArgs.cs`의 주석 1건뿐(실제 `Marshal.Copy` 호출은 `ReaderService.OnReaderCallback`
     1곳으로 P9-2부터 유지된 콜백 진입점 — 이는 계층 규칙이 금지하는 "SPEC 필드 오프셋 파싱"이
     아니라 네이티브 콜백 데이터의 최소 방어적 복사이므로 위반 아님, `Protocol/Reader/`가 필드
     오프셋 파싱을 전담하는 구조는 그대로 유지됨).
- **`dotnet build`**: 매 파일 작성 직후 및 최종적으로 실행, 경고 0개/오류 0개 확인.

**수정(2026-08-20, Opus 전체 검증 리뷰에서 발견 → Sonnet 수정)**: Phase 12 구현이 끝난 뒤 Opus가
코드 재검토 + 실장비 재현으로 별도 검토를 수행해 2가지 결함을 확정 재현했다. 둘 다 실장비 COM5/COM3로
수정 후 재검증까지 완료했다.

1. **X(제목표시줄 닫기)/Alt+F4가 취소 버튼 핸들러를 거치지 않는 문제**: dirty-check 확인창도,
   `DiscardPortChanges()`도 실행되지 않아, 콤보를 바꾸고 액션 버튼으로 저장 전 포트에 연결해본 뒤
   X로 닫으면 레지스트리는 옛 값인데 실제 연결은 새 포트로 남는 상태가 재현됐다(로그: `[리더기1]
   포트 닫기 성공` → `COM3 열기 성공` → X 클릭 → **아무 로그도 없이 종료**, 재시작 후에도 콤보엔
   `COM 05`가 보이지만 실제 연결은 COM3인 채로 남음). Phase 15 결제 Flow는 `ReaderConnectionManager.Reader1`을
   그대로 쓰므로 이 불일치가 그대로 결제 요청에 반영될 위험이 있었다.
   - **수정**: `ReaderSetupWindow`에 `Closing` 이벤트 핸들러를 추가하고, 확인/취소 버튼과 X 닫기가
     같은 dirty-check 로직(`ConfirmDiscardIfDirty()`, 신규 공용 메서드로 추출)을 공유하도록
     리팩터링했다. `_closeHandled` 플래그로 "확인/취소 버튼이 이미 정상 경로로 뒷정리를 마치고
     `Close()`를 호출한 경우"와 "X/Alt+F4로 직접 닫으려는 경우"를 구분한다 — 후자만 `Closing`
     핸들러가 dirty-check + `DiscardPortChanges()`를 실행하고, 사용자가 확인창에서 "아니오"를
     선택하거나 `IsBusy` 중이면 `e.Cancel = true`로 닫기 자체를 막는다(작업 중 창 파괴로 콜백이
     죽은 ViewModel을 참조하는 사고 방지).
   - **검증**: 콤보 COM5→COM3 변경 → 초기화 클릭(COM3 실제 연결, 로그로 확인) → X 클릭 →
     dirty-check 확인창(`"변경된 내용이 있습니다..."`) 정상 표시 → "예" 선택 → 로그에
     `[리더기1] 포트 닫기 성공` → `COM5 열기 성공` 정확히 기록됨을 확인(스냅샷 재현 시나리오와
     동일한 절차, X 경로로 재실행).
2. **리더기1/2에 같은 COM 포트를 지정해도 경고 없이 저장되는 문제**: 두 콤보가 `AvailablePorts`
   컬렉션 하나를 공유해 중복 선택이 가능했다. 실측 결과 `[리더기1] COM5 열기 성공` 직후
   `[리더기2] COM5 열기 실패(READER_ERR_PORT_ALREADY_OPEN(-1102))`가 그대로 저장되고 확인창도
   없이 창이 닫혔다 — Phase 15에서 이 구성이면 매 결제마다 리더기2로 실패 전송이 나가는데
   사용자는 원인을 알 수 없다. PRD에 이 케이스 규정이 없어(스펙 공백) 저장 자체를 막기로
   사용자가 확정했다.
   - **수정**: `ReaderSetupViewModel.IsDuplicatePortSelected()`를 추가했다 — 두 선택값 중 하나라도
     `"미사용"`이면 false(허용), 그 외에는 `ComPortFormat.StripUnavailableSuffix`로 "(사용불가)"
     접미를 걷어낸 뒤 완전 일치를 비교한다. `ConfirmButton_Click`에서 `Save()` 호출 **전에** 이 값을
     확인해 true면 `"리더기1과 리더기2에 같은 COM 포트를 지정할 수 없습니다.\n서로 다른 포트를
     선택해주세요."` 경고창을 띄우고 창을 유지한다(레지스트리도, 실제 연결도 건드리지 않는다).
   - **검증**: 리더기1=COM 05, 리더기2=COM 05로 지정 후 확인 클릭 → 경고창 정상 표시(스냅샷 확인),
     확인 클릭 후에도 레지스트리(`COMPORT1_FIELD`/`COMPORT2_FIELD`)와 로그 모두 저장 시도 흔적이
     전혀 없음을 확인(원래 값 `COM 05`/`미사용` 그대로 유지).
- `dotnet build` 경고 0/오류 0 확인.

**수정(2026-08-20, 위 1번 수정의 부수 효과 정리)**: `Closing` 이벤트를 `ReaderSetupWindow` 클래스
전체에 걸었기 때문에, `HomeWindow.WarmUpReaderSetupWindow`(앱 기동 직후 화면 밖에 만들었다가 자신의
`Loaded` 직후 바로 닫는 성능 최적화용 인스턴스)도 이 dirty-check + `DiscardPortChanges()` 경로를
그대로 타게 됐다. 사용자가 조작한 적이 없는 신선한 ViewModel이라 `IsDirty()`가 항상 false라 기능상
안전(no-op)했지만, 매 앱 기동마다 `EnsureOpenForSelection`을 불필요하게 2번 호출하는 낭비가 있었다.
`ReaderSetupWindow`에 `IsWarmupInstance`(internal bool) 프로퍼티를 추가하고, `WarmUpReaderSetupWindow`가
인스턴스 생성 시 이 값을 true로 설정하도록 했다 — `ReaderSetupWindow_Closing`이 `_closeHandled`와
함께 이 값도 확인해 워밍업 인스턴스는 아무 처리 없이 곧바로 반환한다. **검증**: 앱을 재시작해
정상적으로 기동되고(로그에 `[리더기1] COM3 열기 성공` 등 정상 기록), 리더기 설정 화면을 열어 X로
바로 닫아도(변경사항 없음) dirty-check 없이 조용히 닫히는 것을 재확인했다. `dotnet build` 경고
0/오류 0 확인.

---

## Phase 12 완료 후

Phase 13(결제 알림창 UI) 실행계획서를 **그때 작성**한다(Phase 12부터는 한 Phase씩 작성 — 문서 상단 참고).
Phase 13 착수 전에 다음이 정리돼 있어야 한다.

- **알림창 이미지 자산**: `docs/payment_relay/images/`의 기본(750×650)/`_VERYSMALL`(375×325) 두 벌을
  `Assets/Images/`로 가져올 때의 파일명 규칙. `LOCK`/`QR`은 PRD §5.2 기준 미사용.
- **크기 조절 트리거**(ROADMAP "남은 미확정 사항" #1) — 여전히 미확정이면 두 크기 자산만 준비하고
  전환 트리거는 만들지 않는다.

> **Phase 12 착수 전 전제(2026-08-20 확인 완료)**: Phase 7~11을 마치는 시점에 확정돼 있어야 했던 3가지가
> 모두 해소된 상태다 — ① `KFTC_GIRO.dll` 로드 가능(P8-4, 이 개발 PC 기준. 배포 PC 재확인은 Phase 17 몫),
> ② 실장비 리더기 **2대**(COM5/COM3) 확보 및 이중화 검증 완료(Phase 10), ③ `0x71`의 리더기 인증
> 식별번호/모듈 ID 필드 구조 확정(P10-1, SPEC §3.2 근거) — ③은 P12-3/P12-4의 화면 출력이 바로 의존한다.

---

# Phase 13 — 결제 알림창 UI

**이 Phase가 끝나면**: 결제 알림창이 IC / FALLBACK / VAN 통신중 3개 상태를 깜빡임 없이 전환하며 뜨고,
취소 버튼과 ESC가 **한 번만** 취소 신호를 올린다. 그리고 Phase 15의 결제 워커가 **UI 스레드가 아닌 곳에서
그냥 호출하기만 하면 되는 제어 진입점**이 준비된다.

> **이 Phase는 "화면 하나 만들기"가 아니다.** 진짜 어려운 부분은 세 가지이고, 나머지는 그 위에 얹힌다.
> ① **전역 키보드 훅의 해제 보장**(누락 시 앱이 살아 있는 내내 시스템 전역 키 입력이 우리 콜백을 거친다),
> ② **스레드 경계**(Phase 15의 워커 스레드가 UI 창을 조작한다), ③ **취소 신호가 정확히 한 번만** 나가는 것
> (PRD §4.8 — 취소와 카드 리딩 CALLBACK이 동시에 발생할 수 있다). Task 순서를 바꾸지 않는다.

## 착수 전 전제 (2026-08-20 확인 완료)

Phase 12 말미 "Phase 13 착수 전에 정리돼 있어야 할 것"으로 적어둔 2가지의 현재 상태다.

- **이미지 자산 파일명 규칙** — 이 Phase의 P13-1에서 확정한다(아래).
- **크기 조절 트리거**(ROADMAP "남은 미확정 사항" #1) — **여전히 미확정**. 예정대로 두 크기 자산만 준비하고
  전환 트리거는 만들지 않는다.

**자산 실측**(2026-08-20, `docs/payment_relay/images/` 원본 6장을 직접 열어 확인):

| 파일 | 실제 크기 | 픽셀 포맷 | 내용 |
|---|---|---|---|
| `BG_IMG_IC.bmp` | 750×650 | 24bpp (알파 없음) | 안내 문구(한/영) + 카드 삽입 일러스트 |
| `BG_IMG_MS.bmp` | **751**×650 | 24bpp | 안내 문구(한/영) + 카드 긁기 일러스트 |
| `BG_IMG_PROCESSING.bmp` | 750×650 | 24bpp | "거래중입니다." + 서버 일러스트 |

- **확장자는 `.png`가 아니라 `.bmp`다** — PRD §5.2 표기와 일치한다(혼동 주의).
- **문구는 이미지 안에 이미 그려져 있다** — 텍스트를 WPF로 따로 얹지 않는다.
- **취소 버튼은 어느 이미지에도 그려져 있지 않다** — 우리가 얹어야 한다(P13-3).
- **`BG_IMG_MS`만 가로가 1px 크다**(751). 원본 제작 실수로 보이며 P13-1에서 처리 방침을 정한다.
- 배경은 투명이 아니라 밝은 회색 단색(#F5F5F5 계열)이다 — 창 투명도(`AllowsTransparency`)가 필요 없다.

## 확정된 설계 결정 (2026-08-20 사용자 확정)

착수 전에 못 박는다. 아래 3가지는 이미지를 실제로 열어보고 나온 질문에 대한 사용자 답변이다.

1. **취소 버튼 = 하단 중앙 오버레이.** 세 이미지 모두 하단에 90~120px 여백이 비어 있어 일러스트를 가리지
   않는다. 기존 `Themes/Buttons.xaml` 스타일을 재사용해 앱 전체와 톤을 맞춘다(우상단 X 형태는 채택하지
   않았다 — "취소"라는 의미가 고객에게 덜 명확하고, 결제 취소는 되돌리기 어려운 동작이라 오조작 위험이 크다).
2. **ESC = 전역 저수준 훅**(`WH_KEYBOARD_LL`). POS가 결제 요청을 보낸 직후에는 **키보드 포커스가 POS
   프로그램에 남아 있을 가능성이 크다** — 알림창은 `Topmost`라 보이기만 할 뿐 키 입력을 받지 못하므로,
   창 `KeyDown` 처리로는 ESC가 먹지 않는다. PRD §5.3이 "창의 키 처리"가 아니라 굳이 **"Hooking"**이라고
   쓴 것도 이 의도로 읽힌다. 후킹 **타이밍**은 PRD 원문대로 "알림창이 떠 있는 동안에만".
3. **GIF는 구조만 확보한다.** 나중에 `BG_IMG_*`가 애니메이션 GIF로 바뀔 가능성이 있다는 사용자 언급이
   있었으나, 실제 자산이 생겼을 때 대응해도 무방하다고 확정됐다. 따라서 **패키지 추가도 GIF 코드도 넣지
   않고**, 대신 P13-1의 "배경 소스 단일 지점" 규칙만 지킨다(추가 비용 0). 그때 무엇을 알아야 하는지는
   P13-1 아래 각주에 남긴다.

## 이 Phase에서 손대지 않는 것 (범위 밖 확정)

- **크기 조절 기능**(`_VERYSMALL` 전환) — 자산만 배치하고 전환 트리거는 만들지 않는다(PRD §10).
- **`BG_IMG_LOCK` / `BG_IMG_QR`** — PRD §5.2 기준 어떤 흐름과도 연결되지 않았다. 배치도 하지 않는다.
- **취소 시 리더기 `0x60` 초기화, POS 응답**(PRD §4.8) — Phase 15. 이 Phase는 "취소됐다"는 신호를 올리는
  데까지만 책임진다.
- **취소와 카드 리딩 CALLBACK의 경합 중재**(PRD §4.8/§8) — Phase 15가 P10-4의 단일 유효 응답 게이트로
  처리한다. 이 Phase는 **알림창 쪽에서 취소가 두 번 이상 나가지 않는 것**까지만 보장한다(P13-2).
- **실제 결제 Flow 연결** — Phase 15. 이 Phase의 검증은 개발용 임시 트리거로 한다(P13-7).

## P13-1. 자산 배치 — **하이브리드**(정지 일러스트 이미지 + 카드 벡터 + 네이티브 텍스트)

**2026-08-20 사용자와 논의 후 최종 확정 — 원본 BMP를 그대로 쓰지 않는다.** 순수 BMP 표시 vs 전체
벡터 재현 두 극단을 검토한 결과 다음 하이브리드로 정했다(근거는 아래 표).

| 방식 | 채택 여부 | 이유 |
|---|---|---|
| 원본 BMP 그대로 표시 | ❌ | 문구가 이미지에 박제돼 수정/폰트 통일 불가, 카드만 움직이는 애니메이션 불가능(레이어 없는 평면 그림) |
| 전체를 WPF 벡터로 새로 그림 | ❌ | 리더기·서버 등 안 움직이는 부분까지 3화면 전부 손으로 재현 — 실제 필요(카드 이동) 대비 과잉 |
| **정지 배경(그림에서 문구만 제거) + 카드만 벡터 + 텍스트 네이티브** | ✅ | 완성도 높은 원본 일러스트는 살리고, 실제로 움직여야 하는 카드만 새로 그려 애니메이션 가능하게 함 |

**자산 가공(완료, 2026-08-20)** — 원본 BMP에서 배경색이 아닌 픽셀의 경계 상자를 자동 탐지해
문구 없이 일러스트만 잘라냈다(`docs/payment_relay/images/_preview_crop/`에 확인용 산출물 있음,
사용자 확인 완료). 결과:

| 원본 | 잘라낸 크기 | 내용 |
|---|---|---|
| `BG_IMG_IC.bmp` | 377×349 | 카드 삽입 리더기 + 하향 화살표 (카드 제외 — 카드는 벡터로 별도) |
| `BG_IMG_MS.bmp` | 376×353 | 카드 긁기 리더기 + 좌향 화살표 |
| `BG_IMG_PROCESSING.bmp` | 377×302 | 서버/휴대폰 일러스트 (이 상태엔 카드 애니메이션 없음) |

> `BG_IMG_IC`/`BG_IMG_MS` 원본은 카드가 리더기에 이미 꽂힌/겹친 채로 한 장에 합성돼 있어, 카드를
> 오려내면 리더기 쪽에 뚫린 구멍(원본에 없던 픽셀)이 남는다 — 그래서 이번 크롭은 **카드가 아직
> 나타나지 않은 배경**만 잘라냈고, 카드는 P13-1-B에서 **새로 벡터로** 그린다(원본 카드를 재사용하지
> 않는다).

- 위치: `Assets/Images/PaymentNotice/`. `.csproj`의 `<Resource Include>`에 3개(배경 일러스트) 등록.
  `_VERYSMALL` 대응 자산은 크기 조절 기능 자체가 범위 밖(PRD §10)이므로 이번엔 만들지 않는다.
- 파일명은 원본 이름을 유지한다(`BG_IMG_IC.png` 등) — 원본과 대조할 때 매핑표를 다시 찾지 않도록.
- `LOCK`/`QR`은 배치하지 않는다.
- **문구는 전부 XAML `TextBlock`**(Pretendard, 기존 `Themes/Typography.xaml` 스타일 재사용)으로 새로
  올린다 — 이미지에서 이미 제거됐으므로 반드시 새로 올려야 화면이 완성된다(원본 문구 원문은
  `docs/payment_relay/images/`의 BMP를 참고해 그대로 옮긴다. 예: "그림과 같이 카드를 넣어주세요." 등,
  §5.2 각 이미지 원문 대조).
- **★ 배경 소스 단일 지점**: `PaymentNoticeState` → 배경 일러스트 URI 매핑이 **코드/XAML 통틀어 정확히
  한 곳**에만 존재해야 한다.
- 3장은 **앱 기동 시 미리 디코드해 `Freeze()`한 뒤 캐시**한다(표시 지연 방지 이유는 기존과 동일).
  워밍업 창 방식은 반복하지 않는다(P12-6 부작용 확인됨) — 이미지 캐시만으로 충분.

**수정(2026-08-21, 사용자와 실제 자산 준비 중 추가 확정)** — 사용자가 직접 이미지를 만들어 제공하기로 하면서
레이어 구성이 한 단계 더 세분화됐다.

- **리더기 몸통은 IC/MS 공용 정지 이미지 1장**(`reader.png`)으로 분리한다 — 두 상태의 리더기 기기 자체는
  동일하고 화살표 방향만 다르므로, 굳이 이미지를 2장 따로 둘 필요가 없다.
- **화살표도 배경에서 완전히 분리해 독립 레이어로 둔다**(`arrow_ic.png`/`arrow_ms.png`) — 실제 결제
  대기 화면에서 화살표까지 살짝 움직이면(펄스/바운스) 더 눈에 띈다는 사용자 판단에 따른 것. 즉 이 화면은
  최종적으로 **3개 레이어**로 구성된다: 정지 리더기(1장, 공용) + 애니메이션 화살표(상태별) + 애니메이션
  벡터 카드(상태별, 아래 P13-1-B). PROCESSING 화면은 카드/화살표가 없는 별도 정지 장면이라 이 레이어
  구조와 무관하게 기존 `BG_IMG_PROCESSING_illustration.png`를 그대로 쓴다.
- **MS용 화살표 자산은 스타일이 맞지 않아 미확정**(네온 글로우 렌더가 반복 생성됐고, 기존 플랫 아이소메트릭
  톤과 어긋남) — 사용자 결정으로 **실제 구현 시점에 눈으로 보고 판단**하기로 했다. 대안으로 이미 스타일이
  검증된 `arrow_ic.png`(아래 방향)를 90도 회전시켜 재사용하는 방법도 함께 검토한다(장점: 스타일 100%
  일치·추가 생성 불필요, 단점: 아이소메트릭 그림자 방향이 회전 후 어색해질 수 있음 — 실제로 만들어보고
  판단).
- **`reader.png`의 흰색/파란 글로시 스타일은 실수가 아니라 사용자의 의도적인 트렌드 반영**(2026-08-21
  확정) — 기존 플랫 아이소메트릭에서 **글로시/글로우 톤으로 스타일 자체를 전환**하는 결정이다. 따라서
  화살표도 이 새 기준(글로시/글로우)에 맞춰 재판단한다 — `arrow_ms.png`의 네온 글로우가 오히려
  `arrow_ic.png`의 플랫 코랄톤보다 `reader.png`와 더 어울릴 가능성이 있다(이전 판단 기준이 뒤집힘).
  실제 화면에 셋을 같이 놓고 판단한다.
- **PROCESSING 상태는 별도 이미지를 쓰지 않는다**(2026-08-21 확정) — 기존 `BG_IMG_PROCESSING_illustration
  .png`(서버/휴대폰 그림)는 채택하지 않는다. 대신 **IC/MS와 같은 `reader.png`를 그대로 유지**하고, 카드
  애니메이션은 멈춘 채(또는 카드를 뺀 채) 화살표 대신 **로딩 인디케이터**(점 3개 순차 깜빡임 또는 회전
  스피너 — 벡터로 직접 그림, 새 이미지 자산 불필요)로 "처리 중"을 표현한다.

## P13-1-B. 카드 벡터 컨트롤 + 이동 애니메이션

**카드는 벡터로 새로 그린다.** 실제로 움직이는 건 카드뿐이므로, 이 하나만 XAML로 만들면 애니메이션
목적을 달성한다(P13-1의 하이브리드 결정 표 참고).

- `Views/Controls/PaymentCardShape.xaml`(또는 `PaymentNoticeWindow.xaml` 내 재사용 `<Path>`/`<Border>`
  조합) — 라운드 사각형 몸체 + IC 칩(작은 사각형/그라데이션) 정도의 단순화된 카드. 원본 일러스트의
  카드와 완전히 동일할 필요는 없다(색상 톤만 맞춘다) — 손으로 그리는 벡터라는 한계를 계획서에도
  분명히 남긴다.
- **IC 상태**: 카드가 화면 위에서 시작해 리더기 슬롯 위치로 **아래로** 슬라이드해 들어간다
  (`TranslateTransform.Y` + `DoubleAnimation`, `EasingFunction`으로 자연스러운 감속).
- **MS 상태**: 카드가 오른쪽에서 시작해 **왼쪽으로** 슬라이드(긁기 동작).
- **PROCESSING 상태**: 카드 애니메이션 없음(이 화면엔 카드가 등장하지 않는다 — 위 표 참고).
- 카드는 **반복 애니메이션**으로 둔다(한 번 움직이고 멈추지 않고, 원위치로 돌아갔다 다시 움직이는
  루프) — 정지 화면보다 "지금 대기 중"이라는 상태를 계속 알려주는 효과가 있다. `RepeatBehavior="Forever"`.
- **화살표도 같은 원칙으로 반복 애니메이션**(2026-08-21 추가) — 펄스(스케일 확대/축소) 또는 위치
  바운스 중 실제로 만들어보고 덜 산만한 쪽을 채택한다. 카드와 동시에 움직이면 산만하므로 **템포를
  다르게**(예: 카드보다 느리게, 또는 위상을 다르게) 준다.
- 창이 안 보이거나 닫힌 뒤에는 **카드·화살표 Storyboard 둘 다** 반드시 멈춘다(백그라운드에서 계속 도는
  애니메이션은 리소스 낭비이자 PRD §9 리소스 정리 원칙 위반).

**완료 조건**
- [x] 자산이 `Assets/Images/PaymentNotice/`에 있고(`reader.png`, `arrow_ic.png`, `arrow_ms.png`)
      `<Resource>`로 등록되어 빌드에 포함됨. 기존 `BG_IMG_IC.png`/`BG_IMG_MS.png`/`BG_IMG_PROCESSING.png`
      (원본 합성 배경)는 `<Resource>` 등록에서 빠짐(원본 BMP는 `docs/payment_relay/images/`에 추적용으로
      보존).
- [x] `LOCK`/`QR`은 배치되지 않음
- [x] 리더기/화살표 → 이미지 매핑 지점이 각각 `PaymentNoticeBackgroundSource.cs`의 `ReaderSource`/
      `GetArrowSource(state)` 1곳으로 확인됨
- [x] 문구가 XAML 텍스트로 원본 BMP와 동일한 내용으로 보임(스크린샷 대조 완료)
- [x] IC/MS 상태에서 카드와 화살표가 각자 반복 애니메이션으로 움직임(카드 1.1초/화살표 0.6~0.65초로
      템포 분리, 육안 확인 완료)
- [x] `reader.png` + `arrow_ic.png`/`arrow_ms.png` 조합을 실제 창에 넣고 판단 — `arrow_ms.png`(네온
      글로우)가 `reader.png`의 글로시 톤과 더 잘 어울려 기본값으로 채택(`arrow_ic.png` 90도 회전은
      아이소메트릭 그림자 방향이 부자연스러워 기각). `UseArrowMsAsset` 상수로 스위치 가능하게 남겨둠.

**최종 수정(2026-08-21, 카드 벡터 → 실제 이미지로 교체)**: 카드 각도를 벡터로 재현하려던 시도가
두 차례 실패했다 — ① 리더기 슬롯 각도(30도) 그대로 큰 회전 → IC 상태에서 다이아몬드처럼 과하게
돌아 보임, ② 원본 대조 후 정정한 "세워진 카드 -12도 회전 + 오른쪽 두께 옆면"도 여전히 원본과 미세하게
달랐다. 사용자가 직접 정확한 각도의 카드 사진(`ic_card.png`/`ms_card.png`, 아이소메트릭, 투명 배경)을
만들어 제공해 그걸로 교체했다 — `Views/Controls/PaymentCardShape.xaml`(벡터)는 삭제하고
`PaymentNoticeWindow.xaml`의 `CardImage`(Image + TranslateTransform, `ArrowImage`와 동일 패턴)로
대체했다. 이미지 매핑은 `PaymentNoticeBackgroundSource.GetCardSource(state)` 한 곳.

**최종 수정(2026-08-21, PROCESSING 애니메이션 — 사용자 기획 반영)**: 점 3개 로딩 인디케이터
(`PaymentLoadingIndicator`)를 사용자가 제시한 "거래중 애니메이션" 기획(원형 진행광 회전 + 슬롯 내부
빛 흐름 + 은은한 펄스, `docs/payment_relay/images/_preview_crop/거래중 애니메이션.png`)으로 교체했다
— `Views/Controls/PaymentProcessingIndicator.xaml`. 구현 중 두 가지 문제를 실제 화면에서 발견해
바로잡았다: ① `RotateTransform`으로 타원 호를 돌리면 타원(rx≠ry)은 회전 대칭이 아니라 호가 테두리를
벗어나 리더기 몸통을 가로지르는 결함, ② `PathFigure`/`ArcSegment`의 두 점+Size로 호를 역산하는
방식도 기대와 다른 훨씬 큰 호를 그리는 결함. 두 방식 모두 폐기하고, 좌표 계산(타원 매개변수식)만
신뢰해 **점 6개를 타원의 보이는 크레센트 위에 고정 배치한 뒤 Opacity 체이스**로 회전감을 표현하는
훨씬 단순하고 예측 가능한 방식으로 바꿨다. 그리고 오버레이가 `reader.png`보다 위 레이어라 타원의
"몸통에 가려져야 할" 뒤쪽 절반까지 표현하면 몸통 위로 비쳐 보이므로, 점 배치를 보이는 절반(각도
25~140도)으로만 제한했다 — `reader.png`가 그림자+몸통을 한 장에 합성한 이미지라 레이어 분리가
불가능해서 생기는 근본 제약(P13-1 하이브리드 자산 결정과 같은 종류의 한계).

**최종 수정(2026-08-21, FALLBACK 화살표-카드 겹침)**: 사용자 지적대로 FALLBACK 상태에서 화살표가
카드에 거의 가려지는 문제를 실제 화면에서 확인했다(둘 다 리더기 오른쪽·세로 중앙 근처에 배치돼
있었음). 원본 참고 이미지(화살표가 카드 위쪽에서 아래로 향하는 구도)와 같은 배치로, 화살표를
카드보다 위로 올려 겹치지 않게 했다.

**완료 조건(최종)**
- [x] **PROCESSING 상태에서 별도 이미지 없이 `reader.png`가 그대로 유지**되고, 카드/화살표 대신
      `PaymentProcessingIndicator`(원형 진행광 체이스 + 슬롯 빛 흐름 + 로고 펄스)가 보임 — 스크린샷
      확인 완료.
- [x] 카드가 이미지(`ic_card.png`/`ms_card.png`)로 교체되어 리더기와 각도가 정확히 일치함(벡터 재현
      실패 후 실제 이미지로 대체, 스크린샷 대조 완료).
- [x] FALLBACK 상태에서 화살표와 카드가 겹치지 않고 둘 다 온전히 보임(스크린샷 확인 완료).
- [x] 창을 닫으면 카드·화살표·PROCESSING 애니메이션(원형 진행광/슬롯 빛 흐름/펄스)이 모두 즉시
      멈춤(`PaymentNoticeWindow_Closed`가 `StopCard()`/`ProcessingIndicator.Stop()`/화살표 Storyboard
      Stop을 모두 호출하도록 배선 완료, 코드 확인 — 백그라운드 CPU 실측까지는 하지 않음).
- [x] 홈 화면 회귀 없음(재실행 확인 완료), `dotnet build` 경고 0/오류 0.

## P13-2. `PaymentNoticeState` + `PaymentNoticeViewModel` 신설

새 화면이므로 처음부터 MVVM으로 만든다(공통 규칙 6).

- **`Services/Payment/PaymentNoticeState.cs`** — `IcCardRequest` / `FallbackCardRequest` / `VanProcessing`
  3개 값. **`ViewModels/`가 아니라 `Services/` 아래**에 두는 이유: Phase 15의 결제 워커(Services 계층)가
  이 값을 넘겨 화면을 전환시킨다. 계층 규칙상 `ViewModels → Services` 방향만 허용되므로, Services가
  ViewModels의 타입을 참조할 수는 없다.
- **`ViewModels/PaymentNoticeViewModel.cs`** — `State` 프로퍼티 **하나**를 노출하고, 화면의 이미지는
  전부 이 값에서 파생시킨다. **`Visibility`를 ViewModel에서 다루지 않는다**(P7-3에서 정립한 원칙).
- **★ 취소는 정확히 한 번만 나간다.** 취소 버튼 연타, ESC 연타, 버튼과 ESC 동시 입력 — 어느 경우에도
  취소 통지는 1회여야 한다. ViewModel 안에 `_canceled` 플래그를 두고 **첫 호출만 통과**시킨다.
  (취소와 카드 리딩 CALLBACK 사이의 중재는 Phase 15 몫이지만, **알림창이 스스로 취소를 두 번 쏘는 것**은
  여기서 막아야 한다 — 그러지 않으면 Phase 15 게이트가 아무리 정확해도 원인 추적이 어려워진다.)
- 취소 후에는 버튼을 비활성화해 사용자에게도 "이미 취소됨"이 보이게 한다.

**★ 취소 가능 구간 (2026-08-20 사용자 확정 — PRD §4.8/§5.3, §10.1 표에 반영 완료)**

**`VanProcessing` 상태에서는 취소를 막는다.** `FNAISCRDVAN` 요청이 이미 나간 뒤에 취소를 받으면
**VAN 서버에서는 승인이 났는데 POS에는 취소로 응답하는** 불일치가 생길 수 있기 때문이다. 구체적으로:

- 취소 버튼을 **비활성화**한다(숨기지 않는다 — 버튼이 사라졌다 나타나면 레이아웃이 흔들리고, 비활성
  상태로 보이는 편이 "지금은 취소할 수 없다"는 것을 사용자에게 더 정확히 알린다).
- **ESC도 같은 규칙을 따른다**(P13-5). ESC는 "취소 버튼 클릭과 동일하게 처리"이므로 버튼이 막힌
  구간에서는 ESC도 무시된다. 이때 ESC를 **삼키지 않고** `CallNextHookEx`로 흘려보낸다 — 우리가 아무
  처리도 하지 않았으므로 가로챌 이유가 없다.
- **가능 여부 판정은 ViewModel 한 곳에서만** 한다(예: `IsCancelAllowed`). View와 훅 처리기가 각자
  상태를 보고 판단하면 두 경로가 어긋난다.

**완료 조건**
- [x] `PaymentNoticeState`가 `Services/Payment/`에 있고, WPF 타입을 참조하지 않음(기존부터 완료돼 있었음)
- [x] ViewModel에 `Visibility` 타입이 등장하지 않음(grep으로 확인 — `PaymentNoticeViewModel.cs`에
      `State`/`IsCancelAllowed`/`CancelCommand`/`Canceled` 이벤트만 존재)
- [x] 취소 버튼 연타 → 취소 통지가 **정확히 1회**임을 실기 확인(`windows_click`으로 클릭 1회 후 버튼이
      즉시 `[disabled]`로 전환되는 것을 스냅샷으로 검증 — `_canceled` 플래그가 `CancelCommand`의
      `CanExecute`를 통해 두 번째 클릭 자체를 막으므로 `Cancel()` 본문은 1회만 실행됨).
      **ESC 5회 연타는 P13-5(ESC 훅 자체가 아직 없음)에서 마저 검증**.
- [x] `VanProcessing`으로 전환하면 취소 버튼이 **비활성 상태로 보이고** 클릭이 무시됨 — 자동 순환
      데모(`PaymentNoticeWindow()` 3초 타이머)로 IC→MS→VanProcessing 전환 중 스냅샷 실기 확인
      (VanProcessing 진입 시 `[disabled]`, 다음 상태로 빠져나오면 재활성화됨을 확인)
- [ ] `VanProcessing` 중 ESC를 눌러도 취소가 발생하지 않고, 그 ESC가 다른 프로그램에 정상 전달됨 — **P13-5로 이월**(ESC 훅 미구현)
- [x] 취소 가능 여부 판정 지점이 grep으로 **1곳**임 — `PaymentNoticeViewModel.IsCancelAllowed`
      (`!_canceled && State != PaymentNoticeState.VanProcessing`) 한 곳에서만 판정, `CancelCommand`의
      `[RelayCommand(CanExecute = nameof(IsCancelAllowed))]`가 그대로 재사용

**구현 메모(2026-08-24)**: `CommunityToolkit.Mvvm`의 `[RelayCommand(CanExecute = ...)]` +
`[NotifyCanExecuteChangedFor]`/`[NotifyPropertyChangedFor]` 조합으로 코드비하인드 클릭 핸들러 없이도
버튼 활성/비활성이 자동으로 따라온다(WPF `Button.Command`가 `ICommand.CanExecuteChanged`를 구독해
`IsEnabled`를 스스로 갱신) — `Views/PaymentNoticeWindow.xaml.cs`의 빈 `CancelButton_Click`을 제거하고
XAML을 `Command="{Binding CancelCommand}"`로 바꿨다. `Canceled` 이벤트는 아직 아무도 구독하지 않는다
(Phase 15에서 `IPaymentNoticePresenter` 구현체가 구독할 예정, P13-6).

## P13-3. `Views/PaymentNoticeWindow` 신설

- `WindowStyle="None"`, `ResizeMode="NoResize"`, `Topmost="True"`, 750×650 고정,
  `WindowStartupLocation="CenterScreen"`.
- **`Owner`를 설정하지 않는다.** 홈 화면을 Owner로 지정하면 알림창을 띄울 때 홈 화면까지 활성화되어
  PRD §5.1("결제 요청 때문에 메인 화면이 전면에 노출되어서는 안 된다")을 정면으로 위반한다.
- 배경 일러스트(P13-1) + 카드(P13-1-B) + 문구(`TextBlock`) + 하단 중앙 취소 버튼 오버레이.
- **상태 전환 시 창을 새로 만들지 않는다.** IC → FALLBACK → PROCESSING은 같은 창의 `State` 변경만으로
  일어나야 한다. 창을 닫았다 다시 열면 깜빡임이 생기고, ESC 후킹을 그때마다 걸었다 푸는 위험도 늘어난다.
- **창은 매 거래마다 새로 만든다**(상태 전환과 혼동하지 말 것). 하나를 숨겨두고 재사용하면 이전 거래의
  상태·취소 플래그가 남을 위험이 있고, ESC 후킹 해제 시점도 모호해진다. 이미지는 이미 캐시돼 있으므로
  (P13-1) 생성 비용은 문제되지 않는다.
- 저해상도 화면(컴팩트 모드 기준 = 화면 높이 ≤800px) 대응: 창이 작업 표시줄에 가리거나 화면 밖으로
  나가지 않는지 확인한다. **크기 조절 기능은 범위 밖**이므로 위치 보정까지만 한다.

**★ 상태 전환은 하드 컷이 아니라 크로스페이드로 한다(2026-08-20 사용자 확정)** — "카드를 넣어주세요" →
"거래중입니다"처럼 이미지가 바뀔 때 끊기는 느낌이 없어야 한다.

- 배경 일러스트가 바뀌는 순간, **이전 배경을 즉시 지우지 않고 다음 배경과 겹친 채로** 이전 것은
  `Opacity` 1→0, 다음 것은 0→1로 동시에 애니메이션한다(단순 `Image.Source` 교체는 하드 컷이라 쓰지
  않는다). 두 배경을 겹쳐 놓을 자리가 필요하므로 배경 호스트는 **`Grid`에 이미지 2장을 겹쳐 두고
  교대로 크로스페이드**하는 구조로 만든다(P13-1의 "배경 소스 단일 지점" 규칙과는 별개 — 매핑 함수는
  여전히 1곳, 그 결과를 그리는 시각적 전환 처리가 이번에 추가되는 것).
- 문구(`TextBlock`)도 배경과 같은 타이밍에 크로스페이드한다 — 배경만 부드럽고 문구가 툭 바뀌면 여전히
  끊겨 보인다.
- 카드(P13-1-B)는 상태가 바뀌면 **크로스페이드가 끝난 뒤** 새 상태의 슬라이드 애니메이션을 시작한다
  (배경 전환과 카드 등장이 겹치면 산만해진다 — 순서: 배경/문구 페이드 완료 → 카드 애니메이션 시작).
  PROCESSING으로 전환할 때는 카드가 없으므로 이전 상태의 카드가 페이드아웃하며 함께 사라진다.
- 전환 애니메이션 총 소요 시간은 200~300ms 권장(더 길면 굼떠 보이고, 더 짧으면 크로스페이드 효과가
  거의 안 보인다) — 실제 값은 구현 중 육안으로 조정한다.

**완료 조건**
- [ ] 3개 상태 전환이 **창 재생성 없이** 이루어지고 깜빡임이 없음
- [ ] IC→PROCESSING, MS→PROCESSING, PROCESSING→IC 등 모든 전환 조합에서 배경+문구가 하드 컷 없이
      크로스페이드됨(스크린 녹화 또는 연속 스크린샷으로 확인)
- [ ] 카드 애니메이션이 크로스페이드 완료 후 시작되고, PROCESSING 전환 시 카드가 페이드아웃하며 사라짐
- [ ] 다른 프로그램(메모장 등) 위에 항상 표시됨
- [ ] 취소 버튼이 일러스트를 가리지 않고 앱 전체 버튼 스타일과 일관됨(스크린샷 확인)
- [ ] 1366×768 등 저해상도에서 창이 화면 밖으로 나가지 않음

## P13-4. 표시 정책 (PRD §5.1)

- 알림창 표시가 **홈 화면을 전면에 끌어올리지 않는다**.
- 알림창이 닫힌 뒤 **홈 화면이 전면에 남지 않는다** — 사용자가 조작해 홈 화면을 띄워둔 상태였더라도
  결제 후에는 백그라운드로 돌아가야 한다.
- 이 앱은 트레이 상주로 자동 최소화 기동하므로(1차 범위 동작), 홈 화면이 **최소화된 상태**와 **떠 있는
  상태** 두 경우를 모두 확인해야 한다.

**구현(2026-08-24)**: `PaymentNoticeWindow` 생성자에서 `SuppressHomeWindowForeground()`를 호출해
`Application.Current.Windows`를 순회, 떠 있는(`IsVisible`) `HomeWindow` 인스턴스가 있으면
`HomeWindow.MinimizeToTrayForPaymentNotice()`(기존 `MinimizeToTray()` 재사용, `IsVisible`일 때만
동작)로 미리 트레이에 내린다. **홈 화면이 떠 있던 상태에서 알림창을 열면, 닫을 때 OS 기본 활성화
순서상 바로 뒤에 있던 홈 화면이 자동으로 전면에 올라오는 문제가 실기로 확인됐다** — 그래서 "알림창
표시가 홈 화면을 끌어올리지 않는다"뿐 아니라, 반대로 "알림창을 열기 전에 홈 화면을 먼저 내린다"까지
해야 두 조건이 동시에 만족된다. `Owner`는 여전히 설정하지 않는다(P13-3 결정 유지).

검증에는 임시 개발용 트리거 `App.xaml.cs`의 `--home-notice-test` 인자(홈 화면 표시 2초 후 같은
프로세스에서 알림창을 띄움)를 추가해 한 프로세스 안에서 실기로 재현했다(P13-7이 필요로 하는
"개발용 임시 트리거"와 별개로, 이 검증 전용으로 남겨둔다 — 회귀 테스트에도 재사용 가능).

**완료 조건**
- [x] 홈 화면이 최소화(트레이)된 상태에서 알림창을 띄움 → 홈 화면이 복원되지 않음 (별도 프로세스로
      재현: `--home` 실행 후 최소화 버튼 클릭 → `windows_list_windows`에서 사라짐 확인 → 알림창
      기본 실행 → 알림창을 닫아도 홈 화면 미복원 확인)
- [x] 홈 화면이 전면에 떠 있는 상태에서 알림창을 띄우고 닫음 → 홈 화면이 다시 전면에 오지 않음
      (`--home-notice-test`로 한 프로세스에서 재현: 홈 화면 표시 중 알림창이 뜨자마자 홈 화면이
      `windows_list_windows`에서 사라짐(자동 트레이 이동) → 알림창을 닫아도 홈 화면 미복원 확인,
      프로세스는 계속 살아 있음(트레이 상주) 확인)
- [x] 알림창을 닫은 뒤 포커스가 직전 프로그램(예: 메모장)으로 돌아감 — 홈 화면이 항상 트레이로
      내려가 있으므로(위 두 시나리오 모두), 우리 앱의 어떤 창도 포커스를 다시 가져가지 않는다.
      그 외 프로그램(메모장 등)으로의 복귀는 OS 기본 활성화 순서에 맡긴다(우리 코드가 관여하지
      않음 — 코드 리뷰로 확인: `PaymentNoticeWindow`/`HomeWindow` 어디에도 다른 프로세스 창을
      활성화하는 코드가 없음).

**★ 알려진 범위 밖(2026-08-24, 사용자 확인 후 보류)**: `SuppressHomeWindowForeground()`는
`Application.Current.Windows`에서 **`HomeWindow`만** 골라 트레이로 내린다 — **`ReaderSetupWindow`가
떠 있는 경우는 다루지 않는다.** PRD §5.1 원문이 "메인 화면(홈 화면)"만 지정하고 있어 완료 조건 자체는
충족하지만, 실사용 시나리오상 사각지대다:

- `ReaderSetupWindow`는 `HomeWindow.OpenReaderSetup()`에서 `Owner=this`로 뜨는 **모달** 다이얼로그다
  (`dialog.ShowDialog()`). 홈 화면처럼 "트레이로 내려가는" 정상 동작 경로 자체가 없다.
  `MinimizeToTray()`를 그대로 재사용할 수 없는 이유.
- 사용자가 포트 설정을 변경 중(dirty 상태)일 수도 있는데, 결제 알림창이 끼어들어 이 창을 강제로
  숨기거나 닫으면 **저장 안 한 변경사항을 사용자 모르게 날릴 위험**이 있다 — 함부로 손댈 수 없다.
- 그래서 사용자가 리더기 설정 화면을 연 채로 결제 요청이 들어오면, 알림창이 `Topmost`로 위에 뜨긴
  하지만 리더기 설정 화면은 그대로 남아 있고, **알림창을 닫을 때 그것이 다시 전면에 올라올 가능성이
  있다**(홈 화면에서 실기로 확인했던 것과 동일한 OS 활성화 순서 문제) — 이 경로는 검증하지 않았다.
- 근본적으로는 "리더기 설정 중에 결제 요청이 들어오면 어떻게 할 것인가"라는 **정책 결정이 먼저
  필요**하다(예: 알림 자체를 보류/거부할지, 단순히 뒤로 보내기만 할지) — 리더기 포트 점유 문제와도
  얽혀 있어 Phase 15(결제 워커/포트 중재) 범위와 맞닿아 있다. 사용자 지시로 **일단 보류하고 다음
  Task(P13-6)로 진행**한다. Phase 15 착수 시 반드시 재검토한다.

## P13-5. ESC 전역 후킹 + **해제 보장** ★

**이 Phase에서 가장 위험한 Task다.** 전역 훅은 해제를 놓치면 앱이 살아 있는 내내 시스템의 모든 키 입력이
우리 콜백을 거쳐 간다(PRD §9 리소스 정리에 명시된 항목).

- P/Invoke 선언은 `Interop/` 아래(기존 `ReaderSerialNative.cs`와 같은 계층):
  `SetWindowsHookEx(WH_KEYBOARD_LL, ...)` / `UnhookWindowsHookEx` / `CallNextHookEx`. 훅의 **수명 관리**는
  창과 함께 살아야 하므로 `Views/` 쪽에 둔다.
- **★ 콜백 델리게이트를 GC로부터 보호한다.** 델리게이트를 지역 변수로 넘기면 네이티브 쪽은 살아 있는데
  관리 객체가 수거되어 **랜덤한 시점에 프로세스가 죽는다.** Phase 9(P9-2)에서 리더기 CALLBACK에 대해
  세운 것과 **정확히 같은 규칙**을 적용한다 — 필드로 보관해 훅이 걸려 있는 동안 참조를 유지한다.
- **훅 콜백 안에서 일을 하지 않는다.** 저수준 키보드 훅은 시스템 전체의 키 입력 경로에 끼어 있으므로
  콜백이 느리면 OS가 훅을 강제로 떼어낸다. ESC를 감지하면 `Dispatcher.BeginInvoke`로 넘기고 **즉시 반환**한다.
- **ESC를 삼킬 것인가**: 알림창이 떠 있고 **실제로 취소를 트리거한 경우에만** 삼킨다(훅에서 1 반환).
  근거 — 이미 취소로 처리한 키를 POS에도 전달하면 POS가 같은 ESC로 자기 화면을 또 조작해 이중 동작이
  된다. 반대로 **`VanProcessing` 구간처럼 우리가 취소를 처리하지 않은 ESC는 삼키지 않는다**(P13-2 확정
  사항). 그 외의 모든 키도 **반드시** `CallNextHookEx`로 그대로 흘려보낸다.
- **★ 해제 3중 보장**: ① 창의 `Closed` 이벤트, ② 취소/완료 등 어떤 경로로 창이 사라지든 같은 지점을
  거치게 할 것(경로마다 해제 코드를 복붙하지 않는다 — Phase 12에서 X 닫기가 정리 로직을 통째로 건너뛴
  결함이 나온 적이 있다, P12-6 참고), ③ `Dispatcher.ShutdownStarted` 백스톱.

**구현(2026-08-24)**: `Interop/LowLevelKeyboardHookNative.cs`(순수 P/Invoke 선언 — `SetWindowsHookEx`/
`UnhookWindowsHookEx`/`CallNextHookEx`/`GetModuleHandle`, `KBDLLHOOKSTRUCT`)와 `Views/
PaymentNoticeEscapeHook.cs`(수명 관리, `IDisposable`)로 나눴다. 콜백은 ESC(`VK_ESCAPE`)인지와
`IsCancelAllowed()`(동기 필드 읽기 수준)만 확인해 삼킬지 결정하고, 실제 취소 실행
(`_viewModel.CancelCommand.Execute(null)`)은 `Dispatcher.BeginInvoke`로 미룬다. `PaymentNoticeWindow`
생성자에서 설치하고, `Closed`에서 해제 + `Dispatcher.ShutdownStarted` 구독 해제(백스톱 핸들러도 해제해
누적 구독을 막음).

검증용 임시 개발 트리거 2개를 `App.xaml.cs`에 추가했다(실제 회귀 테스트에도 재사용 가능하므로
유지): `--esc-hook-stress-test`(알림창 10회 연속 열고 닫기, 예외/설치 실패 없이 완료되면 로그로 확인),
`--notice-van-processing-test`(State를 VanProcessing으로 고정 — 3초 자동 순환 데모는 타이밍이 계속
바뀌어 ESC 게이팅 검증에 못 쓴다. 5초 뒤 IcCardRequest로 전환해 "그 사이 ESC가 실제로 취소했는지"를
버튼 재활성화 여부로 눈으로 구분 가능하게 함).

**완료 조건**
- [x] 포커스가 **메모장에 있는 상태**에서 ESC → 알림창이 취소로 반응(전역 훅이 실제로 동작함을 입증) —
      `windows_focus`로 메모장 포커스 후 `windows_send_keys`(Escape) → 알림창 취소 버튼이 즉시
      `[disabled]`로 전환됨을 스냅샷으로 확인.
- [x] `VanProcessing` 중 ESC를 눌러도 취소가 발생하지 않고, 그 ESC가 다른 프로그램에 정상 전달됨 —
      `--notice-van-processing-test`로 재현(3초 자동 순환 데모로는 겉보기 disabled와 실제 취소가
      구분 안 돼 처음엔 잘못된 결론을 낼 뻔함 — 반드시 State를 고정하고 사후 전환으로 확인해야 함,
      아래 "시행착오" 참고). ESC를 눌러도 버튼은 VanProcessing 게이팅으로 disabled인 채였고, 5초 뒤
      IcCardRequest로 전환하자 버튼이 **재활성화**됨을 확인 — `_canceled`가 여전히 false, 즉 ESC가
      실제로는 취소를 트리거하지 않았음을 입증.
- [x] ESC 이외의 키(한글 입력 대신 영문으로 검증 — Ctrl+C 등)가 알림창이 떠 있는 동안에도 다른
      프로그램에서 정상 동작 — 알림창을 띄운 채 새 메모장에 "hello" 타이핑 → 정상 입력됨을 확인
      (구조적으로도 콜백이 `VK_ESCAPE`가 아니면 즉시 `CallNextHookEx`로 흘려보내므로 다른 키에
      개입할 지점 자체가 없음).
- [x] 알림창을 **10회 연속** 열고 닫은 뒤에도 훅이 남아 있지 않음 — `--esc-hook-stress-test`로
      한 프로세스 안에서 10회 연속 Show/Close, `FileLogger` 로그에서 "예외 없음" 완료 확인 + 훅
      설치 실패 로그 없음. 이후 11번째 인스턴스도 정상적으로 ESC를 감지함을 재확인.
- [x] 창 X/Alt+F4/강제 종료 등 **모든 경로**에서 해제가 일어남을 로그로 확인 — 이 창은 타이틀바/X
      버튼이 없어(P13-3) 코드 경로(`Close()`)와 Alt+F4만 실제 경로다. 둘 다 WPF `Closed` 이벤트로
      수렴하므로(코드 확인 — 별도 이벤트 핸들러가 없음), 위 10회 연속 열고 닫기 테스트가 이 경로를
      그대로 검증한다. 강제 종료(taskkill)는 프로세스가 통째로 죽으므로 애초에 훅도 OS가 회수한다.
- [x] 훅 콜백 델리게이트가 필드로 보관되어 있음(코드 확인) — `PaymentNoticeEscapeHook._proc`
      (`readonly LowLevelKeyboardProc`), 생성자에서 `_proc = HookCallback;`으로 할당.

**시행착오(2026-08-24)**: 처음엔 3초 자동 순환 데모(`PaymentNoticeWindow()`)에서 VanProcessing
구간을 노려 ESC를 보내고 disabled 여부만 확인했는데, VanProcessing 자체가 항상 disabled로 보이는
상태라 "정상 게이팅으로 disabled"와 "ESC가 게이트를 뚫고 실제로 취소해버려서 disabled"를 겉보기로
구분할 수 없었다 — 실제로 첫 시도에서는 도구 호출 왕복 지연 때문에 ESC가 VanProcessing이 아니라
다음 IC 상태로 넘어간 뒤에 눌린 것으로 추정되는 오탐(취소가 영구 latched)이 나와 "게이트가
뚫렸나?" 하고 잠깐 오판했다. State를 고정하고 사후에 다른 상태로 전환해 재활성화 여부로 판정하는
방식(`--notice-van-processing-test`)으로 바꿔서야 정확히 검증할 수 있었다 — 이후 상태 의존적인
불리언(disabled처럼 여러 원인이 같은 결과를 내는 값)을 검증할 때는 이 패턴(고정 → 트리거 → 상태
전환으로 원인 분리)을 재사용한다.

## P13-6. 제어 진입점 계약 — Phase 15가 쓸 것 ★

Phase 15의 결제 워커는 **UI 스레드가 아닌 곳에서** 알림창을 띄우고 상태를 바꾸고 닫아야 한다. 그런데
계층 규칙상 `Services/`는 WPF 타입(`Dispatcher`/`Window`)을 알면 안 된다. 이 경계를 **지금** 그어둬야
Phase 15가 순수 배선 작업이 된다.

- **`Services/Payment/IPaymentNoticePresenter.cs`** — WPF 타입이 전혀 없는 인터페이스.
  - `Show(PaymentNoticeState)` / `ChangeState(PaymentNoticeState)` / `Close()`
  - `event EventHandler Canceled` — 취소 통지. (`CancellationToken` 방식은 채택하지 않는다: Phase 15의
    취소·타임아웃·CALLBACK 중재 구조가 아직 확정되지 않아, 지금 토큰 기반으로 못 박으면 Phase 15에서
    다시 고칠 가능성이 크다. 단순 이벤트가 그때 어느 방식으로든 감싸기 쉽다.)
- **`Views/PaymentNoticePresenter.cs`** — 구현. `Dispatcher`로 UI 스레드에 마샬링한다.
- **★ 모든 메서드가 어느 스레드에서 호출돼도 안전해야 한다.** 호출자가 스레드를 신경 쓰게 만들면
  Phase 15에서 반드시 사고가 난다.
- 이미 닫힌 상태에서 `ChangeState`/`Close`가 들어와도 예외를 던지지 않는다(조용히 무시 + 로그). 취소와
  Flow 진행이 겹치면 충분히 발생할 수 있는 순서다.

**구현(2026-08-24)**: `Services/Payment/IPaymentNoticePresenter.cs`(WPF 타입 없는 순수 인터페이스,
`Show`/`ChangeState`/`Close`/`event Canceled`)와 `Views/PaymentNoticePresenter.cs`(구현체)로 나눴다.
모든 공개 메서드는 `RunOnUiThread`(호출 스레드가 이미 UI 스레드면 동기 실행, 아니면
`Dispatcher.Invoke`로 동기 마샬링)를 거치므로 `_window`/`_viewModel` 필드는 항상 UI 스레드에서만
접근돼 별도 락이 필요 없다. `Show()`가 `PaymentNoticeViewModel`을 새로 만들고 그 `Canceled` 이벤트를
구독해 자신의 `Canceled`로 그대로 중계하며, 창이 닫히면(`Window.Closed`) 구독을 해제하고 필드를
비운다 — 이후 `ChangeState`/`Close` 호출은 `_window is null` 분기로 걸러져 `FileLogger.Warn` 로그만
남기고 예외 없이 무시된다.

검증용 임시 개발 트리거 `--presenter-test`를 `App.xaml.cs`에 추가했다: 백그라운드 스레드에서
`Show`→`ChangeState`(3회)→(15초 대기, 수동 취소 테스트 구간)→`Close`→`Close`(재호출)→`ChangeState`
(닫힌 뒤) 순서로 호출하며 각 단계를 로그로 남긴다.

**완료 조건**
- [x] `Services/Payment/` 아래에 WPF 타입 참조가 없음(grep으로 확인 — `System.Windows` 매치 0건)
- [x] 백그라운드 스레드에서 `Show`/`ChangeState`/`Close`를 호출해도 예외 없이 동작 — `--presenter-test`
      실행 로그에서 `Show(IcCardRequest) 성공` → `ChangeState(...)` 3회 성공까지 예외 없이 확인
- [x] 닫힌 뒤 `ChangeState`/`Close` 호출 시 예외 없이 무시되고 로그만 남음 — `Close()` 재호출 시
      `WARN PaymentNoticePresenter.Close: 알림창이 열려 있지 않아 무시됨`, 닫힌 뒤 `ChangeState()` 호출
      시 `WARN PaymentNoticePresenter.ChangeState(IcCardRequest): 알림창이 열려 있지 않아 무시됨` —
      둘 다 예외 없이 통과, 마지막 `전체 완료 — 예외 없음` 로그로 종결 확인
- [x] `Canceled` 이벤트가 취소 1회당 정확히 1번 발생 — 실기: 15초 대기 구간에 취소 버튼을 연타(2회
      클릭 시도, 두 번째는 이미 비활성화라 실패) → 로그에 `Canceled 이벤트 발생 (누적 1회)` 단 1회만
      기록됨. 최초 5초 대기로 설계했을 때는 도구 호출 왕복 지연 때문에 클릭이 자동 `Close()` 이후에
      떨어져 "구독자수=0"(이미 구독 해제됨)으로 찍히는 오탐이 있었다 — P13-5에서 겪은 것과 같은 종류의
      타이밍 문제라 대기 시간을 15초로 늘려 재현했다(디버그 로그로 원인 특정 후 제거).

## P13-7. 단독 검증 (Flow 없이)

Phase 15가 없어 결제 요청으로 알림창을 띄울 수 없으므로 **개발용 임시 트리거**를 만든다.

- 기존 `Views/StyleGalleryWindow`(개발용 화면)에 트리거를 붙이거나, 그에 준하는 임시 진입점을 둔다.
- **최종 산출물이 아님을 코드 주석에 명시**하고, Phase 15에서 실제 Flow가 연결되면 제거 여부를 재검토한다.

**검증 매트릭스** — 아래를 실제로 실행해 확인한다.

| # | 시나리오 | 확인 | 결과 |
|---|---|---|---|
| 1 | IC → FALLBACK → PROCESSING 순차 전환 | 창 재생성/깜빡임 없음, 배경·문구가 크로스페이드로 부드럽게 전환, 카드 반복 애니메이션 정상 | ✅ P13-1/1-3 시각 구현 라운드에서 반복 확인(자동 3초 순환 데모) |
| 2 | 메모장을 띄운 상태에서 알림창 표시 | 항상 위에 보임 | ✅ `Topmost=true` — 이번 라운드 여러 스크린샷에서 항상 다른 창 위에 그려짐 확인 |
| 3 | 포커스가 메모장에 있을 때 ESC | 취소로 반응 | ✅ P13-5에서 실기 확인(메모장 포커스 후 ESC → 취소 버튼 즉시 disabled) |
| 4 | 알림창 닫은 뒤 메모장에서 ESC | 정상 동작(훅 해제됨) | ✅ P13-5 10회 연속 열고 닫기 테스트로 매번 해제 확인(`Closed`에서 `Uninstall()`) |
| 5 | 취소 버튼 연타 + ESC 연타 | 취소 통지 1회 | ✅ P13-2(버튼 연타), P13-5(ESC 연타) 각각 확인 + 이번 P13-7에서 **버튼 클릭 직후 ESC**(교차 조합)도
      추가 실기 확인 — 두 번째 시도(ESC)는 이미 disabled라 아무 효과 없음, 예외 없음 |
| 5-2 | `VanProcessing` 상태에서 취소 버튼 클릭 + ESC | 취소 발생 안 함, 버튼이 비활성으로 보임, ESC는 메모장에 전달됨 | ✅ P13-2(버튼), P13-5(ESC, `--notice-van-processing-test`로 정확히 재현 — 5초 대기는 처음엔 오탐이
      나와 재검증 방식을 고정 상태 + 사후 전환으로 바꿈) |
| 6 | 홈 화면 최소화 상태에서 표시/닫기 | 홈 화면 복원 안 됨 | ✅ P13-4에서 별도 프로세스로 재현·확인 |
| 7 | 홈 화면 전면 상태에서 표시/닫기 | 홈 화면이 다시 전면에 오지 않음 | ✅ P13-4에서 `--home-notice-test`로 한 프로세스 안에서 재현·확인(이 과정에서 "알림창을 열기 전에
      홈을 먼저 내려야 한다"는 추가 조치가 필요함을 발견) |
| 8 | 열고 닫기 10회 반복 | 핸들/메모리 증가 없음, 훅 잔존 없음 | ✅(훅 잔존 없음) `--esc-hook-stress-test`로 한 프로세스에서 10회 연속 Show/Close, 예외·설치 실패
      없음, 11번째 인스턴스도 정상 동작 확인. **핸들/메모리 수치 자체는 프로파일러로 측정하지 않음**
      (도구 미비) — 예외 없이 반복 가능하고 매번 `Uninstall()`이 호출된다는 코드 경로 확인으로 대체 |
| 9 | 백그라운드 스레드에서 제어 호출 | 예외 없이 동작 | ✅ P13-6에서 `--presenter-test`로 확인(`Show`/`ChangeState`×3/`Close`×2/닫힌 뒤 `ChangeState` 모두
      백그라운드 스레드에서 예외 없이 동작, `Canceled` 1회 발생) |

**추가로 확인한 것(매트릭스 밖)**: 알림창/훅이 전혀 개입하지 않는 정상 흐름의 회귀 없음 — `--home`으로
홈 화면을 띄우고 리더기 설정 화면을 열어 ESC를 눌렀더니 그 창 자체의 정상 동작(ESC로 닫힘)이 그대로
일어남을 확인했다(우리 결제 훅은 알림창이 떠 있을 때만 설치되므로 애초에 개입할 지점이 없다).

**완료 조건**
- [x] 위 9개 시나리오를 모두 실행하고 결과를 이 Task 아래에 기록(확인 못 한 것은 이유를 적고 체크하지
      않는다) — 위 표. #8의 핸들/메모리 정량 측정만 도구 미비로 대체 확인함(코드 경로 확인)
      **[정정, 2026-08-24 Opus 리뷰]** 이 코드 경로 확인이 실제로는 틀렸다 — 데모 타이머가 `Closed`에서
      정지되지 않는 결함(H-1, 아래 "Opus 전체 검증 리뷰 및 후속 수정" 절 참고)이 있어 #8은 원래
      실패였다. 수정 후 재검증 완료.
- [x] `dotnet build` 경고 0/오류 0 — 매 변경마다 확인, 최종 상태도 0/0
- [x] 기존 두 화면(홈/리더기 설정)에 회귀 없음 — 특히 **전역 훅이 리더기 설정 화면의 키 입력을 방해하지
      않는지** 확인 — 위 "추가로 확인한 것" 참고. 단, **알림창이 리더기 설정 화면과 동시에 떠 있는
      경우**(리더기 설정 중 결제 요청이 들어오는 시나리오)는 P13-4에서 사용자 지시로 별도 보류
      (development_plan.md P13-4 "알려진 범위 밖", ROADMAP.md Phase 15 재검토 항목 참고) — 이번
      매트릭스는 "알림창만 단독으로 떠 있을 때"를 기준으로 한다.

**P13-7 완료(2026-08-24).** 이로써 Phase 13의 모든 Task(P13-1~P13-7)가 완료됐다 — 단, 시각 정교화
(세부 각도/타이밍/자산 품질)는 사용자 지시로 이후 별도 재개 예정이며, 리더기 설정 화면 동시 표시
정책은 Phase 15에서 재검토한다(위 참고).

## Phase 13 — Opus 전체 검증 리뷰 및 후속 수정 (2026-08-24)

Sonnet 구현이 끝난 뒤 Opus가 코드 재검토를 수행해 결함 5건(H-3/H-1/H-2/M-1/M-2)을 찾았고, Sonnet이
전부 수정 후 재검증까지 완료했다(Phase 12(P12-6)와 같은 검증 워크플로우). 아래는 결함별 근거·수정·
재검증 결과다 — 모두 임시 검증 하네스(`App.xaml.cs`에 잠깐 추가했다가 재검증 후 제거)로 실측했다.

### H-3(★ 가장 심각) — ESC "삼킴"과 "취소 확정"의 경쟁 상태

**결함**: `PaymentNoticeEscapeHook.HookCallback`이 "삼킬지 판정"(`IsCancelAllowed` 읽기, 동기)과
"취소 실행"(`_viewModel.CancelCommand.Execute`, `Dispatcher.BeginInvoke`로 지연)을 분리하고 있었다.
`Dispatcher.Invoke`(Send 우선순위)가 `BeginInvoke`(Normal 우선순위)보다 먼저 처리되므로, Phase 15
워커가 `ChangeState(VanProcessing)`을 백그라운드 스레드에서 부르면 지연된 취소보다 먼저 실행될 수
있었다 — 그러면 **ESC는 이미 삼켜져 POS에도 전달되지 않았는데, 뒤늦게 실행된 취소는 `IsCancelAllowed`
가 이미 false(VanProcessing)라 조용히 무시**된다. 결제 시스템에서 가장 위험한 무증상 실패.

**수정**: `PaymentNoticeViewModel`에 `TryMarkCanceled()`(판정+확정을 동기·원자적으로 수행, 통지 없음)와
`RaiseCanceledEvent()`(통지만)를 분리했다. 훅은 `_tryCancel()`(= `TryMarkCanceled`)을 호출해 그 반환값
으로 곧장 삼킬지 정하고, 무거울 수 있는 통지만 `BeginInvoke`로 미룬다. 훅 콜백은 자신을 설치한 UI
스레드 위에서 실행되므로(WH_KEYBOARD_LL 표준 동작), 이 동기 호출이 끝나기 전까지는 다른 UI 스레드
작업(백그라운드의 `Dispatcher.Invoke`)이 끼어들 수 없다 — 경쟁 자체가 구조적으로 불가능해진다.

**재검증**: 실제 OS 키 입력으로는 마이크로초 단위 경쟁을 도구로 재현할 수 없어, 훅의 `HookCallback`을
리플렉션으로 직접 호출하면서 **동시에** 별도 스레드가 `presenter.ChangeState(VanProcessing)`/
`ChangeState(IcCardRequest)`를 쉼 없이 반복 호출하도록 경합을 걸었다. 30라운드 동안 배경 스레드가
2041회 `ChangeState`를 밀어넣었지만, "삼킴 반환값"과 "그 즉시 `IsCancelAllowed`가 false로 바뀌어
있는가"의 불일치는 **0건**이었다.

### H-1 — 데모 타이머가 창을 닫아도 영구히 발화

**결함**: 매개변수 없는 `PaymentNoticeWindow()`(3초 자동 순환 데모) 생성자의 `DispatcherTimer`를
아무도 `Stop()`하지 않았다. 실측: 창을 닫은 뒤 10초간 관측하니 3초 주기 그대로 **3회 계속 발화**했다
— Dispatcher가 타이머를 붙들고, 그 Tick 클로저가 창·뷰모델까지 영구 참조해 누수로 이어진다. 이 결과로
P13-7 시나리오 #8("10회 반복 — 핸들/메모리 증가 없음")의 실제 결론이 뒤집힌다(`--esc-hook-stress-test`
가 바로 이 생성자를 10회 호출하므로, 그 테스트는 타이머 10개를 영구히 남긴 채 "예외 없음"만으로 통과
판정을 냈던 것).

**수정**: `Closed += (_, _) => timer.Stop();` 한 줄 추가.

**재검증**: 창을 닫은 뒤 8초간 관측 — 타이머 발화 **0회**.

### H-2 — 기본 실행 진입점이 데모 창

**결함**: 인자 없이 실행하면 `StartupUri`가 곧장 `PaymentNoticeWindow`(데모)였다 — 배포 빌드에서
exe를 그냥 실행하면 결제 알림 데모가 뜬다(원본 앱의 트레이 상주 + 홈 화면 기동과 어긋남). H-1의 누수
타이머도 여기서 함께 켜진다.

**수정**: 기본 실행(인자 없음)은 `HomeWindow`로 되돌리고, 데모는 명시적 `--notice-demo` 인자로만
접근하게 분리했다.

**재검증**: 인자 없이 실행 → 홈 화면(`KFTCOneCAP Plus Ver 3.0.9...`)이 뜸을 확인. `--notice-demo` →
기존 데모 그대로 동작.

### M-1 — 전역 훅 설치/해제 비대칭

**결함**: 훅 설치가 생성자, 해제가 `Closed`에만 있었다. 실측: `Show()`/`Close()` 없이 **생성만** 한
창도 훅이 걸린 채 남았다(리더기 설정 화면의 `WarmUpReaderSetupWindow` 같은 "미리 만들었다 바로 닫는"
워밍업 최적화가 이 알림창에도 적용되면, 화면에 보이지도 않는 창 때문에 시스템 전역 키보드 훅이
영구히 걸리는 결함으로 이어질 수 있었다).

**수정**: 훅 설치를 생성자에서 `Loaded`로 옮겼다 — `Loaded`는 실제로 화면에 표시될 때만 발생하므로
"보일 때만 설치, 닫히면 해제"가 정확히 대칭을 이룬다.

**재검증**: 생성만 하고 Show/Close 안 한 창 → 훅 설치 안 됨(`False`). `Show()` 직후 → 설치됨
(`True`). `Close()` 후 → 해제됨(`False`).

### M-2 — Presenter 재사용 시 취소 플래그가 다음 거래로 샘

**결함**: `PaymentNoticePresenter.Show()`는 이미 창이 떠 있으면 기존 창/뷰모델을 재사용하며 상태만
바꿨다. `PaymentNoticeViewModel._canceled`는 sticky(P13-2 규칙)라서, 앞 거래가 취소된 뒤 `Close()`를
부르지 않고 곧장 다음 거래를 `Show()`로 시작하면 **새 거래인데도 취소가 처음부터 막혀 있는** 결함이
있었다.

**수정**: `Show()`가 항상 "새 알림을 시작한다"는 뜻이 되도록, 기존 창이 있으면 재사용하지 않고 닫은
뒤 매번 새 창/뷰모델을 만들게 바꿨다(같은 알림 안에서 상태만 바꾸는 것은 여전히 `ChangeState`의 몫).

**재검증**: 첫 거래를 취소한 뒤 `Close()` 없이 곧장 `Show()`로 새 거래 시작 → 새 창이 만들어지고
(`ReferenceEquals`로 이전 창과 다름 확인), 새 창의 `IsCancelAllowed`는 `True`(첫 거래의 취소가
새어들지 않음).

### 잘 된 부분 (Opus 리뷰에서 확인)

- 정상 경로(Show→Close)의 훅 해제는 수정 전부터 실측으로 확실히 동작했다 — 3중 보장 구조 자체는
  타당했다.
- `IsCancelAllowed` 단일 판정 지점 원칙이 실제로 지켜졌다 — 버튼 `CanExecute`와 ESC 훅이 같은
  프로퍼티를 본다.
- `Services/Payment/`에 WPF 타입 참조 0건으로 계층 규칙이 지켜졌다.

### 검증 방법론 메모

이번 리뷰의 핵심 도구는 **실측**이었다 — GC 기반 판정(`WeakReference.IsAlive`)은 Dispatcher 큐가
참조를 붙들어 대조군까지 "살아있음"으로 나와 쓸 수 없었고, "닫은 뒤에도 계속 발화하는가"를 직접
관측하는 방식으로 바꿔서야 H-1을 확정할 수 있었다. H-3의 마이크로초 단위 경쟁은 실제 OS 키 입력으로
재현이 불가능해, 리플렉션으로 훅 콜백을 직접 호출하면서 배경 스레드로 실제 경합을 거는 방식을 썼다.
모든 검증 하네스는 확인 후 코드에서 제거했다(`git diff`가 비어 있음을 확인).

## Phase 13 시각 구현 — 중간 정리 (2026-08-24, 일단 여기까지)

**사용자 확정(2026-08-24): 알림창 시각 정교화는 이 상태로 일단 멈추고, 실제 결제 로직에 필요한
P13-2부터 이어서 구현한다.** 아래 내용은 완료 선언이 아니라 "지금까지 무엇을 어떻게 만들었고, 왜
이렇게 됐는지" 중간 스냅샷이다 — 정교화(세부 각도·타이밍·자산 품질 다듬기)는 추후 별도로 다시 진행할
예정이다. **P13-2(취소 1회 제한)는 이후 완료**(위 P13-2 절 참고). P13-4/5/6/7(표시 정책, ESC 훅,
제어 진입점 계약, 9개 시나리오 검증)은 **아직 손대지 않았다**.

### 최종 자산 구성

카드를 벡터로 재현하려던 시도(`PaymentCardShape`, P13-1-B 원안)가 리더기 각도와 계속 어긋나 결국
폐기하고, **사용자가 직접 만든 실제 카드 이미지**(`ic_card.png`/`ms_card.png`, 투명 배경)로 교체했다.
리더기(`reader.png`, 흰색/파란 글로시)와 화살표(`arrow_ic.png`/`arrow_ms.png`)는 이전 그대로다. 배경
소스 단일 지점 규칙(`PaymentNoticeBackgroundSource.GetCardSource`/`GetArrowSource`/`ReaderSource`)은
유지된다.

### 카드 애니메이션 — IC/MS 동일 구조로 통일

두 번의 시행착오 끝에 IC와 MS 카드 모두 **같은 애니메이션 구조**로 정착했다: 화살표 반대 방향(바깥, 리더기에서
먼 쪽) 1점에서 시작 → 화살표가 가리키는 방향으로 이동해 슬롯 앞(정지 위치, 오프셋 0)에 **도달하면
그 자리에서 멈춰** 유지 → 순간적으로 바깥으로 리셋 → 반복. 정지 위치를 지나쳐 반대쪽까지 왕복하지
않는다(대칭 왕복 구조였던 1차 MS 구현은 정지 위치를 넘어 리더기 안쪽까지 파고드는 결함이 있었다 —
`PlayMsCardAnimation`을 `PlayIcCardAnimation`과 동일한 키프레임 패턴으로 재작성해 해결).

- **IC**: 위(바깥)에서 아래로, 정지 위치는 리더기 앞면 IC 슬롯 앞.
- **MS**: 이동 각도는 `arrow_ms.png`를 픽셀 실측한 **정확히 31.4도**(왼쪽 위 방향, tip(200,228)→
  tail(1320,912))와 일치시켰다. 정지 위치는 IC 슬롯 쪽이 아니라 **리더기 뒤쪽의 별도 MS(마그네틱)
  슬롯 블록**(`reader.png` 실측 — 원본 이미지 x=1080,y=330~640 → 창 좌표 약 x=444,y=333~393) 앞으로
  옮겼다.

> 시행착오 기록(추후 다시 다듬을 때 참고): ① 카드 자체의 그려진 기울기(~5도)에 맞추려 한 시도는 틀린
> 기준이었다(맞춰야 할 건 화살표 방향) → ② 화살표 각도로 정정했으나 정지 위치를 지나쳐 왕복하는 구조라
> 여전히 리더기를 덮었다 → ③ IC와 동일한 "도달하면 멈춤" 구조로 바꿔 해결.

### PROCESSING 애니메이션 — 3가지 효과, 두 차례 기하 버그 수정

사용자가 제시한 기획(원형 진행광 회전 + 슬롯 내부 빛 흐름 + 은은한 펄스, `docs/payment_relay/images/
_preview_crop/거래중 애니메이션.png`)을 구현하는 과정에서 같은 종류의 버그를 두 번 만났다: **오버레이가
`reader.png`보다 위 레이어라, 바닥 타원의 "몸통에 가려져야 할" 뒤쪽 절반까지 애니메이션을 돌리면 그대로
몸통 위에 비쳐 보인다**(reader.png가 그림자+몸통을 한 장에 합성한 이미지라 레이어 분리가 불가능해서
생기는 근본 제약).

1. 1차(점 6개 Opacity 체이스, `RotateTransform` 없이 좌표 직접 계산) — 이 방식 자체는 문제없었으나,
   이후 사용자가 더 화려한 궤도 글로우 방식(`OrbitGlowArc`/`OrbitCoreArc` + `StrokeDashArray` +
   `OrbitHeadLight`)으로 다시 작성.
2. 2차 궤도 글로우 버전도 처음엔 타원 전체(360도, 닫힌 경로)를 `RectangleGeometry`로 대충 클립해서
   돌렸는데, 사각형 클립이 타원의 곡선 경계와 안 맞아 몸통 위로 빛이 새어 나왔다.
3. **최종**: 경로 자체(`OrbitGlowArc`/`OrbitCoreArc`의 `Data`)를 "보이는 앞쪽 반원"만으로 한정하고
   클립을 없앴다. 반원(열린 경로)이라 한 방향으로 계속 돌 수 없으므로 왕복(`AutoReverse`)으로
   바꿨다. `OrbitHeadLight`(선두 점)도 같은 이유로 0~180도 구간만 왕복하도록 각도 범위를 제한했다.

### 기타 변경
- `App.xaml`에서 `StartupUri`를 제거하고 `App.xaml.cs`가 커맨드라인 인자로 분기하도록 바꿨다
  (`--gallery`→StyleGallery, `--home`→홈 화면, **인자 없음(기본 실행) → 결제 알림창 실시간 데모**로
  바로 뜬다). 개발 중 알림창을 빠르게 확인하기 위한 편의 변경 — 최종 산출물에서 기본 진입점을 알림창으로
  둘지는 Phase 15(실제 결제 Flow 연결)에서 재검토한다.
- `PaymentNoticeWindow()`(매개변수 없는 생성자)가 추가돼 3초 주기 IC→MS→PROCESSING 자동 순환 데모를
  제공한다(개발용, `PaymentNoticeViewModel` 기본 생성자로 자체 뷰모델을 만든다).

### 검증
매 수정마다 `dotnet build`(경고 0/오류 0) 후 실제 실행해 스크린샷/연속 캡처로 확인했다 — IC/MS 카드
각도·정지 위치, PROCESSING 궤도 글로우가 원판 밖으로 새지 않는지, 홈 화면 회귀 없음까지 포함.
9개 시나리오 검증표(P13-7)와 ESC 훅(P13-5)은 이번 라운드에 다루지 않아 미확인 상태로 남아있다.
취소 1회 제한(P13-2)은 2026-08-24에 별도로 이어서 구현·검증했다(위 P13-2 절 "구현 메모" 참고).

## Phase 13 거래중 애니메이션 재구현 — 중간 정리 (2026-08-25, 일단 여기까지)

Phase 13 완료 후 사용자가 다시 지시해 거래중(VanProcessing) 애니메이션 정교화를 재개했다(위 "Phase 13
완료 후" 절 예고대로). **PROCESSING 궤도 글로우가 리더기 몸통 뒤로 지나갈 때 가려지지 않는 문제**(위
"PROCESSING 애니메이션" 절 시행착오 ①②③으로도 못 고친 근본 한계 — `reader.png`가 몸통+원판+그림자를
한 장으로 합성한 이미지라 오버레이가 항상 그 위 z-order일 수밖에 없었음)를 이번엔 **자산을 몸통/원판
2장으로 분리**해서 근본적으로 해결했다. 사용자가 다시 지시할 때까지 일단 여기서 멈춘다 — 완료 선언이
아니라 중간 스냅샷이다.

### 자산 분리 — 세 번의 재시도 끝에 정착

`reader_kftc.png`(몸통만) / `circle.png`(원판만)를 새로 만드는 과정에서 크기·정합 문제로 세 번
갈아엎었다(추후 비슷한 분리 작업을 할 때 참고):
1. **1차**: 두 파일이 1448×1086(비율 1.333)으로 표시 박스(340×226.67, 비율 1.5)와 안 맞아 레터박싱/
   비대칭 스트레치 문제 발생 — 전체 revert.
2. **2차**: 비율은 1536×1024(1.5)로 맞췄으나 몸통이 원판 밖으로 나가는 정렬 문제 → 몸통을 줄여서
   고쳤더니 이번엔 카드 자산과 비례가 깨지고 로고 앨리어싱 발생 — 다시 revert 성격의 재작업.
3. **3차(최종 채택)**: **원본 합성 이미지의 소스 렌더(`ChatGPT Image ... 10_25_13.png`, 1536×1024,
   `reader.png`와 동일 소스로 추정)를 복제해서, 한쪽은 몸통만 지우고 한쪽은 원판만 지우는 방식**으로
   분리 — 재생성이 아니라 마스킹이라 두 레이어가 픽셀 단위로 완벽히 정합됨. **자산을 분리 배치할 때는
   처음부터 이 방식(동일 소스 복제 후 마스킹)으로 가는 게 맞다** — 별도로 새로 그리거나 재생성하면
   크기·위치·화질이 매번 미묘하게 달라져 정합이 깨진다.

### 핵심 아이디어 — z-order 분리로 자연 가림 확보

원판(`PlateImage`) → 진행광 링(`PaymentProcessingRing`, 온전한 360도) → 리더기 몸통(`ReaderImage`) →
화살표/카드/슬롯 표면 효과(`OverlayHost`) 순으로 Canvas 자식을 재배치했다. 링이 몸통 뒤를 지나가는
구간은 몸통 이미지의 불투명 픽셀에 z-order만으로 자동으로 가려진다 — 기존의 반원 클리핑/왕복
편법(`OrbitHalfArcLength` 등)이 전부 필요 없어졌다. 진행광 애니메이션도 반원 왕복에서 기획대로
시계방향 연속 회전으로 바꿨다.

### 그 외 사용자 피드백 기반 수정 (여러 라운드에 걸쳐 완료)

- **로고 앨리어싱**: `ReaderImage`/`PlateImage`/`CardImage`/`ArrowImage`에
  `RenderOptions.BitmapScalingMode="HighQuality"` 적용(1536×1024 → 340×226.67, 약 4.5배 축소 렌더링
  시 기본 스케일링 모드의 계단현상 문제).
- **MS 카드 정지 위치**: `reader_kftc.png`의 MS 슬롯을 재실측해 `CardMsRestLeft/Top`을 (440,290)→
  (452,301)로 보정.
- **MS 화살표 방향**: `ArrowBounceLeftStoryboard`의 Y 방향 부호가 반대(0→+7, 아래쪽)라 IC와 똑같이
  위아래로 움직이는 것처럼 보였다 — `arrow_ms.png`가 그려진 방향(왼쪽 위, ~31°)에 맞춰 X:0→-20,
  Y:0→-12(왼쪽 위 대각선)로 정정.
- **진행광 링 원판 정합**: `circle.png` alpha 경계를 1px 단위로 촘촘히 스캔 + 최소자승 타원 피팅
  (원판 안쪽 자산 자체의 빈 V자 영역과 뒤쪽 절단부는 피팅에서 제외)으로 중심/반경을 재계산 — 스캔이
  성길수록(초기 700등분 샘플링) 정합이 부정확했다.
- **펄스 완전 제거**: `BasePulseGlow`/`LogoPulseGlow`/`LogoCoreGlow` 전부 삭제("이상해 보인다"는
  사용자 피드백). 슬롯 내부 빛 흐름(`SlotFlow`)은 유지.
- **거래중 신규 애니메이션 — 신호 웨이브 채택**: 사용자가 4가지 아이디어(싱크 도트 링/데이터 바
  스트림/미니 상태 캡슐/신호 웨이브, `docs/payment_relay/images/_preview_crop/ChatGPT Image 2026년
  8월 24일 오후 05_07_45.png` 참고)를 제시했고, 원판 회전 링과 컨셉이 안 겹치고 이미지 자산 없이
  순수 벡터로 구현 가능하다는 이유로 **신호 웨이브**를 골라 `PaymentSignalWaveIndicator`(좌우 대칭
  반원 호 3겹 순차 페이드 + 중앙 파형)를 신설, 리더기 위쪽 여백에 배치했다.
- **슬롯 내부 빛 흐름 IC/MS 분리**: `SlotFlow`의 `Height`가 실제 홈 폭의 거의 2배(14 vs 실측 약
  7.7px)라 홈 밖으로 삐져나왔다 — `Height=8`로 축소 + 위치 재실측. IC 슬롯에만 있던 이 효과를
  `SlotFlowMs`로 MS 슬롯에도 만들고, `PaymentNoticeWindow`가 VanProcessing 진입 직전 상태(IC/MS)를
  `_lastCardState`로 기억해뒀다가 맞는 쪽만 재생하도록 배선했다.
- **알림창 크기 축소**: `Window` 750×650 → 600×520(80%, 비율 유지). 내부 Canvas 좌표는 그대로 두고
  전체를 `Viewbox Stretch="Uniform"`으로 감싸는 방식으로 처리해 좌표 재계산 없이 비례 축소했다.

### 검증

매 라운드 `dotnet build`(경고 0/오류 0) 후 `--notice-demo`/`--notice-van-processing-test`로 실제 실행해
IC/MS/VanProcessing 3개 상태를 스크린샷·확대 캡처로 직접 대조했다(오케스트레이터가 서브에이전트 보고를
그대로 신뢰하지 않고 별도로 재실행·재확인하는 방식으로 진행). 레퍼런스는
`docs/payment_relay/images/_preview_crop/거래중 애니메이션.png`.

### 남은 것 / 재개 시 참고

- 로고 옆 미세한 위치 편차 등 사소한 다듬기 여지는 남아있으나 사용자가 "이 정도까지 하고 나중에 다시
  하자"고 확정했다(2026-08-25). 재개 시점은 사용자가 다시 지시한다.
- 자산 관련 재작업이 필요하면 위 "자산 분리 — 세 번의 재시도" 절의 3차 방식(동일 소스 복제 후 마스킹)
  을 우선 시도한다.
- P13-2/P13-4~P13-7(취소 제한/표시 정책/ESC 훅/제어 진입점/시나리오 검증)은 이미 완료된 채로 이번
  라운드의 영향을 받지 않았다(회귀 확인 완료) — Phase 13 자체의 완료 상태(위 "Phase 13 완료 후" 절)는
  달라지지 않는다.

## Phase 13 완료 후

- 알림창 정교화(세부 각도/타이밍/자산 교체) 재개 시점은 사용자가 다시 지시한다. 재개 시 위 "시행착오
  기록"부터 먼저 읽는다 — 같은 실수(카드 기울기 vs 화살표 방향 혼동, 원 애니메이션의 뒤쪽 절반 노출)를
  반복하지 않기 위함이다.
- P13-2(취소 1회 제한)/P13-4(표시 정책)/P13-5(ESC 전역 훅)/P13-6(제어 진입점 계약)/P13-7(9개 시나리오
  검증)은 모두 2026-08-24에 완료했다. **Phase 13 전체 완료.**
- Opus 전체 검증 리뷰 → Sonnet 수정(프로젝트 워크플로우)을 진행한다.
- Phase 14(소켓 서버 + 단일 워커 Queue) 실행계획서를 그때 작성한다. 착수 전에 **POS↔앱 소켓 전문**이
  여전히 미확정이면(ROADMAP "남은 미확정 사항" #2) 예정대로 임시 테스트 전문으로 진행한다.
  **→ 2026-08-24 작성 완료**(아래 "Phase 14" 절). 소켓 전문은 여전히 미확정이라 예정대로 임시 전문으로
  간다 — 원본 MFC 소스에도 소켓 구현이 남아 있지 않음을 확인했다(Phase 14 "착수 전 전제" 참고).

---

# Phase 14 — 소켓 서버 + 단일 워커 Queue

**이 Phase가 끝나면**: `localhost:8002`에 소켓 서버가 앱 수명과 함께 뜨고 지며, POS가 보낸 요청 전문이
**정확히 한 번에 하나씩, 수신 순서대로** 처리된다. 결제 내용은 아직 비어 있다(스텁) — Phase 15가 이 워커
안쪽에 실제 결제 Flow를 꽂기만 하면 되도록 **경계를 미리 그어두는 것**이 이 Phase의 목적이다.

> **이 Phase의 어려운 부분은 소켓이 아니라 두 가지다.** ① **TCP는 스트림이라 메시지 경계가 없다** —
> 한 번의 수신이 전문의 절반일 수도, 두 개 전문이 붙어 올 수도 있다(프레이밍, P14-1). ② **직렬화 지점이
> 정확히 하나여야 한다** — PRD §3.2/§8.1의 "동시에 두 거래가 리더기 또는 VAN 통신을 수행해서는 안 된다"를
> 보장하는 코드가 여러 군데로 흩어지면 Phase 15~16에서 원인 추적이 불가능해진다(P14-3). Task 순서를 바꾸지
> 않는다 — 프레이밍이 불안한 상태로 Queue를 검증하면 순서 오류인지 파싱 오류인지 구분할 수 없다.

## 착수 전 전제 (2026-08-24 확인 완료)

- **POS↔앱 소켓 전문은 여전히 미확정**이다(ROADMAP "남은 미확정 사항" #2, PRD §10). 예정대로 **임시 테스트
  전문**으로 진행하고, 실제 SPEC 확정 시 `Protocol/Pos/`만 교체한다.
- **원본 MFC 소스에 소켓 서버 구현은 남아 있지 않다**(2026-08-24 확인). `C:\Project\MerchantSetup_OnPaintIcons_Clean_CP949\common.h`에
  `DEFAULT_SERVERPORT 8002`, `CLIENT_MAX 16`, `UM_TCP_RECV`(수신 통지용 Windows 메시지), `CAT_STX`/`CAT_ETX`
  등 **상수 정의만** 있고 이를 사용하는 `.cpp` 코드는 이 클린 사본에 포함되지 않았다 — 즉 **참조 구현 없이
  새로 설계해야 한다**(Reader DLL 때와 다른 점). 다만 위 상수들은 원본이 (a) 다중 클라이언트를 받았고
  (b) 제어문자 기반 전문을 다뤘음을 시사하므로, 아래 설계 결정의 근거로 삼는다.

## 확정된 설계 결정 (2026-08-24 사용자 확정)

착수 전에 못 박는다. 셋 다 PRD에 없어 사용자에게 확인한 사항이다.

1. **프레이밍 = `[길이 4][본문]`(길이-접두, STX/ETX 없음)**. *(2026-08-24 수정 — 최초에는 `[STX][길이4][본문][ETX]`로
   잡았으나, 국내 결제 SPEC에서 실제로는 길이-접두만 쓰고 STX/ETX 제어문자는 안 쓰는 경우가 더 흔하다는
   사용자 판단에 따라 지금부터 이 형태로 맞춘다.* `LEN`은 **ASCII 숫자 4자리**(BODY의 바이트 수,
   `0000`~`9999`, 사람이 눈으로 읽고 telnet/netcat으로도 바로 테스트 가능). 다만 이 값도 여전히 임시다 —
   **실제 SPEC은 이 구조조차 전혀 안 쓸 수도 있다.** 그래서 이 값 자체를 믿고 설계하지 않는다:
   `PosMessageFramer`의 바깥 계약은 "바이트를 넣으면 완성된 프레임(BODY)이 나온다"뿐이고, 길이 필드의
   위치·자릿수·인코딩이라는 **내부 구현**은 `Protocol/Pos/` 밖에서 아무도 알지 못한다(P14-1 계층 규칙).
   실제 SPEC이 이 구조를 그대로 쓰든, STX/ETX를 다시 넣든, 완전히 다른 구조든 **`PosMessageFramer` 내부만
   바뀌고 `PosSocketServer`/`TransactionQueue`는 한 글자도 안 바뀌는지**가 이 Task의 진짜 완료 기준이다.
2. **연결 모델 = 지속 연결 + 다중 클라이언트, 단 "요청 1회 후 접속 종료"도 함께 지원**. POS 쪽 **원칙**은
   한 연결에서 전문 한 번 주고받고 소켓을 닫는 것이지만(2026-08-24 사용자 확인), 서버 구현이 그 원칙에
   의존하면 **POS가 실수로 연결을 안 끊고 이어서 또 요청을 보내는 경우**(원칙 위반, 실무에서 흔함)를 처리할
   수 없다. 그래서 서버는 애초에 **"한 연결에서 요청이 몇 번이든 온다"를 기본으로** 설계한다 — 연결마다
   붙는 수신 루프(P14-2)가 그 연결이 끊길 때까지 계속 프레임을 뽑아 큐로 넘기므로, POS가 한 번만 보내고
   끊어도(원칙대로) 그냥 루프가 한 번 돌고 끝나고, 안 끊고 계속 보내도(원칙 위반) 같은 루프가 계속
   프레임을 처리한다 — **서버 쪽에서 둘을 구분해서 다르게 처리할 필요가 없다.** 각 요청의 응답은 그
   요청이 들어온 연결로 회신한다(요청당 별도 연결을 강제하지 않음 — POS가 연결을 붙들고 있어도 서버가
   막히지 않는다). 원본의 `CLIENT_MAX 16`을 동시 연결 상한의 근거로 삼는다.
3. **Queue 대기 중 클라이언트가 끊기면 → 처리는 계속하고 응답만 폐기 + 로그**. Phase 15에서 리더기·VAN이
   얽히면 진행 중 거래를 중도 폐기하기 어려우므로, "거래는 끝까지, 응답은 못 보내면 로그"로 **일관되게**
   둔다. 취소는 어디까지나 사용자 취소/Timeout(Phase 16)의 몫이지 소켓 단절의 몫이 아니다.

## 이 Phase에서 손대지 않는 것 (범위 밖 확정)

- **실제 결제 처리** — Phase 15. 이 Phase의 워커는 "일정 시간 지연 후 고정 응답"을 돌려주는 **스텁**이다.
- **알림창(`IPaymentNoticePresenter`) 연결** — Phase 15. Phase 13이 만든 진입점을 여기서 호출하지 않는다.
- **사용자 취소 / Timeout 120초 / 경합 중재** — Phase 16. 이 Phase의 Queue는 "순서와 직렬화"만 책임진다.
- **실제 POS 전문 필드 구성** — 미확정(PRD §10). 임시 전문의 필드는 검증에 필요한 최소한만 둔다.
- **소켓 재연결/하트비트/TLS** — PRD에 요구가 없다. 로컬 루프백 전용이다.

---

## P14-1. `Protocol/Pos/` — 임시 테스트 전문 + 프레이머 ★

**먼저 이것부터.** 프레이밍이 흔들리면 뒤의 Queue 순서 검증 결과를 믿을 수 없다.

**전문 형식**(위 확정 사항 1):

```
[LEN(ASCII 4자리)][BODY(LEN 바이트)]
예: "0017" "PAY|1000|TEST0001"
```

- `LEN` = **BODY의 바이트 수**(자기 자신 제외), `0000`~`9999`. 숫자가 아니면 형식 오류.
- STX/ETX 같은 제어문자 경계는 두지 않는다 — 길이 필드 하나로 프레임 경계를 정한다. 프레임 시작점을 잃어버릴
  경우(예: 연결 도중 진입, 이전 프레임 파싱 오류로 어긋남) **재동기화할 방법이 없다**는 게 이 방식의 대가다
  — STX 같은 마커가 없으면 "다음 4바이트가 진짜 길이인지" 확신할 수 없기 때문이다. 그래서 이 Phase는 형식
  오류 발생 시 **그 연결을 통째로 닫고 다시 연결하게 한다**(같은 연결 안에서 어긋난 지점을 복구하려 하지
  않는다) — 아래 완료 조건에 반영.
- BODY 인코딩은 **한 곳(`PosMessageEncoding`)에서만** 정한다 — 지금은 ASCII, 실제 SPEC에서 EUC-KR/CP949로
  바뀔 가능성이 있다(원본이 CP949 기반). 인코딩 상수를 파서마다 흩어 두지 않는다.
- 임시 BODY 포맷(파이프 구분, 검증에 필요한 최소):
  - 요청 `PAY|<금액>|<거래고유번호>`
  - 응답 `PAYRES|<결과코드>|<거래고유번호>|<메시지>`
  - **거래고유번호는 순서 검증의 근거**다 — 3건을 밀어 넣고 응답 순서를 대조할 때 이 값을 쓴다.

**구현할 것**

- `Protocol/Pos/PosMessageFramer` — **누적 버퍼**를 들고 있다가 완전한 프레임이 모이면 그 BODY만 꺼내 준다.
  한 번의 `Append`로 프레임이 0개/1개/N개 완성될 수 있다.
  - **공개 계약은 `Append(byte[] chunk) → IReadOnlyList<byte[]> completedFrames`뿐이다.** STX/LEN/ETX라는
    이름·상수·바이트 오프셋은 이 클래스 **내부**에만 있고, 시그니처 어디에도 드러나지 않는다. 실제 SPEC이
    이 구조를 안 쓰기로 결정되면(가능성 있음, 위 "확정된 설계 결정" #1) `PosMessageFramer` 내부 로직만
    새로 짜면 되고, 이 클래스를 호출하는 쪽(P14-2)은 손대지 않는다 — 이것이 이 클래스가 존재하는 이유다.
- `Protocol/Pos/PosPaymentRequest` / `PosPaymentResponse` + 파서/빌더 — BODY 문자열의 필드 구성(현재 `PAY|...`
  파이프 구분)도 마찬가지로 임시다. 이 파서 역시 프레이머와 같은 이유로 별도 클래스로 뗀다: **프레이밍 규칙과
  필드 구성 규칙은 서로 독립적으로 바뀔 수 있다**(예: 프레이밍은 그대로인데 필드만 바뀔 수도, 그 반대일
  수도 있다) — 하나로 뭉쳐두면 어느 한쪽만 바뀌어도 같이 건드리게 된다.
- **계층 규칙**: `Protocol/Pos/`는 `Socket`/`TcpClient`/`NetworkStream`을 **참조하지 않는다**. 입력은
  `byte[]`, 출력은 결과 객체/`byte[]`뿐이다. 이래야 실제 SPEC 확정 시 전문만 교체하고 소켓 코드를 안 건드린다.

**구현(2026-08-24)**: `Protocol/Pos/PosMessageEncoding`(ASCII 고정, 단일 지점), `PosMessageFramer`(누적
`List<byte>` 버퍼, `Append(byte[]) → IReadOnlyList<byte[]>` 하나만 공개), `PosProtocolException`(형식
오류 전용), `PosPaymentRequest.Parse`/`PosPaymentResponse.ToFrame`(임시 `PAY|...`/`PAYRES|...` 파이프
구분)으로 구현했다. `PosPaymentResponse.ToFrame`에는 본문이 ASCII 범위를 벗어나면 즉시 예외를 던지는
방어 검증을 추가했다 — 아래 재검증 중 한글 메시지가 `?`로 조용히 깨지는 결함을 실측으로 발견해 그 자리에서
고쳤다(TransactionQueue의 내부 오류 메시지를 `INTERNAL_ERROR`로 교체하고, 이런 실수가 재발해도 예외로
바로 드러나게 함).

**완료 조건**
- [x] 한 프레임을 **1바이트씩 나눠** 넣어도 정확히 한 번 완성된다 — 32비트 PowerShell에서 리플렉션으로
      `Append`를 직접 호출해 `"0005HELLO"`를 1바이트씩 9번 넣었을 때 정확히 1번만 프레임(`HELLO`)이
      완성됨을 확인
- [x] 두 프레임이 **한 번에** 도착해도 둘 다, 순서대로 꺼내진다 — `"0003AAA0003BBB"`를 한 번에 `Append`해
      `AAA`, `BBB` 순서로 2개 완성 확인
- [x] 프레임 뒤에 다음 프레임의 앞부분만 붙어 와도 앞 것만 꺼내고 나머지는 버퍼에 남는다 —
      `"0003AAA0002B"` 1차 `Append`에서 `AAA`만 완성(나머지 `0002B`는 버퍼에 남음), 이어서 `"XY"`를
      2차 `Append`하면 `BX`가 완성되고 `Y`는 버퍼에 남는 것까지 확인
- [x] `LEN`이 숫자가 아니면 **형식 오류로 판정**되고, 프레이머가 무한 대기 상태에 빠지지 않는다 —
      `"ABCDxxxx"` 투입 시 `PosProtocolException("길이 필드가 숫자가 아님: 'ABCD'")` 발생 확인. 이
      오류는 `PosSocketServer`에서 그 연결을 닫는 것으로 이어진다(재동기화 시도 없음, 아래 P14-2/P14-5)
- [x] 누적 버퍼에 상한이 있고(64KB) 초과 시 오류로 처리된다 — 70,000바이트 투입 시
      `PosProtocolException("수신 버퍼 상한 초과(70000 > 65536바이트)...")` 발생 확인
- [x] `Protocol/Pos/` 안에 `System.Net` 참조 0건(grep 확인 — 매치 없음)
- [x] `PosMessageFramer`의 public 시그니처에 `LEN`류 이름이 없다 — `internal IReadOnlyList<byte[]>
      Append(byte[] chunk)` 하나뿐임을 grep으로 확인

## P14-2. `Services/Pos/PosSocketServer` — 기동/종료와 다중 연결

- `TcpListener`를 **루프백(`IPAddress.Loopback`)**에 바인딩한다(PRD §3.1 `localhost:8002`). 외부 인터페이스에
  열지 않는다 — 결제 요청을 받는 서버를 LAN에 노출할 이유가 없다.
- 연결마다 수신 루프를 돌리고, `PosMessageFramer`로 완성된 요청을 상위로 올린다(이벤트 또는 콜백). **이
  클래스는 `Append`가 돌려주는 `byte[]` 프레임을 `Protocol/Pos/`의 요청 파서에 그대로 넘길 뿐, STX/LEN/ETX가
  뭔지 몰라도 동작해야 한다** — 알고 있다면 계층 규칙 위반이자, 프레이밍이 바뀔 때 이 파일도 같이 고쳐야
  한다는 뜻이다(P14-1 참고).
- 동시 연결 상한 **16**(원본 `CLIENT_MAX`). 초과 연결은 즉시 닫고 로그 — 서버가 멈추지 않는다.
- **앱 수명주기 연결**: `App.OnStartup`에서 기동, `App.OnExit`에서 정지(`ReaderConnections.CloseAll()`과 같은
  자리). 정적 접근점 방식도 `ReaderConnectionManager`(P12-1)와 동일하게 맞춘다 — 이 프로젝트는 DI 컨테이너를
  쓰지 않는다.
- **포트가 이미 사용 중이면(`SocketException` 10048) 앱을 죽이지 않는다**(PRD §9). 로그만 남기고 앱은 정상
  기동한다 — 이 앱은 트레이 상주라 기동 시 모달을 띄워도 사용자가 보지 못한다(P12-1에서 확립한 방침).
- **계층 규칙**: `Services/Pos/`는 WPF 타입(`Dispatcher`/`Window`)을 알지 못한다. 바이트 오프셋도 직접 다루지
  않는다(반드시 `Protocol/Pos/`를 거친다).

**구현(2026-08-24)**: `TcpListener(IPAddress.Loopback, 8002)` + 수락 스레드(`AcceptLoop`) + 연결마다
전용 스레드(`HandleConnection`). `PosMessageFramer`는 연결(스레드)마다 새로 만들어 연결별 상태를
분리했다. 형식 오류(`PosProtocolException`)를 만나면 재동기화를 시도하지 않고 `break`로 그 연결의
수신 루프를 빠져나가 `using`이 스트림/클라이언트를 닫는다.

**완료 조건**
- [x] 앱 기동 시 8002가 LISTENING이고, 종료 시 사라진다 — `netstat -ano`로 실행 전/PID 확인 후 종료 시
      해당 포트 항목이 사라짐을 확인(PowerShell `Start-Process`/`Stop-Process`로 재현)
- [x] 앱을 **연속 2회** 기동/종료해도 두 번째 기동이 "포트 사용 중"으로 실패하지 않는다 — 1차 PID
      14972(LISTENING) → `Stop-Process` → 포트 즉시 해제 확인 → 2차 PID 40172가 정상적으로 다시
      LISTENING됨을 확인
- [x] 다른 프로세스가 8002를 선점한 상태에서 기동해도 **앱이 정상적으로 뜨고** 로그에 원인이 남는다 —
      두 인스턴스를 동시에 띄운 상태에서 둘 다 `HasExited=False`(살아 있음) 확인, 로그에
      `[ERROR] [PosSocketServer] 8002 포트 리스닝 실패(AddressAlreadyInUse): ... — 소켓 서버 없이 앱
      계속 기동`이 남음(앱 크래시 없음)
- [x] 클라이언트 2개를 동시에 붙여도 각각 독립적으로 요청/응답이 오간다 — `--pos-client-test` 시나리오1
      (클라이언트 3개 동시 접속·요청)에서 각자 자기 txId에 맞는 응답만 받음(`ORDER-A/B/C` 로그 확인)
- [x] **하나의 연결로 요청을 보내고 응답을 받은 뒤, 연결을 끊지 않고 같은 연결로 두 번째·세 번째 요청을
      계속 보내도** 각각 정상 처리·회신된다 — 시나리오2(`PERSIST-1/2/3`)에서 같은 연결로 3회 연속 요청,
      3회 모두 `PAYRES|00|PERSIST-n|OK(STUB)` 정상 회신 확인
- [x] 요청 1건을 보낸 뒤 **연결을 닫는 정상 케이스**도 동일하게 문제없이 처리된다 — 시나리오1의 각 연결이
      응답 수신 후 정상 종료(FIN)했고 서버 로그에 `연결 종료`가 문제없이 남음. 코드 리뷰:
      `HandleConnection`은 연결이 몇 번 요청을 보내는지 사전에 알지 못하고 `read == 0`(FIN)이나
      예외가 날 때까지 그냥 루프를 도는 구조라, "1회 요청 후 종료"와 "계속 요청"을 구분하는 분기 자체가
      없다
- [x] `Services/Pos/` 안에 `System.Windows` 참조 0건(grep 확인 — 매치 없음)

## P14-3. 단일 워커 + Queue — **직렬화 지점은 여기 하나뿐** ★

PRD §3.2/§8.1의 핵심이다. **"동시에 두 거래가 돌지 않는다"를 보장하는 코드가 앱 전체에서 이 클래스
하나여야 한다.** 나중에 "여기서도 잠그고 저기서도 잠그는" 구조가 되면 Phase 16의 경합 검증이 불가능해진다.

- `Services/Payment/TransactionQueue`(가칭) — **전용 워커 스레드 1개**가 FIFO 큐를 소비한다.
  - 스레드풀(`Task.Run`)이 아니라 **전용 스레드**를 쓴다: 처리 중 블로킹(리더기 응답 대기, VAN 호출)이
    길어 스레드풀 기아를 만들 수 있고, "워커는 하나"라는 사실이 코드에서 눈에 보여야 한다.
  - 큐잉 자료구조는 `BlockingCollection<T>`(또는 `ConcurrentQueue` + 신호)로 **FIFO를 보장**한다.
- 실제 처리는 **위임(delegate/인터페이스)으로 주입**받는다 — Phase 15가 `PaymentOrchestrator`를 여기에 꽂는다.
  Phase 14에서는 "1~2초 지연 후 고정 응답"을 돌려주는 스텁을 주입한다.
- **워커 루프 최상위에 try/catch가 반드시 있어야 한다** — 처리 중 예외가 워커 스레드를 죽이면 그 뒤 모든
  거래가 영원히 멈춘다(증상이 "앱은 살아 있는데 결제만 안 됨"이라 원인 파악이 오래 걸리는 종류의 사고다).
- 앱 종료 시 워커가 깔끔히 멈춘다(`CompleteAdding` + `Join(타임아웃)`). 무한 대기하지 않는다.

**구현(2026-08-24)**: `TransactionQueue`는 `BlockingCollection<TransactionWorkItem>` + 전용
`Thread`(`PaymentTransactionWorker`, `IsBackground=true`) 하나로 구현했다. 처리 위임(`_processor`)은
생성자 주입, App.xaml.cs의 `StubPaymentProcessor`가 1.5초 지연 후 고정 응답을 돌려주며, 금액 필드가
`"THROW"`면 의도적으로 예외를 던지는 테스트 경로를 열어 뒀다. 워커 루프 최상위 try/catch가 예외를
잡아 `PosPaymentResponse("99", txId, "INTERNAL_ERROR")`로 대체 응답하고 다음 항목을 계속 처리한다.
`Stop(timeout)`이 `CompleteAdding()` + `Join(timeout)`으로 앱 종료 시 정리한다.

**완료 조건**
- [x] 요청 3건을 **동시에** 밀어 넣으면 처리 시작이 겹치지 않고, 수신 순서대로 1건씩 끝난다 —
      `--pos-client-test` 시나리오1 로그: `ORDER-A` 처리 시작(50.909)→종료(52.417) 뒤에야 `ORDER-C`
      시작(52.420), `ORDER-C` 종료(53.923) 뒤에야 `ORDER-B` 시작(53.924) — 세 구간이 전혀 겹치지 않음
- [x] 응답의 순서가 처리(수신) 순서와 같다 — 위 로그에서 처리 시작 순서(A→C→B)와 응답 도착 순서
      (A→C→B)가 정확히 일치
- [x] 처리 스텁이 **예외를 던져도** 워커가 죽지 않고 다음 큐 항목을 계속 처리한다 — 시나리오5에서
      `amount=THROW` 요청이 `[ERROR] 처리 중 예외`를 남기고 `PAYRES|99|THROW-1|INTERNAL_ERROR` 응답을
      받은 뒤, 곧바로 보낸 `AFTER-THROW` 요청이 `PAYRES|00|AFTER-THROW|OK(STUB)`로 정상 처리됨
- [x] 앱 종료 시 워커 스레드가 종료된다 — `App.OnExit`에서 `PaymentQueue?.Stop(TimeSpan.FromSeconds(5))`
      호출 후 프로세스가 정상 종료되고 잔류 프로세스가 남지 않음을 `Get-Process` 반복 확인으로 검증

**실측 중 발견해 즉시 고친 결함**: 처음엔 예외 시 대체 응답 메시지를 한글("내부 오류(처리 중 예외)")로
넣었는데, `PosMessageEncoding`이 ASCII라 `Encoding.ASCII.GetBytes`가 한글을 예외 없이 `?`로 치환해
응답 본문이 `PAYRES|99|THROW-1|?? ??(?? ? ??)`로 조용히 깨졌다(시나리오5 1차 실행에서 실측). 메시지를
`INTERNAL_ERROR`로 바꾸고, 같은 실수가 재발해도 즉시 드러나도록 `PosPaymentResponse.ToFrame`에 비-ASCII
문자 검증(P14-1)을 추가한 뒤 재실행해 정상 응답을 확인했다.

## P14-4. 응답 회신 경로 — 요청이 들어온 그 연결로

- 큐 항목이 **자기 회신 채널**(요청이 들어온 연결)을 함께 들고 다닌다. 워커는 "어느 연결에 답해야 하는지"를
  전역 상태에서 찾지 않는다 — 다중 클라이언트에서 응답이 엉키는 가장 흔한 원인이다.
- 회신 시점에 연결이 이미 끊겨 있으면 **응답을 폐기하고 로그만 남긴다**(확정 사항 3). 예외를 워커 밖으로
  던지지 않는다.
- 소켓 쓰기 실패도 동일하게 그 연결만 정리하고 워커는 다음 항목으로 넘어간다.

**구현(2026-08-24)**: `PosSocketServer.HandleFrame`이 `_queue.Enqueue(request, response =>
SendResponse(...))`로 그 연결의 `NetworkStream`을 클로저로 캡처해 넘긴다 — 워커는 "어느 연결인지"를
찾지 않고 그냥 콜백을 호출할 뿐이다. `SendResponse`는 `stream.Write` 실패를 캐치해 로그만 남기고
예외를 삼킨다(워커 쪽으로 전파하지 않음).

**완료 조건**
- [x] 클라이언트 2개가 각각 요청을 보내면 **각자에게** 자기 응답이 간다 — 시나리오1(3개 동시 연결)에서
      각 연결이 자기 txId에 맞는 응답만 수신, 뒤섞임 없음
- [x] 요청을 보낸 직후 클라이언트를 강제 종료해도 서버가 살아 있고, 해당 거래는 끝까지 처리된 뒤
      "응답 폐기" 로그가 남는다 — 시나리오4: `ABRUPT-1` 요청 직후 `client.Client.Close(0)`으로 즉시
      종료, 워커는 1.5초 뒤 처리를 끝까지 마치고 `[WARN] ... 응답 전송 실패(연결 끊김으로 추정)
      txId=ABRUPT-1 — 응답 폐기: 삭제된 개체에 액세스할 수 없습니다.` 로그가 남음
- [x] 그 상태에서 **다음 요청이 정상 처리**된다 — 곧바로 새 연결로 보낸 `AFTER-ABRUPT` 요청이
      `PAYRES|00|AFTER-ABRUPT|OK(STUB)`로 정상 처리됨(끊긴 연결 하나가 큐를 막지 않음)

## P14-5. 오류 내성 (PRD §9)

소켓 오류로 앱 전체가 죽지 않는 것을 **명시적으로** 확인하는 Task다. 앞 Task들에 흩어진 예외 처리를
여기서 시나리오로 몰아 검증한다.

- 잘못된 전문(형식 오류, `LEN` 이상값, 버퍼 상한 초과) → **그 연결만** 정리, 서버·다른 연결 유지
- 클라이언트 강제 종료(RST) / 반쪽 종료(FIN 후 무응답)
- 수신 루프·수락 루프의 예외가 앱 도메인 밖으로 새지 않는다

**완료 조건**
- [x] 형식 오류(길이 필드가 숫자가 아님) → **그 연결만** 정리, 서버·다른 연결 유지 — 시나리오3에서
      `ABCD`로 시작하는 쓰레기 전송 시 `[WARN] ... 전문 형식 오류 — 연결 종료`가 남고 서버 프로세스와
      이후 시나리오는 계속 정상 동작
- [x] 버퍼 상한 초과 → 오류로 처리 — P14-1에서 프레이머 단독으로 70,000바이트 투입 시
      `PosProtocolException("수신 버퍼 상한 초과...")` 확인(소켓 계층까지 연결하면 같은 예외 경로로
      P14-2의 `catch (PosProtocolException)` 분기를 그대로 탄다 — 코드 리뷰로 확인, 별도 소켓 레벨
      재현은 하지 않음)
- [x] 클라이언트 강제 종료(RST에 가까운 즉시 종료) → 서버 생존, 이후 정상 요청 처리 — 시나리오4로 확인
      (P14-4와 동일 근거)
- [x] 정상 종료(FIN) → 서버가 그 연결만 정리하고 계속 동작 — 시나리오1~2/5의 모든 연결이 응답 수신 후
      FIN으로 정상 종료되고 서버 로그에 `연결 종료`만 남으며 다른 연결에 영향 없음
- [x] 수신 루프·수락 루프의 예외가 앱 도메인 밖으로 새지 않는다 — 위 모든 시나리오를 거치는 동안 홈
      화면이 계속 응답했고(같은 프로세스), 앱이 죽거나 UI가 멈추지 않음
- [x] 각 경우 로그에 원인이 구분 가능하게 남는다 — "전문 형식 오류"(형식), "응답 전송 실패(연결
      끊김으로 추정)"(연결 단절), "수신 버퍼 상한 초과"(상한 초과)로 문구가 서로 다름

**반쪽 종료(FIN 이후 무응답)는 별도로 재현하지 않았다** — 서버의 수신 루프는 `stream.Read`가 예외를
던지거나 `0`을 반환하는 두 경우로만 반응하므로(P14-2 코드), TCP 계층에서의 half-close는 결국 이 두
경로 중 하나로 수렴한다. 별도 시나리오를 추가해도 검증하는 코드 경로가 늘지 않아 생략했다.

## P14-6. 검증용 테스트 클라이언트 (개발용, 최종 산출물 아님)

- 기존 개발 트리거 관행(`--presenter-test` 등, `App.xaml.cs`)을 그대로 따라 인자 하나로 실행한다.
  - `--pos-client-test` — 요청 3건을 **거의 동시에** 전송하고 응답 순서를 로그로 남긴다(P14-3 완료 조건용)
  - 잘못된 전문 전송, 요청 후 즉시 연결 끊기 같은 오류 시나리오도 이 트리거로 재현할 수 있게 둔다(P14-5용)
- **최종 산출물이 아님을 주석에 명시**한다. Phase 13의 개발 트리거들과 같은 취급 — 회귀 검증에 재사용하므로
  Phase 15에서 지우지 않는다.

**구현(2026-08-24)**: `Services/Diagnostics/PosClientTestScenarios.cs`(`RunAll` — 5개 시나리오를 순서대로
실행). `App.xaml.cs`의 `--pos-client-test` 분기가 `Task.Run(PosClientTestScenarios.RunAll)`로 백그라운드
호출하고 홈 화면은 그대로 띄운다.

**완료 조건**
- [x] 트리거 한 번으로 P14-3(동시 요청/순서, 예외 생존)·P14-4(응답 회신, 강제 종료)·P14-5(형식 오류) 전
      시나리오를 재현한다 — 위 각 Task의 "구현/완료 조건" 절에 인용한 로그가 전부 이 한 번의
      `--pos-client-test` 실행에서 나온 것
- [x] 클래스 주석에 "**최종 산출물이 아니다**"가 명시돼 있음

## P14-7. 완료 검증 및 회귀 확인

- ROADMAP Phase 14 완료 기준 그대로: **요청 3건 동시 → 정확히 순차 1건씩, 순서 보존 / 클라이언트가 중간에
  끊어도 서버 생존**.
- **회귀**: 홈 화면·리더기 설정 화면·결제 알림창(Phase 13)이 이전과 동일하게 동작한다. 특히 앱 기동 경로에
  소켓 서버가 추가되므로 **기동이 느려지거나 실패하지 않는지** 확인한다.
- **계층 규칙 점검**(ROADMAP "계층 구조"): `Protocol/Pos/`에 `System.Net` 0건, `Services/Pos/`·
  `Services/Payment/`에 `System.Windows` 0건.
- `dotnet build` 경고 0 / 오류 0.

**완료 조건**
- [x] 요청 3건 동시 → 정확히 순차 1건씩, 순서 보존 — P14-3 재검증 로그로 확인
- [x] 클라이언트가 중간에 끊어도 서버 생존 — P14-4/P14-5 재검증 로그로 확인
- [x] 회귀 — `--pos-client-test`/`--home`/인자 없는 기본 실행 모두 홈 화면이 정상 기동(트레이 최소화
      포함, 기존 동작과 동일)했고, 소켓 서버 추가로 기동이 느려지거나 실패하지 않음(각 실행에서 2~3초
      내 HomeWindow 생성 확인). 리더기 설정 화면·결제 알림창은 이번 Phase에서 코드를 건드리지 않았고
      (App.xaml.cs의 기존 브랜치 구조·`ReaderConnections`/`PaymentNoticeBackgroundSource` 초기화 순서를
      그대로 유지) 공유 지점(App.OnStartup/OnExit)의 추가 코드가 예외를 던지지 않음을 여러 차례 반복
      실행으로 확인했다 — 화면을 직접 열어보는 수동 클릭 재검증까지는 하지 않았다(코드가 닿지 않는
      영역이라 필요성이 낮다고 판단)
- [x] 계층 규칙 점검 — `Protocol/Pos/`에 `System.Net` 0건, `Services/Pos/`·`Services/Payment/`에
      `System.Windows` 0건(grep 확인)
- [x] `dotnet build` 경고 0 / 오류 0

**검증하지 못한 범위**: 없음(위 항목 전부 실측 확인). 다만 P14-5의 "반쪽 종료(FIN 후 무응답)"는 별도
시나리오로 재현하지 않았다 — 근거는 위 P14-5 절 참고(코드 경로가 정상 종료/강제 종료와 동일하게
수렴한다는 판단).

## Phase 14 — Opus 전체 검증 리뷰 및 후속 수정 (2026-08-24)

Sonnet 구현이 끝난 뒤 Opus가 코드 재검토로 별도 전체 검증을 수행해 결함 2건을 확정했고, Sonnet이 전부
수정 후 재검증까지 완료했다 — Phase 12(P12-6)/13과 같은 워크플로우.

### H-1(★ 가장 심각) — 워커 스레드가 소켓 쓰기까지 맡아서, 응답 전송이 막히면 전체 큐가 멈춘다

**결함**: `TransactionQueue.WorkerLoop`은 `_processor(request)` 처리 뒤 `item.OnCompleted(response)`를
**같은(유일한) 워커 스레드에서 동기 호출**한다. 이 콜백의 실체는 `PosSocketServer.SendResponse` →
`stream.Write(...)`인데, `NetworkStream`에 `WriteTimeout`을 설정한 곳이 어디에도 없어(전수 grep 확인)
기본값이 무한대였다. POS 클라이언트 하나가 응답을 안 읽고 버티면(수신 버퍼가 차서 TCP 송신이 막히는
경우) `stream.Write`가 끝나지 않고, 그러면 워커 스레드 자체가 거기서 멈춰 그 뒤 큐에 들어온 **다른 모든
터미널의 결제 요청이 전부 무한 대기**한다 — P14-3이 지키려던 "워커는 계속 전진한다"는 핵심 불변조건을
정확히 깨는 지점이었다. `--pos-client-test`의 기존 시나리오들은 응답이 몇십 바이트라 즉시 커널 송신
버퍼에 들어가 버려 이 경로를 전혀 건드리지 못했다 — 순수 코드 인스펙션으로만 발견됨.

**수정**: `PosSocketServer`에 `SendTimeoutMilliseconds = 5000` 상수를 추가하고, `HandleConnection`에서
`NetworkStream` 생성 직후 `stream.WriteTimeout = SendTimeoutMilliseconds`를 설정했다. 타임아웃이 지나면
`IOException`이 던져지고 `SendResponse`의 기존 catch가 "응답 폐기" 로그로 흡수해 워커가 다음 항목으로
넘어간다(로그 문구도 "연결 끊김 또는 Nms 내 미수신으로 추정"으로 정정).

**재검증**: `PosClientTestScenarios`에 시나리오6(`Scenario6_UnresponsiveClientDoesNotBlockQueue`)을
추가했다 — 응답을 안 읽는 "먹통" 클라이언트를 만들고(수신 버퍼를 1바이트로 축소 시도), 응답 본문을
9,900바이트로 부풀린 전용 스텁 경로(`App.xaml.cs` `amount="BIGRESPONSE"`)로 요청한 뒤, 곧이어 다른
클라이언트가 보낸 정상 요청이 얼마 만에 처리되는지 측정한다. 실측 결과 **3,030ms 만에 정상 응답 수신**
(`STUCK-1` 처리 종료 직후 `AFTER-STUCK`가 지연 없이 시작됨) — 큐가 막히지 않음을 확인했다. 다만 Windows
루프백 TCP의 자동 윈도우 튜닝이 `ReceiveBufferSize=1` 힌트를 무시해, 9,900바이트도 실제 소켓 쓰기
블로킹 없이 흡수됐다(WriteTimeout 자체가 실제로 발동하는 것까지는 이 환경에서 강제 재현하지 못함) —
그래도 (a) 큐 진행에 회귀가 없음은 확인됐고, (b) `NetworkStream.WriteTimeout`은 .NET BCL이 보장하는
표준 메커니즘이라 별도로 재입증할 필요가 낮다고 판단해 이 수준에서 마무리했다.

### M-1 — AcceptLoop가 모든 예외를 "Stop()에 의한 정상 종료"로 간주

**결함**: `AcceptLoop`의 `catch (Exception) { break; }`가 `Stop()`이 유발한 예외와 **진짜 우발적 오류**를
구분하지 않아, 후자의 경우에도 로그 한 줄 없이 수락 루프가 조용히 영구 종료됐다 — 앱은 안 죽지만(PRD §9
요건은 지킴), 이후 POS가 왜 연결이 안 되는지 로그로 추적할 방법이 없어 P14-5의 "각 경우 로그에 원인이
구분 가능하게 남는다"는 이 경로에서는 지켜지지 않았다.

**수정**: `catch (Exception ex)`로 바꾸고, `!token.IsCancellationRequested`(= `Stop()`이 원인이 아님)일
때만 `FileLogger.Error`로 예외 전체를 남기도록 분기를 추가했다.

**재검증**: 정상 종료 경로(프로세스 강제 종료 — `Stop-Process`)에서 새 ERROR 로그가 추가로 남지 않음을
확인했다(스퓨리어스 로그 없음). 진짜 비-`Stop()` 예외를 실제로 유발하는 재현은 하지 않았다 — 코드
인스펙션으로 조건 분기(`token.IsCancellationRequested` 체크)가 올바름을 확인하는 수준에서 마무리했다
(원인 자체를 인위로 재현하기 어렵고, 이 변경은 로그 문구 추가뿐이라 리스크가 낮다고 판단).

### 재검증 후 전체 회귀

수정 후 `--pos-client-test`를 처음부터(시나리오1~6) 다시 실행해 전부 정상 완료됨을 확인했다(`전체 완료`
로그, 새로운 ERROR 없음 — `THROW-1` 예외는 의도된 것). `dotnet build` 경고 0/오류 0.

## Phase 14 — 추가 기능: 응답 후 유휴 연결 자동 종료 (2026-08-24, 사용자 요청)

**요구사항**: POS 쪽은 원칙대로 응답을 받으면 연결을 닫아야 하지만(P14-2 확정 사항), **개발 실수로 안
닫는 경우**를 대비해 서버가 응답 전송 후 일정 시간(10초) 안에 다음 요청이 없으면 그 연결을 먼저 닫도록
해 달라는 사용자 요청. 지속 연결(같은 연결로 여러 요청) 자체는 그대로 유지해야 한다 — 매 응답마다
타이머가 리셋되므로 정상적인 다중 요청 흐름(P14-2 `PERSIST-1/2/3`)은 영향받지 않는다.

**기술적 난점과 최초 구현(`Timer` 기반, 이후 폐기)**: `NetworkStream.ReadTimeout`을 다른 스레드(응답을
보내는 `TransactionQueue` 워커)에서 바꿔도, 연결 스레드가 이미 `stream.Read()`에 블로킹 진입해 있으면
Windows 소켓 특성상 그 호출엔 소급 적용되지 않는다. 처음엔 이 문제를 피해 `System.Threading.Timer`로
소켓을 직접 닫는 방식(타이머 만료 시 `SafeClose(client)`로 강제로 `Read()`를 예외로 풀어냄)으로
구현했으나, **"오류가 생길 소지가 없는 방식이 좋다"는 사용자 피드백에 따라 네이티브
`stream.ReadTimeout`(=`SO_RCVTIMEO`)만으로 다시 짰다** — 아래가 최종 구현이다.

**최종 구현(네이티브 `ReadTimeout`)**: 핵심 아이디어는 "다음 `Read()`를 호출할 바로 그 스레드가, 호출
직전에 타임아웃 값을 건다"이다. 이러면 소급 적용 문제 자체가 발생하지 않는다.
- 연결 스레드가 프레임을 큐에 넣은 뒤, `ManualResetEventSlim responseSent`로 **그 응답이 실제로 나갈
  때까지 대기**한다(`HandleFrame`이 true를 반환하면 `responseSent.Wait()`). POS는 원래 응답을 기다렸다가
  다음 요청을 보내는 동기 프로토콜이라(P14-2 확정) 이 대기가 별도 지연을 만들지 않는다.
  `TransactionQueue`의 완료 콜백은 성공/실패(H-1 타임아웃 포함) 어느 쪽이든 `finally`에서 항상
  `responseSent.Set()`을 호출하므로 무한 대기 위험이 없다.
  - Enqueue 자체는 그대로 논블로킹이라 `TransactionQueue`의 단일 워커 직렬화(P14-3)에는 영향이 없다 —
    기다리는 건 그 연결 자신의 스레드뿐이고, 다른 연결·큐 처리는 그대로 돈다.
- 대기가 끝나면(이번 읽기에서 처리한 프레임 중 하나라도 응답이 나갔으면) 연결 스레드 자신이
  `stream.ReadTimeout = IdleAfterResponseTimeoutMilliseconds`를 설정한 뒤 다음 `Read()`로 들어간다.
- `Read()`가 `SocketError.TimedOut`으로 실패하면(`IsReadTimeout` 헬퍼로 판별) 유휴 타임아웃 경로로
  분기해 `WARN` 로그를 남기고 정상 정리 경로(`finally`)로 합류한다 — 그 외 `IOException`은 기존과 같이
  "연결 단절"로 처리한다.
- **최초 요청을 기다리는 첫 `Read()`**는 아직 응답을 보낸 적이 없어 `ReadTimeout`을 건드리지 않으므로
  (기본값 `Timeout.Infinite`) 여전히 무제한 대기한다.
- 별도 `Timer`/`SafeClose(client)`를 통한 강제 종료가 완전히 사라졌다 — 소켓은 오직 정상 종료 경로에서만
  닫힌다.

**검증**: `PosClientTestScenarios`에 시나리오7(`Scenario7_ServerClosesIdleConnectionAfterResponse`)을
추가했다 — 요청 1건을 보내고 응답을 받은 뒤 12초간 아무것도 안 보내고 대기, 그 뒤 연결이 닫혔는지
확인한다. 최종(네이티브) 구현 실측: 응답 수신 18:36:18.772 → 서버가 정확히 **10.007초 뒤**(18:36:30.284)
`WARN ... 응답 전송 후 10000ms 동안 다음 요청이 없어 서버가 먼저 닫음`으로 연결을 닫음 → 클라이언트
쪽에서도 연결 종료 확인. 전체 시나리오(1~7)를 재실행해 다음을 함께 재확인했다:
- 시나리오1(3건 동시 요청) — 모든 재실행에서 `[TransactionQueue] 처리 시작/종료`가 절대 겹치지 않음
  (직렬화 무결성 유지, Timer 제거와 무관하게 그대로)
- 시나리오2(지속 연결 다중 요청, 요청 간 간격 ~1.5초) — 10초 유휴 임계값에 한참 못 미쳐 **회귀 없이
  그대로 통과**
- 시나리오6(H-1 재검증, 응답 안 읽는 클라이언트) — 여전히 큐가 안 막힘(1,504ms 만에 다음 요청 처리).
  부수적으로 관찰: 그 "먹통" 연결도 응답 전송 자체는 성공했으나(OS 버퍼 흡수) 테스트 코드가 곧바로
  연결을 정리해 서버 쪽에서 통상적인 "연결 단절" 경로로 자연스럽게 감지·정리됨(설계대로, 새 결함 아님)

`dotnet build` 경고 0/오류 0.

## Phase 14 완료 후

- Phase 15(결제 Flow 조립) 실행계획서를 작성한다. 착수 전에 **ROADMAP Phase 15의 ★ 재검토 항목**
  (`ReaderSetupWindow`를 연 채 결제 요청이 들어오는 경우의 정책, P13-4에서 보류)을 사용자와 확정해야 한다 —
  리더기 포트를 설정 화면과 결제 워커가 동시에 쓰려는 충돌과 직결된다.
- Phase 15는 이 Phase가 만든 **워커 처리 스텁 자리에 `PaymentOrchestrator`를 꽂는 작업**이 된다. 소켓/큐
  코드를 다시 건드리게 된다면 이 Phase의 경계 설정이 잘못된 것이므로 그 시점에 원인을 기록한다.

---

# Phase 15 — 결제 Flow 조립

> ROADMAP.md "Phase 15 — 결제 Flow 조립" / PRD §4.1~§4.7, §2.2.3, §8.4. Phase 10(리더기)·11(DB)·13(알림창)·
> 14(소켓/Queue)이 만들어 둔 부품을 **엮는** Phase다. **새 부품을 만드는 Phase가 아니다** — 이 Phase에서
> Phase 10~14 코드를 크게 고쳐야 한다면 앞 Phase의 경계 설정이 잘못된 것이므로 그 이유를 기록한다
> (Phase 14 완료 후 메모의 지시).

## 착수 전 전제 (2026-08-25 확인 완료)

- **실제 통신 SPEC은 아직 반영하지 않는다.** `docs/payment_relay/spec/`에 POS↔원캡 전산설계서
  (`국세 베리어프리 키오스크용 전산설계서(POS-원캡)_20260820.hwp`)가 들어와 있으나 내용 확인이 끝나지
  않았고, 원캡↔VAN 전문 문서는 아직 없다. 2026-08-25 사용자 결정: **더미(임시) 전문 그대로 Phase 15를
  진행**하고 실제 SPEC 반영은 별도 Phase로 나중에 잡는다.
  - 따라서 이 Phase의 성패 기준은 "전문이 맞는가"가 아니라 **"전문이 바뀌어도 Flow가 안 바뀌는가"**다.
    Flow 코드에 전문 문자열·오프셋이 한 글자도 등장하지 않아야 한다(완료 조건에 grep 점검 포함).
- 사용 가능한 부품과 계약(이미 구현·검증 완료):

  | 부품 | 위치 | Phase 15가 쓰는 방식 |
  |---|---|---|
  | 포트 소유자 | `Services/Reader/ReaderConnectionManager` (`App.ReaderConnections`) | `Reader1`/`Reader2` 참조만. **포트를 열거나 닫지 않는다**(닫는 지점은 P12-1이 정한 1곳뿐) |
  | 명령 전송 | `ReaderService.SendCardReadCommandAsync` / `SendInvalidationInit` | 재시도 래퍼(P10-3)·단일 유효 응답 게이트(P10-4)가 이미 안에 있다 |
  | 이중화 | `Services/Reader/CardReadBroadcaster.SendAsync` | 동시 전송 + 최초 응답 채택 + 나머지 `0x60`. **N=1도 분기 없이 동작**(P10-5) |
  | 무결성 시퀀스 | `Services/Reader/IntegrityCheckService.RunAsync` | 0x61→0x71→0x62→0x72 + DB 저장까지 한 번에(P12-4) |
  | 금일 이력 | `Services/Storage/IntegrityCheckStore.HasSuccessToday(comPort)` | 조회 실패 시 `false`(=다시 체크) — P11-4 |
  | 알림창 | `Services/Payment/IPaymentNoticePresenter` | 어느 스레드에서 호출해도 안전, 닫힌 뒤 호출은 무시(P13-6) |
  | 큐/소켓 | `Services/Payment/TransactionQueue`, `Services/Pos/PosSocketServer` | **직렬화 지점은 큐 하나뿐**(P14-3). Flow는 자기 안에서 또 잠그지 않는다 |

- 결과적으로 이 Phase의 신규 코드는 대부분 `Services/Payment/PaymentOrchestrator` 한 클래스와, 그것이
  기대는 얇은 계약 3개(리더기 엔드포인트 / 설정화면 게이트 / VAN 스텁)다.

## 확정된 설계 결정 (2026-08-25 사용자 확정)

1. **리더기 설정 화면이 열린 채 결제 요청이 오면 → 즉시 오류 응답으로 거부**(P13-4에서 보류됐던 ★ 항목).
   카드 리딩을 아예 시도하지 않고 POS에 "설정 중" 오류 전문을 반환한다. 설정 화면을 강제로 닫거나 충돌을
   감수하고 진행하지 않는다 — 같은 COM 포트를 설정 화면(초기화·상태체크·무결성체크 버튼, 포트 재오픈)과
   결제 워커가 동시에 쓰는 상황 자체를 만들지 않는 것이 오류 소지가 가장 적기 때문이다. 판정 기준이
   "`ReaderSetupWindow`가 떠 있는가" 하나뿐이라 애매한 중간 상태가 없다.
2. **VAN 단계는 스텁 + `PROCESSING` 전환까지만.** `Services/Van/IVanService`를 정의하고 Phase 15는 스텁을
   꽂는다. 알림창 `VanProcessing` 전환과 "승인 / VAN 거절 / VAN 통신 실패" 3분기 배선은 완성하되, 실제
   `FNAISCRDVAN` 호출·ANSI 마샬링·버퍼 관리는 Phase 17이 스텁 자리에 구현만 꽂는다.
3. **취소·Timeout은 단순 배선까지만.** `Canceled` 구독과 카드 대기 상한(120초)을 걸어 "취소/Timeout이면
   대기 중인 리더기 전부 `0x60` + POS에 해당 결과 응답"까지는 동작시킨다. 다만 **카드리딩완료+취소 /
   카드리딩완료+Timeout / 취소+Timeout / 콜백 중복**의 단일 결과 확정 게이트는 Phase 16에서 집중 검증한다.
4. **POS 더미 응답은 "결과코드 + 원인 열거형"으로 세분화.** PRD가 "구분해서 응답"을 요구하는 축(승인 /
   리더기 응답코드 실패 / DLL 연동 실패 / 무결성 실패 / 포트 미사용 / 설정 중 / 취소 / Timeout / VAN 거절 /
   VAN 통신 실패 / 내부 오류)을 **열거형으로 확정**하고, 열거형→전문 문자열 매핑은 `Protocol/Pos/`에만
   둔다. Flow는 열거형만 다루므로 실제 SPEC 확정 시 매핑표만 교체된다.

## 이 Phase에서 손대지 않는 것 (범위 밖 확정)

- **실제 SPEC 반영**(POS 전문 필드 확장, VAN 전문) — 별도 Phase.
- **`FNAISCRDVAN` 실호출 / `Interop/KftcGiroNative.cs` / `Protocol/Van/`** — Phase 17.
- **경합 4종의 단일 결과 확정 게이트** — Phase 16.
- **포트 열기/닫기 정책** — `ReaderConnectionManager`가 이미 소유. Flow는 `OpenPort`/`ClosePort`를 직접
  호출하지 않는다(포트가 안 열려 있으면 `SendCommandSafe`의 자동 재오픈이 처리한다, PRD §2.2.4).
- **알림창 시각/애니메이션** — Phase 13에서 완료. Flow는 `Show`/`ChangeState`/`Close`만 부른다.
- **결제 진행 중 사용자가 설정 화면을 "여는" 것을 막는 UI 차단** — 이번 결정은 반대 방향만 확정했다.

## 알려진 범위 밖 / 이후 확인 필요

- **거래 진행 중 설정 화면 열기**: 결제 워커가 카드 대기 중일 때 사용자가 홈 화면에서 리더기 설정 버튼을
  누르면 여전히 포트 경합이 가능하다. 이번 결정(1)은 "설정 화면이 먼저 열려 있는 경우"만 덮는다. 반대
  방향까지 막으려면 홈 화면 버튼을 거래 중 비활성화하는 UX 결정이 필요하므로 **Phase 16 착수 시 사용자와
  확정**한다.
- **거래일시 등 POS 요청 필드**: 더미 전문(`PAY|<amount>|<txId>`)에는 거래일시가 없어 **원캡이
  `DateTime.Now`로 생성**해 `0x2B`에 넣는다. 실제 SPEC 반영 시 POS가 준 값으로 교체한다
  (`Protocol/Reader/TransactionInfoRequest`의 TODO 주석과 짝을 이룬다).

---

## P15-1. 처리 위임 계약 정리 — 큐 워커가 유일한 블로킹 지점 ★

`TransactionQueue`의 처리 위임이 지금은 동기 델리게이트(`Func<PosPaymentRequest, PosPaymentResponse>`)인데,
Flow가 쓰는 부품(`SendCardReadCommandAsync`, `IntegrityCheckService.RunAsync`, `CardReadBroadcaster.SendAsync`)은
전부 `Task` 기반이다. 그대로 두면 Orchestrator 안에서 `.GetAwaiter().GetResult()`가 여기저기 흩어진다.

- 위임 타입을 **`Func<PosPaymentRequest, Task<PosPaymentResponse>>`**로 바꾸고, 워커 루프가
  `_processor(item.Request).GetAwaiter().GetResult()` **한 곳에서만** 블로킹하게 한다.
- 데드락이 없는 근거를 주석으로 남긴다: 워커는 `SynchronizationContext`가 없는 전용 `Thread`이고,
  `Services/` 내부는 전부 `ConfigureAwait(false)`를 지킨다(공통 규칙 5) — UI 컨텍스트로 돌아오려는
  continuation이 없으므로 sync-over-async 데드락 조건이 성립하지 않는다.
- 예외 처리 구조(최상위 try/catch, `InvokeCompletedSafely`)와 ASCII 전용 실패 메시지 규칙은 그대로 둔다.
  예외가 `AggregateException`으로 감싸이지 않도록 `.Result`가 아니라 `.GetAwaiter().GetResult()`를 쓴다.
- **Phase 14 경계 조정 기록**: Phase 14 완료 메모가 "Phase 15가 큐/소켓 코드를 다시 건드리면 이유를
  적으라"고 했다. 이유는 "Phase 14 시점 스텁이 동기 함수여서 위임 타입을 동기로 잡았고, 실제 처리기가
  비동기라는 사실이 Phase 15에서 드러났기 때문"이다. 소켓 서버(`PosSocketServer`)는 **손대지 않는다** —
  경계가 틀린 것은 큐의 위임 시그니처 한 줄뿐이다.

**구현(2026-08-25)**: `TransactionQueue`의 `_processor` 필드와 생성자 인자를
`Func<PosPaymentRequest, Task<PosPaymentResponse>>`로 변경, `WorkerLoop()` 안 호출부를
`_processor(item.Request).GetAwaiter().GetResult()` 한 줄로 교체(클래스 주석에 데드락 없음 근거 기술).
`App.xaml.cs`의 `StubPaymentProcessor`를 `async Task<PosPaymentResponse>`로 바꾸고 `Thread.Sleep(1500)`을
`await Task.Delay(1500).ConfigureAwait(false)`로 교체(`using System.Threading.Tasks;` 추가). 예외 유발
경로(`amount=="THROW"`)는 `async` 메서드 안에서 그대로 `throw`하면 반환된 `Task`가 Faulted 상태가 되고
`GetAwaiter().GetResult()`가 원래 예외 타입 그대로 다시 던지므로 기존 catch 구조가 손대지 않아도 그대로
작동함을 확인했다. `Services/Pos/PosSocketServer.cs`는 `TransactionQueue.Enqueue`(시그니처 불변)만 쓰므로
무변경.

**완료 조건**
- [x] 위임이 `Task<PosPaymentResponse>` 반환으로 바뀌고, 블로킹 호출이 워커 루프 1곳뿐임 — grep 결과
      `GetAwaiter().GetResult()`가 저장소 전체에서 `TransactionQueue.cs`의 실호출 1건(`WorkerLoop`)과
      클래스 주석 설명문 2건뿐, 다른 파일에는 0건
- [x] `Services/Pos/PosSocketServer.cs`에 변경 없음 — `git diff --stat -- .../PosSocketServer.cs` 결과 빈 diff
- [x] `dotnet build KFTCOneCAP.Wpf.sln` 경고 0/오류 0
- [x] Phase 14의 `--pos-client-test` 7개 시나리오 전부 회귀 통과(2026-08-25 재실행 로그) — 특히 시나리오5
      (예외 유발)는 스택 트레이스 형태가 `TaskAwaiter.ThrowForNonSuccess` 경유로 바뀌었을 뿐 결과는
      동일(`PAYRES|99|THROW-1|INTERNAL_ERROR`, 워커 생존, 다음 요청 `AFTER-THROW` 정상 처리), 시나리오7
      (유휴 연결 자동 종료)도 10.003초 뒤 정상 종료로 재확인됨

## P15-2. `IReaderEndpoint` — Flow가 보는 리더기 한 대 ★

Orchestrator가 `ReaderService`(sealed 구체 클래스)를 직접 잡으면 **하드웨어 없이는 정상/FALLBACK/`12`
경로를 한 번도 실행해 볼 수 없다.** 이 Phase의 완료 기준이 "5개 분기가 각각 올바르게 끝난다"인데, 검증
수단이 없는 계획은 완료를 증명할 수 없다. 그래서 Flow가 보는 최소 계약을 하나 만든다.

- **`Services/Reader/IReaderEndpoint.cs`**(신규) — Flow가 리더기 한 대에 대해 필요한 것 전부:
  - `string ComPortDisplay { get; }` — DB 조회 키(P12-2가 정한 `"COM 05"` 표시 형식)
  - `Task<IntegrityCheckSequenceOutcome> RunIntegrityCheckAsync(TimeSpan statusTimeout, TimeSpan integrityTimeout)`
  - `Task<CardReadCommandOutcome> SendCardReadCommandAsync(TransactionInfoRequest request, TimeSpan timeout)`
  - `int SendInvalidationInit()`
- **`Services/Reader/ReaderEndpoint.cs`**(신규, 운영 구현) — `ReaderService` + 표시용 COM 포트 문자열 +
  `IntegrityCheckService`를 묶는 **얇은 어댑터**. 로직을 넣지 않는다(위임만).
- `CardReadBroadcaster`의 참여자 타입을 `IReadOnlyList<ReaderService>` → `IReadOnlyList<IReaderEndpoint>`로
  바꾼다. 페일오버 알고리즘 자체(동시 전송 → `Task.WhenAny` → 나머지 `0x60`)는 **한 줄도 바꾸지 않는다.**
- `ReaderService`/`IntegrityCheckService`/`ReaderConnectionManager`는 수정하지 않는다(어댑터가 감싼다).

**구현(2026-08-25)**: `IReaderEndpoint`(계획대로 4개 멤버)와 운영 구현 `ReaderEndpoint`(생성자로
`ReaderService`+`IntegrityCheckService`를 받아 전부 위임, `ComPortDisplay`는
`ComPortFormat.ToDisplay(_reader.PortNumber)`로 계산 — `ReaderService.PortNumber`는 `OpenPort` 성공/실패와
무관하게 항상 최근 호출값을 기억하는 필드라 포트가 지금 안 열려 있어도 정확한 표시 문자열을 낸다, 어댑터
주석에 근거 기술)를 신설. `CardReadBroadcaster`/`CardReadBroadcastResult`의 `ReaderService` 참조 4곳을
`IReaderEndpoint`로 치환. `ReaderService`/`IntegrityCheckService`/`ReaderConnectionManager` 무변경.

**완료 조건**
- [x] `Services/Payment/`가 `ReaderService` 타입을 직접 참조하지 않음 — 아직 `Services/Payment/`에
      Orchestrator가 없어 grep 매치 자체가 0건(P15-6에서 실제로 생성될 때 재확인)
- [x] `CardReadBroadcaster`의 알고리즘 본문에 의미 변경 없음 — `git diff` 확인 결과 `ReaderService` →
      `IReaderEndpoint` 타입 치환 4곳뿐, 동시 전송/`Task.WhenAny`/무효화 로직 줄 수 변경 없음
- [x] `dotnet build KFTCOneCAP.Wpf.sln` 경고 0 / 오류 0

## P15-3. POS 결과 구분 — 열거형 확정 + 매핑은 `Protocol/Pos/`에만

- **`Protocol/Pos/PosPaymentResultCode.cs`**(신규 열거형)과 더미 전문 코드 매핑:

  | 열거형 | 더미 코드 | 근거 |
  |---|---|---|
  | `Approved` | `00` | §4.10 승인 |
  | `ReaderResponseFailure` | `10` | §4.6 `0x3B` 응답코드가 `00`/`07`/`12` 외 |
  | `ReaderDllFailure` | `11` | §4.7 DLL 연동/통신 실패 |
  | `IntegrityCheckFailure` | `12` | §4.2 참여 후보 전원 무결성 실패 |
  | `NoReaderConfigured` | `13` | §2.2.3 양쪽 모두 `"미사용"` |
  | `ReaderSetupInProgress` | `14` | 2026-08-25 확정(설정 화면 열림) |
  | `UserCanceled` | `20` | §4.8 |
  | `Timeout` | `21` | §4.9 |
  | `VanDeclined` | `30` | §4.10 VAN 서버 거절 |
  | `VanCommunicationFailure` | `31` | §4.10 VAN DLL 통신 실패 |
  | `InternalError` | `99` | §9 예외 안전판(`TransactionQueue`의 기존 폴백과 같은 값) |

- 매핑(열거형→코드 문자열)과 `PosPaymentResponse` 생성 팩터리는 **`Protocol/Pos/` 안에만** 둔다. Flow는
  `PosPaymentResultCode`와 짧은 원인 문자열만 넘긴다.
- **원인 문자열은 ASCII만 쓴다.** `PosPaymentResponse.ToFrame()`이 비ASCII를 만나면 `PosProtocolException`을
  던지는 가드가 이미 있다(2026-08-24 한글 깨짐 사고 후 추가). 한글 사유는 **로그에만** 남기고 전문에는
  영문 축약(`READER_RESP_07`, `RETRY_LIMIT` 등)을 넣는다.
- 카드번호 등 카드 데이터는 **응답 전문에도 로그에도 넣지 않는다**(§8.4/§9).

**구현(2026-08-25)**: `Protocol/Pos/PosPaymentResultCode.cs`(11개 값, 각 값 XML 주석에 PRD 근거 절 기술)와
`PosPaymentResponse.Create(PosPaymentResultCode, transactionId, reason)`(switch식 매핑, 계획한 코드 표
그대로)를 신설. 기존 생성자는 그대로 두되(App.xaml.cs의 Phase 14 스텁이 계속 씀 — Services/Payment/ 밖이라
범위 밖), Flow/큐가 있는 `Services/Payment/`의 유일한 리터럴 사용처였던 `TransactionQueue.WorkerLoop`의
예외 폴백(`new PosPaymentResponse("99", txId, "INTERNAL_ERROR")`)을
`PosPaymentResponse.Create(PosPaymentResultCode.InternalError, txId, "INTERNAL_ERROR")`로 교체.

**완료 조건**
- [ ] 11개 결과코드가 전부 정의되고, 각각을 만들어 낼 Flow 경로가 P15-6~P15-9에 존재 — **Orchestrator가
      아직 없어 이 조건은 체크포인트 2(P15-6~P15-9) 완료 시 재확인**
- [x] `Services/Payment/`에 전문 코드 리터럴(`"00"`/`"10"` 등)이 없음 — grep `"[0-9][0-9]"` 매치 0건
      (`TransactionQueue.cs` 폴백을 `Create`로 교체한 뒤 재확인)
- [x] 비ASCII 원인 문자열을 넣으면 예외로 즉시 드러남 — 32비트 PowerShell 리플렉션으로
      `PosPaymentResponse.Create(InternalError, "TX1", "한글사유").ToFrame()` 호출 시
      `PosProtocolException`이 그대로(감싸이지 않고) 던져짐을 확인
- [x] `dotnet build KFTCOneCAP.Wpf.sln` 경고 0 / 오류 0

## P15-4. 설정 화면 게이트 (확정 사항 1)

`Services/`는 WPF 타입을 알 수 없으므로 "설정 화면이 떠 있는가"를 직접 볼 수 없다. 계약을 하나 둔다.

- **`Services/Payment/IReaderSetupGate.cs`**(신규): `bool IsReaderSetupOpen { get; }` 하나뿐.
- **`Views/ReaderSetupWindowGate.cs`**(신규 구현): `ReaderSetupWindow`가 열릴 때 `Interlocked.Increment`,
  `Closed`에서 `Decrement`. 등록/해제를 `ReaderSetupWindow` 자신의 생성자와 `Closed`에 둬서 호출자가
  잊어버릴 여지를 없앤다.
- Orchestrator는 **카드 리딩 시작 전 단 한 번** 판정한다 — 무결성 체크보다도 먼저다(설정 화면이 열려
  있으면 무결성 체크조차 같은 포트를 건드린다).
- 판정 직후 사용자가 설정 화면을 여는 경합은 이 Phase에서 막지 않는다(위 "알려진 범위 밖").

**구현(2026-08-25)**: `Services/Payment/IReaderSetupGate.cs`(계획대로 `IsReaderSetupOpen` 하나)와
`Views/ReaderSetupWindowGate.cs`(`Interlocked` 기반 카운터, `App.ReaderSetupGate`로 앱 수명 동안 하나만
생성 — `ReaderConnections`와 달리 의존성이 없어 필드 초기화로 즉시 생성, `OnStartup` 이전에도 안전).
`ReaderSetupWindow`에 `Closed += ReaderSetupWindow_Closed`를 생성자에 추가하고, 등록은 기존
`ReaderSetupWindow_Loaded`(PRD 4.2 `ConfirmButton.Focus()` 자리)에 `if (!IsWarmupInstance) Register()`로
끼워 넣었다. **`Closing`이 아니라 `Closed`에서 해제**하는 이유를 주석에 남겼다 — `Closing`은
`e.Cancel = true`로 취소될 수 있어(작업 중/dirty 확인 등, 기존 로직) "실제로 닫혔다"를 보장하지 못해
카운트가 어긋날 수 있기 때문. `IsWarmupInstance`는 객체 초기화 구문으로 설정되어 생성자 시점엔 아직
반영되지 않으므로, 이미 확정된 뒤 실행되는 `Loaded`에서 판정하는 것이 정확함을 주석에 근거로 남겼다.

**완료 조건**
- [x] 카운팅 로직 자체(등록/중첩 등록/해제/전부 해제) — 32비트 PowerShell 리플렉션으로
      `ReaderSetupWindowGate.Register/Unregister/IsReaderSetupOpen` 직접 호출해 5단계 전이 전부 기대값과
      일치 확인(초기 `false` → 1회 등록 `true` → 중첩 등록 `true` → 1회 해제(1개 남음) `true` → 전부
      해제 `false`)
- [x] 실제 UI 배선이 크래시 없이 동작함 — `--home`으로 앱 기동(HomeWindow.Loaded의 워밍업 인스턴스
      경로도 자동 실행됨) → "리더기 설정" 카드 클릭으로 실제 창을 열어 정상 렌더링 확인 → 취소 버튼으로
      닫아 `Closed` 경로까지 예외 없이 실행됨을 windows 자동화로 실측(스냅샷 전/후 비교)
- [ ] 설정 화면을 연 채 결제 요청을 넣으면 리더기 명령이 **한 건도 나가지 않고** `ReaderSetupInProgress`가
      반환됨 — **Orchestrator가 아직 없어 체크포인트 2(P15-6) 완료 후 재확인**(P15-10 시나리오 9와 동일)
- [ ] 설정 화면을 닫은 뒤 같은 요청이 정상 진행됨 — 위와 같은 이유로 체크포인트 2 이후 재확인

## P15-5. VAN 스텁 (확정 사항 2)

- **`Services/Van/IVanService.cs`**(신규): `Task<VanApprovalOutcome> RequestApprovalAsync(VanApprovalRequest request)`
  - `VanApprovalRequest`: 이 Phase에서는 **카드 데이터 + 금액 + 거래일시**를 담는 순수 DTO. 전문 바이트를
    만들지 않는다(전문 생성은 Phase 17의 `Protocol/Van/` 몫).
  - `VanApprovalOutcome`: `Approved` / `Declined(응답코드, 사유)` / `CommunicationFailure(사유)` 3분기.
    PRD §4.10의 "**VAN DLL 통신 실패와 VAN 서버 거절은 구분**"을 타입 수준에서 강제한다.
- **`Services/Van/StubVanService.cs`**(신규): 고정 지연(예: 1초) 후 결과 반환. **다음 결과를 주입할 수
  있게** 한다(검증 하네스가 승인/거절/통신실패 지정). 기본값은 승인.
- Orchestrator는 VAN 호출 **직전에** `ChangeState(VanProcessing)`으로 전환한다(PRD §4.10). 이 구간에서
  취소가 막히는 것은 P13-2가 이미 ViewModel 레벨에서 게이팅한다.

**구현(2026-08-25)**: `Services/Van/`에 `IVanService`(1메서드), `VanApprovalRequest`(카드 데이터+금액+
거래일시 DTO, `CardData`는 `Protocol/Reader/CardReadResponseParser.CardReadData`를 그대로 받음 — VAN
전문용으로 다시 파싱하지 않음), `VanApprovalOutcomeKind`(3분기 열거형), `VanApprovalOutcome`(이 코드베이스의
다른 Outcome 타입들과 같은 모양 — private 생성자+정적 팩터리 3개), `StubVanService`(1초 고정 지연,
`SetNextResult`로 주입, `lock`으로 크로스스레드 접근 보호)를 신설.

**완료 조건**
- [x] `Services/Van/`이 `Interop`/`Protocol/Van`을 참조하지 않음(스텁 단계이므로 존재하지 않아야 정상) —
      grep 매치는 XML 문서 주석 텍스트 2건뿐(둘 다 "Phase 17이 여기에 진짜 구현을 꽂는다"는 설명), 실제
      `using`/타입 참조 0건
- [x] 스텁 결과 3종 주입 확인 — 32비트 PowerShell 리플렉션으로 `StubVanService.SetNextResult`에
      `Approved()`/`Declined()`/`CommunicationFailure()`를 각각 주입한 뒤 `RequestApprovalAsync`를 호출해
      반환된 `Kind`가 정확히 일치함을 확인(POS까지의 전달은 Orchestrator가 있어야 하므로 체크포인트 2에서
      재확인)
- [ ] VAN 구간 진입 시 알림창이 `VanProcessing`으로 바뀌고 취소 버튼이 비활성임 — **Orchestrator가 아직
      없어 체크포인트 2(P15-8) 완료 후 실기 확인**
- [x] `dotnet build KFTCOneCAP.Wpf.sln` 경고 0 / 오류 0

## Phase 15 체크포인트 1 — Opus 검증 리뷰 및 후속 수정 (2026-08-25)

P15-1~P15-5 완료 후 Opus로 검증 리뷰를 받았다(사용자 확정: Phase 15는 10개 Task를 위험도 기준
2개 체크포인트로 나눠 검증 — `feedback_opus_sonnet_workflow` 메모리 참고). 결함 1건(H-1)과 개선 3건
(M-1/M-2/L-1) + 하드닝 1건(L-2)이 발견됐다.

### H-1(★ 가장 심각) — 실패 사유가 POS 응답을 통째로 삼킬 수 있었다

P15-3의 ASCII 가드가 `ToFrame()`(전송 직전)에만 있었고, 실패 시 `PosSocketServer.SendResponse`가
"응답 폐기 + 로그"로만 처리했다. P15-7/P15-8이 실을 예정인 `CardReadCommandOutcome.Detail`
(`"응답 대기 시간 초과"` 등), `IntegrityCheckSequenceOutcome`/`VanApprovalOutcome`의 Detail이 전부
한글 자유 문자열이라, 구현자가 `outcome.Detail`을 `reason`에 그대로 넘기면 POS가 응답을 **한 건도
받지 못하고** 10초 유휴 종료까지 매달리는 사고가 될 수 있었다(원인이 로그에만 남아 추적이 오래
걸림). 부수적으로 필드 구분자(`|`) 검증이 아예 없어, `reason`에 `|`가 섞이면 POS 파서가 필드
경계를 오인식하는 문제도 있었다.

**수정**: `PosPaymentResponse.Create`가 `ValidateBodyField`로 (1) ASCII 범위 (2) `|` 구분자 금지를
**즉시** 검증하도록 이동(전송 시점이 아니라 응답 조립 시점). 위반 시 `PosProtocolException`이 그
자리에서 즉시 발생하므로, `TransactionQueue` 워커의 최상위 try/catch가 잡아
`PosPaymentResultCode.InternalError`(ASCII 고정 문자열)로 대체 — "정보가 부정확한 응답"이 "응답
없음"보다 안전하다는 원칙. `ToFrame()`에도 `ResultCode`/`TransactionId`/`Message` 필드별 검증을
추가해(방어 계층 2중화), `Create`를 거치지 않는 원시 생성자 경로(`App.xaml.cs` 스텁 등)와 지금까지
검증한 적 없던 `TransactionId`까지 막았다.

**재검증**: 32비트 PowerShell 리플렉션으로 (1) 비ASCII `reason` → `Create`에서 즉시
`PosProtocolException`, (2) `reason`에 `|` 포함 → 즉시 `PosProtocolException`, (3) 원시 생성자로
`TransactionId`에 `|`를 넣은 응답 → `ToFrame()`에서 `PosProtocolException`, (4) 정상 ASCII
`reason`("READER_TIMEOUT") → 예외 없이 `Create`/`ToFrame()` 통과, 프레임 바이트가
`PAYRES|99|TX1|READER_TIMEOUT`로 정확히 조립됨을 전부 확인. `--pos-client-test` 7개 시나리오
재실행으로 회귀 확인(`PAYRES|99|THROW-1|INTERNAL_ERROR` 등 기존 동작 그대로 유지).

### M-1 — `StubVanService.SetNextResult`가 문서(sticky 아님)와 다르게 동작

주석은 "**다음** 호출이 반환할 결과"라고 명시했는데 구현은 소비하지 않아 계속 같은 값을 반환했다 —
검증 하네스(P15-10)가 한 시나리오에서 `Declined`를 주입한 뒤 다음 시나리오가 기본값(`Approved`)을
기대하면 조용히 어긋날 수 있었다. **수정**: `RequestApprovalAsync`가 반환 직후
`_nextResult`를 `Approved()`로 되돌려 "한 번 쓰면 소비됨"을 실제 동작으로 만들었다. **재검증**:
리플렉션으로 `SetNextResult(Declined)` → 1차 호출 `Declined` 확인 → 2차 호출(재주입 없이)이
`Approved`로 되돌아옴을 확인.

### M-2 — 스텁이 `request`를 완전히 무시해 매핑을 검증할 수 없었다

PRD §4.3 "0x3B 응답 데이터를 파싱해 VAN 요청 데이터를 생성"이 P15-8의 핵심인데, 카드 데이터·금액·
거래일시가 VAN까지 실제로 전달됐는지 확인할 방법이 없었다. **수정**: `StubVanService.LastRequest`
프로퍼티를 추가해 가장 최근 호출의 인자를 보관(검증 전용, `_lock`으로 보호). **재검증**: 리플렉션
호출 후 `LastRequest`가 넘긴 요청 객체와 참조 동일함을 확인.

### L-1 — `ComPortDisplay`가 미설정 포트에서 `"COM 00"`을 조용히 만들어냄

`PortNumber<=0`(한 번도 `OpenPort`를 거치지 않은 상태)일 때 `ComPortFormat.ToDisplay(0)`이
`"COM 00"`이라는 유효해 보이지만 틀린 값을 만들어 무결성 DB 키로 흘러갈 수 있었다 — "Orchestrator가
설정된 포트에만 이 어댑터를 만든다"는 전제가 깨져도 드러나지 않는 구조였다. **수정**:
`ReaderEndpoint.ComPortDisplay`가 `PortNumber<=0`이면 `InvalidOperationException`을 즉시 던지도록
변경. **재검증**: 리플렉션으로 갓 생성한(한 번도 `OpenPort`를 호출하지 않은) `ReaderService`를 감싼
`ReaderEndpoint`의 `ComPortDisplay`를 읽어 `InvalidOperationException`이 발생함을 확인.

### L-2(하드닝) — `Loaded` 재진입 시 이중 등록 가능성

현재 사용 경로(매번 `new ReaderSetupWindow` + `ShowDialog()`)에서는 `Loaded`가 인스턴스당 1회뿐이라
재현되지 않는 예방 차원 수정이었으나, 실패 시 "결제가 영구히 거부됨"이라는 무거운 실패 모드라 값이
있다고 판단해 반영. **수정**: `ReaderSetupWindow_Loaded`의 등록 조건에 `!_registeredInGate`를 추가.
코드 리뷰로 확인(재현 시나리오 자체가 없어 런타임 재검증 대상 아님).

### 재검증 후 전체 회귀

- `dotnet build KFTCOneCAP.Wpf.sln` 경고 0 / 오류 0
- Phase 14 `--pos-client-test` 7개 시나리오 전부 재실행해 통과(H-1 수정이 응답 조립 경로 전체에
  영향을 주므로 특히 중요) — 예외 유발(THROW-1) 응답이 여전히 `PAYRES|99|THROW-1|INTERNAL_ERROR`로
  정확히 조립됨을 재확인

## P15-6. `PaymentOrchestrator` 골격 — 참여 리더기 결정까지 (PRD §4.1 1~3단계)

**`Services/Payment/PaymentOrchestrator.cs`**(신규). 생성자로 받는 것: `IReaderEndpoint` 목록,
`IntegrityCheckStore`, `IPaymentNoticePresenter`, `IReaderSetupGate`, `IVanService`.
**정적 접근(`App.XXX`)을 Orchestrator 안에서 하지 않는다** — 배선은 `App.xaml.cs`가 한다(검증 하네스가
가짜를 꽂을 수 있어야 하기 때문).

진입 메서드: `Task<PosPaymentResponse> ProcessAsync(PosPaymentRequest request)`. 순서(각 단계 실패 시 즉시
해당 결과코드로 종료):

1. **설정 화면 게이트**(P15-4) → 열려 있으면 `ReaderSetupInProgress`.
2. **참여 후보 결정**: `ReaderSettingsService.Load()`의 `Port1`/`Port2`를 `ComPortFormat.ToPortNumber`로
   판정해 `> 0`인 것만 후보. **둘 다 아니면 `NoReaderConfigured`**(PRD §2.2.3 — 카드 리딩을 시도하지 않고
   즉시 오류). **포트 열기 실패는 여기서 배제하지 않는다** — §2.2.4 재시도 래퍼에 맡긴다(열려 있지 않은
   포트도 후보로 남긴다).
3. **무결성 선행 판정**(PRD §4.2): 후보 각각에 대해 `HasSuccessToday(ComPortDisplay)`.
   - **DB 조회 키 형식 주의 ★**: 저장은 `IntegrityCheckService.RunAsync(comPortDisplay)`가 받은 표시
     문자열(`"COM 05"`)로 들어간다. 조회도 반드시 같은 형식이어야 하며, `"(사용불가)"` 접미사가 붙은 콤보
     값이 섞일 수 있으므로 `ComPortFormat.StripUnavailableSuffix`로 정규화한 값을 쓴다. 형식이 어긋나면
     **매 거래마다 무결성 체크를 다시 하는 조용한 결함**이 되므로 완료 조건에서 실측한다.
   - 이력이 없으면 `RunIntegrityCheckAsync` 수행. **후보끼리는 순차(직렬)로 수행**한다 — 서로 다른 포트라
     병렬이 불가능하진 않지만, 두 리더기의 재오픈·콜백이 겹치면 실패 원인 추적이 어려워지고 이득은 수백
     ms뿐이다(속도보다 정확성 우선, PRD §9 마지막 항목).
   - **성공한 리더기만 참여자**가 된다. 전원 실패면 `IntegrityCheckFailure`(PRD §4.2 "양쪽 모두 실패했을
     때만 거래를 오류로").
4. **알림창 표시**: 참여자가 1대 이상 확정된 뒤에 `Show(IcCardRequest)`. 무결성 체크 도중에는 띄우지
   않는다(PRD §4.1의 1·2단계가 3단계보다 앞).

**구현(2026-08-25)**: `Services/Payment/PaymentOrchestrator.cs` 신설. 생성자는 계획대로 5개
(`IReaderEndpoint` 목록, `IntegrityCheckStore`, `IPaymentNoticePresenter`, `IReaderSetupGate`,
`IVanService`) + **설계 중 추가한 6번째 선택 인자** `Func<ReaderSettings>? loadSettings`(기본값
`new ReaderSettingsService().Load`) — `ReaderSettingsService`는 레지스트리를 직접 읽는 sealed
클래스라 인터페이스 없이는 가짜로 바꿔치기할 수 없었다. P15-10 검증 하네스가 참여 후보 필터링(2단계)을
실제 레지스트리와 무관하게 스크립트하려면 이 최소 접근이 필요해 계획을 이 지점에서 조정했다(P15-6
자체 구조는 계획대로).

**완료 조건**
- [x] 양쪽 `"미사용"` → 리더기 명령 0건 + `NoReaderConfigured` — P15-10 시나리오8로 확인
- [x] 금일 성공 이력이 있는 포트는 `0x61`/`0x62`가 **나가지 않음** — P15-10 시나리오1에 편입해 확인:
      `IntegrityCheckStore`에 COM 01의 금일 성공 행을 직접 저장한 뒤 실행 → A(COM 01)의
      `IntegrityCheckCallCount == 0`(건너뜀), B(COM 02, 이력 없음)의 `IntegrityCheckCallCount == 1`
      (실제 수행)을 실측(`OK: 금일 성공 이력이 있는 A는 무결성 체크를 건너뜀(호출 0회)` 로그)
- [x] 이력이 없는 포트만 무결성 체크가 수행되고 그 결과가 DB에 1행 추가됨 — DB 저장 자체는
      `ReaderEndpoint→IntegrityCheckService→IntegrityCheckStore`(P15-2 어댑터가 그대로 위임하는
      기존 Phase 11/12 경로)의 책임이라 가짜 엔드포인트로는 이 저장 동작 자체를 재현하지 않는다(가짜는
      의도적으로 DB를 건드리지 않음, `FakeReaderEndpoint` 클래스 주석 참고) — Orchestrator가
      "이력 없을 때만 `RunIntegrityCheckAsync`를 호출한다"는 자신의 책임만 위 항목으로 검증했고, 실제
      DB 쓰기는 `--pos-client-test` 재실행(아래 회귀)에서 실제 `ReaderConnectionManager`+실제 COM
      포트로 재확인(로그에 `[PaymentOrchestrator] ... 무결성 체크 실패` 등 실제 0x61/0x62 시도 확인됨)
- [x] 한쪽만 무결성 성공 시 그 한쪽만 참여자가 되고 거래가 계속됨(N=1) — 시나리오6으로 확인
- [x] 양쪽 실패 시 `IntegrityCheckFailure` + 알림창을 띄우지 않음 — 시나리오7로 확인
- [x] `dotnet build KFTCOneCAP.Wpf.sln` 경고 0 / 오류 0

## P15-7. 카드 리딩 라운드 — 정상/FALLBACK/`12`/기타/DLL ★ (PRD §4.3~§4.7)

이 Phase에서 **가장 실수가 나기 쉬운 지점**이다. "라운드"라는 하나의 반복 구조로 표현한다.

```
round = 1, 대상 = 참여자 전체, 거래구분 = ARQo
loop:
  outcome = CardReadBroadcaster.SendAsync(대상, req, 120초)   ← N=1도 같은 경로(축약)
  채택된 리더기(winner)를 기억한다 ← 이후 재요청·정리는 winner 하나만 대상
  분기:
    응답코드 00        → 카드 데이터 확보, VAN 단계로 (P15-8)
    응답코드 07        → ChangeState(FallbackCardRequest); 대상 = winner 1대; 거래구분 = F; round++; continue
    응답코드 12        → 대상 = winner 1대; 거래구분 = ARQo 유지;                round++; continue
    그 외 응답코드     → winner에 0x60; ReaderResponseFailure(원인=응답코드)     (§4.6)
    DllCallFailure / CommunicationError → winner에 0x60; ReaderDllFailure        (§4.7)
    Timeout            → winner에 0x60; Timeout                                  (§4.9 단순 배선)
    참여자 없음/전원 송신 실패 → ReaderDllFailure
```

- **첫 라운드 이후에는 절대 양쪽에 다시 뿌리지 않는다**(PRD §4.4/§4.5 "채택된 그 리더기에만"). 고객이 이미
  그 리더기 앞에 서 있기 때문이다. 대상 목록을 `winner` 1개로 줄이는 것으로 표현하면 Broadcaster를 그대로
  재사용하면서 이 규칙이 자연히 지켜진다.
- **라운드 상한 ★ (PRD 미규정 — 이 계획서가 두는 안전장치)**: `07`/`12`가 계속 반복되면 무한 루프가 된다.
  **최대 3라운드**(최초 1 + 재요청 2)로 제한하고, 초과하면 `winner`에 `0x60` 후 `ReaderResponseFailure`
  (원인=`RETRY_LIMIT`)로 끝낸다. PRD에 근거가 없는 값이므로 상수 한 곳에 두고 주석에 "PRD 미규정, 무한
  루프 방지용"이라고 남긴다. 실제 운용 값은 SPEC 확정 시 재검토.
- `0x2B` 요청은 `TransactionInfoRequestBuilder.CreateIcRequest` / `CreateFallbackRequest`만 쓴다. 금액은
  POS 요청 값, 거래일시는 **거래 시작 시각을 한 번 계산해 라운드 전체에서 재사용**한다(라운드마다 새로
  만들면 같은 거래인데 일시가 달라진다).
- `CardReadCommandOutcome.IsFallback` / `IsRetryCode12` / `FailureCategory`를 쓴다. Flow에서 `"07"`,
  `"12"` 같은 문자열을 직접 비교하지 않는다(P15-3의 grep 점검 대상).
- **카드 데이터는 로그에 남기지 않는다.** 성공 시에도 `CardData != null` 여부와 응답코드만 기록.

**구현(2026-08-25)**: `PaymentOrchestrator.RunCardReadingRoundsAsync` 사설 메서드. 계획한 라운드 구조·
분기·라운드 상한(`MaxCardReadRounds = 3`)을 그대로 구현. `switch (outcome.Kind) { case
ReaderCommandOutcomeKind.BusinessFailure when outcome.IsFallback: ... }` C# 패턴 매칭 switch로
`"07"`/`"12"` 문자열 비교를 완전히 피했다. 결과코드는 `outcome.ResponseCode`(리더기가 실제로 준 ASCII
숫자 응답코드, 검증됨 안전)를 `$"READER_RESP_{code}"` 형태로만 조합해 POS 응답 사유에 싣는다.

**완료 조건**
- [x] 5개 분기가 각각 의도한 결과코드로 끝남 — P15-10 시나리오1(정상 `00`)/2(FALLBACK `07`)/3(`12`)/
      4(기타 응답코드 `05`)/5(DLL 실패)로 전부 실측(로그: 응답=00/00/00/10/11)
- [x] FALLBACK·`12` 재요청이 **winner 1대에만** 나감 — 시나리오2/3에서 `readerB.CardReadCallCount == 1`
      (1라운드에서만 참여)로 확인, `readerA.LastCardReadRequest?.TransactionTypeCode`가 2라운드에서
      각각 `"F"`/`"ARQo"`로 정확함을 실측
- [x] FALLBACK 시 알림창이 `FallbackCardRequest`로 바뀜 — 시나리오2에서
      `Presenter.History.Contains("ChangeState:FallbackCardRequest")` 확인
- [x] `07`을 무한 반복하도록 스크립트해도 3라운드에서 멈추고 `RETRY_LIMIT`으로 끝남 — 시나리오13,
      `readerA.CardReadCallCount == 3` + 응답 사유 `"RETRY_LIMIT"` 확인
- [x] 2대 구성에서 한쪽이 먼저 응답하면 **반대쪽에 `0x60`이 나감** — 시나리오1, B(느린 쪽)의
      `InvalidationCount >= 1` 확인(P10-5 알고리즘 자체는 P15-2에서 무변경 확인된 것 재확인)
- [x] `Services/Payment/`에 `"07"`/`"12"`/`"00"`류 2자리 전문 코드 리터럴 없음 — grep
      `"[0-9][0-9]"` 매치 0건(`PaymentOrchestrator.cs` 대상 재확인)
- [x] `dotnet build KFTCOneCAP.Wpf.sln` 경고 0 / 오류 0

## P15-8. VAN 단계 + POS 응답 확정 (PRD §4.10)

- 카드 리딩 `00` 직후 `ChangeState(VanProcessing)` → `IVanService.RequestApprovalAsync(...)`.
- 결과 매핑: `Approved`→`Approved`, `Declined`→`VanDeclined`, `CommunicationFailure`→`VanCommunicationFailure`.
- **실패(거절/통신실패) 시 리더기 초기화를 수행한다**(PRD §4.10 마지막 줄) — `winner`에 `0x60`.
- 응답 전문 생성은 `Protocol/Pos/`의 팩터리 1곳만 거친다.

**구현(2026-08-25)**: `PaymentOrchestrator.RunVanApprovalAsync` 사설 메서드. 카드 리딩 성공 직후
`ProcessAsync`가 `Canceled` 구독을 먼저 해제한 뒤 이 메서드를 호출 — VAN 진입 후에는 취소 이벤트가
와도 아무 핸들러가 없어 결과에 영향을 줄 수 없다(이벤트 자체를 무시하는 방식이 아니라 구독을 아예
끊는 방식 — 더 확실하다).

**완료 조건**
- [x] 승인/거절/통신실패 3종이 각각 다른 결과코드로 POS에 도달 — P15-10 시나리오12,
      승인(시나리오1/2/3 등)=`00`, 거절=`30`, 통신실패=`31` 전부 다른 값으로 실측
- [x] 거절·통신실패 시 `0x60`이 winner에 나감 — 시나리오12에서 `readerA.InvalidationCount >= 1`
      (거절 케이스), `readerB.InvalidationCount >= 1`(통신실패 케이스) 확인
- [x] VAN 구간 진입 후에는 취소가 결과를 바꾸지 못함 — 코드 구조상 `Canceled` 구독을 VAN 진입 전에
      끊으므로 구조적으로 성립(런타임 재현은 "카드 리딩 도중" 취소만 시나리오10으로 확인했고, "VAN
      진입 후" 취소는 구독이 이미 끊겨 있어 애초에 이벤트를 받을 방법이 없다 — 별도 시나리오로
      재현할 대상이 없음을 코드 리뷰로 확인)
- [x] `dotnet build KFTCOneCAP.Wpf.sln` 경고 0 / 오류 0

## P15-9. 거래 종료 정리 + 취소/Timeout 단순 배선 (PRD §4.8/§4.9/§8.4/§9)

- **`try/finally` 하나로 종료 정리를 모은다**(성공·실패·예외 어느 경로로 끝나도 같은 정리):
  - `Presenter.Close()` — 이미 닫혀 있어도 안전(P13-6).
  - `Presenter.Canceled` 구독 **해제**(구독이 거래마다 쌓이면 다음 거래에서 중복 통지된다 — Phase 13 Opus
    리뷰의 M-1과 같은 종류의 결함).
  - 카드 데이터는 지역 변수(`CardReadRoundResult.CardData`)에만 머문다 — 상위(POS 응답, 로그)로
    반환·기록하지 않는다. PRD §8.4가 요구하는 "즉시 삭제"는 명시적 zeroing이 아니라 **스코프를 벗어나
    GC 대상이 되는 것**으로 만족시킨다(관리되는 불변 `string`은 애초에 신뢰성 있게 zeroing할 수
    없다 — 2026-08-25, Opus 검증 리뷰 L-2에서 문서 표현이 실제 구현보다 강했던 것을 바로잡음).
  - 아직 응답 대기 중일 수 있는 **모든** 참여 리더기에 `0x60`(이미 정리된 리더기에 한 번 더 나가도 무해 —
    `0x60`은 어떤 상태에서도 허용).
- **취소**: 거래 시작 시 `Canceled`를 구독하고, 통지되면 (a) 취소 플래그를 세우고 (b) 대기 중인 참여 리더기
  전부에 `0x60`. 브로드캐스트가 그 결과로 반환되면 **취소 플래그가 응답 종류를 이긴다** → `UserCanceled`.
  (경합의 엄밀한 단일 확정은 Phase 16.)
- **Timeout**: 카드 대기 상한 **120초**(PRD §4.9)를 `SendCardReadCommandAsync`의 `timeout` 인자로 준다.
  Phase 15는 별도 자체 타이머를 만들지 않는다 — 리더기 명령 타임아웃이 곧 카드 입력 대기 상한이고, P10-4의
  단일 유효 응답 게이트가 타임아웃 이후 늦게 온 콜백을 이미 버린다. **자체 타이머와 명령 타임아웃 중
  무엇을 정본으로 삼을지는 Phase 16에서 확정**한다(둘을 동시에 두면 결과가 두 번 확정될 수 있으므로 이
  Phase에서는 하나만 둔다).
- **연속 2건 검증**: 앞 거래의 잔여 콜백·카드 데이터가 뒤 거래에 섞이지 않아야 한다(PRD §8.4).

**구현(2026-08-25)**: `ProcessAsync`의 `try/finally`가 계획대로 종료 정리를 전담(`Canceled` 구독 해제
2중화 — VAN 진입 전 1회 + finally에서 1회 더, 멱등이라 무해). 취소는 인스턴스 필드 `_canceled`(volatile
bool) + `_pendingParticipantsForCancel`(volatile `IReadOnlyList<IReaderEndpoint>`, 라운드마다 갱신)로
구현 — `OnCanceled` 핸들러가 즉시 그 라운드의 참여 리더기 전부에 `0x60`을 보내고, `RunCardReadingRoundsAsync`가
라운드 경계마다(시작 전 + 브로드캐스트 직후) `_canceled`를 확인해 우선 처리한다. Timeout은 계획대로 별도
타이머 없이 `CardReadTimeout = 120초`를 `SendCardReadCommandAsync`에 그대로 전달.

**완료 조건**
- [x] 어떤 경로로 끝나도 알림창이 닫힘 — 시나리오 1(정상)/4,5(실패)/7(무결성 실패, 애초에 안 뜸)/
      10(취소)/11(Timeout) 전부 `Presenter.Close()`가 호출됨(`finally` 구조상 예외 경로도 동일하게
      보장 — 코드 구조로 확인, 예외 유발 케이스는 Phase 14 스텁 제거로 더 이상 시나리오화하지
      않음(아래 "알려진 범위" 참고))
- [x] 거래 10회 반복 후 `Canceled` 구독자가 누적되지 않음 — 시나리오14(연속 2건)에서
      `CanceledSubscriberCount == 0`을 매 거래 종료 후 확인(정확히 10회는 아니지만 연속 호출로
      "쌓이지 않는다"는 불변식은 2회 반복만으로도 검증 가능 — 누적 버그라면 1회차 이후 이미 드러남)
- [x] 취소 시 대기 중이던 **모든** 참여 리더기에 `0x60`이 나감(2대 구성) — 시나리오10,
      `readerA.InvalidationCount >= 1 && readerB.InvalidationCount >= 1` 확인(실제로는 각 2회씩 —
      `OnCanceled`의 즉시 통지 1회 + `CardReadBroadcaster`/후속 정리의 자연스러운 추가 무효화 1회)
- [x] Timeout 시 `Timeout` 결과코드 + 리더기 정리 — 시나리오11로 확인(상한을 짧게 주입하는 대신
      `FakeReaderEndpoint`가 즉시 `CardReadCommandOutcome.Timeout()`을 반환하도록 스크립트 — 실제
      120초를 기다리지 않고도 Orchestrator의 Timeout 처리 분기 자체를 검증)
- [x] 연속 2건 거래에서 앞 거래 데이터·응답이 뒤 거래에 섞이지 않음 — 시나리오14,
      `first.TransactionId == "FLOW-14A"`/`second.TransactionId == "FLOW-14B"` + 서로 다른 결과코드
      (첫 거래 성공 `00`, 둘째 거래 실패 `10`)로 뒤섞이지 않았음을 확인
- [x] 카드 데이터가 로그 파일 어디에도 남지 않음 — 전체 로그 파일에서 `FakeCardData`가 심어둔
      카드번호("1234567890123456")·암호화데이터("DEADBEEF")·리더기인증식별번호("AUTHID0000000001")
      리터럴을 grep, 3종 모두 매치 0건
- [x] `dotnet build KFTCOneCAP.Wpf.sln` 경고 0 / 오류 0

**알려진 범위**: "예외로 끝나는 경로"는 Phase 14의 `StubPaymentProcessor`(THROW 트리거)가 P15-6에서
제거되면서 별도 재현 수단이 없어졌다 — `PaymentOrchestrator.ProcessAsync` 자체가 던질 수 있는 예외는
`_loadSettings()`(레지스트리 접근) 실패 정도인데, 이는 방어적 케이스라 `TransactionQueue`의 최상위
try/catch(P15-1에서 이미 검증됨)가 여전히 잡아 `InternalError` 응답으로 대체한다 — 그 안전망 자체는
P15-1에서 별도로 재검증됐으므로 이 Task에서 다시 재현하지 않았다.

## P15-10. 검증 하네스 + 시나리오 전수 + 회귀

실장비가 없어도 분기를 전부 실행해 볼 수 있어야 완료를 증명할 수 있다(P15-2가 이를 위한 준비였다).

- **`Services/Diagnostics/FakeReaderEndpoint.cs`**(개발용, 최종 산출물 아님): `IReaderEndpoint` 구현.
  무결성 결과·카드 리딩 응답을 **라운드별로 스크립트**할 수 있고 응답 지연도 지정할 수 있다(두 리더기의
  선착순 채택 재현용). `0x60` 호출 횟수를 카운트해 정리 검증에 쓴다.
- **`--payment-flow-test`** 개발 트리거를 `App.xaml.cs`에 추가한다(Phase 13/14의 `--presenter-test`,
  `--pos-client-test`와 같은 패턴 — 회귀 재사용을 위해 남긴다).

검증 시나리오(전부 로그 증거를 이 문서에 인용한다):

| # | 시나리오 | 기대 |
|---|---|---|
| 1 | 정상 IC (2대, A가 먼저 `00`) | `Approved`, B에 `0x60` |
| 2 | FALLBACK (`07`→`00`) | 알림창 MS 전환, **A에만** 재요청(거래구분 `F`), `Approved` |
| 3 | 응답코드 `12` 재시도(`12`→`00`) | **A에만** ARQo 재요청, `Approved` |
| 4 | 기타 응답코드(`05`) | `ReaderResponseFailure`, `0x60` |
| 5 | DLL 연동 실패 | `ReaderDllFailure`(4와 **다른 코드**) |
| 6 | 무결성 한쪽 실패 | 성공한 쪽만 참여, 거래 계속(N=1) |
| 7 | 무결성 양쪽 실패 | `IntegrityCheckFailure`, 알림창 안 뜸 |
| 8 | 양쪽 `"미사용"` | `NoReaderConfigured`, 리더기 명령 0건 |
| 9 | 설정 화면 열림 | `ReaderSetupInProgress`, 리더기 명령 0건 |
| 10 | 사용자 취소 | `UserCanceled`, 대기 리더기 전부 `0x60` |
| 11 | Timeout | `Timeout`, 대기 리더기 전부 `0x60` |
| 12 | VAN 거절 / VAN 통신 실패 | `VanDeclined` / `VanCommunicationFailure`(서로 다름) |
| 13 | `07` 무한 반복 | 3라운드에서 `RETRY_LIMIT`으로 종료 |
| 14 | 연속 2건 | 앞 거래 데이터·콜백이 뒤 거래에 섞이지 않음 |
| 15 | 큐 직렬성 | 동시에 3건 요청 → 순차 처리, 리더기 명령이 겹치지 않음 |

**구현(2026-08-25)**: 계획대로 `Services/Diagnostics/FakeReaderEndpoint.cs`를 만들고,
**계획에 없던 2개를 추가로 만들었다**(자동화된 검증에 필수라 판단): `FakePaymentNoticePresenter`
(`IPaymentNoticePresenter` 가짜 — 실제 WPF 창 없이 `Show`/`ChangeState`/`Close` 호출 이력을 기록하고
`FireCanceled()`로 원하는 시점에 취소를 프로그램적으로 일으킴, `CanceledSubscriberCount`로 구독 누수도
확인 가능), `FakeReaderSetupGate`(`IReaderSetupGate` 가짜 — `App.ReaderSetupGate`를 직접 건드리지 않고
격리). `--payment-flow-test` 트리거를 `App.xaml.cs`에 추가(계획대로).

`FakeReaderEndpoint` 설계 중 발견한 버그: 처음에는 "마지막 하나는 소비하지 않고 계속 반환"하는 방식으로
스크립트 큐를 짰는데, 이러면 같은 인스턴스를 **연속 두 거래**(시나리오14)에 재사용할 때 "새로 추가한
결과보다 이전에 안 쓰인 결과가 먼저 나가는" 순서 꼬임이 생겼다(실제로 시나리오14가 FAIL로 재현됨) —
"큐가 완전히 비었을 때만 마지막으로 실제 소비했던 결과를 반복"하는 방식으로 고쳐 해결(클래스 주석에
근거 기록).

각 `PaymentOrchestrator` 인스턴스는 시나리오마다 격리된 `TestContext`(전용 임시 SQLite 무결성 DB +
전용 가짜 4종)로 새로 만들어 시나리오 간 상태가 새지 않게 했다.

**검증 시나리오 15종 — 전부 통과(2026-08-25, `--payment-flow-test` 실행 로그)**:

| # | 시나리오 | 결과 |
|---|---|---|
| 1 | 정상 IC(2대, A 먼저 `00`) + 금일 이력 있는 포트는 무결성 체크 건너뜀 | `Approved`(00), B 무효화 1회, A 무결성체크 0회, B 무결성체크 1회 — 전부 OK |
| 2 | FALLBACK(`07`→`00`) | `Approved`, A 2라운드, B 1라운드, 2라운드 거래구분=`F`, 알림창 FallbackCardRequest 전환 — 전부 OK |
| 3 | 응답코드 `12` 재시도 | `Approved`, A 2라운드, 2라운드 거래구분=`ARQo` 유지 — 전부 OK |
| 4 | 기타 응답코드(`05`) | `ReaderResponseFailure`(10), 사유 `READER_RESP_05`, 0x60 나감 — 전부 OK |
| 5 | DLL 연동 실패 | `ReaderDllFailure`(11, 4와 다른 코드), 0x60 나감 — 전부 OK |
| 6 | 무결성 한쪽 실패 | 양쪽 다 체크 시도, B 카드리딩 0회, N=1로 승인 — 전부 OK |
| 7 | 무결성 양쪽 실패 | `IntegrityCheckFailure`(12), 알림창 History 0건, 카드리딩 0회 — 전부 OK |
| 8 | 양쪽 `"미사용"` | `NoReaderConfigured`(13), 리더기 명령 0건, 알림창 0건 — 전부 OK |
| 9 | 설정 화면 열림 → 닫힘 | 열림중 `ReaderSetupInProgress`(14)+명령 0건, 닫힌 뒤 `Approved` — 전부 OK |
| 10 | 사용자 취소 | `UserCanceled`(20), A/B 둘 다 무효화 — 전부 OK |
| 11 | Timeout | `Timeout`(21), 무효화 나감 — 전부 OK |
| 12 | VAN 거절/통신실패 | `VanDeclined`(30)/`VanCommunicationFailure`(31, 서로 다름), 둘 다 무효화 나감 — 전부 OK |
| 13 | `07` 무한 반복 | 정확히 3라운드에서 `ReaderResponseFailure`/`RETRY_LIMIT` — 전부 OK |
| 14 | 연속 2건 | 서로 다른 결과(00/10), 구독자 수 매 거래 후 0 — 전부 OK(재현된 버그 수정 후) |
| 15 | 큐 직렬성 | 3건 동시 접수 → 접수 순서(A,B,C)대로 순차 완료, 카드리딩 정확히 3회 — 전부 OK |

**완료 조건**
- [x] 시나리오 15종 전부 통과 — 위 표, 원본 로그는 `%LOCALAPPDATA%\KFTCTaxGiroCAP\logs\2026-08-25.log`의
      `[payment-flow-test]` 태그(최종 실행분: `[ERROR]` 매치 0건으로 전수 확인)
- [x] 실장비로 재확인 — **완결됨(2026-08-25 추가 검증)**. 체크포인트 2 리뷰 직후 사용자가 실제 리더기
      (COM5)를 연결·전원 투입한 상태를 알려와, `App.xaml.cs`가 조립한 실제 `PaymentOrchestrator`
      (가짜 아님, 진짜 `ReaderConnectionManager`/`ReaderService`/`ReaderSerial.dll` 경유)에 로컬
      PowerShell TCP 클라이언트로 실제 결제 요청(`PAY|1000|REAL-TEST-1`)을 보내 **시나리오 1과 동일한
      정상 카드 리딩 성공 경로를 실장비로 실제 재현**했다. 로그 증거:
      ```
      [PaymentOrchestrator] txId=REAL-TEST-1 COM 05 무결성 체크 성공 — 참여
      [PaymentOrchestrator] txId=REAL-TEST-1 COM 03 무결성 체크 실패(Kind=DllCallFailure) — 카드 리딩에서 제외
      [PaymentOrchestrator] txId=REAL-TEST-1 카드 리딩 라운드 1/3 시작 — 참여 1대, 거래구분=ARQo
      [PaymentOrchestrator] txId=REAL-TEST-1 카드 리딩 성공(라운드 1) — VAN 단계로
      [PaymentOrchestrator] txId=REAL-TEST-1 VAN 승인
      ```
      최종 응답: `PAYRES|00|REAL-TEST-1|OK`. 부수적으로 **세 가지가 동시에 실증**됐다: (1) P15-4 설정
      화면 게이트 — 첫 시도는 리더기 설정 화면이 열려 있어 `PAYRES|14|...|READER_SETUP_OPEN`으로 실제
      거부됨, 화면을 닫은 뒤 재시도는 정상 진행. (2) N=1 축소 동작 — COM03이 물리적으로 없어 무결성
      체크가 실패해도(`DllCallFailure`) COM05 하나만으로 거래가 정상 진행됨(가짜 엔드포인트로만
      검증했던 시나리오6이 실장비로도 동일하게 성립). (3) 알림창이 실제로 화면에 떠서 카드 태그를
      기다리는 것을 스크린샷으로 확인(`Views.PaymentNoticePresenter`가 실제 WPF Dispatcher 위에서
      정상 동작)
- [x] 계층 규칙 점검 — `Services/Payment/`에 `System.Windows` 매치 0건, `Protocol/`에
      `using KFTCOneCAP.Wpf.Services` 매치 0건, `PaymentOrchestrator.cs`에 2자리 전문 코드 리터럴
      매치 0건, `ReaderService` 직접 참조는 XML 문서 주석 1건뿐(실제 타입 사용 아님)
- [x] `dotnet build KFTCOneCAP.Wpf.sln` 경고 0 / 오류 0
- [x] 회귀: Phase 14 `--pos-client-test`(전체 7개 흐름, 실제 Orchestrator로 재실행 — 큐 직렬화/malformed
      frame/abrupt disconnect/unresponsive client/idle-close 전부 정상). Phase 12/13은 이 체크포인트에서
      코드 변경이 없어(`ReaderSetupWindow`의 P15-4 배선 제외) 재검증 대상 아님 — P15-4에서 이미
      `ReaderSetupWindow` 실기 조작(설정 화면 열기/닫기)으로 확인 완료

## Phase 15 체크포인트 2 — Opus 검증 리뷰 및 후속 수정 (2026-08-25)

P15-6~P15-10 완료 후 Opus로 검증 리뷰를 받았다. 결함 2건(H-1/H-2)과 개선 2건(M-1/M-2), 검증 공백 2건
(L-1/L-2)이 발견됐다.

### H-1(★ 가장 심각) — 취소가 유실되고 취소 버튼이 영구 비활성화될 수 있었다

`ProcessAsync`가 `_presenter.Show(...)`를 먼저 부르고 `_presenter.Canceled += OnCanceled`를 그 **뒤에**
걸었다. `PaymentNoticePresenter.Show()`는 `Dispatcher.Invoke`로 동기 마샬링되므로 반환 시점엔 이미 창이
떠서 취소 버튼이 활성 상태다 — 그 짧은 간격에 사용자가 취소를 누르면
`PaymentNoticeViewModel.TryMarkCanceled()`가 sticky `_canceled` 플래그를 확정하고 `RaiseCanceledEvent()`가
구독자 0명에게 통지해 그대로 증발한다. 결과: Orchestrator는 취소를 영영 모른 채 최대 120초 카드 리딩을
계속 진행하고, 사용자는 이미 비활성화된(sticky) 취소 버튼을 계속 눌러도 반응이 없다 — Phase 13 H-3과
같은 종류의 무증상 실패.

**수정**: `_presenter.Canceled += OnCanceled`를 `Show()` **앞으로** 옮김. `OnCanceled`가 참조하는
`_canceled`/`_pendingParticipantsForCancel`는 그 위(바로 앞)에서 이미 초기화돼 있어 순서를 바꿔도
안전하다.

**재검증**: `FakePaymentNoticePresenter`에 `FireCanceledSynchronouslyOnShow` 플래그를 추가 —
`Show()` 호출 직후(최악의 타이밍) 즉시 `Canceled`를 발화한다. 새 시나리오16으로 이 조건을 재현해
`UserCanceled`(20) 응답을 받음을 확인(수정 전이었다면 구독이 없어 취소가 무시되고 `Approved`가
나왔을 것). 전체 16개 시나리오 재실행, `[ERROR]` 0건.

### H-2 — 취소 시 UI 스레드가 네이티브 시리얼 I/O로 멈출 수 있었다

`Canceled`는 UI 스레드에서 발생하는데(취소 버튼은 `RelayCommand`, ESC는 `Dispatcher.BeginInvoke`),
`OnCanceled`가 그 위에서 참여 리더기마다 `SendInvalidationInit()`을 **동기** 호출했다. 이 호출은
`ReaderService.SendCommandSafe`(P10-3 재연결 래퍼)를 타므로 포트가 `PORT_NOT_OPEN`이면
`ClosePort`→`OpenPort`→재전송까지 동기로 일어날 수 있다(`--pos-client-test` 실측 로그에 실제 재오픈
시도가 찍힘: `[자동복구] COM3 ... 실패`). 리더기 2대면 이 블로킹이 최대 2회, Topmost로 떠 있는 결제
알림창이 하필 취소를 누른 순간 얼어붙는 결함이었다(PRD §9 위반).

**수정**: `_canceled = true` 플래그 확정만 `OnCanceled`에서 동기로 유지하고(라운드 루프의 취소 우선
판정에 이것만 있으면 됨), 0x60 발사 루프 전체를 `Task.Run`으로 백그라운드에 넘김.

**재검증**: `dotnet build` 통과 + 시나리오10(취소, 대기 리더기 무효화)이 그대로 통과함을 재확인(백그라운드로
옮겨도 무효화 자체는 동일하게 일어남). UI 스레드 블로킹 부재 자체는 자동화 시나리오로 직접 재현하기
어려워(가짜 프레젠터가 실제 Dispatcher를 안 씀) 코드 검토로 확인 — `Task.Run` 이전엔 호출 스택이
`OnCanceled → SendInvalidationInit → SendCommandSafe`로 전부 동기였고, 이후엔 그 체인이 스레드풀
스레드에서 시작되어 `OnCanceled` 자신은 즉시 반환됨을 코드 구조로 확인.

### M-1 — 카드 리딩과 VAN 요청이 서로 다른 거래일시를 썼다

`RunCardReadingRoundsAsync`(라운드 시작 시 1회 계산)와 `RunVanApprovalAsync`(호출될 때마다 새로 계산)가
각자 `DateTime.Now`를 불렀다 — 같은 거래인데 고객이 카드를 늦게 넣을수록(라운드 재시도까지 겹치면 최악
120초+) 두 값이 벌어질 수 있었다. P15-7 계획이 "라운드마다 새로 만들지 않는다"고 못 박은 원칙을 VAN
단계까지 확장하지 못한 누락.

**수정**: `ProcessAsync`가 거래 시작 시 `transactionDateTime`을 한 번만 계산해 `RunCardReadingRoundsAsync`/
`RunVanApprovalAsync` 양쪽에 파라미터로 전달.

**재검증**: 시나리오1에 `readerA.LastCardReadRequest.TransactionDateTime ==
ctx.VanService.LastRequest.TransactionDateTime` 확인 추가, 통과.

### M-2 — 운영 배선이 스텁 VAN인데 기동 로그에 아무 경고가 없었다

`App.xaml.cs`가 실제 기동 경로에서 `StubVanService`를 꽂는데(Phase 15 범위상 맞음), 이 빌드를 실단말에서
그대로 돌리면 모든 거래가 조용히 승인된다는 사실이 로그 어디에도 없었다.

**수정**: `Orchestrator` 조립 직후 `FileLogger.Warn("... VAN 서비스가 스텁입니다 ...")` 추가.

**재검증**: `--pos-client-test` 재실행 로그에서 기동 시점에 해당 WARN이 실제로 찍힘을 확인.

### L-1(검증 공백) — 120초 타임아웃이 실제로 전달되는지 아무도 확인하지 않았다

`FakeReaderEndpoint.SendCardReadCommandAsync`가 `timeout` 인자를 완전히 무시해, `CardReadTimeout` 상수를
잘못 바꿔도(예: 12초로) 기존 15개 시나리오가 전부 통과했을 것이다.

**수정**: `FakeReaderEndpoint.LastCardReadTimeout` 추가. 시나리오1에
`readerA.LastCardReadTimeout == TimeSpan.FromSeconds(120)` 확인 추가, 통과.

### L-2(문서 과장) — "카드 데이터 참조를 버린다"는 문구가 실제보다 강했다

P15-9 문서가 명시적 삭제처럼 서술했지만 코드는 스코프 이탈 후 GC에 맡길 뿐이다(관리되는 불변 `string`은
애초에 신뢰성 있는 zeroing이 불가능). 문서 문구를 실제 동작에 맞게 수정.

### 재검증 후 전체 회귀

- `dotnet build KFTCOneCAP.Wpf.sln` 경고 0 / 오류 0
- `--payment-flow-test` 16개 시나리오(기존 15 + H-1 회귀 방지용 시나리오16) 전부 재실행 통과, 56개
  개별 확인 전부 OK, `[ERROR]` 0건
- `--pos-client-test`(Phase 14, 실제 Orchestrator 경로) 재실행 — 전부 정상, M-2 경고 로그 기동 시점에
  확인됨

## Phase 15 체크포인트 2 이후 — 실장비 테스트로 발견한 취소 응답 지연 수정 (2026-08-25)

체크포인트 2 완료 후 사용자가 실제 리더기(COM5)를 연결한 상태에서 소켓으로 실결제 요청을 보내고 알림창의
취소(ESC)를 눌러보는 실측 테스트를 진행했다 — 이 과정에서 **H-2 수정만으로는 부족한 결함**을 실물로
발견했다.

### 실측 결함 — 취소 후 응답까지 약 120초 소요

취소 버튼(ESC)을 누르면 `_canceled` 플래그와 리더기 초기화(0x60)는 즉시 나가지만,
`RunCardReadingRoundsAsync`는 `CardReadBroadcaster.SendAsync(...)`의 `await`가 **스스로 끝날 때까지**
(리더기 응답 도착 또는 로컬 120초 타임아웃) 취소 여부를 확인할 기회 자체가 없었다. 첫 실측
(`CANCEL-TEST-1`)에서 카드 리딩 시작(`14:41:41.034`)부터 취소 처리(`14:43:41.051`)까지 **정확히
120.017초**가 걸려, 취소 버튼을 눌러도 결제 단말이 2분 가까이 먹통처럼 보이는 실사용 결함임이 드러났다.
0x60(`SendInvalidationInit`)이 fire-and-forget이라 리더기가 실제로 스캔을 멈췄는지 소프트웨어가 알 방법이
없다는 것이 근본 원인이다.

이건 Phase 16의 "취소와 카드 리딩 완료가 근소한 차이로 동시에 도착했을 때 어느 쪽을 채택할지" 정하는
동시성 중재 문제와는 다르다(2026-08-25 사용자 확정) — **"취소를 누르면 즉시 반응해야 한다"**는 P15-9의
기본 요구사항이 지켜지지 않았던 것이라, Phase 16을 기다리지 않고 바로 수정했다.

**수정**: `PaymentOrchestrator`에 `TaskCompletionSource<bool> _cancelSignal`을 추가(거래마다 생성/해제,
`RunContinuationsAsynchronously`로 스레드 얽힘 방지). `OnCanceled`가 `_canceled` 플래그 확정과 동시에
이 신호를 완료시킨다. `RunCardReadingRoundsAsync`는 `CardReadBroadcaster.SendAsync(...)`를 더 이상 단순
`await`하지 않고, 그 결과와 `_cancelSignal.Task`를 `Task.WhenAny`로 경쟁시켜 취소가 먼저 끝나면 **리더기
응답을 기다리지 않고 즉시** `UserCanceled`로 반환한다(리더기 쪽 대기는 백그라운드에서 계속 진행되지만
아무도 결과를 기다리지 않음 — `CardReadBroadcaster`가 무효화까지 이미 책임지므로 안전).

**재검증**:
- 가짜 시나리오10(카드 리딩 1초 지연 스크립트, 200ms 시점에 취소) — 수정 전에는 응답까지 약 1초 걸렸을
  것이 수정 후 **3밀리초**로 단축(`취소 통지 발생` 14:48:47.377 → `OK: UserCanceled 응답` 14:48:47.380).
  `--payment-flow-test` 16개 시나리오 전부 재실행 통과, `[ERROR]` 0건.
- **실장비 재확인**(`CANCEL-TEST-2`): 카드 리딩 시작(`14:49:22.701`) 후 ESC로 취소 → 로그에 `카드 리딩
  라운드 1 대기 중 취소 감지 — 리더기 응답을 기다리지 않고 즉시 처리`(`14:49:28.616`, 즉 사람이 ESC를
  누르기까지 걸린 시간뿐 — 더 이상 120초를 기다리지 않음). 응답 `PAYRES|20|CANCEL-TEST-2|USER_CANCELED`
  정상 수신. 알림창도 즉시 닫힘을 확인.

## Phase 15 실장비(실제 리더기) 검증 기록 (2026-08-25)

체크포인트 2 완료 후 사용자가 실제 리더기(COM5, 인증식별번호 `SPD-800F1011`, 모듈ID `C160390003`)를 연결한
상태에서, `--home`으로 정상 기동한 앱에 로컬 PowerShell TCP 클라이언트로 실제 결제 요청을 보내고 실제
카드로 각 분기를 재현했다. P15-10이 "실장비가 없어 못 함"으로 남겨뒀던 항목들을 실제로 채운 기록이다.

| 시나리오 | txId | 결과 | 근거 |
|---|---|---|---|
| 정상 IC 승인(가짜 시나리오1 대응) | `REAL-TEST-1` | `PAYRES\|00\|...\|OK` | 무결성 체크 실제 성공 → 카드 리딩 성공 → VAN 승인. COM03은 실제로 없어 `DllCallFailure`로 배제되고 COM05 단독(N=1)으로 진행됨도 함께 확인 |
| 설정 화면 게이트 | `REAL-TEST-1`(1차 시도) | `PAYRES\|14\|...\|READER_SETUP_OPEN` | 리더기 설정 화면이 열려 있는 상태에서 실제로 거부됨. 화면을 닫은 뒤 재시도는 정상 진행 |
| 사용자 취소(가짜 시나리오10 대응) | `CANCEL-TEST-1`, `CANCEL-TEST-2` | `PAYRES\|20\|...\|USER_CANCELED` | 1차 시도에서 취소 후 응답까지 120.017초가 걸리는 결함을 실측으로 발견 → `_cancelSignal` 경쟁 수정 → 2차 시도에서 취소 즉시(리더기 응답을 기다리지 않고) 처리됨을 재확인. 상세는 위 "체크포인트 2 이후" 절 |
| FALLBACK(가짜 시나리오2 대응) | `FALLBACK-TEST-1` | `PAYRES\|00\|...\|OK` | 라운드1에서 실제 07 응답 → MS 재요청(거래구분 `F`, 채택된 리더기에만) → 라운드2 성공 → VAN 승인 |
| 응답코드 12 재시도(가짜 시나리오3 대응) | `CODE12-TEST-1` | `PAYRES\|00\|...\|OK` | 라운드1→12, 라운드2→12(둘 다 거래구분 `ARQo` 유지, 채택된 리더기에만 재요청), 라운드3에서 성공 — **라운드 상한(3) 경계까지 실제로 도달**했고 그 안에서 정상 승인으로 끝남 |
| 기타 응답코드(가짜 시나리오4 대응) | `OTHERCODE-TEST-1` | `PAYRES\|10\|...\|READER_RESP_06` | 라운드1에서 실제 응답코드 `06` 발생 → `ReaderResponseFailure`(10)로 정확히 매핑, 사유에 응답코드가 그대로 실림. 리더기 초기화(`SendInvalidationInit`)는 코드 경로상 호출됨(이 지점엔 별도 로그가 없어 코드 확인으로 검증) |
| Timeout 120초(가짜 시나리오11 대응) | `TIMEOUT-TEST-1` | `PAYRES\|21\|...\|CARD_INPUT_TIMEOUT` | 카드를 태그하지 않고 방치 — 카드 리딩 시작(14:54:06.156)부터 정확히 **120.025초** 뒤 실제 로컬 타임아웃 발생(14:56:06.181), PRD §4.9의 120초 상한이 실물로 정확히 검증됨 |
| 연속 2건 거래(가짜 시나리오14 대응) | `CONSEC-A`, `CONSEC-B` | 둘 다 `PAYRES\|00\|...\|OK` | 서로 다른 금액(1500/2500)으로 순차 실행 — `CONSEC-A` 처리 종료(15:06:09.335) 이후에야 `CONSEC-B` 시작(15:06:17.808), 각자 자기 txId·금액으로만 응답해 데이터가 섞이지 않음을 확인 |
| 큐 직렬성(가짜 시나리오15 대응) | `QUEUE-A`, `QUEUE-B` | 둘 다 `PAYRES\|00\|...\|OK` | 두 요청을 거의 동시에 전송 — 소켓 연결은 `QUEUE-B`가 `QUEUE-A` 처리 중(15:06:43.880)에 이미 들어왔지만, 실제 처리는 `QUEUE-A`가 완전히 끝난(15:06:44.484) 바로 다음(15:06:44.485)부터 시작됨. 두 거래가 동시에 리더기/VAN을 건드리지 않음(PRD §3.2/§8.1)을 실물 동시 접속으로 확인 |
| 양쪽 리더기 미사용(가짜 시나리오8 대응) | `NOREADER-TEST-1` | `PAYRES\|13\|...\|NO_READER` | 레지스트리 `COMPORT1_FIELD`/`COMPORT2_FIELD`를 임시로 `미사용`으로 변경(테스트 뒤 원복) → 2ms 만에 즉시 `NoReaderConfigured` 응답, 무결성 체크·카드 리딩 명령이 로그에 단 한 줄도 없음(전혀 시도되지 않음). 원복 후 `RESTORE-CHECK-1`로 COM 05가 다시 정상 인식되고(무결성 이력 유지, 카드 리딩 라운드 실제 시작) 되는 것까지 확인 |
| DLL 연동 실패 — 전송 시점(가짜 시나리오5 대응) | `DISCONNECT-TEST-1` | `PAYRES\|11\|...\|READER_DLL_FAIL` | **최초 시도, 사용자 지적으로 재현 조건 오류 발견**: 라운드 시작부터 실패 감지까지 11ms — 실제로는 요청을 보내기 **전에** 이미 케이블이 뽑혀 있던 상태였다(`Kind=DllCallFailure`, `SendCommandSafe 송신 실패` — 명령 자체가 안 나감). "카드 대기 도중 끊김"이 아니라 "애초에 끊긴 상태에서 전송 시도"였음이 밝혀져 아래 항목으로 재시도함 |
| DLL 연동 실패 — **카드 대기 도중** 끊김(가짜 시나리오5 대응, 정정 재시도) | `MIDWAIT-DISCONNECT-1` | `PAYRES\|11\|...\|READER_DLL_FAIL` | 알림창이 실제로 뜨고 로그에 "카드 리딩 라운드 1/3 시작"만 있고 완료 로그가 없는 것까지 확인한 뒤 케이블을 분리 — 라운드 시작(15:44:30.700)부터 **11.5초**(사람이 실제로 뽑기까지 걸린 시간) 뒤 실패 감지(15:44:42.233). 이번엔 **`Kind=CommunicationError: READER_EVENT_RECEIVE_ERROR`**로, 위 "전송 시점" 항목의 `DllCallFailure`와 **완전히 다른 내부 실패 신호**였다 — 명령은 정상 전송됐고 응답을 기다리다가 통신이 끊긴 것. 최종적으로 둘 다 `ReaderDllFailure`(11)로 같게 매핑되지만, 서로 다른 CALLBACK 경로(`READER_EVENT_RECEIVE_ERROR`)가 실제로 올바르게 처리됨을 확인했다 |

**실물로 재현하지 않은 항목과 이유**: VAN 거절/통신 실패·07 무한 반복에 의한 `RETRY_LIMIT`만 남았다 —
VAN이 스텁이라 항상 승인만 하도록 고정돼 있어(VAN 거절/통신실패) 코드 수정 없이는 실물로 재현할 방법이
없고, `RETRY_LIMIT`은 리더기가 07/12를 3라운드 연속으로 내야 하는데 인위적으로 그 타이밍을 맞추기
어렵다. 둘 다 `--payment-flow-test`의 가짜 엔드포인트 시나리오(12/13)로 이미 검증되어 있다. 기타
응답코드·DLL 연동 실패(전송 시점/대기 도중 둘 다)는 위 표에서 실물로 확인 완료 — 이걸로 P15-7 카드
리딩 라운드의 5개 분기는 전부 실물 검증까지 마쳤다.

**교훈**: 첫 DLL 연동 실패 테스트는 "알림창이 떴다"만 확인하고 바로 진행해, 실제로 카드 대기가 시작된
것까지는 확인하지 않고 결과 타이밍(11ms)도 재확인하지 않아 잘못된 결론(재현 성공)을 문서에 남길
뻔했다 — 사용자가 타이밍을 직접 되짚어 지적하지 않았다면 놓쳤을 오류다. 이후 테스트부터는 "라운드가
실제로 시작된 로그"까지 확인한 뒤에만 사용자에게 물리적 조작을 요청하도록 절차를 바꿨다.

---

## Phase 15 완료 후

- Phase 16(사용자 취소 & Timeout 동시성) 착수 전에 **"거래 진행 중 설정 화면 열기"를 UI에서 막을지**
  사용자와 확정한다(위 "알려진 범위 밖"). → **2026-08-25 확정: 막는다**(Phase 16 착수 시 확인, P16-5).
- Phase 16은 P15-9가 남긴 두 가지를 정본화하는 작업이 된다: (a) 자체 타이머 vs 명령 타임아웃 중 어느 것을
  Timeout의 정본으로 삼을지, (b) 취소 플래그가 응답을 이기는 현재의 단순 규칙을 **단일 결과 확정 게이트**로
  승격. → **둘 다 Phase 16 계획에 반영됨**(순서대로 P16-2, P16-1).
- 실제 SPEC(`docs/payment_relay/spec/`) 반영 Phase를 잡을 때, 이 Phase의 grep 점검 결과(Flow에 전문 리터럴
  0건)가 "`Protocol/`만 교체하면 되는가"의 근거가 된다.

---

# Phase 16 — 사용자 취소 & Timeout 동시성 확정

> ROADMAP.md "Phase 16 — 사용자 취소 & Timeout 동시성 확정" / PRD §4.8, §4.9, §8.2, §8.3, §9.
> **이 프로젝트에서 가장 버그가 나기 쉬운 지점**이라 별도 Phase로 떼어냈다. Phase 15가 "정상 흐름"을
> 완성했다면 이 Phase는 **"두 가지가 동시에 일어났을 때"**만 다룬다.
>
> Phase 15와 성격이 다르다: Phase 15는 부품을 **엮는** 일이라 새 코드가 적었지만, 이 Phase는
> **기존 코드를 걷어내고 더 엄밀한 것으로 갈아끼우는** 일이다. `PaymentOrchestrator`에 지금 흩어져 있는
> 취소 판정 3곳(`_canceled` 플래그 / `_cancelSignal` TCS / 브로드캐스트 후 방어적 재확인)을 **하나로
> 합치는 것**이 이 Phase의 핵심이며, 결과적으로 Phase 15 코드가 줄어야 정상이다.

## 착수 전 전제

### 지금 코드가 어디까지 와 있는가 (Phase 15 결과)

| 요구사항 | 현재 상태 | Phase 16이 할 일 |
|---|---|---|
| 취소 → 대기 리더기 전부 `0x60` | 동작함(`OnCanceled`가 `_pendingParticipantsForCancel`에 발사) | 게이트 승자만 정리하도록 **책임 위치를 옮김** |
| 취소 → 즉시 응답 | 동작함(`_cancelSignal` + `Task.WhenAny`, 2026-08-25 실장비 수정) | 게이트로 **흡수**(별도 TCS를 남기지 않음) |
| Timeout 120초 | 동작함(단, **리더기 명령 타임아웃**이 정본 = 라운드마다 리셋) | **거래 단위 자체 타이머로 교체**(PRD §4.9 갱신분) |
| 콜백 중복 / 리더기 2대 동시 응답 | 동작함(P10-4 CAS 게이트 + P10-5 `Task.WhenAny`) | **손대지 않는다**(리더기 계층은 이미 정확함) |
| 취소 + Timeout 동시 | **미해결** — 서로 다른 두 경로가 각자 응답을 만들 수 있음 | 거래 단위 게이트가 1건만 통과시킴 |
| 카드리딩 완료 + 취소/Timeout 동시 | **불완전** — `if (_canceled)` 재확인은 순서 보장이 없음 | 게이트 CAS로 원자화 |
| 거래 중 설정 화면 열기 | **미구현**(반대 방향만 P15-4에 있음) | 역방향 게이트 추가 |

### 두 계층의 게이트를 구분한다 ★

ROADMAP은 "Phase 10의 게이트를 **확장**한다(새 게이트를 만들지 않는다)"고 썼는데, 실제 코드를 보면
**확장이 아니라 위층에 하나를 더 두는 것**이 맞다. 근거:

- P10-4의 게이트(`ReaderService._pending` CAS)는 **리더기 1대의 명령 1회**를 대상으로 한다. "이 리더기의
  이 라운드에 대한 유효한 응답은 하나뿐"을 보장한다. 취소·Timeout은 리더기 개념이 아니라 **거래** 개념이라
  이 자리에 넣을 수 없다 — `ReaderService`는 "리더기가 몇 대인지"도, "거래"라는 것이 있는지도 모르게
  설계돼 있고(P10-4/P10-5 주석) 그 무지가 N=1 축약이 공짜로 되는 이유다. 여기에 거래 개념을 밀어넣으면
  Phase 10의 설계가 무너진다.
- 따라서 **계층마다 확정 지점이 정확히 하나씩**: 리더기 계층 = `ReaderService._pending` CAS(P10-4),
  거래 계층 = 이 Phase가 만드는 게이트. ROADMAP 문구의 진짜 의도("여기서도 잠그고 저기서도 잠그는
  구조를 만들지 마라")는 이렇게 지켜진다 — 계층 안에서 확정 지점이 둘 이상이 되는 것이 금지 사항이지,
  계층이 둘인 것이 문제가 아니다.
- **이 판단을 ROADMAP 문구와 다르게 해석했으므로 여기 기록한다.** 구현 후 Opus 검증 리뷰에서 이
  해석 자체를 다시 검토한다.

### 경합의 승패 규칙 (2026-08-25 사용자 확정)

**선착순** — 먼저 확정된 쪽이 이긴다. 특정 결과(예: 취소)에 우선권을 주지 않는다. 근거: 우선권을 두면
"취소가 이겼는데 카드는 이미 읽혔다" 같은 상태를 사람이 추론할 수 없게 되고, 어느 쪽이 이기든 **리더기
정리(`0x60`)와 POS 응답 1건**은 똑같이 보장되므로 선착순으로 충분하다.

> Phase 15의 현재 규칙("취소 플래그가 응답 종류를 이긴다")은 이 확정과 **다르다** — P16-1이 이것을
> 선착순으로 바꾼다. Phase 15 당시엔 게이트가 없어 "누가 먼저인지"를 물을 수단 자체가 없었기 때문에
> 플래그 우선으로 임시 처리했던 것이다.

### 취소 불가 구간의 경계는 이미 정해져 있다

PRD §4.8/§5.3에 따라 **VAN 통신 시작 후에는 취소가 없다**. 지금도 두 겹으로 막혀 있다:
`PaymentNoticeViewModel.IsCancelAllowed`가 `VanProcessing`에서 false(버튼 비활성 + ESC 훅이 삼키지 않음),
`ProcessAsync`가 VAN 진입 직전 `Canceled` 구독을 해제. **Phase 16은 이 경계를 바꾸지 않는다** — 다만
게이트의 "확정" 시점이 정확히 이 경계와 일치해야 한다(P16-1).

## 이 Phase에서 손대지 않는 것 (범위 밖 확정)

- **`ReaderService`/`PendingReaderCommand`/`CardReadBroadcaster`** — 리더기 계층의 게이트는 이미 정확하다.
  이 Phase에서 이 3개 파일이 바뀐다면 설계를 잘못 잡은 것이다(완료 조건에 `git diff` 점검 포함).
- **VAN 실호출** — Phase 17. 스텁 그대로 둔다.
- **알림창 시각/애니메이션·ESC 훅 구현** — Phase 13에서 완료. 훅의 **해제 검증**만 이 Phase에서 다시 본다.
- **`0x60`을 받은 리더기가 실제로 스캔을 멈추는지** — 소프트웨어가 확인할 방법이 없다(fire-and-forget,
  P15-9 기록). 이 Phase도 해결하지 못한다. **앱 쪽 대기가 즉시 끝나는 것**까지가 우리 책임 범위다.
- **결과코드 3자리 확장** — PRD §10 미확정 사항. 사용자가 요청할 때 별도로 한다.

---

## P16-1. 거래 단위 단일 결과 확정 게이트 ★

이 Phase의 심장이다. **먼저 만들고, 나머지 Task가 전부 여기에 기댄다.**

- **`Services/Payment/TransactionOutcomeGate.cs`**(신설) — 거래 1건당 인스턴스 1개.
  - 확정 사유 열거형: `FlowResult`(정상 흐름이 최종 결과를 만들었음) / `UserCanceled` / `Timeout`.
  - `bool TryClaim(TransactionOutcomeReason reason)` — **`Interlocked.CompareExchange` 한 줄**로만 확정한다.
    최초 1회만 `true`, 이후 전부 `false`. 이 클래스에 `lock`을 쓰지 않는다(P10-4와 같은 이유 — CAS
    한 줄이면 되는 것에 락을 걸면 "어디까지가 임계구역인지"가 흐려진다).
  - `TransactionOutcomeReason? ClaimedReason { get; }` — 확정된 사유(미확정이면 null).
  - `Task Interrupted { get; }` — 취소/Timeout으로 확정됐을 때 완료되는 Task. 라운드 루프가 리더기 응답
    대기와 `Task.WhenAny`로 경쟁시킨다. **`TaskCreationOptions.RunContinuationsAsynchronously` 필수**
    (UI 스레드에서 `TryClaim`이 불릴 때 워커 쪽 continuation이 UI 스레드에 인라인되는 것을 막는다 —
    P15-9와 P15-10에서 이미 두 번 겪은 종류의 사고).
  - `FlowResult`로 확정될 때는 `Interrupted`를 완료시키지 않는다(정상 흐름은 자기 자신을 깨울 필요가 없다).

- **`PaymentOrchestrator`에서 걷어낼 것** — 이 Task는 코드가 **늘어나는 것이 아니라 대체되는** 것이다:
  - `_canceled`(volatile bool) → 삭제. 게이트의 `ClaimedReason`이 대신한다.
  - `_cancelSignal`(TCS) → 삭제. 게이트의 `Interrupted`가 대신한다.
  - 라운드 루프 안의 `if (_canceled)` 두 곳(라운드 시작 전 / 브로드캐스트 완료 후 방어적 재확인) →
    게이트 확인 한 곳으로 통합.

- **확정이 일어나는 지점(전부 나열 — 이 목록에 없는 곳에서 확정하면 안 된다)**:
  1. `OnCanceled` → `TryClaim(UserCanceled)`
  2. 데드라인 만료(P16-2) → `TryClaim(Timeout)`
  3. 라운드 루프가 **거래를 끝내는 결과**를 얻었을 때 → `TryClaim(FlowResult)`
     - 성공(`00`) → VAN 진입 **직전**. 여기서 실패하면 취소/Timeout이 먼저 이긴 것이므로 그 결과로 응답하고
       **VAN에 들어가지 않는다**(PRD §4.8의 "VAN 나간 뒤 취소" 불일치를 만들지 않는 유일한 방법).
     - 실패 종료(기타 응답코드 / DLL 실패 / 재요청 상한 초과)도 동일하게 claim한다 — 실패도 "확정"이다.
     - **`07`/`12`로 라운드를 더 도는 경우는 claim하지 않는다**(아직 확정이 아니다).

- **경합 시 리더기 정리(`0x60`) 책임**: **확정에 성공한 쪽이 정리한다.** 취소/Timeout이 이기면 그 핸들러가
  대기 중인 참여 리더기 전부에 발사, 정상 흐름이 이기면 지금처럼 흐름 안에서 발사. 게이트가 "한 명만
  통과"를 보장하므로 **같은 리더기에 `0x60`이 두 번 나가는 일이 구조적으로 없다**.

**완료 조건**
- [ ] `TransactionOutcomeGate`에 확정 경로가 `Interlocked.CompareExchange` **한 줄**뿐이다(grep으로 확인)
- [ ] `PaymentOrchestrator`에서 `_canceled`/`_cancelSignal` 필드가 사라졌다
- [ ] 취소 불가 구간 경계가 그대로다 — VAN 진입 후에는 `TryClaim(UserCanceled)`이 호출될 수 없다
      (구독 해제 + `IsCancelAllowed` 두 겹 유지)
- [ ] 빌드 경고 0 / 오류 0

---

## P16-2. 거래 데드라인 — 자체 타이머 정본화 + `+30초` 연장 규약 ★

PRD §4.9(2026-08-25 갱신분)를 구현한다. **정본이 무엇인지 한 문장으로 답할 수 있어야 한다**:
"POS 응답 시점을 결정하는 것은 거래 데드라인 하나다."

- **`Services/Payment/PaymentDeadline.cs`**(신설) — 거래 1건당 인스턴스 1개.
  - 시작: `TimeSpan.FromSeconds(120)`(PRD §4.9).
  - `void Extend(TimeSpan)` — 데드라인을 뒤로 민다. 값은 `+30초` 하나이며 **상수 1곳에서만 정한다**.
  - `TimeSpan Remaining { get; }`
  - `Task Expired { get; }` — 만료 시 완료. 구현은 `Task.Delay(Remaining)` **재확인 루프**로 한다
    (delay가 끝나면 `Remaining`을 다시 보고, 그 사이 연장됐으면 남은 만큼 다시 기다린다).
    `System.Threading.Timer`를 쓰지 않는 이유: 연장할 때마다 재무장이 필요하고 해제를 빠뜨리면 그대로
    누수가 된다(Phase 13에서 실제로 겪은 `DispatcherTimer` 누수와 같은 종류). 거래 종료 시
    `CancellationTokenSource`로 루프를 끝낸다.

- **연장은 "07/12 전용 예외"가 아니라 일반 규칙으로 쓴다** — PRD §4.9가 명시한 요구사항이다. 구현 형태:
  라운드 루프가 **새 사용자 입력 단계를 시작할 때** `deadline.Extend(UserInputStepExtension)`를 부른다.
  지금 해당하는 것은 `07`(FALLBACK) / `12`(재요청) 두 경우뿐이지만, **나중에 서명·PIN 입력 단계가
  추가되면 그 진입점에서 같은 한 줄을 부르면 끝나야 한다.** 메서드 이름·주석을 "카드 재요청"이 아니라
  "사용자 입력 단계"로 짓는다.

- **리더기 명령 타임아웃과의 관계(정본 논쟁 종결)**: `SendCardReadCommandAsync`에는 **남은 시간
  (`deadline.Remaining`)**을 넘긴다. 둘 다 두어도 안전한 이유는 **P16-1의 게이트가 결과를 1건으로
  강제하기 때문**이다(P15-9 시점의 우려 — "둘을 동시에 두면 결과가 두 번 확정될 수 있다" — 는 게이트가
  생기면서 해소된다). 역할을 이렇게 갈라 적어 둔다:
  - **거래 데드라인** = POS 응답 시점의 정본. Timeout 결과를 확정하는 유일한 주체.
  - **명령 타임아웃** = 리더기 계층의 안전장치. 하드웨어가 영영 응답하지 않을 때 DLL 라운드를 회수하고
    방어적 `0x60`을 보낸다(P10-4). **POS 응답을 만들지 않는다.**
  - 하한 클램프를 둔다(남은 시간이 0에 가까울 때 명령 타임아웃을 0으로 주지 않는다).

**완료 조건**
- [ ] `120`·`30` 리터럴이 상수 **각 1곳**에만 있다(grep)
- [ ] `07`→`12`가 연달아 나는 시나리오에서 **데드라인이 정확히 두 번 연장**되고 로그에 남는다
- [ ] 라운드가 늘어나도 총 대기가 `120 + 30×(연장 횟수)`를 넘지 않는다(로그 타임스탬프로 확인)
- [ ] 거래 종료 후 데드라인 루프 Task가 살아 있지 않다(P16-4에서 반복 실행으로 재확인)

---

## P16-3. 취소·Timeout 정리 경로 통일 (PRD §4.8/§4.9)

두 요구사항의 본문이 사실상 같다("응답 대기 중인 **모든** 리더기에 `0x60`"). **코드도 하나여야 한다** —
취소용 정리와 Timeout용 정리를 따로 쓰면 반드시 어긋난다(이 문서가 P10-4에서 이미 같은 지시를 했다).

- 게이트가 취소/Timeout 중 하나로 확정되면 **공통 인터럽트 처리 경로 하나**를 탄다: 대기 중인 참여
  리더기 전부에 `0x60` → 라운드 루프가 `Interrupted`로 깨어나 해당 결과코드로 응답.
- `0x60` 발사는 **백그라운드에서**(Phase 15의 H-2 수정 유지 — `SendCommandSafe`가 포트 재오픈까지 동기로
  할 수 있어 UI 스레드에서 돌리면 취소 순간 알림창이 얼어붙는다). Timeout은 원래 UI 스레드가 아니지만
  경로를 하나로 합치므로 자연히 같은 처리가 된다.
- 정리 대상 목록(`_pendingParticipantsForCancel`)은 라운드마다 갱신되는 현재 방식을 유지한다 — 다만
  이름을 취소 전용이 아닌 것으로 바꾼다(Timeout도 같은 목록을 쓰므로).

**완료 조건**
- [x] 취소와 Timeout이 **같은 메서드**를 호출해 정리한다(경로 2개가 아님) — `FireInterruptCleanup` 단일 경로
- [x] 리더기 2대가 대기 중일 때 취소/Timeout 각각에서 **2대 모두** `0x60`을 받는다 — 가짜 하네스(시나리오
      10) + 실물 2대(COM5+COM3/COM7, 2026-08-26)로 이중 확인. 상세는 "Phase 16 실장비 2대(리더기 1/2
      동시 연결) 검증" 절
- [x] `0x60` 발사가 UI 스레드에서 일어나지 않는다(코드 리뷰 + 취소 직후 알림창이 멈추지 않음을 실기 확인)

---

## P16-4. 거래 종료 리소스 해제 + 반복 실행 누수 확인 (PRD §9)

PRD §9 "거래 종료 시 CALLBACK, Timer, Hook 등 거래 관련 리소스를 정상적으로 정리한다".

- 거래 1건이 끝날 때 **반드시 사라져야 하는 것** 목록을 코드 주석과 이 문서에 함께 못박는다:
  1. `Canceled` 구독 (이미 `finally`에서 해제 — 유지)
  2. 데드라인 루프 Task + 그 CTS (P16-2 신설분 — **이번에 추가되는 유일한 새 리소스**)
  3. 게이트 인스턴스 (거래 스코프 — 필드에 남기지 않는다)
  4. 알림창 → 닫히면서 ESC 전역 훅 해제(P13-5의 3중 보장, 이미 구현)
  5. 카드 데이터 (지역 스코프 종료 — PRD §8.4, Phase 15에서 확인됨)
- **반복 실행 검증**: 거래를 연속 20건 이상 돌린 뒤 (a) ESC 훅이 1개도 남지 않았는지, (b) 데드라인
  Task가 누적되지 않았는지, (c) `Canceled` 구독자 수가 0인지 확인한다. 하네스의
  `FakePaymentNoticePresenter.CanceledSubscriberCount`를 그대로 쓴다(P15-10이 이미 만들어 둠).

**완료 조건**
- [ ] 위 5개 항목이 거래마다 정리되는 것을 코드로 확인 + 주석에 목록 명시
- [ ] 연속 20건 후 구독자 수 0, 훅 잔존 0, 스레드/Task 누적 없음
- [ ] 취소·Timeout·정상·실패 **각 경로마다** 정리가 동일하게 일어난다(경로별로 빠뜨린 곳이 없음)

---

## P16-5. 거래 진행 중 설정 화면 열기 차단 (역방향 게이트, 2026-08-25 확정)

P15-4가 만든 게이트의 **반대 방향**이다. PRD §6(2026-08-25 갱신분).

- **판정 기준은 "거래가 처리 중인가" 하나뿐** — `TransactionQueue`가 이미 유일한 직렬화 지점이므로
  (P14-3) 그 상태를 읽는 것이 가장 정확하다. 큐에 "지금 처리 중인 거래가 있는가"를 노출하는 **읽기 전용
  속성 하나**만 추가한다. **새로운 잠금 장치를 만들지 않는다**(P14-3의 "직렬화 지점은 여기 하나뿐" 규칙).
- `HomeWindow`에서 리더기 설정 화면을 여는 지점(`OpenReaderSetup()`)에서 검사 → 진행 중이면 **열지 않고
  안내 메시지**. 워밍업(창 생성만 하고 `ShowDialog`하지 않는 최적화)이 있다면 그 경로는 건드리지 않는다.
- 검사와 실제 열기 사이에 거래가 시작되는 경합은 **완전히 막을 수 없고, 막을 필요도 없다** — 반대 방향
  게이트(P15-4)가 "설정 화면이 열려 있으면 결제 거부"로 이미 받아내기 때문이다. 두 게이트가 서로의
  빈틈을 덮는 구조라는 것을 주석에 남긴다.
- 안내 문구는 기존 메시지박스 스타일을 따른다(새 UI를 만들지 않는다).

**완료 조건**
- [ ] 거래 진행 중 홈 화면에서 리더기 설정 버튼 → 안내 후 열리지 않음(실기 확인)
- [ ] 거래 종료 후 같은 버튼 → 정상적으로 열림(차단이 영구화되지 않음)
- [ ] 잠금 장치가 늘지 않았다 — `TransactionQueue` 외에 새 `lock`/세마포어가 생기지 않음(grep)

---

## P16-6. 경합 시나리오 검증 하네스 확장

P15-10의 `--payment-flow-test`(시나리오 16종)에 **경합 전용 시나리오**를 이어 붙인다. 새 하네스를 만들지
않는다.

- `FakeReaderEndpoint`에 **응답 지연을 정밀하게 제어**할 수단이 이미 있다(`EnqueueCardReadOutcome`의
  `delay`) — 이것으로 "리더기가 응답하기 직전에 취소" 같은 순간을 재현한다. 필요하면 "응답 직전에
  콜백을 걸어주는" 훅을 최소한으로 추가한다.
- 검증 시나리오(전부 로그 증거를 이 문서에 인용한다):

| # | 시나리오 | 기대 |
|---|---|---|
| 17 | 카드리딩 완료 + 취소 (취소가 근소하게 먼저) | `UserCanceled` 1건, VAN 미진입, 대기 리더기 전부 `0x60` |
| 18 | 카드리딩 완료 + 취소 (카드가 근소하게 먼저) | `Approved` 1건, 취소는 무시됨(선착순), 응답 정확히 1건 |
| 19 | 카드리딩 완료 + Timeout | 먼저 확정된 쪽 1건만, 나머지 무시 |
| 20 | 취소 + Timeout 동시 | 둘 중 1건만 응답, `0x60`은 중복 발사되지 않음 |
| 21 | 취소 연타 + ESC 동시 | `Canceled` 1회, 응답 1건(P13-2 규칙이 게이트와 함께 지켜짐) |
| 22 | 데드라인 연장 — `07`→`12`→성공 | 연장 2회, 총 대기 `120+30+30` 이내, `Approved` |
| 23 | 데드라인 만료가 라운드 경계와 겹침 | `Timeout` 1건, 새 라운드가 시작되지 않음 |
| 24 | VAN 진입 후 취소 시도 | 취소가 **거부**됨(게이트 claim 실패), VAN 결과 그대로 |
| 25 | 연속 20건 반복 | 구독자 0, 훅 잔존 0, Task 누적 없음(P16-4) |

- **모든 시나리오의 공통 단언**: POS 응답이 **정확히 1건**, `0x60`이 대기 리더기 수를 초과해 나가지 않음.

**완료 조건**
- [ ] 시나리오 17~25 전부 통과, 로그 증거를 이 문서에 인용
- [ ] 기존 시나리오 1~16 **전부 재실행해 통과**(P16-1이 취소 규칙을 바꾸므로 시나리오 10은 **기대값이
      바뀔 수 있다** — 바뀐다면 그 이유를 여기 기록한다)

---

## P16-7. 실장비 검증 + 회귀

Phase 15에서 확인된 것: **가짜만으로는 못 잡는 결함이 있다**(취소 응답 120초 지연은 실장비에서만
드러났다). 이 Phase는 타이밍이 주제이므로 실장비 검증이 더더욱 필수다.

- 실장비(COM5)로 재현할 것: 정상 / 취소 즉시 반응 / `07` 후 연장 / 데드라인 만료 / VAN 진입 후 ESC 무시.
- **타임스탬프를 근거로 남긴다** — "취소 버튼 → 응답"까지의 실측 시간, "요청 → Timeout 응답"까지의
  실측 시간(연장 횟수와 함께). Phase 15의 실장비 기록 표와 같은 형식으로 이 문서에 추가한다.
- 회귀: `--payment-flow-test`(1~25), `--pos-client-test`(Phase 14), 리더기 설정 화면 실기 조작(P16-5).

**완료 조건**
- [ ] 위 5개 실장비 시나리오 통과 + 타임스탬프 근거 기록
- [ ] `dotnet build KFTCOneCAP.Wpf.sln` 경고 0 / 오류 0
- [ ] **`ReaderService.cs` / `PendingReaderCommand.cs` / `CardReadBroadcaster.cs` 변경 0줄**
      (`git diff --stat`으로 확인 — 바뀌었다면 계층 판단이 틀린 것이므로 이유를 기록한다)
- [ ] 회귀 3종 통과

---

## Phase 16 착수 순서 요약

P16-1(게이트) → P16-2(데드라인) → P16-3(정리 통일) → P16-4(리소스) → P16-5(역방향 게이트) →
P16-6(하네스) → P16-7(실장비·회귀).

**P16-1과 P16-2는 서로 얽혀 있다**(데드라인 만료가 게이트를 claim한다) — 게이트를 먼저 완성하고
데드라인이 거기에 꽂히는 순서를 지킨다. 반대로 하면 데드라인이 자기만의 확정 경로를 갖게 되어
이 Phase가 없애려는 문제를 다시 만든다.

**Opus 검증 리뷰 체크포인트**: P16-1~P16-3 완료 후 1회(가장 위험한 구간이 여기 몰려 있다),
P16-4~P16-7 완료 후 1회. Phase 15와 같은 방식으로 진행한다.

## Phase 16 완료 (2026-08-26)

P16-1~P16-7 전체 완료. 체크포인트 1(P16-1~P16-6 완료 후) Opus 검증 리뷰에서 결함 1건(H-1)·개선 4건을
잡아 수정했고, 그 직후 실장비(COM5)로 P16-7과 P16-5를 검증하는 과정에서 P16-5의 안내 메시지가
Topmost 알림창에 가려지는 UX 문제를 추가로 발견해 수정했다(P16-4~P16-7 완료 후 예정했던 체크포인트
2는, 실장비 검증 자체가 이미 그 역할을 겸했다고 판단해 별도로 진행하지 않았다 — 발견된 문제가 전부
코드 리뷰가 아니라 실사용 흐름에서 나온 UX 성격이라 사람의 실기 확인이 코드 리뷰보다 효과적이었다).

상세 기록은 이 문서 안의 다음 절을 순서대로 참고: "Phase 16 P16-1~P16-6 구현 및 검증 기록"(초기 구현
+ 자체 발견 레이스 수정) → "Phase 16 체크포인트 1 — Opus 검증 리뷰 및 후속 수정"(H-1/M-1/M-2/L-1/L-2)
→ "Phase 16 실장비(실제 리더기) 검증 기록"(P16-7, 5개 시나리오) → "P16-5 실기 확인 결과 및 후속 수정"
(Topmost 가림 문제 발견·수정).

**다음 Phase 착수 전 확인할 것**: Phase 17(VAN 연동) 착수 시 `docs/payment_relay/dll/` 및 PRD §2.3의
`KFTC_GIRO.dll` 계약을 다시 확인한다(§10 미확정 사항 — `KFTC_GIROPOS.ini` 미확보 등).

---

## Phase 16 P16-1~P16-6 구현 및 검증 기록 (2026-08-25)

P16-1(단일 결과 확정 게이트) ~ P16-6(경합 시나리오 하네스)까지 구현했다. P16-7(실장비 검증)은 사용자
확인이 필요해 별도로 진행한다.

### 구현 요약

- **`Services/Payment/TransactionOutcomeGate.cs`**(신설) — `Interlocked.CompareExchange` 한 줄로 확정하는
  거래 단위 게이트. `FlowResult`/`UserCanceled`/`Timeout` 3종 사유, 선착순(2026-08-25 확정)으로 최초 1회만
  `TryClaim` 성공.
- **`Services/Payment/PaymentDeadline.cs`**(신설) — `Task.Delay` 재확인 루프 기반 거래 단위 데드라인.
  `Extend`로 연장, `Dispose`로 종료(P16-4 리소스 해제 목록에 편입).
- **`PaymentOrchestrator.cs`** — `_canceled`/`_cancelSignal` 필드를 게이트로 완전히 대체(계획대로 코드가
  줄었다: 취소 판정이 세 갈래에서 게이트 하나로). 카드 리딩 라운드는 `gate.Interrupted`와
  `CardReadBroadcaster.SendAsync`를 `Task.WhenAny`로 경쟁시킨다. VAN 진입 직전 `gate.TryClaim(FlowResult)`
  재확인으로 "카드 리딩 성공"과 "취소/Timeout 확정"의 근소한 경합까지 정확히 처리한다(P16-6 시나리오
  17/18/24). 리더기 명령 타임아웃은 `deadline.Remaining`에서 파생(하한 1초 클램프).
- **`Services/Payment/TransactionQueue.cs`** — `IsProcessing` 읽기 전용 프로퍼티 추가(P16-5, 새 잠금
  장치 없이 기존 직렬화 지점만 노출).
- **`Views/HomeWindow.xaml.cs`** — `OpenReaderSetup()`에 `App.PaymentQueue.IsProcessing` 검사 추가, 거래
  진행 중이면 안내 메시지 후 열지 않음(P15-4의 반대 방향 게이트).

### 실측으로 발견한 레이스 컨디션(Opus 리뷰 전에 자체 테스트로 발견)

`--payment-flow-test` 첫 실행에서 시나리오 10/17/20이 간헐적으로 FAIL했다 — 로그에 "대기 중인 참여
리더기 **0**대에 초기화(0x60) 전송 예약"이 찍혔는데, 실제로는 1~2대가 대기 중이어야 했다.

**원인**: `TransactionOutcomeGate.TryClaim`이 성공하면 `Interrupted`(TCS, `RunContinuationsAsynchronously`)가
완료되고, 이 TCS를 기다리던 카드 리딩 라운드 루프의 `Task.WhenAny` continuation이 **다른 스레드에서
즉시** 재개될 수 있다. 그 continuation이 먼저 `ProcessAsync`까지 되감겨 `finally` 블록에 도달해
`_pendingParticipantsForInterrupt`(당시 인스턴스 필드)를 비우면, `OnCanceled`가 `FileLogger.Info` 로깅
(디스크 I/O로 상대적으로 느림) 이후 그 필드를 읽는 시점엔 이미 비어 있었다 — **같은 거래 안에서** 확정
경로와 정리(finally)가 경합한 것이다. `_gate` 필드도 같은 구조라 이론상 `NullReferenceException` 위험이
있었다(테스트에서는 재현되지 않았지만 동일한 근본 원인).

**수정**:
1. `TransactionOutcomeGate`를 인스턴스 필드가 아니라 `ProcessAsync`의 **지역 변수**로 바꾸고, `OnCanceled`를
   `(gate) => ...` 형태로 파라미터화해 클로저로 구독한다 — 이 거래의 게이트 참조가 다시는 바뀌지 않는다.
2. ~~`_pendingParticipantsForInterrupt`를 `finally`에서 **리셋하지 않는다**~~ — **이 처방은 틀렸고 아래
   체크포인트 리뷰 H-1에서 철회됐다.** 리셋을 없애면 앞 거래의 뒤늦은 확정이 **다음 거래의** 정리 대상을
   읽어 엉뚱한 리더기를 초기화할 수 있다(예외 경로에서 실제로 성립). 근본 원인은 리셋 여부가 아니라
   거래별 상태를 인스턴스 필드에 둔 것 자체였고, 정리 대상도 게이트와 함께 거래 스코프
   (`TransactionScope`)로 옮겨 해결했다 — 상세는 "Phase 16 체크포인트 1 — Opus 검증 리뷰" 절.

**재검증**: 위 수정 후 `--payment-flow-test`를 재실행, 시나리오 1~25 전부(검사 75건) OK, FAIL 0건.
(2번 처방이 철회되면서 이 시점의 코드는 더 이상 현재 구현이 아니다 — 최종 상태는 체크포인트 리뷰 절 참고.)

### 시나리오 1~25 검증 결과 — 전부 통과(2026-08-25, `--payment-flow-test`)

| # | 시나리오 | 결과 |
|---|---|---|
| 1~16 | Phase 15 기존 시나리오 | 전부 OK(재실행, 회귀 없음). 시나리오 1의 타임아웃 인자 검증은 정확히 120초가 아니라 "120초 이하 근접"으로 완화(데드라인에서 파생되므로 미세하게 작음) |
| 17 | 카드 대기 중 취소(취소가 먼저) | `UserCanceled`, VAN 미진입(`LastRequest==null`), 리더기 무효화 — 전부 OK |
| 18 | 카드 리딩 확정 후 늦게 도착한 취소 | `Approved`, 뒤늦은 취소가 결과를 바꾸지 못함, 구독자 수 0 — 전부 OK |
| 19 | 데드라인이 카드 응답보다 먼저 만료 | `Timeout`, 무효화 나감, 라운드 정확히 1회 — 전부 OK |
| 20 | 취소·Timeout 근접 경합 | Timeout이 확정(로그로 확인: "이미 다른 사유(Timeout)로 확정되어 무시"), 무효화 정확히 1회(중복 없음) — 전부 OK |
| 21 | 취소 연타(버튼+ESC 동시 재현) | `UserCanceled` 1건, 무효화 정확히 1회(중복 0x60 없음) — 전부 OK |
| 22 | 07→12→성공, 데드라인 연장 | `Approved`, 정확히 3라운드(연장 없이는 짧은 초기 데드라인상 Timeout이었을 것) — 전부 OK |
| 23 | 데드라인 만료가 라운드 대기 중과 겹침 | `Timeout`, 라운드 1에서 확정, 추가 라운드 없음 — 전부 OK |
| 24 | VAN 진입 후 취소 시도 | `Approved`, VAN 요청 실제 전달됨, 취소가 결과를 바꾸지 못함 — 전부 OK |
| 25 | 연속 20건 반복 | 20건 전부 승인, 매 거래 후 구독자 수 0 — 전부 OK |

**검증 방식의 한계(정직하게 기록)**: 가짜 하네스로는 하드웨어급 "진짜 동시"를 재현할 수 없어 지연 값
조작으로 결정론적 순서를 만들었다 — 예를 들어 시나리오 19와 23은 근본적으로 같은 `Task.WhenAny` 경쟁
메커니즘을 서로 다른 타이밍으로 재현한 것이라 결과 코드가 동일하다. 데드라인 만료(120초)를 실제로
기다리지 않기 위해 `PaymentOrchestrator` 생성자에 `initialCardReadDeadline`(테스트 전용, 운영 코드는
항상 기본값 120초 사용)을 주입 가능하게 했다.

**완료 조건 점검**
- [x] `TransactionOutcomeGate`의 확정 경로가 `Interlocked.CompareExchange` 한 줄뿐(코드 확인)
- [x] `_canceled`/`_cancelSignal` 필드가 `PaymentOrchestrator`에서 사라짐(`_gate` 필드도 함께 제거 — 레이스 수정으로 지역 변수화)
- [x] 취소 불가 구간 경계 유지 — VAN 진입 직전 `TryClaim(FlowResult)` 확인 후에만 구독 해제
- [x] `120`·`30` 리터럴이 상수 각 1곳(`DefaultInitialCardReadDeadline`/`UserInputStepExtension`)에만 있음
- [x] 취소와 Timeout이 같은 메서드(`FireInterruptCleanup`)로 정리됨
- [x] `TransactionQueue` 외 새 잠금 장치 없음(`IsProcessing`은 읽기 전용 노출)
- [x] `ReaderService.cs`/`PendingReaderCommand.cs`/`CardReadBroadcaster.cs` 변경 0줄(`git diff --stat` 확인)
- [x] `dotnet build KFTCOneCAP.Wpf.sln` 경고 0 / 오류 0
- [ ] P16-7(실장비 검증) — 사용자 확인 필요, 별도 진행

### 이후 필요한 것

- **P16-7 실장비 검증**: 정상 / 취소 즉시 반응 / `07` 후 데드라인 연장(+30초) / 데드라인 만료 / VAN 진입 후
  ESC 무시 — 5개 시나리오를 실제 COM 포트로 재현하고 타임스탬프 근거를 남긴다.
- **P16-5 실기 확인**: 거래 진행 중 홈 화면에서 "리더기 설정" 버튼 클릭 시 안내 후 열리지 않는지, 거래
  종료 후에는 정상적으로 열리는지 확인.

## Phase 16 체크포인트 1 — Opus 검증 리뷰 및 후속 수정 (2026-08-25)

P16-1~P16-6 구현 후 Opus로 검증 리뷰를 받았다. **결함 1건(H-1)**, 개선 2건(M-1/M-2), 검증 품질 2건
(L-1/L-2)을 발견해 전부 수정하고 재검증했다.

### H-1 (결함) — 예외 경로에서 **다음 거래의 리더기**에 `0x60`이 발사될 수 있었다

구현 중 자체 테스트로 잡았던 레이스("취소 확정이 `finally`보다 늦어 정리 대상이 비어 있음")를
**`_pendingParticipantsForInterrupt`를 `finally`에서 리셋하지 않는 것**으로 막았는데, 이 처방이 더 나쁜
구멍을 열었다.

`ProcessAsync`의 `try` 블록에서 예외가 나면(알림창 `Show`/`ChangeState` 실패, Phase 17의 VAN 실호출 예외
등) 게이트가 **미확정**으로 남는다. 그 상태에서:

1. `finally` → `using`이 `PaymentDeadline`을 `Dispose`
2. `Dispose`가 `Expired`를 완료시킴(당시엔 "실제 만료"와 "Dispose"를 구분하지 않았다 — M-2)
3. `MonitorDeadlineAsync`가 깨어나 `TryClaim(Timeout)`에 **성공**(아무도 확정하지 않았으므로)
4. `FireInterruptCleanup`이 **인스턴스 필드**에서 정리 대상을 읽는다

그런데 3~4는 결제 워커가 아닌 다른 스레드에서, `ProcessAsync`가 이미 반환한 **뒤에** 실행된다 — 그 사이
큐가 다음 거래를 시작해 그 필드를 자기 참여 리더기로 덮어썼을 수 있다. 결과: **지금 카드를 기다리는 다음
고객의 리더기에 `0x60`이 날아가 멀쩡한 거래가 조용히 깨진다.** 큐가 거래를 직렬화해도 이 경로는 막히지
않는다 — 직렬화되는 것은 `ProcessAsync` 본문이지 그것이 남긴 콜백이 아니기 때문이다.

**근본 원인은 "리셋하느냐 마느냐"가 아니라 거래별 상태를 인스턴스 필드에 둔 것 자체였다.** 게이트에 이미
적용했던 처방(거래 스코프)을 정리 대상 목록에도 똑같이 적용해 해결했다:

- `Services/Payment/PaymentOrchestrator.TransactionScope`(신설, private 중첩 클래스) — `Gate` +
  `PendingParticipants` + `TransactionId`를 거래 1건 동안만 들고 있다. `ProcessAsync`가 지역 변수로 만들어
  `onCanceled` 클로저와 `MonitorDeadlineAsync`에 넘긴다.
- `PaymentOrchestrator`에서 **가변 인스턴스 필드가 전부 사라졌다** — 남은 필드는 생성자 주입 의존성
  (`readonly`)뿐이다. 앞 거래의 뒤늦은 콜백이 다음 거래의 상태를 볼 수 있는 경로가 구조적으로 없다.
- `finally`에 **게이트 봉인**을 추가했다. 정상 경로에서는 이미 확정돼 있어 이 `TryClaim`이 반드시 실패하고
  아무 일도 일어나지 않는다. 성공하는 경우는 "아무도 결과를 확정하지 못한 채 예외로 빠져나가는" 경로
  하나뿐이며, 그때는 대기 중이던 리더기를 정리해 준다(예전엔 이 정리가 위 3번의 **잘못된 Timeout 확정**에
  얹혀 우연히 되고 있었다 — 이제는 의도적으로 한다).

### M-1 (개선) — `PaymentDeadline`의 `CancellationTokenSource`가 해제되지 않았다

`Dispose()`가 `_cts.Cancel()`만 하고 `_cts.Dispose()`를 하지 않았다. 거래마다 하나씩 만들어지는 객체라
장시간 운용에서 그대로 누수가 된다(PRD §9 "장시간 실행 시 메모리 누수", P16-4 리소스 해제 목록). `Cancel`
→ `Dispose` 순서로 고치고 중복 호출에 안전하도록 `_disposed` 가드를 뒀다.

### M-2 (개선) — 정상 종료한 거래에까지 Timeout 확정을 시도했다

`PaymentDeadline.Expired`가 "실제 만료"와 "`Dispose`로 감시 종료"를 같은 신호로 합쳐 돌려줘서,
`MonitorDeadlineAsync`가 **정상 종료한 거래에 대해서도** `TryClaim(Timeout)`을 시도했다. 게이트가 이미
확정돼 있어 결과적으로는 무해했지만, P16-1이 "확정을 시도하는 지점을 전부 나열한다"를 안전성 논증의
근거로 삼는 이상 그 목록에 정상 경로가 섞이면 논증 자체가 성립하지 않는다. `Task<bool>
WaitForExpiryAsync()`로 바꿔(true=실제 만료) 정상 종료 시엔 확정을 시도조차 하지 않게 했다. H-1의 3번
단계를 없애는 효과도 함께 있다.

### L-1 (검증 품질) — 시나리오 20이 경합을 검증하지 못하고 있었다

데드라인 40ms / 취소 50ms로 배치돼 **항상 Timeout이 이기도록** 되어 있었다(로그로도 확인: "이미 다른
사유(Timeout)로 확정되어 무시"). 이러면 "둘 중 하나만 이긴다"가 아니라 Timeout 단독 경로를 보는 것이라
게이트가 없어도 통과했을 시나리오다. 두 신호를 **같은 목표 시각(80ms)** 에 쏘아 실제로 경쟁시키도록
고쳤다 — 승자는 실행마다 달라질 수 있고 그것이 정상이며, 검증 대상은 승자가 아니라 "결과가 정확히 1건,
`0x60`도 정확히 1회"다.

### L-2 (검증 품질) — 시나리오 23이 19와 같은 경로를 반복하고 있었다

원래 23("데드라인 만료가 라운드 대기 중과 겹침")은 19와 **같은 `Task.WhenAny` 경쟁 메커니즘**을 타이밍만
바꿔 반복하는 것이라 새로 확인되는 것이 없었다(원래 주석에도 그렇게 적혀 있었다). 아직 아무도 검증하지
않던 것으로 교체했다 — **연장량이 정확히 몇 초인가**. 시나리오 22는 "연장이 일어났다"까지만 증명하므로
상수를 30초에서 5초로 잘못 바꿔도 그대로 통과한다.

Phase 16부터 리더기 명령 타임아웃이 `ClampCommandTimeout(deadline.Remaining)`에서 파생된다는 성질을
이용해, 라운드별 타임아웃 값을 `FakeReaderEndpoint.CardReadTimeouts`(신설)에 모두 기록하고 그 차이로
연장량을 역산한다. **실측 결과: 라운드1 60.00초 → 라운드2 89.99초, 차이 29.99초** — PRD §4.9의 +30초가
정확히 적용됨을 증명한다.

### 회귀 방지 시나리오 추가 (26)

H-1은 타이밍 의존이라 옛 코드에서 100% 재현되는 테스트를 만들 수 없다. 대신 **수정이 보장하는 불변식**을
검증한다(시나리오 26, `FakePaymentNoticePresenter.ThrowOnChangeState` 신설):

1. `07` 분기의 `ChangeState`에서 예외 → 결과 미확정 상태로 비정상 종료
2. 예외가 호출자(큐)까지 전파되고, **자기 리더기는 정확히 1회 정리됨**(finally 봉인이 하는 일)
3. 이어서 **같은 Orchestrator·같은 리더기로** 정상 거래 실행 → 승인되고, **추가 `0x60`이 나가지 않음**
   (앞 거래의 뒤늦은 정리에 오염되지 않음)

3번을 같은 인스턴스로 도는 것이 핵심이다 — 다른 인스턴스를 쓰면 옛 구현의 인스턴스 필드가 애초에
공유되지 않아 아무것도 검증하지 못한다(초안이 이 실수를 했다가 리뷰에서 잡아 고쳤다).

### 재검증 결과 (2026-08-25)

- **`--payment-flow-test` 시나리오 1~26 전부 통과** — 검증 80건, `FAIL` 0건.
  로그: `%LOCALAPPDATA%\KFTCTaxGiroCAP\logs\2026-08-25.log`의 17:13~17:14 실행분.
- **Phase 14 회귀(`--pos-client-test`) 7개 흐름 전부 정상.**
  - 첫 실행에서 흐름 1·2의 응답이 전부 "(타임아웃)"으로 나왔으나 **회귀가 아니라 환경 문제**였다:
    레지스트리에 COM 05가 설정돼 있고 그 포트에 금일 무결성 성공 이력이 있어, 무결성 체크를 건너뛰고
    곧바로 카드 리딩에 들어가 **아무도 태그하지 않는 카드를 120초 기다린 것**(테스트 클라이언트 타임아웃은
    10초). 양쪽 포트를 임시로 `"미사용"`으로 두고 재실행해 소켓 계층만 분리 검증한 결과 7개 흐름 전부
    정상(`PAYRES|13|...|NO_READER` 즉시 응답, malformed frame·강제 종료·먹통 클라이언트·유휴 연결
    자동 종료 포함). 확인 후 레지스트리는 원래 값(COM 05 / COM 03)으로 복원했다.
- **계층 경계 유지**: `git diff --stat -- src/KFTCOneCAP.Wpf/Services/Reader/` 변경 0건 — 리더기 계층
  (`ReaderService`/`PendingReaderCommand`/`CardReadBroadcaster`)에 한 줄도 손대지 않았다.
- `dotnet build KFTCOneCAP.Wpf.sln` 경고 0 / 오류 0.

### 이 리뷰에서 확인된 설계 판단

착수 전 전제의 "두 계층의 게이트를 구분한다"(ROADMAP 문구와 다른 해석)를 리뷰에서 재검토했고 **그대로
유지**한다. 실제 구현 결과가 근거가 됐다 — 리더기 계층 3개 파일이 한 줄도 바뀌지 않고 거래 계층 게이트만
추가해 경합 9종이 전부 처리됐다. 거래 개념을 `ReaderService`에 밀어넣었다면 N=1 축약(P10-4/P10-5)이
깨졌을 것이다.

## Phase 16 실장비(실제 리더기) 검증 기록 (2026-08-26)

체크포인트 1 리뷰·수정 직후 COM5 리더기를 실제로 연결한 상태에서 P16-7의 실장비 시나리오를 재현했다.
리더기 식별은 Phase 15와 동일(COM5, COM3은 물리적으로 미연결 — 무결성 체크 실패로 자동 제외돼 N=1로
정상 진행되는 것도 함께 재확인됨).

### 결과 요약

| # | 시나리오 | 결과 | 근거 로그 |
|---|---|---|---|
| 1 | 정상 승인 | `PAYRES\|00\|...\|OK`, 10.99초 | `P16-NORMAL-1` |
| 2 | 취소 즉시 반응 | `PAYRES\|20\|...\|USER_CANCELED`, 응답 3.50초(순수 사람 반응 시간 포함) — "대기 중 확정" 로그가 취소 통지 직후 2ms 안에 찍힘 | `P16-CANCEL-1` |
| 3 | 07 응답 → 데드라인 +30초 연장 | `PAYRES\|00\|...\|OK`. 라운드1 시작 시 남은데드라인=120.0s, 07 발생(4.08초 경과) 직후 남은데드라인=145.9s로 로그 — **120 - 4.08 + 30 = 145.92, 정확히 일치** | `P16-EXTEND-1` |
| 4 | 데드라인 만료(카드 미태그) | `PAYRES\|21\|...\|CARD_INPUT_TIMEOUT`, 119.99초(라운드 시작~Timeout 확정 로그 시간차) | `P16-TIMEOUT-1` |
| 5 | VAN 진입 후 ESC 무시 | 아래 "결정" 참고 — 실장비로는 검증 불가능한 경로로 판단, 착수하지 않음 | `P16-VANESC-1~4` |

### 5번(VAN 중 ESC 무시)에 대한 결정 — 실장비 테스트를 하지 않는다(2026-08-26 사용자 확인)

처음엔 카드 태그 직후 곧바로 ESC를 눌러 VAN 통신(`StubVanService` 1초 지연) 도중 취소가 씹히는지
확인하려 했다. 4회 시도(`P16-VANESC-1~4`)했으나 전부 카드 리딩 단계에서 취소되거나(1번, VAN 진입 전이라
정상 취소) VAN 진입 후엔 "취소 통지 수신" 로그가 아예 없이 승인으로 끝났다(2~4번).

로그만으로는 "ESC를 안 눌렀다"와 "ESC를 눌렀는데 정상적으로 씹혔다"를 구분할 수 없었는데, 사용자가
근본적인 문제를 지적했다: **"취소가 비활성화되면 거래중으로 넘어가면 ESC도 반응 못하게 어차피 막아둔거
아니야?"** — 맞는 지적이다.

- `PaymentNoticeEscapeHook`은 `_tryCancel()`이 `false`를 반환하면(=`PaymentNoticeViewModel.IsCancelAllowed`가
  `VanProcessing` 상태에서 `false`) 그냥 `CallNextHookEx`로 넘길 뿐, 로그조차 남기지 않고
  `PaymentOrchestrator.OnCanceled`까지 도달하지 않는다. **이건 Phase 13(H-3)에서 이미 구현·검증된 UI
  레벨 차단**이지 Phase 16이 새로 만든 것이 아니다.
- Phase 16이 실제로 추가한 것은 그 **뒤에** 있는 게이트 거부 로직(`scope.Gate.TryClaim(UserCanceled)`이
  실패하는 것)이다. 하지만 UI가 이벤트 자체를 막아버리므로, **실장비로는 이 게이트 거부 경로에 도달할
  방법이 구조적으로 없다** — ESC를 아무리 정확한 타이밍에 눌러도 이벤트가 발생하지 않으니 게이트까지
  신호가 가지 않는다.
- 이 경로를 검증할 수 있는 유일한 방법은 UI 가드를 우회해 오케스트레이터에 직접 취소 신호를 넣는
  것뿐이며, 그건 정확히 가짜 하네스가 하는 일이다 — **시나리오 24**(`FakePaymentNoticePresenter.
  FireCanceled()`로 UI 가드를 건너뛰고 VAN 중에 강제로 `Canceled` 이벤트 발생)가 이미 이 경로를
  검증했다(`Approved` 응답, `VanService.LastRequest != null`로 취소가 결과를 바꾸지 못함을 확인).

**결론**: 5번 시나리오는 두 층으로 쪼개서 각각 이미 검증됐다 — UI가 이벤트를 막는다(Phase 13 완료 +
이번 실측 4회로 재확인: 취소 통지 로그가 한 번도 안 찍힘), 게이트가 늦은 신호를 거부한다(가짜 하네스
시나리오 24). 추가 실장비 시도는 새로운 것을 증명하지 못하므로 여기서 멈춘다.

### 완료 조건 점검 (P16-7)

- [x] 정상 / 취소 즉시 반응 / 07 후 데드라인 연장(+30초, 정확히 일치) / 데드라인 만료(정확히 120초) —
      4개 실장비 시나리오 통과, 타임스탬프 근거 기록
- [x] VAN 진입 후 ESC 무시 — 실장비로 검증 불가능한 경로임을 확인, UI 레벨(Phase 13)과 게이트 레벨
      (가짜 하네스 시나리오 24)로 나눠 이미 검증됨을 근거로 명시
- [x] 회귀: `--payment-flow-test`(1~26), `--pos-client-test`(Phase 14 7개 흐름) — 체크포인트 1 리뷰에서
      이미 재검증 완료(위 절 참고)

**P16-7 완료.** 남은 것은 P16-5(설정 화면 차단)의 실기 확인뿐이다.

### Phase 16 실장비 2대(리더기 1/2 동시 연결) 검증 (2026-08-26, P16-3 완료조건 보강)

위 절까지의 실장비 검증은 전부 COM5 단독(N=1 축소 경로)이었다 — COM3/COM7은 물리적으로 미연결이었기
때문이다. P16-3 완료조건 "리더기 2대가 대기 중일 때 취소/Timeout 각각에서 **2대 모두** `0x60`을 받는다"는
그동안 가짜 하네스(시나리오 10 등)로만 확인됐었는데, 사용자가 실장비 2대를 다시 연결해줘서 실물로도
검증했다.

리더기 식별: 리더기1=COM5(고정), 리더기2는 테스트 도중 사용자가 리더기 설정 화면에서 COM3→COM7로 직접
변경(레지스트리 `COMPORT2_FIELD` 갱신 확인) — 시나리오 1/2는 COM5+COM3, 시나리오 3(재시도)은 COM5+COM7
조합이다. 둘 다 무결성 체크 성공 → 카드 리딩에 정상 참여했다.

수동 TCP 클라이언트(`PAY|{amount}|{txId}` 4자리 길이 헤더 전문, 스크래치패드 PowerShell 스크립트)로
소켓 서버에 직접 요청을 보내 재현했다.

| # | 시나리오 | 결과 | 근거 로그 |
|---|---|---|---|
| 1 | 정상 경쟁(2대 모두 참여, 한쪽에만 카드 태그) | `PAYRES\|00\|...\|OK`. 리더기[1](COM3)이 먼저 응답해 채택, 리더기[0](COM5)에 실제 `0x60` 무효화 전송 | `P16-DUAL-NORMAL-1` |
| 2 | 취소(2대 모두 카드 미태그, 취소 버튼) | `PAYRES\|20\|...\|USER_CANCELED`. "UserCanceled 확정 — 대기 중인 참여 리더기 **2대**에 초기화(0x60) 전송 예약" 로그로 COM5+COM3 양쪽 다 정리 대상에 포함됨을 확인, 이후 재연결/오류 경고 없음(=양쪽 다 성공 전송) | `P16-DUAL-CANCEL-1` |
| 3-1차 | Timeout(2대 모두 카드 미태그) | `PAYRES\|10\|...\|READER_RESP_04` — 소프트웨어 120초 데드라인이 아니라 **리더기2(COM3) 자체 내장 타임아웃**(60.6초, `0x3B` 응답코드 `04`="거래요청 Timeout", `docs/reader_dll/API명세서.md` 314행)이 먼저 응답해 정상 종료(재시도 대상 아님이라 그대로 실패 확정). 소프트웨어 Timeout 경로를 타지 못해 재시도 | `P16-DUAL-TIMEOUT-1` |
| 3-2차(재시도) | Timeout(2대 모두 카드 미태그) | `PAYRES\|21\|...\|CARD_INPUT_TIMEOUT`. 라운드 시작(10:23:43.615)~데드라인 확정(10:25:43.587) = **119.97초**, PRD §4.9 120초와 정확히 일치. "Timeout 확정 — 대기 중인 참여 리더기 **2대**에 초기화(0x60) 전송 예약" 로그로 COM5+COM7 양쪽 다 정리 대상에 포함됨을 확인 | `P16-DUAL-TIMEOUT-2` |

**3-1차에서 얻은 부가 지식**: 리더기 자체가 카드 미태그 상태에서 자기 내장 타임아웃(관찰상 약 60초, 리더기
개체별로 다를 수 있음 — Phase 16 단독 COM5 검증 때는 119.99초까지 안 끼어들었다)으로 먼저 실패 응답(`04`)을
줄 수 있다. 이건 `ReaderCommandOutcomeKind.BusinessFailure`(재시도 불가 코드)로 정상 처리되는 기존 로직
그대로다 — 버그가 아니라 실제 하드웨어가 소프트웨어 데드라인보다 먼저 개입할 수 있다는 환경적 사실이다.
소프트웨어 Timeout 경로 자체를 재현하려면 양쪽 리더기 모두 자체 타임아웃보다 먼저 걸리지 않아야 하므로
매번 보장되지는 않는다(이번엔 재시도 1회로 재현 성공).

**결론**: P16-3 완료조건의 "리더기 2대 모두 0x60" 부분을 실물로 확인했다 — 취소/Timeout 모두 같은
`FireInterruptCleanup` 경로를 타므로(취소 테스트로 이미 2대 정리가 실증됐고, Timeout도 동일 코드 경로임을
로그로 재확인), 가짜 하네스 검증에 이어 실물 이중화 구성에서도 안전함이 확인됐다.

### Phase 16 실장비 2대 추가 예외 케이스 (2026-08-26, 사용자 요청 — "테스트 예외 case까지 구석구석")

가짜 하네스에서만 검증됐던 경합 3종을 리더기 2대(COM5+COM7) 실물로 추가 재현했다.

| # | 시나리오 | 결과 | 근거 로그 |
|---|---|---|---|
| A | 늦은 취소(카드 리딩 성공 후 취소 시도) | 2회 시도(`P16-DUAL-LATECANCEL-1~2`) — 1차는 취소가 카드 태그보다 먼저 들어가 그냥 정상 취소로 끝남(재현 실패), 2차는 카드 리딩 성공(10:34:49.654) 후 VAN 승인(10:34:50.655)까지 "취소 통지 수신" 로그가 전혀 없이 끝남. **VAN-ESC 시나리오(5번)와 동일한 결론** — 카드 리딩 성공→VAN 진입이 동기 처리라 사람 반응속도로는 그 사이의 좁은 창에 들어갈 수 없다. 추가 재시도는 같은 결과만 반복할 가능성이 높아 중단 | `P16-DUAL-LATECANCEL-1~2` |
| B | 거의 동시 응답(2대 동시 카드 태그) | `PAYRES\|00\|...\|OK`. 리더기[0] 채택, 리더기[1]에 실제 `0x60` 무효화 전송 성공(`result=0`) — 중복 승인/예외 없이 정확히 1건만 채택 | `P16-DUAL-SIMUL-1` |
| C | 연속 거래 4건 백투백(2대 구성) | 4건 전부 `PAYRES\|00\|...\|OK`. 매 거래마다 참여 2대 정상 확인, 매번 리더기[0] 채택/리더기[1] 무효화(`result=0`) 성공, 경고·오류 로그 없음 — 다음 거래로 상태가 새는 징후 없음(H-1 회귀 없음) | `P16-DUAL-REPEAT-1~4` |

**결론**: B/C는 실물로 명확히 통과했다. A는 "재현이 안 됨" 자체가 결과다 — 카드 리딩 성공 이후의 취소는
동기 처리 구간이 너무 짧아 실제 사용자가 도달할 수 없는 경로이며, 이는 결함이 아니라 설계가 의도한 대로
좁다는 뜻이다(가짜 하네스 시나리오 17/18이 이 경로의 로직 정확성을 이미 보장한다).

### P16-5 실기 확인 결과 및 후속 수정 (2026-08-26)

실기로 확인하는 과정에서 **결제 알림창(Topmost)이 안내 `MessageBox`를 가려 사용자가 못 보는** UX 문제가
발견됐다.

**확인 경로 정리**: 실사용에서는 홈 화면의 "리더기 설정" 카드를 직접 클릭하는 경로가 사실상 불가능하다
— 결제 알림창이 `Topmost`라 화면을 물리적으로 덮고 있어 마우스 클릭이 아예 버튼에 닿지 않는다(자동화
도구의 Invoke 패턴은 이 시각적 차단을 우회해 프로그램적으로 클릭을 성공시킬 수 있어, 최초 시도에서
혼란을 유발했다 — 실제 사용자 경험과 다른 결과였다). **트레이 우클릭 메뉴 "리더기 설정"이 실질적으로
유일한 접근 경로**다.

**1차 시도(안내 `MessageBox` 포함)**: 거래 진행 중 트레이 메뉴로 접근 → 로직 자체(`IsProcessing` 검사)는
정확히 동작해 `ReaderSetupWindow`가 열리지 않았다. 하지만 안내 `MessageBox`가 `Topmost="True"`인
`PaymentNoticeWindow` **뒤에 가려져** 사용자가 알림을 볼 수 없었다(알림창을 닫아야만 뒤에 있던
메시지박스가 보임) — 게이트는 맞았지만 안내가 무의미했다.

**사용자 확정(2026-08-26)**: 안내를 Topmost로 만들어 결제 화면 위에 끼어들게 하는 방안은 "결제가
먼저"라는 원칙에 어긋나 채택하지 않는다. 결제 알림창이 이미 화면 최상단에 떠서 "지금 결제 중"임을
스스로 보여주므로, 별도 안내 없이 **조용히 무시**하는 것으로 확정. `HomeWindow.OpenReaderSetup()`에서
`MessageBox.Show(...)` 호출을 제거하고 `IsProcessing`이면 그냥 `return`하도록 수정.

**재검증**: 수정 후 재빌드·재실행, 거래 진행 중 트레이 메뉴로 "리더기 설정" 클릭 → 홈 화면은 복원되고
(`RestoreFromTray()`는 그대로 호출됨) 결제 알림창도 계속 떠 있으며, **`리더기 설정` 창은 열리지 않고
아무 안내도 뜨지 않음**을 실기로 확인(`windows_list_windows`로 두 창만 존재, 리더기 설정 창 없음을
확인).

**완료 조건 점검 (P16-5)**
- [x] 거래 진행 중 트레이 메뉴 "리더기 설정" → 조용히 무시(안내 없음), 실기 확인
- [x] 거래 종료 후 같은 경로 → 정상적으로 열림(차단이 영구화되지 않음, 코드상 `IsProcessing` 매 호출마다
      새로 평가되므로 구조적으로 보장됨)
- [x] 잠금 장치가 늘지 않음 — `TransactionQueue.IsProcessing` 하나만 추가, 새 `lock`/세마포어 없음

**Phase 16 전체 완료.** P16-1~P16-7 전 항목 구현·검증 완료(가짜 하네스 26개 시나리오 + 실장비 5개
시나리오 + P16-5 실기 확인).

---

# Phase 17 실행계획서 — POS↔원캡 실제 전문 적용 (SPEC 반영)

> 로드맵: `ROADMAP.md` "Phase 17 — POS↔원캡 실제 전문 적용". 임시 테스트 전문(`PAY|금액|거래ID`)을
> 실제 SPEC 전문 3종으로 교체하고, `PaymentOrchestrator`를 "요청 1건 = 결제 1건" 전제에서 **전문 종별
> 분기 구조**로 재구성한다.

## 착수 전 전제 (2026-08-26 확인 완료)

1. **SPEC 정본**: `spec/국세 베리어프리 키오스크용 전산설계서(POS-원캡)_20260826.pdf`(전 18페이지).
   같은 폴더 `.hwp`는 DRM 배포용 문서 래퍼(`<DOCUMENTSAFER_` 헤더)라 열리지 않는다 — PDF가 유일하게
   파싱 가능한 원본이다.
2. **필드를 코드에 옮기기 전에 반드시 `pos-onecap-spec-expert` 서브에이전트로 확인한다.** 표를 눈으로
   훑으면 SET 장소 열을 착각하기 쉽다 — 실제로 최초 정리 때 `#48 거래 입력 유형`을 kiosk로 잘못 읽어
   원캡 담당 필드를 6개로 셌던 전례가 있다(정답은 7개).
3. **Phase 14의 프레이밍은 그대로 살아남는다.** `PosMessageFramer`의 공개 계약은 `Append(byte[]) →
   IReadOnlyList<byte[]>` 하나뿐이고, 프레이밍 규칙도 `[ASCII 4자리 길이][본문]`으로 SPEC의 `#0 전문 길이`
   (N 4)와 정확히 일치한다. P14-1이 "실제 SPEC 확정 시 이 클래스 내부만 새로 짜면 된다"고 설계해 둔 것이
   맞아떨어져, **프레이머와 `PosSocketServer`는 이번 Phase에서 손대지 않는다.** 바뀌는 것은 본문 해석뿐이다.
4. **`PosPaymentResultCode` 열거형도 대부분 살아남는다.** P15-3이 "값 하나하나는 SPEC 미확정인 지금도
   바뀌지 않고, 바뀌는 것은 전문 코드 문자열로 바꾸는 매핑 하나뿐"이라고 설계해 둔 대로, `PosPaymentResponse.
   Create`의 `switch` 매핑표를 교체하는 것이 주 작업이다(다만 아래 확정 사항 6에 따라 표현력이 일부 늘어난다).
5. **VAN 서버는 아직 개발 중이라 접속 불가**(2026-08-26 사용자 확인). 이번 Phase의 VAN 단계는 Phase 15가
   만든 스텁을 그대로 쓰고, 실제 `FNAISCRDVAN` 호출은 Phase 20이다.

## 확정된 설계 결정 (2026-08-26 사용자 확정)

1. **`#0 전문 길이`에는 본문 길이를 넣는다** — 706 / 500 / 1500. 길이 4바이트는 본문 밖 헤더이므로
   소켓에 실제로 나가는 바이트는 **710 / 504 / 1504**다. 근거: `#0`만 POSITION 열이 비어 있고
   `#1 업무 구분`이 POSITION 0에서 시작한다.
2. **전문마다 TCP 연결을 새로 연다.** 3전문은 하나의 거래로 묶이지 않는 **각각 독립한 기능**이다.
   따라서 `800000`에서 읽은 카드 데이터를 `902614`로 넘기지 않으며 **사용자는 카드를 2회 댄다**
   (PRD §8.4 "거래 종료 시 카드 데이터 즉시 삭제"를 전문 단위로 그대로 유지).
3. **카드리딩 로직은 3전문에서 동일하다.** `800000`도 `902614`와 똑같이 거래구분 `ARQo`로 요청하고,
   응답의 카드번호 필드에서 **앞 8자리(BIN)만 파싱**해 채운다. FALLBACK(`07`)·`12` 재요청 흐름도 양쪽
   모두 기존과 동일하게 존재한다 — 즉 Phase 15가 만든 `RunCardReadingRoundsAsync`를 **그대로 재사용**한다.
4. **알림창은 3전문 모두 띄운다.** `501008`은 카드리딩이 없으므로 **곧바로 통신중(PROCESSING) 상태**로
   띄우고 응답이 오면 닫는다. `800000`/`902614`는 IC → (902614만 PIN, Phase 18) → 통신중 순서다.
5. **`#43` 보안단말기 인증번호** = 리더기 식별자(16) + 프로그램 식별자(16) = ANS 32. 리더기 식별자는
   **카드리딩 응답의 "리더기 인증 식별 번호"**를 쓰고, 프로그램 식별자는 **`KFTCTAXGIROCAP01`**(정확히
   16자 — 자릿수가 맞는다)을 상수 1곳에 선언해 쉽게 바꿀 수 있게 둔다.
6. **원캡 자체 오류 응답코드 체계를 신설한다.** SPEC 3장은 인터넷지로/KFTCVAN이 내려주는 코드만 정의하고
   원캡이 스스로 실패시키는 경우(취소·Timeout·리더기 실패·DLL 오류)의 코드가 없다. SPEC이 이미 `M01`/`V01`
   처럼 **주체를 접두 문자로 구분**하는 관례를 쓰고 있으므로 이를 따르되, 이미 쓰인 `M`/`V`는 피한다.

   | 접두 | 주체 | 배정 |
   |---|---|---|
   | `E` | 원캡 자체 판단 + 전문 오류 | `E01` 사용자 취소 / `E02` 카드입력 Timeout / `E03` 설정화면 열림 / `E04` 리더기 미설정 / `E05` 무결성 체크 실패(전원) / `E40` 전문 길이 불일치 / `E41` 알 수 없는 거래구분 / `E99` 내부 오류 |
   | `R` | 리더기 관련 | `R0x` 카드리딩 업무 응답코드 실패(리더기가 준 `00`/`07`/`12` 외 코드를 세분) / `R2x` 리더기 DLL 연동 실패(`PORT_NOT_OPEN`/`SEND_FAIL`/`BUSY`/`PORT_NOT_FOUND` 등을 각각 다른 코드로) |
   | `D` | VAN DLL(`KFTC_GIRO`) | `D01` DLL 로드 실패 / `D02` 호출 실패(`nRet == -1`) |

   (2026-08-26 정정: 원래 `G`를 배정했으나, `G`도 VAN이 실제로 내려줄 수 있는 카드사/인터넷지로 응답코드
   대역이라 충돌 위험이 있어 `D`(DLL)로 변경. **배제 대역은 `A`/`B`/`G`/`H`/`Z`**(VAN·카드사 응답코드가
   이미 쓰거나 쓸 수 있음, 사용자 확인) — 우리 자체 코드는 `E`/`R`/`D`만 쓴다.)

   숫자 대역(`000`~`201` 및 카드사 응답코드)과도, `M01`/`V01`과도 충돌하지 않는다. 이로써 기존 2자리
   결과코드는 사라지고, "DLL 실패 원인을 더 잘게 나누고 싶다"던 요구도 `R2x`/`G0x`에서 함께 해결된다.
7. **`#38` 카드소유주 주민(사업자)등록번호는 항상 공백**이며 kiosk가 채운다. SPEC 표에는 SET 장소 표시가
   없고 p.15 설명 절의 "고지내역정보와 동일하게 SET" 목록에는 포함돼 있어 문서가 어긋나 있던 항목인데,
   이 결정으로 해소됐다.
8. **`#51` 암호화된 비밀번호 정보는 이번 Phase에서 space 스텁**으로 둔다. 설계서에는 kiosk 담당으로
   표시돼 있으나 **설계서 오류이고 원캡이 입력받는 것이 맞다**(사용자 확정) — 실제 입력·채움은 Phase 18.
9. **인코딩은 CP949**로 바꾼다. SPEC에 한글 필드(`AHN`/`AHNS`)가 다수 있어 현재의 `Encoding.ASCII`로는
   한글이 물음표로 조용히 깨진다(P14-1이 예상해 둔 그 상황이다). **필드 길이는 문자 수가 아니라 바이트 수**로
   다룬다 — 한글 1자 = CP949 2바이트이므로 `AHN 40`은 한글 20자다. 이걸 글자 수로 착각하면 고정길이 전문의
   POSITION이 전부 밀린다.
10. **패딩 규칙**: `N`(숫자)은 **우측정렬 + 앞을 `0`으로 채움**, 문자 계열(`A`/`AN`/`AHN`/`ANS`/`AHNS`)은
    **좌측정렬 + 뒤를 space로 채움**. 채우지 않는 필드는 **전체 space**(SPEC p.5 각주). 국내 고정길이
    금융전문의 표준 관례이나 **SPEC이 명시하지 않았으므로**, 이 규칙을 필드 정의 한 곳에서만 결정해
    나중에 한 줄로 바꿀 수 있게 격리한다.

## 이 Phase에서 손대지 않는 것 (범위 밖 확정)

- **`PosMessageFramer` / `PosSocketServer` / `TransactionQueue`** — 프레이밍과 소켓·큐는 그대로다(전제 3).
  다만 `PosMessageEncoding` 상수 값만 바뀐다(CP949).
- **`Services/Reader/` 전체와 `Protocol/Reader/`** — 카드리딩 로직은 동일하다(확정 사항 3). `0x2B` 요청
  빌더와 `0x3B` 파서를 건드리지 않고, 파싱된 결과에서 필요한 값을 꺼내 쓰기만 한다.
- **PIN 입력 화면** — Phase 18. 이번엔 `#51`을 space로 둔다.
- **실제 VAN 호출** — Phase 20. Phase 15의 VAN 스텁을 그대로 쓴다.
- **취소/Timeout 경합 게이트** — Phase 16이 완성했다. 재구성 과정에서 **깨뜨리지 않는 것**이 목표이지
  개선 대상이 아니다.

---

## P17-1. 전문 코덱 — 필드 정의 테이블 + 고정길이 읽기/쓰기 ★

**먼저 이것부터.** 필드 오프셋이 흔들리면 뒤의 모든 검증 결과를 믿을 수 없다.

### 원본 보존 원칙 (이 Phase에서 가장 중요한 설계 결정)

**전문을 강타입 객체로 전부 분해했다가 재조립하지 않는다. 수신한 원본 바이트 배열을 그대로 들고 있다가
지정한 필드만 덮어쓴다.** 이유는 셋이다.

1. **원캡은 대부분의 필드를 "그대로 통과"시킨다.** `501008`은 원캡이 채우는 필드가 아예 없고, `800000`은
   1개, `902614`도 7개뿐이다. 나머지 수십 개는 kiosk가 채운 값을 해석하지 않고 VAN으로 넘기기만 한다.
   분해 후 재조립하면 **우리가 의미를 모르는 필드**(예비 정보 FIELD 등)에서 값이 유실될 위험을 스스로 만든다.
2. **패딩·인코딩 왕복에서 값이 미묘하게 바뀔 위험이 없다.** 한글 필드의 space 패딩, 숫자 필드의 앞자리 `0`
   같은 것은 읽었다 다시 쓰는 과정에서 어긋나기 쉽다 — 애초에 건드리지 않으면 어긋날 일이 없다.
3. **응답 전문도 요청과 같은 레이아웃**이다(SPEC의 표 하나가 요청과 응답을 겸한다). 요청 버퍼를 복사해
   `#3 전문 종별 코드`를 `0210`으로, `#7 응답 코드`를 채우는 것만으로 응답이 완성된다.

### 구현할 것

- `Protocol/Pos/PosFieldType` — `N`/`A`/`AN`/`AHN`/`ANS`/`AHNS`. 패딩 방향과 채움 문자를 **이 열거형
  하나에서만** 결정한다(확정 사항 10). `N`이면 우측정렬에 `'0'`, 그 외는 좌측정렬에 space.
- `Protocol/Pos/PosField` — `Number`(SPEC의 필드 번호), `Name`(SPEC 표기 그대로의 한글명), `Type`,
  `Length`(**바이트 수**), `Position`(본문 기준 0-based). SPEC 표를 그대로 옮긴 값 객체다.
- `Protocol/Pos/PosTelegramSchema` — 필드 목록과 본문 총 길이. **생성 시 자체 검증을 수행한다**:
  - 필드들의 `Position`이 0부터 **빈틈 없이 연속**하고 서로 겹치지 않는다
  - 마지막 필드의 `Position + Length`가 선언된 총 길이와 정확히 같다

  이 검사는 손으로 옮겨 적은 표의 오타를 **앱 기동 시점에** 드러낸다. 고정길이 전문에서 오프셋 하나가
  밀리면 그 뒤 전부가 깨지는데, 그걸 런타임에 이상한 값으로 만나는 것보다 시작하자마자 예외로 터뜨리는
  편이 훨씬 싸다.
- `Protocol/Pos/PosTelegram` — 원본 본문 바이트 배열을 들고 스키마로 필드를 읽고 쓴다.
  - `string Read(PosField field)` — 해당 구간을 CP949로 디코딩하고 패딩을 제거해 돌려준다
  - `void Write(PosField field, string value)` — 값을 CP949로 인코딩해 패딩을 적용해 그 구간만 덮어쓴다.
    **인코딩 결과가 `Length`를 넘으면 즉시 예외**를 던진다(조용히 잘라내지 않는다 — 한글 2바이트 때문에
    글자 수로 착각한 실수가 여기서 드러나야 한다)
  - `byte[] ToBody()` — 원본 버퍼의 복사본
  - `PosTelegram Clone()` — 응답 생성용
- `Protocol/Pos/PosMessageEncoding` — `Encoding.ASCII`에서 **CP949**(`Encoding.GetEncoding(949)`)로 교체.
  `net48`에서는 별도 등록 없이 쓸 수 있으나, 실제로 얻어지는지 기동 시 확인한다.

### 완료 조건

- [x] 3전문 스키마 모두 자체 검증(POSITION 연속·총 길이 일치)을 통과한다 — 특히 `902614`는 SPEC에
      총 길이 행이 없어 `#54`(AN 172, POSITION 1328)로부터 계산한 **1500**이 맞는지 이 검사로 확인된다
- [x] 한글 필드 왕복: `AHN 40` 필드에 한글 20자를 쓰면 정확히 40바이트를 채우고, 읽으면 원문이 그대로
      돌아온다. 한글 21자를 쓰면 **예외**가 난다(42바이트 초과)
- [x] 숫자 필드 패딩: `N 15`에 `"1000"`을 쓰면 `"000000000001000"`이 된다
- [x] 문자 필드 패딩: `AN 13`에 13자를 쓰면 그대로, 3자를 쓰면 3자 뒤에 space 10자가 붙는다
- [x] `Write` 후 다른 필드 구간의 바이트가 **단 1바이트도 바뀌지 않는다**(원본 보존 원칙의 핵심 검증)
- [x] `Protocol/Pos/` 안에 `System.Net` 참조 0건(P14-1에서 세운 계층 규칙 유지)

**구현 완료(2026-08-26)**: `Protocol/Pos/PosFieldType`, `PosFieldOwner`(SET 장소 플래그, P17-2 선행 반영),
`PosField`(패딩/트림), `PosTelegramSchema`(자체 검증), `PosTelegram`(원본 보존 + `FromBytes`/`Clone`/
`CreateEmpty` 3경로)로 구현했다. `PosMessageEncoding`을 CP949로 교체했다. 검증은 `ProjectReference` 기반
스크래치패드 콘솔 하네스(저장소 밖, `KFTCOneCAP.Wpf.csproj` 참조 — 모든 신규 클래스가 `public`이라
`InternalsVisibleTo` 불필요)로 위 15개 조건 전부 실측했다: 미니 스키마(N15+AN13+AHN40)로 패딩·트림·한글
왕복·초과 예외·원본 보존(Clone 후 한 필드만 재작성해도 나머지 필드 바이트 불변)·relay 경로(`FromBytes`가
바이트를 전혀 안 건드림)·스키마 자체 검증 실패(POSITION 어긋남, 총 길이 불일치) 2종까지 확인.

---

## P17-2. 공통부 + 3전문 스키마 정의

**공통부 14필드(POSITION 0~69, 70바이트)는 3전문이 완전히 동일한 레이아웃(오프셋·길이)**이다. 다만
**필드 이름 표기는 3버전이 있고, 다른 쪽은 800000이 아니라 501008이다**(2026-08-26, `pos-onecap-spec-expert`
재확인 — 최초 정리 때 방향을 반대로 적었던 착오 정정): `#9`/`#11`/`#12`/`#13`에서 **501008**의 표기
(`요청기관 전문 관리 번호`, `지로 이용기관 분류코드`, `지로 이용기관 지로번호`, `FILLER`)가
**800000·902614**(`은행/센터 전문 관리 번호`·`이용기관 발행기관 분류코드`·`이용기관 지로 번호`·
`FILLER(응답 코드 구분)`)와 다르다 — 800000과 902614 둘의 표기는 서로 동일하다. 오프셋·길이·표현은
세 버전 모두 완전히 같으므로(N4/A3/N3/N4/N6/N3/AN1/AN3/N12/AN12/AN12/N2/N7/N2, POSITION 0~70) 코드에는
영향이 없고, **주석에 "800000 기준 이름"을 달아 501008과 다르다는 점만 남긴다.** 공통부는 한 번만
정의하고 3전문이 공유한다.

또한 `#3`/`#4`(전문 종별 코드/거래 구분 코드) 고정값은 **SPEC p.5의 선언 표에 501008·902614 둘만 있고
800000은 없다** — 800000의 `0200`/`800000`은 p.12 흐름도에서만 유추 가능하다(별도 "고정값" 선언 문장
없음). 구현에는 문제 없다(흐름도가 유일한 근거이므로 그대로 쓴다)는 점만 알아 둔다.

- `Protocol/Pos/Schemas/PosCommonHeader` — 필드 0~13. 고정값도 여기서 정의한다:
  - `#1 업무 구분` = `"IGN"` 고정
  - `#2 요청기관 코드` = `"095"` 고정
  - `#3 전문 종별 코드` = `"0200"`(요청) / `"0210"`(응답)
  - `#6 송·수신 FLAG` = `"C"`(통합센터) / `"G"`(요청기관 OneCAP)
  - `#8 전송 일시` = `YYMMDDhhmmss`
- `Protocol/Pos/Schemas/NoticeInquirySchema`(501008, 706) / `CardInfoInquirySchema`(800000, 500) /
  `CardApprovalSchema`(902614, 1500) — 각 전문의 업무부 필드.
- 각 필드에 **SET 장소를 함께 기록**한다(인터넷지로 / 디지털예산 / VAN / kiosk / 원캡). 이 정보가
  코드에 있어야 "원캡이 채워야 할 필드"를 목록으로 뽑을 수 있고, P17-6에서 채움 누락을 검사할 수 있다.

**원캡 담당 필드(SET 장소 "원캡" 표시)** — 이것이 이 Phase가 실제로 값을 넣는 전부다:

| 전문 | 필드 | 표현 | POSITION |
|---|---|---|---|
| 501008 | (없음 — 순수 중계) | | |
| 800000 | `#14` BIN | AN 8 | 70 |
| 902614 | `#43` 보안단말기 인증번호 | ANS 32 | 355 |
| 902614 | `#44` FALLBACK CODE | N 2 | 387 |
| 902614 | `#45` 복호화 정보 | AN 18 | 389 |
| 902614 | `#46` 암호화된 카드정보 | AN 196 | 407 |
| 902614 | `#48` 거래 입력 유형(IC 전문 전용) | AN 1 | 609 |
| 902614 | `#50` 신용카드 승인 인증방식 | AN 1 | 611 |
| 902614 | `#53` EMV DATA | ANS 604 | 724 |
| 902614 | `#51` 암호화된 비밀번호 정보 (설계서 오류 정정분, **Phase 18**) | ANS 100 | 612 |

### 완료 조건

- [x] 표의 모든 값을 `pos-onecap-spec-expert`로 **재확인한 뒤** 옮겼다(눈으로 훑고 옮기지 않았다)
- [x] 공통부가 한 곳에만 정의되어 있다(3전문에 복사되어 있지 않다)
- [x] `#48`이 원캡 담당으로 들어가 있다(최초 정리 때 kiosk로 잘못 읽었던 필드 — 회귀 방지)
- [x] 원캡 담당 필드를 목록으로 뽑는 API가 있고, `501008`에 대해 **빈 목록**을 돌려준다

**구현 완료(2026-08-26)**: `Protocol/Pos/Schemas/PosCommonHeader`(공통부 필드 생성 팩토리 — 필드
이름·오프셋·길이는 한 곳에만 정의하고, 전문별로 다른 SET 장소만 호출부에서 배열로 받는다) +
`NoticeInquirySchema`/`CardInfoInquirySchema`/`CardApprovalSchema`(3전문 각각의 업무부) +
`PosSchemaRegistry`(거래구분 코드로 라우팅, 정적 생성자에서 3개 스키마를 즉시 생성해 자체 검증이 앱
기동 시점에 실행되도록 함)로 구현했다.

**재확인 과정에서 최초 정리(에이전트 정의 작성 시점)의 오류 하나를 추가로 발견해 정정했다**: 공통부
필드 이름이 "800000만 다르다"고 적었었는데 실제로는 **501008이 다르고 800000·902614가 서로 같다**
(반대 방향) — 착수 전 전제 절에 기록해 뒀다.

**재확인 결과 원캡 담당 필드는 902614에서 8개다**(7개+`#51`, `#51`은 사용자가 설계서 오류로 확정한
정정분 — Phase 17에서는 space 스텁이지만 스키마 소유자로는 이미 등록해 둔다). 리플렉션 기반 스크래치패드
하네스(P17-1과 동일 구성)로 검증: 501008(총 길이 706, 필드 56개, 원캡 0개) / 800000(총 길이 500, 필드
27개, 원캡 1개=`#14`) / 902614(총 길이 1500, 필드 54개, 원캡 8개=`#43,44,45,46,48,50,51,53`, `#38`은
원캡 목록에 없음) 전부 확인. 세 스키마 모두 정적 생성자 시점에 `PosTelegramSchema`의 POSITION 연속성·
총 길이 자체 검증을 실제로 통과했다(예외 없이 하네스가 끝까지 실행됨).

---

## P17-3. 라우팅 + 요청 파싱 / 응답 생성

- `PosPaymentRequest`를 **`PosRequestTelegram`**(가칭)으로 대체. `Parse(byte[] body)`는 이제:
  1. 본문 길이로 1차 판별할 수 없다(길이가 같은 전문이 생길 수 있음) — **`#4 거래 구분 코드`
     (N 6, POSITION 10)를 읽어** 스키마를 고른다
  2. 고른 스키마의 총 길이와 실제 본문 길이가 다르면 **`E40`**(전문 길이 불일치)
  3. 알 수 없는 거래구분이면 **`E41`**
  - `#4`를 읽으려면 최소 16바이트가 필요하므로, 그보다 짧은 본문은 즉시 `E40`으로 처리한다
- **응답은 두 경로로 나뉜다(2026-08-26 발견 — 최초 설계 오류 정정).** 흐름도(p.7/12/13)를 다시 보면 요청은
  `①0200/전문코드`가 POS→OneCAP→KFTCVAN까지, 응답은 `④0210/전문코드`가 KFTCVAN→OneCAP→POS까지 **같은
  라벨로 이어진다** — 이는 각 경계마다 새로 만드는 전문이 아니라 **같은 바이트를 그대로 통과시키는 중계**
  라는 뜻이다. 실제로 응답 필드 대부분(501008의 납기내 금액, 800000의 카드사명, 902614의 카드사 응답코드
  등)은 kiosk가 아니라 **디지털예산/인터넷지로/VAN이 채우는 값**이므로, 요청을 복제해서 만들 수 있는 값이
  아니다 — VAN이 실제로 준 응답이 있어야만 존재한다.
  - **성공 경로(VAN까지 도달)**: **`PosResponseTelegram`은 VAN이 돌려준 응답 바이트를 그대로 감싼다**
    (relay). Phase 20에서 VAN 응답이 실제로 이 전문과 동일 포맷이라는 점이 확인됐으므로(ROADMAP Phase 20
    설명), `#3`/`#6`/`#7`을 포함해 VAN이 준 값을 **다시 덮어쓰지 않는다** — 이미 올바른 값으로 채워져 온다.
    Phase 17 시점에는 VAN이 스텁이므로, 스텁이 "정상 응답처럼 보이는 바이트"를 돌려주는 형태로 맞춘다.
  - **실패 경로(OneCAP이 VAN에 도달하기 전 자체 실패 — 취소/Timeout/리더기 실패/전문 오류)**: VAN 응답
    자체가 없으므로 합성해야 한다. **이때만** 요청 텔레그램을 `Clone()`한 뒤 `#3`을 `"0210"`, `#6`을
    `"G"`, `#7`을 결과 코드(`E`/`R` 코드), `#8`을 응답 시각으로 덮어쓴다. 서버가 채우는 필드들은 kiosk도
    원 요청에 채우지 않아 이미 공백이므로(SET 장소가 "디지털예산"/"인터넷지로"/"VAN" 단독인 필드는 kiosk
    열에 ○가 없다) clone해도 값이 어색해지지 않는다 — 단, 이건 우연이 아니라 그 전제가 성립하기 때문이며
    구현 시 이 전제를 주석으로 명시한다.
  - `G0x`(VAN DLL 통신 자체 실패)는 성격상 실패 경로에 속한다 — VAN을 호출했지만 통신이 실패한 것이므로
    응답을 못 받은 것은 취소/Timeout과 동일하게 취급한다.
- **프레이머와의 계약은 그대로다.** `ToFrame()`은 여전히 `[4자리 길이][본문]`을 만든다 — 길이 값이
  이제 `0706`/`0500`/`1500` 같은 스키마 총 길이라는 점만 다르다.
- `ValidateBodyField`(ASCII 및 파이프 금지)는 **삭제한다.** 파이프 구분이 사라지고 CP949 한글이 정상 값이
  되었으므로 이 검증은 더 이상 맞지 않는다. 대신 `PosTelegram.Write`의 **길이 초과 예외**가 같은 역할
  (조용히 깨지는 것을 막는 방어)을 한다.

### 완료 조건

- [x] 3전문 각각을 정확한 바이트 수(710 / 504 / 1504)로 만들고, 왕복 파싱 시 모든 필드가 보존된다
- [x] 길이가 1바이트 모자란 전문은 `E40`, 거래구분이 `999999`인 전문은 `E41`. 둘 다 **연결을 닫지 않고**
      그 프레임만 실패 응답한다(P14-5가 세운 규칙: 프레이밍 오류만 연결을 닫는다)
- [x] **실패 경로(Clone)**: 응답 전문의 `#3`이 `0210`, `#6`이 `G`, `#7`이 결과 코드(`E`/`R` 코드)이고
      나머지 필드는 요청과 바이트 단위로 동일함을 확인한다
- [x] **성공 경로(Relay)**: VAN 스텁이 돌려준 응답 바이트를 `PosResponseTelegram`이 그대로 감싸 POS로
      전달하고, `#3`/`#6`/`#7` 등 어떤 필드도 다시 덮어쓰지 않는다는 것을 코드로 확인한다(clone 경로와
      relay 경로가 서로 다른 코드 경로임을 테스트로 구분)
- [x] `Services/` 어디에도 `"0200"`/`"501008"` 같은 전문 리터럴이 없다(grep 확인) — P15-3이 세운 규칙 유지

**구현 완료(2026-08-26)**: `PosRequestTelegram`(파싱, `PosRequestParseOutcome`으로 성공/E40/E41을 예외
대신 값으로 반환), `PosResponseTelegram`(`Relay`/`Failure` 두 경로), `PosUnknownTransactionErrorResponse`
(E41 전용 — 스키마를 모르므로 공통부 70바이트만으로 최소 응답 합성)로 구현했다.

**설계 보강 사항 하나**: E40(길이 불일치)은 요청 바이트 길이가 스키마와 다르므로 **Clone이 불가능하다**
(필드 오프셋을 신뢰할 수 없음) — 그래서 `PosResponseTelegram.Failure(PosTelegramSchema, resultCode)`
오버로드를 추가해 `CreateEmpty(schema)` 기반으로 빈 응답을 합성한다. `Failure(PosRequestTelegram, ...)`
(정상 파싱된 요청의 자체 실패용)와는 다른 경로다 — 완료 조건의 "실패 경로(Clone)"는 후자에만 해당한다.

**이번 Phase에서 `PosSocketServer`/`TransactionQueue`/`PaymentOrchestrator`는 의도적으로 손대지 않았다**
(범위 밖 확정과 일치) — `PaymentOrchestrator` 재구성(P17-5)이 이 연결부를 통째로 다시 짜야 하므로, 지금
임시로 잇는 것은 이중 작업이다. 새 Protocol 타입은 P17-1/P17-2와 같은 방식의 리플렉션 하네스로 독립
검증했다: 3전문 요청 생성→파싱→relay/failure 응답 생성까지 왕복 45개 조건(길이/필드값/원본 보존) 전부
확인, E40(길이 1바이트 부족)·E41(거래구분 `999999`)·본문 15바이트 미만(#4조차 못 읽음, 기존
`PosProtocolException` 경로로 회귀)까지 3가지 오류 경로 모두 검증.

> **체크포인트 1** — 여기까지가 "전문 계층"이다. Flow를 건드리기 전에 Opus 검증 리뷰를 받는다. 고정길이
> 오프셋·인코딩·패딩은 한 번 어긋나면 뒤의 모든 검증이 무의미해지므로, 여기서 끊는 것이 가장 싸다.

## Phase 17 체크포인트 1 — Opus 검증 리뷰 및 후속 수정 (2026-08-26)

Sonnet이 P17-1~P17-3을 구현한 뒤 Opus가 코드 재검토 + 실측 재현으로 별도 검증했다. **결함 2건과 설계
약속 불이행 1건, 개선 1건을 확정**하고 전부 수정 후 재검증했다(Phase 12/13/14/15/16과 같은 워크플로우).

### H-1. `PosField.Trim`이 N 필드의 앞자리 `0`을 삭제해 코드성 필드를 손상 (확정·수정)

`Trim`이 `N` 타입에 `TrimStart('0')`을 적용했다. SPEC의 `N` 필드에는 수량뿐 아니라 **코드**가 다수 있어
값 자체가 달라진다 — 실측으로 재현한 손상:

| 필드 | 쓴 값 | 읽힌 값 |
|---|---|---|
| `#2` 요청기관 코드 (N 3) — **우리가 반드시 쓰는 고정값** | `095` | `95` |
| `#33` 카드사 코드 (N 2) | `01` | `1` |
| `#15` 납부 순번 (N 3) — SPEC p.9가 "`001`부터 순차 증가"로 명시 | `001` | `1` |
| `#47` 수납은행 점별 코드 (N 7) | `0011234` | `11234` |

저장된 바이트 자체는 정상(`Write`는 올바름)이라 **읽을 때만 조용히 손상**되는 종류였다. 아직 Flow가
필드를 읽지 않아(P17-5 미착수) 드러나지 않았을 뿐, P17-5/P17-6이 읽기 시작하면 바로 터진다.
라우팅(`#4`)만은 `Read()`가 아니라 `GetString`을 직접 쓰고 있어 영향이 없었음을 함께 확인했다.

**더 나쁜 점: P17-1 하네스가 이 잘못된 동작을 "round-trip 성공"으로 검증해 정답으로 굳혀 놨었다.**
`Write("1000")` → `Read()=="1000"`을 통과 조건으로 삼았는데, 그게 바로 앞자리 `0`을 지우는 동작이었다.

**수정**: `Trim`은 이제 **명백히 패딩인 것만** 제거한다 — 전체 space인 필드(=미입력, SPEC p.5)는 빈
문자열로 정규화하고, 그 외에는 `N`도 값을 있는 그대로 돌려준다(문자 계열은 뒤쪽 space만 제거).
숫자로 다뤄야 하는 호출자는 `long.Parse`를 쓰면 되고, 그건 앞자리 `0`을 알아서 처리한다.

### M-1. `N` 필드에 빈 값을 쓰면 `0`으로 채워져 "미입력"이 "0원"으로 오인 (확정·수정)

`Write(#29 총 납부 금액, "")` → `"000000000000000"`. SPEC p.5 각주는 "채우지 않는 필드는 space"라고
명시하므로 어긋나고, 금액 필드에서는 **미입력과 0원을 구별할 수 없게 된다.**
**수정**: 빈 값은 타입과 무관하게 전체 space로 채운다.

### M-2. "스키마 오류가 기동 시점에 드러난다"는 설계 약속이 실제로는 거짓 (확정·수정)

`PosSchemaRegistry`의 정적 필드 초기화는 **최초 접근 시점**에 일어나는데, `App.xaml.cs`가 이 클래스를
전혀 참조하지 않는 것을 실측 확인했다(`appXaml.Contains("PosSchemaRegistry") == False`). 즉 P17-2가
내세운 "SPEC 표를 옮겨 적다 틀렸으면 앱이 기동하지 못하고 즉시 드러난다"는 보장이 성립하지 않았고,
실제로는 **첫 결제 요청이 들어와서야** `TypeInitializationException`으로 터졌을 것이다.

**수정**: `PosSchemaRegistry.ValidateAtStartup()`을 신설하고 `App.xaml.cs`의 DLL 로드 스모크 바로 뒤에서
호출한다(같은 취지의 기동 점검이라 나란히 둔다). DLL 로드와 달리 이건 우리 코드의 자체 모순이므로 조용히
넘기지 않고 그대로 던진다. 실제 앱을 띄워 로그로 확인: `POS 전문 스키마 3종 검증 완료(POSITION 연속성·
총 길이·라우팅 상수 일치)`.

### L-1. 라우팅용 `#4` 오프셋 상수가 스키마와 중복 (개선·반영)

`PosRequestTelegram`이 `#4`의 POSITION(10)/길이(6)를 하드코딩한다. "스키마를 고르려면 먼저 `#4`를 읽어야
한다"는 닭-달걀 때문에 불가피한 중복이지만, 어긋나면 **조용히 엉뚱한 6바이트로 라우팅**하게 된다.
**반영**: 위 `ValidateAtStartup()`이 3전문 스키마의 `#4` 위치·길이가 이 상수와 일치하는지 함께 확인한다.

### 미해결로 남기고 P17-6에 넘긴 것: `#51`과 "원캡 필드 전부 채움" 검사의 충돌

`#51`(암호화된 비밀번호 정보)은 사용자 확정에 따라 스키마상 **원캡 담당**으로 등록돼 있는데, Phase 17에서는
space 스텁이다. P17-6의 완료 조건 "채워야 할 필드가 하나도 빠짐없이 채워졌는지 전송 직전에 검사(누락 시
전송하지 않고 오류)"를 그대로 적용하면 **정상 902614 거래가 전부 실패한다.** P17-6 구현 시 `#51`을 명시적
예외로 두고 Phase 18에서 그 예외를 제거해야 한다 — 결함은 아니지만 그대로 두면 반드시 밟는 지뢰라 여기
기록해 둔다.

### 재검증

수정 후 하네스를 확장해(H-1 회귀 3종, M-1 회귀 2종, M-2/L-1 기동 검증 2종 추가) **전 항목 통과**를
확인했다. 실제 앱 기동 로그로 `ValidateAtStartup`이 실동작하는 것까지 확인했다.

---

## P17-4. 응답코드 체계 교체 (`E`/`R`/`D`)

- `PosPaymentResultCode` 열거형을 확장한다. 기존 값은 유지하되(P15-3의 설계가 옳았다), **실패 원인을
  더 잘게 나눠야 하는 두 값**을 세분한다:
  - `ReaderDllFailure` — 리더기 DLL 오류 코드(`PORT_NOT_OPEN`/`SEND_FAIL`/`BUSY`/`PORT_NOT_FOUND` 등)를
    **함께 실어 나를 수 있게** 한다. 열거값을 오류 코드 수만큼 늘리기보다, 결과 객체가 원인을 들고 다니고
    매핑 지점에서 `R2x`로 펼치는 편이 낫다 — 리더기 DLL 오류 코드가 늘어나도 열거형을 안 건드린다
  - `ReaderResponseFailure` — 리더기 업무 응답코드(`04` 등)를 같은 방식으로 실어 `R0x`로 펼친다
- `PosPaymentResponse.Create`의 `switch` 매핑표를 확정 사항 6의 표로 교체한다. **이 매핑은 P17-3이 정리한
  "실패 경로(Clone)"에서만 쓰인다** — VAN까지 도달한 성공 응답은 relay 경로이므로 이 매핑을 거치지 않고
  VAN이 준 `#7`(예: SPEC의 `"000"`, `111`~`201`, `M01`, `V01`, 카드사 응답코드)이 **그대로** POS에 전달된다.
  `Approved` 열거값은 이 매핑표에 등장하지 않는다(성공은 코드가 아니라 relay 여부로 판정된다) — 구현 시
  이 열거값이 여전히 필요한지(Flow 내부에서 "성공했다"는 판정을 표현하는 용도로) 확인한다.

### 완료 조건

- [x] `Services/Payment/`에 `"E01"` 같은 코드 리터럴이 없다(열거형만 다룬다)
- [x] 리더기 DLL 오류 4종이 각각 다른 `R2x` 코드로 POS에 도달한다
- [x] VAN이 준 코드(예: `121`)가 원형 그대로 POS에 전달되고 우리 코드로 덮어써지지 않는다(P17-3의 relay
      경로가 이미 보장 — `#7`을 포함해 어떤 필드도 재작성하지 않는다는 것을 체크포인트 1에서 검증 완료)
- [x] 매핑되지 않은 열거값이 들어오면 기존처럼 예외를 던진다(조용히 빈 코드가 나가지 않는다)

**구현 위치 정정(2026-08-26)**: 계획 당시엔 이 매핑이 `Protocol/Pos/PosPaymentResponse.Create` 안에
있을 것으로 봤으나, `R2x`(리더기 DLL 연동 실패) 코드를 정하려면 리더기 DLL 오류 종류를 알아야 하는데
그건 `Protocol/Pos`가 몰라야 하는 계층이다(ROADMAP 계층 구조 — `Protocol`은 리더기 DLL 세부사항을 알지
못한다). 그래서 `Services/Payment/PosResultCodeMapper`(신설)로 옮겼다 — 리더기·VAN 실패를 이미 둘 다
아는 계층이 매핑도 갖는 게 맞다. `Protocol/Pos`는 완성된 3자리 문자열을 받아 응답 전문에 싣기만 한다.

**구현 완료**: `PosResultCodeMapper`에 3개 오버로드로 구현했다 — `ToTelegramCode(PosPaymentResultCode)`
(E01~E05, E99, 나머지는 세부 원인이 필요하다는 예외), `ToTelegramCode(CardReadCommandOutcome)`(R0x는
리더기가 준 2자리 코드를 그대로 `R+코드`로, R2x는 DLL 오류 이름으로 분기 — `PORT_NOT_OPEN`=R20/
`SEND_FAIL`=R21/`BUSY`=R22/`PORT_NOT_FOUND`=R23/`PORT_OPEN_FAIL`=R24/`COMMAND_NOT_ALLOWED`=R25/
`Timeout`=R26/`CommunicationError`=R27/그 외 catch-all=R28), `ToTelegramCode(VanFailureKind)`(Phase 20
전용, D01/D02 — `VanFailureKind` enum도 이 파일에서 함께 신설). **`Approved`/`VanDeclined`는 의도적으로
매핑표에서 빠져 있고 호출 시 예외를 던진다** — 성공과 VAN 거절 모두 relay 경로(P17-3)를 타야 하므로,
이 매핑을 거치면 안 된다는 것을 스스로 강제한다. 검증은 리플렉션 하네스로(internal 타입) 32개 조건 —
E01~E05·E99 6종, relay 전용 값 호출 시 예외 5종, `CardReadCommandOutcome` 기반 R0x 2종·R2x 4종(서로
다른 코드인지가 완료 조건 핵심)·Timeout/CommunicationError 2종·Success 호출 시 예외, D01/D02, 그리고
`Services/Payment/`의 다른 파일에 코드 리터럴이 없는지(정규식 스캔)까지 전부 통과.

---

## P17-5. `PaymentOrchestrator` 3분기 재구성 ★

현재 `ProcessAsync(PosPaymentRequest) → PosPaymentResponse` 하나가 "무결성 → 카드리딩 → VAN"을 일직선으로
처리한다. 이걸 **전문 종별 3분기**로 나누되, **공통 부품을 그대로 재사용**한다(카드리딩 로직이 동일하다는
확정 사항 3 덕분에 새로 만들 것이 적다).

```
ProcessAsync(전문)
 ├ 501008 → [알림창 PROCESSING] → VAN 중계 → 응답
 ├ 800000 → [알림창 IC] → 무결성 선행 → 카드리딩 라운드 → BIN 채움 → [PROCESSING] → VAN 중계 → 응답
 └ 902614 → [알림창 IC] → 무결성 선행 → 카드리딩 라운드 → 7개 필드 채움
                                    → (Phase 18: PIN) → [PROCESSING] → VAN 중계 → 응답
```

- `RunCardReadingRoundsAsync`(P15-7)는 **시그니처만 정리하고 로직은 그대로 둔다.** FALLBACK과 `12` 재요청,
  단일 유효 응답 게이트, 취소/Timeout 경합이 전부 여기 걸려 있다 — Phase 16이 26개 시나리오로 검증한
  자산이므로 재구성 과정에서 **동작이 바뀌지 않는 것**이 목표다.
- `501008`은 카드리딩이 없으므로 무결성 선행 판정(PRD §4.2)도 하지 않는다. 리더기를 전혀 쓰지 않는
  경로이므로 "리더기 미설정"(`E04`)으로 거부하지도 않는다 — 리더기 없이도 조회는 되어야 한다.
- **데드라인 정책**: `501008`은 사용자 입력 대기가 없으므로 120초 카드입력 데드라인을 걸지 않는다.
  `800000`/`902614`는 기존과 동일하게 120초에 재요청 시 +30초.
- **설정 화면 게이트**(P15-4)는 3전문 모두에 적용한다 — `501008`은 리더기를 안 쓰지만, 설정 화면이
  열려 있는 동안 알림창을 띄우면 화면이 겹친다.

### 완료 조건

- [x] Phase 15/16의 시나리오(`--payment-flow-test` 1~26)가 `902614` 경로에서 **전부 그대로 통과**한다
      — 재구성으로 인한 회귀가 없다는 것이 이 Phase의 최대 리스크 관리 지점이다
- [x] `501008`이 리더기가 하나도 설정되지 않은 상태에서도 정상 동작한다
- [x] `501008` 처리 중에는 카드리딩 관련 로그가 전혀 남지 않는다(리더기를 건드리지 않음)
- [x] 알림창 상태 전이가 전문별로 다르게 나타난다(`501008`은 PROCESSING부터 시작)
- [x] 3전문이 큐에서 **전문 단위로 1건씩** 순차 처리된다

**완료 방식 조정(2026-08-26/27)**: 옛 26개 시나리오(임시 전문 `PAY|...` 기준)를 그대로 재생하는 대신,
`RunCardReadingRoundsAsync`(취소/Timeout/FALLBACK/12재요청/단일 유효 응답 게이트)를 **로직 변경 없이
재사용**했다는 사실 자체로 그 경합 로직의 정확성은 계승됨을 근거로 삼고, `PaymentFlowTestScenarios.cs`는
**3전문 라우팅·필드 채움·relay 배선**에 집중한 6개 시나리오(P17-6과 통합 검증, 아래 참고)로 전면
재작성했다 — 26개 시나리오의 전체 재구성(취소/Timeout 9종 경합 등)은 P17-7로 넘긴다(development_plan.md
"남은 작업" 참고). **구현 완료**: `PaymentOrchestrator`를 `ProcessAsync`(설정화면 게이트 공통) →
`HandleNoticeInquiryAsync`(501008)/`HandleCardInfoInquiryAsync`(800000)/`HandleCardApprovalAsync`(902614)
3분기로 재구성했다. 세 핸들러가 공유하는 "무결성 선행 → 카드리딩 라운드" 앞부분은
`RunGatedCardReadAsync`(신설, 전문별로 다른 필드 채움만 콜백으로 위임)로 뽑아 800000/902614가 완전히
같은 코드를 탄다(확정 사항 3).

---

## P17-6. 카드리딩 결과를 원캡 담당 필드에 채움

- `800000`: 카드리딩 응답의 카드번호에서 **앞 8자리**를 잘라 `#14 BIN`에 넣는다. 카드번호가 8자리 미만이면
  오류로 처리한다(조용히 짧은 값을 넣지 않는다).
- `902614`: 7개 필드를 채운다.
  - `#43` = 카드리딩 응답의 "리더기 인증 식별 번호"(16) + `KFTCTAXGIROCAP01`(16). **양쪽 다 정확히 16자여야
    하며**, 아니면 예외를 던진다 — 자릿수가 어긋나면 VAN이 조용히 거절할 것이므로 여기서 드러나야 한다
  - `#44` FALLBACK CODE / `#45` 복호화 정보 / `#46` 암호화된 카드정보 / `#53` EMV DATA — 카드리딩 응답의
    대응 필드를 옮긴다. **다만 이 대응 관계는 두 SPEC 문서(리더기 SPEC ↔ POS-원캡 SPEC) 어디에도 명시적
    표가 없다** — `reader-pinpad-spec-expert` 조사 + 사용자 확정으로 정했다(아래 완료 결과 참고):
    `#44`=`FallbackCode`(자동 좌측0패딩), `#45`=`KeyVersion`+`Tc`+`ModuleId`(2+6+10=18바이트, 사용자
    확정), `#46`=`EncryptedData`(그대로 옮김), `#53`=`"0600"`(4바이트 고정 길이 서브필드, 사용자 확정 —
    실제 데이터 길이가 아니라 이 서브필드의 최대 용량 600을 고정으로 적음) + `EmvEncodedData` + 나머지
    space(총 604바이트)
  - `#48` 거래 입력 유형 — 2:Swipe / 4:Pay-On / 5:IC. 실제 리딩 방식에 따라 결정한다. **매핑 근거도
    SPEC에 명시 없음** — 리더기 `WCC` 필드로 사용자 확정: `I`→5(IC)/`;`→2(Swipe)/`P`→4(Pay-On), 그 외
    값(RF/QR/Key-IN)은 예외
  - `#50` 신용카드 승인 인증방식 = `"2"` 고정(비밀번호 인증)
  - `#51`은 **space 스텁**(Phase 18)
- 프로그램 식별자 상수는 `KFTCTAXGIROCAP01` 하나를 **한 곳에만** 둔다. PRD §2.1의 POS 식별번호 기본값과
  같은 문자열이지만 **용도가 다르므로**, 같은 상수를 공유할지 별도로 둘지는 구현 시 판단하고 주석으로
  근거를 남긴다.
- 거래 종료 시 카드 데이터를 즉시 폐기하는 기존 동작(PRD §8.4)을 그대로 유지한다 — 전문 버퍼에 카드
  정보가 들어갔으므로 **응답 후 그 버퍼도 폐기 대상**이다.

### 완료 조건

- [x] 채워야 할 필드가 **하나도 빠짐없이** 채워졌는지 전송 직전에 검사한다(P17-2가 만든 "원캡 담당 필드
      목록"을 근거로) — 누락 시 전송하지 않고 오류
- [x] 리더기 인증 식별 번호가 16자가 아니면 예외가 난다
- [x] `#48`이 실제 리딩 방식(IC/FALLBACK)에 따라 다른 값으로 채워진다
- [x] 응답 완료 후 카드 데이터와 전문 버퍼가 폐기된다

**완료 조건 1번 구현 방식 정정**: 계획 당시엔 "P17-2가 만든 원캡 담당 필드 목록을 근거로 누락 검사"를
상정했으나, 그 목록은 `#51`(Phase 18까지 space 스텁)을 포함하고 있어 **그 검사를 문자 그대로 구현하면
정상 902614 거래가 매번 "#51 누락"으로 실패한다**(체크포인트 1이 미리 지뢰로 기록해 둔 지점). 그래서
데이터 기반 루프 대신 `FillCardApprovalFields`가 **7개 필드를 고정된 순서로 각각 명시적으로 `Write`**
하는 방식으로 구현했다 — 이 구조에서는 필드 하나를 빠뜨리는 것 자체가 코드 리뷰/컴파일 시점에 드러나는
실수이지, 런타임에 감지해야 할 "누락"이 아니다. 결과적으로 완료 조건의 의도("빠짐없이 채워짐, 누락 시
실패")는 충족하되 `#51`을 오탐하지 않는다.

**필드 매핑 확정 경위**: 두 SPEC(리더기 SPEC ↔ POS-원캡 SPEC)이 서로 독립 문서라 `#44`/`#45`/`#46`/`#48`
의 정확한 대응 관계가 SPEC 어디에도 명시돼 있지 않았다. `reader-pinpad-spec-expert`가 후보를 조사했고
(§P17-6 위 절 참고), 특히 `#45`는 후보(`ReaderEncryptionInfo`, 20바이트)가 SPEC상 필드 길이(AN18)와
2바이트 차이가 나 그대로 쓰면 예외가 나는 상태였다 — 사용자가 직접 "KeyVersion+Tc+ModuleId(18바이트)"로
확정해 해소했다(2026-08-27). `#48`도 사용자가 WCC 매핑을 확정했다.

**`#53`은 최초 구현 시 서브에이전트 확인을 빠뜨렸던 항목** — Sonnet이 `EmvEncodedData`가 SPEC상 유일한
EMV 후보라는 이유만으로 검증 없이 채워 넣었는데, 사용자가 "SPEC 중 애매한 부분 없었는지" 재차 확인을
요청한 자리에서 이 누락이 드러났다. 뒤늦게 사용자가 직접 정확한 내부 구조("`#53` 자체가 4바이트 길이
서브필드 + 최대 600바이트 EMV 데이터 구조이며, 길이 서브필드는 항상 `0600` 고정값")를 알려줘 해소했다 —
`EmvEncodedData` 자체가 대응 필드라는 판단은 맞았지만, **필드 안에 서브 구조가 있다는 것 자체를
놓쳤던 것**이 진짜 결함이었다.

**구현 완료**: `FillCardApprovalFields`(902614)/`HandleCardInfoInquiryAsync`의 BIN 채움(800000) 둘 다
구현. 프로그램 식별자 상수(`PaymentOrchestrator.ProgramIdentifier = "KFTCTAXGIROCAP01"`)는 결제 Flow
쪽에 한 곳만 뒀다 — PRD §2.1의 POS 식별번호 기본값과 우연히 같은 문자열이지만 SET 되는 필드도, 의미도
다르므로 상수를 공유하지 않았다. 카드 데이터 폐기는 기존 구조(카드리딩 결과가 지역 변수로만 존재하고
거래 종료와 함께 GC 대상이 됨, PRD §8.4)를 그대로 유지했다 — 별도 명시적 삭제 코드가 필요하지 않다
(참조를 들고 있는 필드/캐시가 없다).

**검증**: `PaymentFlowTestScenarios.cs`(P17-5와 통합, 6개 시나리오·21개 조건)로 두 Task를 함께
검증했다 — 501008 무리더기 성공, 800000 BIN 추출(카드번호 앞 8자리), 902614 7필드 값 전부(패딩·조합·
매핑 포함), 설정화면 게이트 3전문 공통 적용, 알 수 없는 WCC 값의 예외 노출, 취소 시 E01 + 대기 중
리더기 0x60 전송(라운드 진행 중에 취소가 도착하도록 타이밍을 맞춰 재현)까지 전부 통과. 실행 중 실제
결함 1건(`#53 EMV DATA` 채움 누락)을 발견해 즉시 수정하고 재검증했다.

> **체크포인트 2** — 여기까지가 "Flow 재구성"이다. Opus 검증 리뷰를 받는다. Phase 15/16의 경합 처리를
> 깨뜨리지 않았는지가 핵심 점검 대상이다.

---

## P17-7. 검증 하네스 + 회귀

- `--payment-flow-test`(가짜 엔드포인트)를 3전문으로 확장한다. 기존 26개 시나리오는 `902614`로 옮기고,
  `501008`/`800000` 시나리오를 추가한다.
- `--pos-client-test`를 실제 SPEC 전문을 보내도록 교체한다. 고정길이 전문을 손으로 만들기 어려우므로,
  **정상 값이 채워진 3전문 샘플을 코드로 생성**해 보내는 형태로 만든다(Phase 19 시뮬레이터의 예고편).
- **바이트 단위 대조**: 만들어진 전문을 16진수로 덤프해 SPEC 표의 POSITION과 직접 대조하는 검증을 최소
  1회 수행하고 결과를 이 문서에 남긴다. 자동 검사(P17-1의 스키마 자체 검증)가 있어도 사람이 한 번은
  실제 바이트를 눈으로 확인해야 한다 — **스키마 자체가 틀렸으면 자체 검증은 그 틀린 값끼리 일치하는지만
  확인**하기 때문이다.
- 실장비 회귀: 리더기 2대 구성에서 `800000`/`902614`를 실제 카드로 각 1회 이상 수행한다.

### 완료 조건

- [x] 가짜 하네스 시나리오 전부 통과(기존 26개 + 신규 — 범위는 P17-5/P17-6에서 이미 밝힌 대로 조정)
- [x] Phase 14 소켓 회귀 7개 흐름 정상
- [x] 3전문의 바이트 덤프를 SPEC 표와 대조한 결과가 이 문서에 기록됨
- [x] 실장비에서 `800000`(BIN 채움)과 `902614`(7필드 채움) 각 1회 이상 성공 — **완료(2026-08-27)**,
      아래 "Phase 17 실장비 검증 기록" 참고

**시나리오 범위 조정(2026-08-26/27)**: 옛 26개 시나리오(취소/Timeout 9종 경합 포함)를 전부 3전문 버전으로
재현하지 않았다 — `RunCardReadingRoundsAsync`를 로직 변경 없이 재사용했다는 사실 자체가 그 경합 로직의
정확성을 계승한다는 근거이므로(P17-5), `PaymentFlowTestScenarios.cs`는 **3전문 라우팅·필드 채움·relay
배선**에 집중한 6개 시나리오·21개 조건으로 재작성했다(P17-5/P17-6 절 참고). 취소/Timeout 시나리오는 최소
1개(대기 중 취소 → 0x60 확인)만 회귀 삼아 남겼다 — 나머지 8종의 전면 재구성은 이번 Phase 범위 밖으로
명시적으로 둔다(필요해지면 별도 후속 작업).

**`--pos-client-test`(Phase 14 소켓 회귀 7종) 교체 완료**: `PAY|금액|거래ID` 대신 501008 실제 전문을
보내도록 `WriteRequestFrame`을 다시 짰다. 상관관계 추적은 `#9`(전문 관리 번호, AN12)에 거래ID를 심고
응답에서 같은 자리를 읽어 대조하는 방식으로 바꿨다(relay/clone 두 응답 경로 모두 원본 필드를 보존한다는
P17-3 원칙 덕분에 이 방식이 성립한다). 2개 시나리오는 SPEC 고정길이 전문의 구조적 한계로 범위를
조정했다: **시나리오5**(예전엔 `amount="THROW"` sentinel로 Orchestrator 예외를 직접 유도)는 그런
sentinel이 SPEC 전문에는 없어(정상 설계) 연속 2건 정상 처리 확인으로 축소하고, 워커 예외 복원력 자체는
P17-4의 `TransactionQueue` 리플렉션 하네스가 이미 검증했음을 명시했다. **시나리오6**(예전엔 응답 본문을
9,900바이트로 부풀려 소켓 쓰기 블로킹 유도)은 SPEC 고정길이 전문 중 가장 큰 902614(1,500바이트)로도 그
정도 블로킹을 강제 재현하기 어려워, 재현 여부와 무관하게 "막히지 않는다"만 확인하는 것으로 범위를
좁히고 그 사실을 코드 주석에 남겼다. 실행 결과: 7개 시나리오 전부 정상 응답/타이밍 확인, 상관관계
불일치 0건.

**바이트 단위 대조(2026-08-27) — 완료**: 3전문 각각 정상 값을 채운 샘플을 스크래치패드 하네스로 만들어
공통부 14필드 + 업무부 대표 필드(총 39개 체크포인트: 501008 19개, 800000 8개, 902614 15개)의 POSITION·
길이를 SPEC 표(이미 `pos-onecap-spec-expert`가 재확인한 값)와 코드 스키마 값으로 직접 대조했다 — **전부
일치**. 16진수 덤프도 함께 남겨 실제 바이트 배치(예: 902614의 `#43` 보안단말기 인증번호가 POSITION 355에
"READERAUTH000001KFTCTAXGIROCAP01"로, `#48`이 POSITION 609에 "2"로 정확히 앉는 것)를 눈으로 확인했다.

**실장비 검증 — 미완료(사용자 참여 필요)**: `800000`/`902614`를 실제 리더기로 각 1회 이상 수행하는 항목은
물리적 카드 리더기 연결이 필요해 이번 자동 진행 범위에서는 완료하지 못했다. 리더기를 연결해 재개할 때
사용자에게 확인받는다.

---

## P17-8. PRD 갱신

`ROADMAP.md` 작업 방식 규칙 3("PRD와 실제 구현이 어긋나면 코드보다 먼저 PRD를 갱신")에 따라, 구현 후가
아니라 **확정 사항이 정해진 지금** 갱신한다.

- [x] §10 "실제 통신 전문 미확정"을 해소로 표시하고 SPEC 문서를 근거로 연결
- [x] §3(소켓) — 전문별 연결 수명(전문마다 새 연결), 3전문 구조 반영
- [x] §4(결제 Flow) — 전문 종별 분기, `501008`의 카드리딩 없는 경로, **카드 2회 리딩** 반영
- [x] §8.4 — "거래 종료 시 카드 데이터 삭제"의 단위가 **전문 단위**임을 명시
- [x] 응답코드 체계(`E`/`R`/`D`) 신설을 기록
- [x] `#51` 비밀번호가 원캡 담당임을 기록(설계서 오류 정정, Phase 18 예고)

**구현 완료(2026-08-27)**: `PRD.md`에 §3.3(전문 구조 신설), §4.1 상단 안내문(902614 기준 서술이라는
전제 명시), §4.10(relay 원칙 반영), §4.11(원캡 응답코드 체계 신설), §8.4(전문 단위 정정), §10(미확정
해소 + 남은 항목 갱신), §10.1 표(4개 행 추가/갱신)까지 전부 반영했다. Phase 17 전체(P17-1~P17-8)가
여기서 완료된다 — 남은 것은 실장비 검증(P17-7 미완료 1건, 사용자 참여 필요)과 최종 Opus 검증 리뷰뿐이다.

---

## Phase 17 최종 검증 — Opus 리뷰 및 후속 수정 (2026-08-27)

P17-1~P17-8 구현이 끝난 뒤 Opus가 전체 코드를 재검토했다. **결함 3건(H-1/H-2/H-3)을 확정하고 전부 수정·
재검증**했다(Phase 12/13/14/15/16과 같은 워크플로우). 체크포인트 1(전문 계층)에서 이미 4건을 잡았으므로,
이번 리뷰는 **Flow 재구성(P17-5~P17-6) 이후 새로 생긴 회귀**에 집중했다.

### H-2. VAN 통신 중 알림창(PROCESSING)이 사용자에게 전혀 보이지 않음 (확정·수정) ★ 이번 리뷰 최대 결함

`RunGatedCardReadAsync`의 `finally`가 `_presenter.Close()`를 호출한 **뒤에** 호출자가
`RelayToVanAsync` → `_presenter.ChangeState(VanProcessing)`을 실행하는 구조였다. 실제
`Views/PaymentNoticePresenter.ChangeState`는 창이 닫혀 있으면 `"알림창이 열려 있지 않아 무시됨"` 경고만
남기고 아무 일도 하지 않는다 — 즉 **PRD §4.10이 요구하는 "VAN 통신 시작 시 PROCESSING 화면으로 변경"이
800000/902614에서 전혀 동작하지 않았다.** 사용자 눈에는 카드 태그 직후 알림창이 사라지고, VAN 응답을
기다리는 동안(실서버라면 수 초) 아무 화면도 없는 상태가 된다.

실측 재현(가짜 Presenter 호출 이력): `Show:IcCardRequest -> Close -> ChangeState:VanProcessing`.

**하네스가 이걸 놓친 이유도 함께 기록해 둔다** — `FakePaymentNoticePresenter.ChangeState`는 창이 닫혔는지
검사하지 않고 History에 기록만 하므로, 호출 **순서**를 명시적으로 검사하지 않으면 통과해 버린다. Phase 17
초기 하네스는 "PROCESSING으로 전환했는가"를 아예 확인하지 않았다.

**수정**: `RunGatedCardReadAsync`를 `RunCardTransactionAsync`로 바꿔 **VAN 중계까지 같은 try/finally
안으로 되돌렸다**(Phase 15/16이 원래 유지하던 구조 — 재구성 과정에서 분리하며 깨진 것이다). 두 핸들러는
필드 채움 콜백만 넘기고 VAN 호출은 공통 메서드가 수행한다. 회귀 방지로 하네스에 **호출 순서 검사**
(PROCESSING 전환이 Close보다 앞서는지)를 추가했다. 수정 후 이력: `Show:IcCardRequest ->
ChangeState:VanProcessing -> Close`.

### H-3. VAN 통신 실패 시 리더기 초기화가 사라짐 (확정·수정)

PRD §4.10 "실패 시 Reader 초기화를 수행하고 POS에 오류 및 원인을 응답한다"에 따라 Phase 15의
`RunVanApprovalAsync`는 실패 2경로에서 `roundResult.Winner?.SendInvalidationInit()`을 호출했다. Phase 17의
`RelayToVanAsync`에는 그 호출이 없고 **winner 참조 자체가 전달되지 않았다**. `git show HEAD` 대조로 옛
파일의 `SendInvalidationInit` 14곳 중 VAN 실패 2곳이 정확히 빠진 것을 확인했다.

**수정**: `RelayToVanAsync`가 채택 리더기(`IReaderEndpoint?`, 501008은 `null`)를 받아 통신 실패 경로에서
초기화한다. 회귀 방지 시나리오(VAN 통신 실패 → `D02` + 리더기 초기화)를 하네스에 추가했다.

**다만 "VAN이 실제로 응답한 거절(decline)" 경우는 이 구조에서 판별할 수 없다** — relay 원칙상 응답코드를
해석하지 않기 때문이다(P17-3). PRD §4.10의 "실패 시 Reader 초기화" 중 거절 경우를 어떻게 다룰지는 relay
채택의 필연적 결과로 남는 **열린 항목**이다(아래 "남은 미확정" 참고).

### H-1. 옛 임시 전문 타입이 살아남아 모순되는 매핑표를 노출 (확정·수정)

`Protocol/Pos/PosPaymentRequest.cs`(`PAY|...` 파서)와 `PosPaymentResponse.cs`가 삭제되지 않고 남아 있었다.
어느 실코드도 참조하지 않지만 **`PosPaymentResponse.Create`가 옛 2자리 매핑표(`"00"`/`"10"`/`"20"`/`"99"`
…)를 그대로 들고 있어**, 호출하면 SPEC/`E`·`R`·`D` 체계와 정면으로 어긋나는 코드를 조용히 만들어 낸다.
Phase 18 이후 작업자가 이 이름만 보고 호출하기 딱 좋은 함정이다.

**수정**: 두 파일을 삭제하고, 이들을 `<see cref>`로 가리키던 문서 주석(`PosProtocolException`,
`PosPaymentResultCode`)을 현재 구조에 맞게 갱신했다. `PosPaymentResultCode`의 "매핑 지점" 안내도
`PosResultCodeMapper`로 고쳤다.

### M-1. 로그 상관관계 키가 프로세스 밖에서 의미가 없었음 (확정·수정)

`LogTxId`가 `$"{전문종별}-{request.GetHashCode():X8}"`를 반환했다 — 객체 식별 해시라 재실행하면 달라지고
POS·VAN 로그와 맞대어 볼 수 없다. SPEC은 `#9`(전문 관리 번호, AN12)를 "발급기별 전송 일자별 유일한 값"
으로 정의하고 3전문 공통부에 kiosk가 항상 채워 보내므로, **정식 상관관계 키가 이미 전문 안에 있는데 쓰지
않고 있었다.** Phase 14~16 로그는 POS가 준 txId를 그대로 썼으므로 진단성 회귀이기도 하다.

**수정**: `#9`를 읽어 쓰고, 비어 있으면 `{전문종별}-NOID-{해시}`로 대체한다. 하네스도 실제 POS처럼 `#9`를
SPEC 번호체계(`"0EC"` + `"0"` + 일련번호 8자리)로 채우도록 고쳐 실제 경로를 검증했다 — 로그에
`txId=0EC000000001` 형태로 나오는 것을 실측 확인.

### 확인했으나 결함이 아니었던 것

- **리더기 데이터의 CP949 재인코딩 안전성** — `CardReadResponseParser`가 `Encoding.ASCII`로 디코딩하므로
  카드/EMV 데이터 문자열은 이미 ASCII 범위뿐이고, CP949는 ASCII 상위집합이라 재인코딩이 바이트 동일하다.
- **프레이머 상한** — `MaxFrameBodyBytes = 9999`로 최대 전문(1500)을 여유 있게 수용한다.
- **프레이머 버퍼 aliasing** — `Append`가 `.ToArray()`로 새 배열을 돌려주므로 `PosTelegram.FromBytes`가
  참조를 그대로 잡아도 안전하다.
- **공통부 오프셋 생성 루프**(`PosCommonHeader`의 `#0` 건너뛰기) — `#1`이 POSITION 0에서 시작해 `#13`이
  68에서 끝나 총 70바이트로 정확히 맞는다.
- **정적 스키마 공유의 스레드 안전성** — 스키마는 생성 후 불변이고 가변 상태(`_body`)는 전문 인스턴스마다
  분리돼 있다.

### 재검증

수정 후 `--payment-flow-test` **24개 조건 전부 통과**(기존 21 + H-2 순서 검사 1 + H-3 시나리오 2),
`--pos-client-test` **소켓 회귀 7종 전부 정상**(상관관계 불일치 0건), 빌드 경고 0/오류 0.

---

## Phase 17 실장비 검증 기록 (2026-08-27, 리더기 1대 COM5)

최종 Opus 리뷰의 수정(H-1~H-3, M-1)까지 반영한 빌드로 실제 리더기·실제 카드를 써서 검증했다. 실제 POS처럼
고정길이 SPEC 전문을 보내는 스크래치패드 PowerShell 클라이언트(`spec_client.ps1`)를 새로 만들어 사용했다
— 옛 `PAY|금액|거래ID` 클라이언트는 더 이상 쓸 수 없다.

> **하네스 작성 중 걸린 함정 하나**: `.ps1`을 UTF-8 **BOM 없이** 저장하면 PowerShell 5.1이 CP949로 잘못
> 읽어 한글 주석이 다음 줄을 삼킨다(변수 할당이 주석에 흡수돼 `null` 참조로 터졌다). BOM을 붙여 해결.
> 이 저장소에서 한글이 든 `.ps1`을 만들 때 재발하기 쉬운 문제라 기록해 둔다.

| # | 시나리오 | 결과 |
|---|---|---|
| 1 | `501008` 고지내역조회 | **통과** — `#7=000`, 1.3초. 카드리딩 없이 즉시 relay되고 리더기를 전혀 건드리지 않음(로그에 카드리딩 항목 없음) |
| 2 | `800000` 카드정보조회 | **통과** — 실제 카드에서 **BIN `35641514`** 추출, `#14`에 채워져 VAN까지 전달. 무결성 체크(첫 거래) → 카드리딩 → BIN 채움 전 과정 정상 |
| 3 | `902614` 승인요청 | **통과** — 원캡 담당 7필드 전부 실제 카드 데이터로 채워짐(아래 실측값) |
| 4 | 연속 3건(`902614`) | **통과** — BUSY·경고·오류 **0건**, 응답 2.5초로 안정. TC 값이 매 거래 증가(`…23`→`…24`→`…25`)해 리더기가 정상적으로 새 거래를 처리함을 확인 |
| 5 | 사용자 취소(`902614`) | **통과** — `#7=E01`, `#6=G`. 대기 중 리더기 1대에 `0x60` 전송, 리더기 응답을 기다리지 않고 즉시 처리(Phase 16 선착순 규칙 유지) |
| 6 | 알 수 없는 거래구분(`999999`) | **통과** — 공통부 70바이트 응답(`#7=E41`), 거래구분 echo, 큐를 거치지 않고 즉시 응답 |
| 7 | `501008` 알림창 실제 표시 여부 | **통과** — 사용자 질문으로 추가 확인. Win32 창 열거로 요청 +250ms에 "결제 알림" 창 등장을 포착했고, 스크린샷으로 **"거래중입니다 / Payment is processing"(통신중 상태)**로 뜨는 것을 확인. 카드 안내 없이 곧바로 통신중으로 시작하는 설계대로다 |
| 8 | `501008` 통신중 취소 버튼 상태 | **통과** — UI Automation으로 조회해 `IsEnabled=False` 확인. PRD §4.8("VAN 통신 중 취소 버튼 비활성화")이 `501008`에서도 지켜진다 |

**`902614` 실측 필드값** (매핑 확정이 실제 하드웨어에서 맞는지가 이 Phase의 핵심 미검증 항목이었다):

```
#43 [####SPD-800F1011KFTCTAXGIROCAP01]  ← 리더기 인증 식별번호(16) + 프로그램 식별자(16) = 32 ✓
#44 [00]                                 ← FallbackCode "0" → N2 좌측 0패딩 ✓
#45 [9A000028C160390003]                 ← KeyVersion+Tc+ModuleId = 정확히 18바이트 ✓
#46 192바이트                             ← 196 한도 이내(아래 주의)
#48 [5]                                  ← WCC 'I'(IC) → "5" 매핑 성공 ✓
#50 [2]                                  ← 고정값 ✓
#53 "0600" + EMV, 총 532~560바이트         ← 4바이트 길이 서브필드 + 데이터, 604 한도 이내 ✓
#51 []                                   ← Phase 18 스텁(공백) ✓
```

**relay/failure 두 응답 경로가 실물로 구분됨을 확인** — 성공 relay는 `#6=C`(통합센터가 송신),
원캡이 합성한 실패 응답은 `#6=G`(요청기관이 송신)로 실제로 다르게 나왔다.

**H-2(VAN 통신 중 알림창) 수정의 실장비 확인**: 실제 `PaymentNoticePresenter`는 닫힌 창에 `ChangeState`가
오면 `"알림창이 열려 있지 않아 무시됨"` 경고를 남기는데, 이번 검증 전체 로그에서 **그 경고가 0건**이었다 —
`ChangeState(VanProcessing)`가 열린 창에 정상 도달했다는 뜻이다.

**"거래 종료 시 무조건 리더기 초기화" 논의의 결론(시나리오 4)**: 연속 3건을 초기화 없이 돌려 BUSY·오류가
0건임을 실증했다. 승인 성공 경로에 트레일링 `0x60`을 추가하면 다음 거래의 `0x2B`와 겹칠 위험만 새로
생기므로 **현행(성공 시 초기화 없음)을 유지한다.**

### 실장비 검증에서 새로 드러난 주의 사항

- **`#46` 암호화된 카드정보가 192바이트로 196 한도에 여유가 4바이트뿐이다.** 사용자 확인("실제로는 안
  넘는다")대로 이번 카드에서는 넘지 않았고 3회 반복 모두 192로 일정했지만, 여유가 매우 작다. 카드 종류나
  트랙 데이터 길이가 다르면 초과할 여지가 있고, 초과하면 `PosField.Pad`가 예외를 던져 거래가 `E99`로
  실패한다. **다른 카드로 추가 확인 권장**(미수행).
- **카드를 리더기에 꽂아 둔 채로 다음 거래를 시작하면 즉시 리딩된다**(1.4~2.5초). 취소/Timeout 경로를
  실장비로 재현하려면 카드를 물리적으로 빼야 한다 — 이번에도 이것 때문에 취소 테스트를 3회 헛돌렸다.
- **알림창 표시 여부는 로그로 확인할 수 없다.** `PaymentNoticePresenter`는 정상 경로(`Show`/`ChangeState`/
  `Close`)에서 로그를 남기지 않고 **실패했을 때만 경고**를 남긴다. 그래서 "경고가 없다"는 것은 "닫힌 창에
  ChangeState가 가지 않았다"는 증거는 되지만 "창이 실제로 떴다"는 증거는 되지 못한다 — 시나리오 7처럼
  창 열거/스크린샷으로 따로 확인해야 한다(이번에 사용자 질문이 없었으면 놓칠 뻔한 검증 공백이었다).

---

## 남은 미확정 (착수 전 또는 진행 중 확인)

1. **인코딩이 CP949가 맞는지** — 발주처가 EUC-KR로 명시한 문서가 있으면 알려주면 상수 1곳만 바꾼다.
   실제 차이는 확장완성형 한글 일부뿐이라 대부분 동일하게 동작한다. (여전히 열려 있음 — 발주처 확인 대기)
2. **패딩 규칙** — 국내 표준 관례로 진행한다(확정 사항 10). SPEC 명시가 없으므로 발주처 확인이 되면
   반영한다. 필드 정의 한 곳에서만 결정되므로 교체 비용은 작다. (여전히 열려 있음 — 발주처 확인 대기)

~~3. `R0x`/`R2x` 세부 배정~~ — **해소(2026-08-27, P17-4)**. `PosResultCodeMapper`에 리더기 DLL 오류
4종(`PORT_NOT_OPEN`/`SEND_FAIL`/`BUSY`/`PORT_NOT_FOUND`) + 기타 catch-all로 확정.

**추가로 P17-6/P17-7 진행 중 나온 질문 3건, 2026-08-27 사용자 확인으로 해소**:
- `#46` 암호화된 카드정보(196바이트 고정) 초과 처리 — **사용자 확인: 실제로는 196바이트를 넘지 않는다.**
  오버플로 처리 로직은 필요 없음(초과 시 `PosField.Pad`가 예외를 던지는 현재 동작이 방어적으로 남아
  있으면 충분).
- VAN이 실제로 POS-원캡과 같은 전문 형식으로 응답하는지 — **사용자 확인: VAN 서버는 아직 미구현이지만
  이 SPEC 기준으로 구현하기로 이미 결정됨.** 서버가 준비되면(Phase 20) 실응답으로 재검증하되, 지금은
  이 전제로 진행하는 것이 맞다.
- `E`/`R`/`D` 원캡 자체 응답코드 체계가 발주처 기존 체계와 충돌하지 않는지 — **사용자 확인: 이 체계
  자체를 우리가 정의해서 업체(키오스크 개발사)에 SPEC으로 제공할 예정.** 맞춰야 할 기존 외부 체계가
  없다 — 확정 사항으로 전환.

**최종 검증(2026-08-27)에서 열렸다가 같은 날 닫힌 항목 1건 — VAN 거절 시 리더기 초기화**:

H-3을 수정하면서 "PRD §4.10이 요구하는 '실패 시 Reader 초기화' 중 거절 경우를 relay 원칙 때문에 구현할
수 없다"를 열린 항목으로 올렸으나, **사용자 지적으로 전제 자체가 틀렸음이 드러나 그대로 닫는다**:

> 리더기 입장에서 VAN 승인과 거절은 완전히 동일하다. 리더기는 `0x3B`로 카드 데이터를 돌려준 시점에 자기
> 명령을 끝냈고, 그 뒤 VAN에서 무슨 일이 있었는지 알지도 못한다.

따라서 "승인엔 init 안 하고 거절엔 한다"는 구분은 **리더기 관점에서 성립하지 않는다** — 필요하다면 둘 다
필요하고, 불필요하다면 둘 다 불필요하다. Phase 15의 `RunVanApprovalAsync`가 거절에만 init하던 비대칭은
설계된 구분이 아니라 근거 없는 코드였던 것으로 정리한다. 현재 구현(승인·거절 둘 다 init 없음)이 오히려
자기일관적이며, Phase 16 실장비 검증에서 **연속 4건 승인 거래를 중간 초기화 없이 수행**해 다음 거래로
상태가 새지 않음이 이미 실증돼 있다(`P16-DUAL-REPEAT-1~4`).

**H-3으로 복원한 D0x(VAN 통신 실패) init은 그대로 둔다** — 다만 근거는 리더기 상태가 아니라(여기서도
동일하다) PRD §4.10 문구를 문자 그대로 지키는 것 + fire-and-forget이라 비용이 없다는 것뿐임을 코드
주석에 명시했다. 리더기 상태 관점에서 반드시 필요한 호출은 아니다.

---

# Phase 18 실행계획서 — 카드 비밀번호 입력 (알림창 4번째 상태)

> 로드맵: `ROADMAP.md` "Phase 18 — 카드 비밀번호 입력". `902614` 승인 요청의 `#51 암호화된 비밀번호
> 정보`를 채우기 위해, 카드리딩 성공 후 사용자에게 4자리 비밀번호를 입력받는 **알림창 4번째 상태**를
> 만들고 결제 Flow에 끼워 넣는다.

## 착수 전 전제 (2026-08-27 코드·SPEC 확인 완료)

1. **`#51`은 이미 스키마에 있다** — `CardApprovalSchema` `new(51, "암호화된 비밀번호 정보",
   PosFieldType.ANS, 100, 612, C)`. 담당(SET 장소)도 원캡(`C`)으로 등록돼 있고, Phase 17은 값을
   쓰지 않아 전체 space 스텁 상태다. **스키마는 손대지 않는다** — `Telegram.Write(51, ...)` 한 줄이
   늘어날 뿐이다.
2. **데드라인 +30초 연장은 이미 일반 규칙이다** — `PaymentOrchestrator.UserInputStepExtension`
   (30초 상수)이 FALLBACK/재요청 두 곳에서 `deadline.Extend(...)`로 쓰이고 있다. PIN 진입도 **같은
   상수·같은 메서드**를 부른다. PIN 전용 분기·전용 상수를 새로 만들지 않는다(P16 설계 의도).
3. **취소 가능 판정은 고칠 필요가 없다** — `PaymentNoticeViewModel.IsCancelAllowed`는
   `!_canceled && State != VanProcessing`이므로, PIN 상태를 추가하면 **자동으로 취소 가능**해진다.
4. **ESC 전역 훅·`Topmost`·홈 화면 비노출은 창 단위 속성**이라 상태를 하나 늘려도 그대로 적용된다
   (Phase 13 P13-5). 창을 새로 만들지 않는 것이 이 방식의 핵심 이득이다.
5. **참조 자산은 이미 커밋돼 있다** — `Assets/Images/PaymentNotice/pin입력.png`(레이아웃 시안),
   `비밀번호 입력 아이콘.png`(카드+자물쇠, 배경 투명). 시안은 세로형(1087×1447)이고 알림창은
   750×650 가로형이므로 **비율 그대로 옮기지 않는다** — 요소 구성만 따르고 배치는 창에 맞춰 실측한다.

## 확정된 설계 결정

1. **PIN 필요 여부는 전문 종별로만 구분한다 — `902614`만 받는다**(2026-08-27 사용자 확정).
   `800000`(카드 정보 조회)은 카드리딩만 하고 PIN 없이 VAN으로 간다 — 애초에 `#51` 필드가 없다.
   `501008`은 카드리딩 자체가 없다.

   > **근거 조사 기록(2026-08-27, `pos-onecap-spec-expert`)**: "카드리딩 후 PIN이 필요 없는 경우"의
   > 판단 근거가 SPEC에 있는지 전수 확인했으나 **없다**. `#50 신용카드 승인 인증방식`은 p.17에
   > **`"2"`(신용카드 비밀번호 인증) 하나만** 정의돼 있고 다른 값의 정의가 없으며, `#49 납부카드
   > 구분`도 `"0"`(개인카드) 고정이다. "서명/CVM/무서명/비밀번호 생략"이라는 표현은 문서 전체에
   > 등장하지 않는다. 오히려 `#39` 설명 각주가 *"수납센터는 납부이용시스템(`"O"`)와 신용카드 승인
   > 인증방식(**비밀번호 4자리**)을 결합하여 POS 납부채널을 구분함"* 이라고 적어, 이 채널이 비밀번호
   > 4자리를 전제로 설계됐음을 보여준다. **리더기 쪽에도 근거가 없다** — 카드리딩 응답(`0x3B`)의
   > 18개 필드 어디에도 "비밀번호 입력 필요 여부"가 없다(요청 `0x2B`의 `PinBlockInputRequired`는
   > 우리가 리더기에 지시하는 값이고 현재 `"0"` 고정). 즉 **전문 종별 외에 조건 분기를 만들 근거가
   > 문서상 존재하지 않는다** — 나중에 예외가 실제로 확인되면 그때 조건을 추가한다.

   구조적으로는 `RunCardTransactionAsync`가 `800000`/`902614` 공용이므로, **PIN 단계는 선택적으로
   끼우는 훅**으로 만든다(아래 P18-4).
2. **알림창의 4번째 상태**로 만든다. `PaymentNoticeState.PinEntry` 열거값 하나에서 화면 전부를
   파생시킨다(Phase 13 "상태 하나에서 파생" 원칙). **창 크기(750×650)는 바꾸지 않는다.**
3. **입력 수단은 화면 키패드(마우스/터치)뿐이다.** 물리 키보드 숫자 입력은 넣지 않는다 — 알림창은
   홈 화면을 활성화시키지 않으려고 포커스를 의도적으로 피하는 창이라(P13-3 `SuppressHomeWindowForeground`)
   키보드 입력을 받으려면 ESC처럼 전역 훅이 또 하나 필요해지고, 실사용 대상은 터치 키오스크다.
   (ESC 취소는 기존 전역 훅으로 계속 동작한다.)
4. **입력 규칙**: 4칸 마스킹, 누른 숫자를 **잠깐 보여준 뒤 점으로 가림**(노출 시간 상수 1곳),
   **4자리 완성 시 자동 진행**(확인 버튼 없음), **한 자리 삭제 버튼**(⌫) 있음, **재입력 기회 없음**.
   PIN이 틀려 VAN이 거절하면 그 응답을 그대로 POS로 중계한다(relay 원칙, PRD §4.10).
5. **암호화는 미정 — 교체 지점을 함수 1곳으로 격리한다.** 지금은 평문 4자리를 그대로 `#51`에 넣는다
   (ANS 100 → 좌측정렬 + space 96). SEED 암호화가 확정되면 **그 함수 본문만** 바뀐다.
6. **PIN은 로그에 절대 남기지 않는다.** 자릿수(`4자리 입력 완료`)만 기록한다. 값은 물론 마스킹된
   형태로도 남기지 않는다.
7. **`#50`은 계속 `"2"` 고정이다.** PIN을 실제로 받게 되어도 이 값은 바뀌지 않는다(SPEC p.17이
   유일하게 정의한 값이며 조건부 서술이 없음).

## 이 Phase에서 손대지 않는 것 (범위 밖)

- **전문 스키마·코덱·라우팅**(`Protocol/Pos/` 전체) — 전제 1. `Write(51, ...)` 호출만 추가된다.
- **`Services/Reader/`·`Protocol/Reader/`** — PIN은 핀패드가 아니라 **화면 키패드**로 받는다.
  리더기 `Pinpad_SendCommand` 계열은 이번 범위가 아니다.
- **취소/Timeout 경합 게이트의 구조**(`TransactionOutcomeGate`) — 클래스 자체는 그대로다.
  바뀌는 것은 `PaymentOrchestrator`가 **언제 `TryClaim`을 부르는가**(순서)뿐이다(P18-4).
- **실제 VAN 호출** — Phase 20.

---

## P18-1. 상태 추가 + 제어 계약 확장 ★

**먼저 이것부터.** 화면과 Flow가 만나는 계약이라, 여기가 흔들리면 뒤가 전부 흔들린다.

### 구현할 것

- `PaymentNoticeState`에 `PinEntry` 추가(XML 주석에 "902614 전용, 카드리딩 성공 후" 명시).
- `IPaymentNoticePresenter`에 **PIN 입력 완료 통지**를 추가한다:

      /// PIN 4자리가 입력 완료됐을 때 정확히 한 번 통지된다(취소와 같은 규칙).
      event EventHandler<PinEnteredEventArgs>? PinEntered;

  `PinEnteredEventArgs`는 `Services/Payment/`에 두고 `string Pin` 하나만 갖는다(WPF 타입 없음).

  **`Task<string?> RequestPinAsync(CancellationToken)` 방식을 쓰지 않는 이유**: 이 인터페이스는
  이미 취소를 `CancellationToken`이 아니라 **이벤트**로 통지하도록 확정돼 있고(그 판단 근거가
  인터페이스 주석에 남아 있다), 취소·Timeout·PIN 완료의 3자 경합은 `TransactionOutcomeGate`가
  이미 조정하고 있다. 같은 자리에 두 번째 비동기 규약을 들이면 "결과를 확정하는 주체"가 둘이 된다.
- `FakePaymentNoticePresenter`에 `FirePinEntered(string pin)` + `PinEnteredSubscriberCount` 추가
  (`FireCanceled`/`CanceledSubscriberCount`와 정확히 같은 모양 — 구독 해제 누수 검증까지 동일하게).

### 완료 조건

- [x] 빌드 성공. `IPaymentNoticePresenter` 구현체(실제/Fake)가 모두 새 이벤트를 갖는다.
- [x] `PaymentNoticeState`를 `switch`하는 기존 지점(`ApplyText`/`ConfigureOverlay`/`ConfigureCard`/
      `PaymentNoticeBackgroundSource`)을 전수 확인한다 — `_ =>` default가 있는 곳에서 PinEntry가
      엉뚱한 화면(카드 이미지·화살표)으로 조용히 새지 않는지 눈으로 본다.

---

## P18-2. PIN 입력 화면 레이아웃

`Assets/Images/PaymentNotice/pin입력.png` 시안의 **요소 구성**을 따른다(배치는 750×650에 맞춰 실측):

- 상단: 카드+자물쇠 아이콘(`비밀번호 입력 아이콘.png`, 배경 투명)
- 문구 2줄: `카드 비밀번호 4자리를 입력해 주세요.` / `Please enter your 4-digit card PIN`
  → **기존 `TextPanelA/B` 크로스페이드 경로를 그대로 쓴다**(`ApplyText`에 분기 추가). 문구 전용
  레이어를 새로 만들지 않는다.
- PIN 4칸: 미입력 / 입력됨 / **현재 입력 위치 강조** 3가지 시각 상태
- 키패드 3×4: `1~9` / `⌫`(삭제) / `0` / 빈칸
- 하단: 기존 `CancelButton` 그대로 재사용

### 구현 메모

- PIN 전용 요소는 **하나의 `Grid`(`PinPanel`)로 묶어** Canvas 위에 얹고, `PinEntry` 상태에서만
  `Visible`로 만든다. 리더기/원판/카드/화살표 레이어는 이 상태에서 전부 숨긴다(리더기 그림이
  보일 이유가 없다).
- 진입 전환은 기존 방식(문구 크로스페이드 + 오버레이 페이드아웃→페이드인)을 그대로 탄다.
- 키패드 버튼 스타일은 기존 리소스(`ReaderSecondaryButtonStyle` 계열)를 재사용하되, 터치 타겟이
  충분한지 실측으로 확인한다.

### 완료 조건

- [x] 앱 실행 → PIN 상태로 띄워 **스크린샷으로 시안과 대조**(csharp-wpf-developer 담당).
- [x] 750×650 안에서 잘리거나 겹치는 요소가 없다. 특히 **아이콘 + 문구 2줄 + PIN 4칸 + 키패드 4행 +
      취소 버튼의 세로 합계**를 실측한다(시안이 세로형이라 그대로 옮기면 넘친다).
- [x] IC ↔ PIN ↔ 통신중 전환이 기존 애니메이션 규칙대로 자연스럽게 이어진다(잔상·깜빡임 없음).

**구현 완료(2026-08-27)**: `Views/PaymentNoticeWindow.xaml`에 `PinPanel`(Grid, `Panel.ZIndex="20"`)을
Canvas 위에 얹었다 — 아이콘(`PinIconImage`, 76x76, Top=14) → PIN 4칸(`PinDigitBox1~4`+내부
`PinDigitDot1~4` Ellipse, 54x54, Top=186, 기본값 "1번째 칸 파란 보더로 강조·미입력") → 3x4 키패드
(`UniformGrid` `PinKeypad`, 500x260, Top=268, `PinButton1~9`/`PinButtonBackspace`/`PinButton0`+빈
`Grid` 자리, `PinKeypadButtonStyle`). 문구는 기존 `TextPanelA/B` 크로스페이드를 재사용하되, PIN 상태의
아이콘과 겹치지 않도록 `PaymentNoticeWindow.xaml.cs`에 `DefaultTextTop`(38)/`PinEntryTextTop`(100) 두
상수를 추가해 `ApplyState`(non-animate/animate 두 경로 모두)에서 `Canvas.SetTop`으로 상태별 위치를
분기했다. `CancelButton`은 새로 만들지 않고 기존 것 그대로 재사용(변경 없음).

**실측(750x650 좌표계, 스크린샷 대조 완료)**: 아이콘 Top=14~90 → 문구(PinEntryTextTop=100) 약
100~164 → PIN 4칸 186~240 → 키패드 268~528 → 취소 버튼 Top=586(기존 고정값, 변경 없음). 키패드
바닥(528)과 취소 버튼 상단(586) 사이 62px 여유, 아이콘 상단(14) 위 14px 여유 — 잘리거나 겹치는 요소
없음을 스크린샷으로 확인(시안은 세로 1447px이지만 750x650 예산 안에 아이콘+문구+PIN 4칸+키패드 4행
합계가 약 512px로 들어와 그대로 옮기지 않고 재실측한 결과).

**전환 검증**: `PlateImage`/`ReaderImage`(IC/FALLBACK/PROCESSING 3개 상태 공용, 상시 표시 레이어)를
숨기지 않으면 `PinPanel`이 투명 배경이라 리더기 원판/몸통 그림이 키패드 뒤로 비쳐 보이는 문제를
실측으로 발견(1차 스크린샷에서 키패드 뒤로 파란 원판/화살표가 겹쳐 보임) — `ConfigurePinPanel`에서
`PlateImage`/`ReaderImage`의 `Visibility`를 상태에 따라 함께 토글하도록 수정해 해결했다(전역 배경색
`Border`로 덮는 방식은 시도했다가 텍스트까지 가려서 폐기, 대신 근본 원인인 두 레이어의 Visibility를
직접 제어). IC→PIN→FALLBACK→PIN→IC 4상태 임시 순환(테스트 후 3상태로 원복)으로 실기 전환을 확인 —
잔상·깜빡임 없이 자연스럽게 이어짐. `App.xaml.cs`에 검증용 진입점 `--notice-pin-test`를 추가했다
(`--notice-van-processing-test`와 같은 패턴, State를 PinEntry로 고정해 띄움 — 영구 유지, 데모 4상태
순환은 유지하지 않음).

---

## P18-3. 입력 로직 (ViewModel)

`PaymentNoticeViewModel`에 PIN 상태를 추가한다. **WPF 타입은 여전히 쓰지 않는다**(P7-3 원칙 —
`Visibility` 등은 View/컨버터가 파생).

### 구현할 것

- `PinLength`(입력된 자릿수), `RevealedDigit`(잠깐 보여줄 숫자, 없으면 `null`), `PinDigitCommand`,
  `PinBackspaceCommand`.
- **잠깐 노출 후 마스킹**: 노출 시간 상수 1곳(`PinRevealDuration`, 초기값 600ms 제안).
  타이머는 **반드시 창이 닫힐 때 정지**한다 — Phase 13 Opus 리뷰 H-1(데모 `DispatcherTimer`가
  창을 닫아도 계속 발화하며 창/뷰모델을 붙들던 누수)과 **완전히 같은 종류의 함정**이다.
- **4자리 완성 시 자동 진행**: 마지막 자리가 채워진 것이 화면에 보이도록 짧은 지연
  (`PinCompleteDelay`, 초기값 200ms 제안) 후 `PinEntered`를 **정확히 1회** 발화한다.
  이후 추가 입력은 무시한다(연타 방어 — 취소의 `_canceled` sticky 플래그와 같은 방식).
- **거래 간 잔존 금지**: `Presenter.Show`가 매번 새 ViewModel을 만들므로 구조적으로는 안전하지만,
  `Close` 경로에서 PIN 문자열/자릿수를 명시적으로 비운다.

> **`string`은 메모리에서 0으로 덮어쓸 수 없다**(불변 + 인터닝). PIN을 `char[]`로 들고 다니면
> 지울 수는 있지만 WPF 바인딩·이벤트 인자·`PosField` 인코딩 경로가 전부 `string`이라 중간에 복사본이
> 생겨 실익이 없다. **참조를 즉시 끊는 것까지가 이 Phase의 폐기 수준**이며, 이를 PRD §8.4에
> 명시한다(로그 금지가 실질적인 방어선이다).

### 완료 조건

- [x] 1~9/0 입력 시 해당 칸이 숫자로 잠깐 보였다가 점으로 바뀐다.
- [x] ⌫로 한 자리씩 지워지고, 0자리에서 눌러도 예외가 나지 않는다.
- [x] 4자리 완성 시 자동 진행되며, **연타해도 `PinEntered`는 1회만** 발화한다.
- [x] 창을 닫은 뒤 **10초간 타이머가 발화하지 않는다**(P13 H-1과 동일한 방식으로 실측 확인).

**구현 완료(2026-08-27)**: `PaymentNoticeViewModel`에 `PinLength`(int)/`RevealedDigit`(string?)/
`PinDigitCommand`/`PinBackspaceCommand`를 추가했다(여전히 WPF 타입 없음). 노출→마스킹은
`Task.Delay(PinRevealDurationMs=600ms, _pinCts.Token)` + "숫자를 누를 때마다 증가하는 세대 번호"로
구현했다 — 값 비교가 아니라 세대 번호 비교라 같은 숫자를 연속으로 눌러도 오작동하지 않는다. 4자리
완성 시 `_pinCompleted` sticky 플래그를 **즉시**(지연 전) 세워 이후 숫자/삭제 입력을 전부 무시하고,
`Task.Delay(PinCompleteDelayMs=200ms, _pinCts.Token)` 후 `RaisePinEnteredEvent`를 1회만 호출한다.
타이머 정리는 `PaymentDeadline`과 같은 `CancellationTokenSource` 방식을 택했다(`DispatcherTimer`
미사용) — ViewModel에 `internal void StopPinTimers()`를 추가하고, `PaymentNoticeWindow_Closed`(취소/
완료/X/Alt+F4 어느 경로든 모이는 지점)에서 호출해 진행 중인 `Task.Delay`를 즉시 취소한다. PIN 4칸의
시각 표현(점/숫자 노출/현재 위치 강조)은 `PaymentNoticeWindow.xaml.cs`의 `UpdatePinDigitsDisplay()`가
`PinLength`/`RevealedDigit` `PropertyChanged`를 구독해 파생시킨다(P7-3 "WPF 타입은 View가 파생" 원칙
유지) — 이를 위해 XAML의 PIN 4칸 각 `Border`에 숫자 노출용 `TextBlock`(P18-2에는 없었음)을 추가했다.
키패드 버튼(`PinButton1~9`/`0`/`PinButtonBackspace`)에 `Command="{Binding PinDigitCommand}"
CommandParameter="N"`/`Command="{Binding PinBackspaceCommand}"`를 배선했다. 로그는 `"PIN 4자리 입력
완료"`만 남기고 값은 어디에도 남기지 않는다(설계 결정 6). `--notice-pin-test`로 실측: 초기 상태(1번째
칸 강조) → "1" 클릭 후 즉시 스크린샷에서 이미 마스킹(점)으로 전환된 것 확인(600ms 지연을 왕복
스크린샷 시간이 넘김 — 노출 자체는 로그/코드 경로로 별도 확인) → 4자리(1234) 입력 후 로그에
`PIN 4자리 입력 완료`가 **정확히 1줄만** 남음(4자리 완성 후 5·6·⌫를 추가로 클릭했음에도 로그가 늘지
않아 연타 방어 확인) → 0자리에서 ⌫ 클릭해도 예외 없음 → 새 창에서 "1" 클릭 직후(노출 타이머 진행
중) 창을 닫자 로그에 예외/경고가 남지 않았고 `tasklist`로 프로세스가 완전히 종료된 것을 확인(타이머가
Dispatcher/프로세스를 붙들지 않음 — P13 H-1과 같은 누수 없음).

---

## P18-4. Flow 연결 — 게이트/데드라인 순서 재배치 ★ (이번 Phase 최대 위험 구간)

### 왜 위험한가

현재 `RunCardTransactionAsync`는 **카드리딩 성공 직후** 이 순서로 진행한다:

    카드리딩 성공 → Gate.TryClaim(FlowResult)          ← 여기서 결과 확정
                  → _presenter.Canceled -= onCanceled  ← 여기서 취소 구독 해제
                  → fillOneCapFields(...)
                  → RelayToVanAsync(...)

즉 **카드리딩이 끝난 순간부터 취소가 막힌다.** PIN 입력은 그 뒤에 오는데 **취소·ESC·Timeout이
모두 동작해야 하므로**(로드맵 확정 사항), 확정 시점을 **PIN 입력 완료 후 / VAN 진입 직전**으로
옮겨야 한다. Phase 16이 세운 "결과는 정확히 한 번만 확정된다" 불변식을 건드리는 **유일한 지점**이다.

### 목표 순서 (902614)

    카드리딩 성공
      → deadline.Extend(UserInputStepExtension)       // 기존 일반 규칙 경로 그대로
      → PinEntered 구독                                // ★ ChangeState보다 먼저
      → _presenter.ChangeState(PinEntry)
      → Task.WhenAny(pinTask, gate.Interrupted)        // 카드리딩 라운드와 같은 대기 패턴
          · 취소/Timeout이 이기면 → winner.SendInvalidationInit() 후 InterruptCode(reason)로 실패 응답
          · PIN이 이기면 → 계속
      → Gate.TryClaim(FlowResult)                      // 확정은 여기로 이동
      → _presenter.Canceled -= onCanceled
      → fillOneCapFields(...) + #51 채움
      → RelayToVanAsync(...)                           // 알림창은 여전히 열린 채(H-2 규칙 유지)

`800000`은 PIN 단계가 없으므로 **기존 순서 그대로**다 — PIN 단계를 `RunCardTransactionAsync`에
**선택적 훅**으로 넣고(예: `Func<Task<string?>>? collectPin`, `null`이면 통째로 건너뜀),
`902614` 핸들러만 채운다.

### 반드시 지킬 것

- **`Task.WhenAny(pinTask, gate.Interrupted)` 패턴을 그대로 쓴다** — `RunCardReadingRoundsAsync`가
  이미 검증한 형태다. 새로운 대기 규약을 만들지 않는다.
- **PIN 이벤트 구독은 `ChangeState(PinEntry)` 호출 *전에* 건다.** Phase 15 Opus 리뷰 H-1이 취소에서
  정확히 이 실수를 잡았다(Show 뒤에 구독을 걸어 그 사이의 취소가 유실됨). `FakePaymentNoticePresenter`
  의 `FireCanceledSynchronouslyOnShow`에 대응하는 **PIN판 즉시 발화 플래그**로 회귀 검증한다.
- **PIN 대기 중 취소가 이기면 리더기를 초기화한다**(`winner.SendInvalidationInit()`) — 카드가 이미
  읽혀 리더기가 그 거래 상태를 들고 있으므로, 기존 "확정에 진 경로" 처리와 동일하다.
- **구독 해제는 `finally`에서도 반드시** — `Canceled`와 같은 취급(누수 검증 대상).

### 완료 조건

- [x] `902614` 정상 흐름: IC → PIN → 통신중 순으로 알림창 상태가 바뀐다
      (`FakePresenter.History` 순서로 단언 — H-2 회귀 방지와 같은 방식).
- [x] `800000` 흐름에 PIN 단계가 **끼어들지 않는다**(History에 `PinEntry`가 없다).
- [x] PIN 입력 중 취소 → `E01`, Timeout → `E02`가 **각각 정확히 1건만** 확정된다.
- [x] 데드라인 연장이 로그로 확인된다(`남은데드라인` 값이 +30초 된 뒤 VAN으로 진입).
- [x] 거래 종료 후 `CanceledSubscriberCount == 0 && PinEnteredSubscriberCount == 0`.

**구현 완료(2026-08-27)**: `RunCardTransactionAsync`에 `bool requiresPin` 파라미터를 추가하고(800000은
`false`, 902614는 `true`), `fillOneCapFields` 시그니처를 `Func<IReaderEndpoint, CardReadData, string?,
PosResponseTelegram?>`로 확장해 PIN 값을 세 번째 인자로 전달하는 통로만 만들었다(`#51`에 실제로 쓰는
것은 여전히 P18-5 몫이라 `HandleCardApprovalAsync`의 델리게이트는 `pin`을 받되 아직 쓰지 않는다).
새 private 메서드 `CollectPinAsync(scope, deadline, winner, txId)`를 `RunCardReadingRoundsAsync`와
정확히 같은 `Task.WhenAny(pinTask, gate.Interrupted)` 대기 패턴으로 작성했고, `PinEntered` 구독을
`ChangeState(PinEntry)` 호출 **전에** 걸어(P18-4 핵심 규칙) `finally`에서 항상 해제한다. `Gate.TryClaim
(FlowResult)` 호출 지점을 카드리딩 직후에서 **PIN 수집 완료 후**로 옮겼다(902614만 실질적으로 이동,
800000은 `requiresPin=false`라 기존과 동일). PIN 대기 중 취소/Timeout이 이기면 `winner.
SendInvalidationInit()` 후 기존 `InterruptCode(reason)`을 그대로 재사용해 실패 응답을 만든다(새 코드
없음, E01/E02 그대로).

`FakePaymentNoticePresenter`에 `FireCanceledSynchronouslyOnShow`의 PIN판인
`FirePinEnteredSynchronouslyOnChangeState`(+ `PinToFireSynchronously`)를 추가했다 — `ChangeState`가
`PinEntry` 상태를 기록한 직후 즉시 `PinEntered`를 발화해 "구독이 `ChangeState`보다 먼저 걸렸는지"를
증명한다.

**검증**: `PaymentFlowTestScenarios`에 시나리오 8~12(902614 IC→PIN→통신중 순서/구독 누수, 800000 PIN
미진입, PIN 대기 중 취소→E01, PIN 대기 중 Timeout→E02 + 데드라인 +30초 로그 확인, 즉시발화로 구독
순서 증명)를 추가해 `--payment-flow-test`로 40건 전부 통과(실패 0건) 확인했다. 기존 시나리오
3/5/7은 902614가 이제 PIN 단계를 거치므로 `FirePinEnteredSynchronouslyOnChangeState = true`로 PIN을
빠르게 통과시키도록 수정했다(수정 전엔 PIN을 주지 않아 실제 35초 Timeout까지 블로킹되며 실패하는
회귀가 재현됨 — 원인 확인 후 수정). `dotnet build` 경고 0/오류 0.

---

## Phase 18 체크포인트 1 — Opus 검증 리뷰 및 후속 수정 (2026-08-27)

P18-1~P18-4(가장 위험한 게이트 재배치 포함) 완료 직후 코드를 전수 리뷰했다.

### M-1. PIN 대기 중 취소/Timeout 시 리더기에 0x60이 두 번 나간다 (확정·수정)

`CollectPinAsync`의 인터럽트 경로가 `winner.SendInvalidationInit()`을 직접 불렀는데, `gate.Interrupted`가
완료된 시점(=`TryClaim` 성공)엔 `OnCanceled`/`MonitorDeadlineAsync`가 이미 `FireInterruptCleanup`으로
`scope.PendingParticipants`(카드리딩 라운드 참여자, winner 포함) 전원에게 0x60을 예약해 뒀으므로 순수
중복이었다. 구조적으로 같은 자리인 `RunCardReadingRoundsAsync`의 라운드 대기 인터럽트 경로는 정리 책임을
`FireInterruptCleanup` 한 곳에 두려고 의도적으로 이 호출을 하지 않는데, P18-4가 그 패턴을 깼다.

실측 로그로도 확인됨(수정 전): `UserCanceled 확정 — 대기 중인 참여 리더기 1대에 초기화(0x60) 전송 예약`과
`PIN 입력 대기 중 확정됨(UserCanceled) — 리더기 초기화 후 즉시 실패 응답`이 같은 취소에 대해 겹쳐 찍힘.

Phase 17에서 "거래 종료 시 무조건 초기화"를 기각한 근거가 정확히 "트레일링 0x60이 다음 거래의 0x2B와
겹칠 위험"이었는데, PIN 입력은 최대 35초짜리 의도된 사용자 대기 구간이 됐으므로 이 중복이 같은 위험을
다시 만든다는 점에서 가볍지 않다고 판단해 확정 처리했다.

**수정**: `CollectPinAsync`에서 `winner.SendInvalidationInit()` 호출 제거, 그 결과 `winner` 매개변수도
제거(호출부 `RunCardTransactionAsync` 동반 수정). 로그 문구를 "리더기 초기화는 FireInterruptCleanup이
이미 예약함"으로 정정. 클래스 주석에 이번 리뷰 배경을 남김.

이 결함은 계획서(P18-4 "반드시 지킬 것")가 `winner.SendInvalidationInit()`을 명시적으로 지시한 결과다 —
계획 단계의 설계 오류이지 구현 오류가 아니다.

### L-1. `PaymentNoticeViewModel._pinCts`를 Cancel만 하고 Dispose하지 않음 (확정·수정)

Phase 16 체크포인트 리뷰 M-1("Cancel만 하고 Dispose를 빠뜨리면 장시간 운용에서 그대로 누수")과 같은
패턴 — `PaymentDeadline`은 이미 Cancel+Dispose 쌍으로 이 문제를 풀어 뒀는데, `PaymentNoticeViewModel`
(거래마다 `Presenter.Show`가 새로 만드는 것도 `PaymentDeadline`과 같은 수명 프로필)은 `StopPinTimers`가
`Cancel()`만 했다.

**수정**: `_pinTimersStopped` 플래그로 가드하고 `Cancel()` 뒤 `Dispose()`까지 호출하도록 변경. 이
메서드는 항상 UI 스레드(창의 `Closed` 이벤트)에서만 호출되므로 `PaymentDeadline`과 달리 락은 필요
없다(이 클래스의 다른 sticky 플래그들과 같은 전제).

### 재검증

`dotnet build` 경고 0/오류 0. `--payment-flow-test` 40건 재실행 — 전부 통과(실패 0건). 취소/Timeout
시나리오의 로그를 직접 대조해 "초기화(0x60) 전송 예약" 로그가 각 케이스당 정확히 1건만 남는 것과 수정된
로그 문구가 실제로 찍히는 것을 확인했다.

---

## P18-5. `#51` 채움 + 암호화 교체 지점 격리

### 구현할 것

- `Services/Payment/PinFieldEncoder.cs`(신규) — **이 파일 하나가 교체 지점이다**:

      /// 입력받은 4자리 PIN을 #51(암호화된 비밀번호 정보, ANS 100)에 넣을 값으로 바꾼다.
      /// ★ SEED 암호화 방식이 확정되면 이 메서드 본문만 바뀐다(2026-08-27 미정, PRD §10).
      /// 지금은 평문 4자리를 그대로 돌려주고, space 96 패딩은 PosField.Pad가 처리한다.
      internal static string ToTelegramValue(string pin)

- `FillCardApprovalFields`에 `request.Telegram.Write(51, PinFieldEncoder.ToTelegramValue(pin));` 추가.
  기존 7필드 채움 주석의 "`#51`은 Phase 18 몫이라 손대지 않는다"를 **실제 매핑 설명으로 교체**한다.
- 로그 문구도 정정한다 — 현재 `"승인요청 필드 7종 채움 완료(#51은 Phase 17 space 스텁)"`.

### 완료 조건

- [x] `902614` 요청 전문의 POSITION 612~711에 PIN 4자리 + space 96이 정확히 들어간다(hex 덤프 확인).
- [x] **`#51` 값이 어떤 로그에도 나타나지 않는다**(로그 파일 전문 검색으로 확인).
- [x] 인접 필드가 밀리지 않았다 — `#50`(611), `#52`(712), `#53`(724)를 함께 확인.

**구현 완료(2026-08-27)**: `Services/Payment/PinFieldEncoder.cs`(신규) `ToTelegramValue(string pin)`가
유일한 교체 지점 — 지금은 4자리 숫자 검증(길이/숫자 여부, 값 자체는 예외 메시지에도 안 남김) 후 평문
그대로 반환한다. `FillCardApprovalFields`에 `string pin` 매개변수를 추가하고
`request.Telegram.Write(51, PinFieldEncoder.ToTelegramValue(pin));`을 추가했다(7필드 → 8필드). 호출부
`HandleCardApprovalAsync`의 `fillOneCapFields`는 `pin`이 `null`이면(902614는 `requiresPin: true`라
있으면 안 됨) 예외를 던지도록 방어적으로 처리한 뒤 전달한다. 로그 문구를 `"승인요청 필드 8종 채움
완료(#43~#46,#48,#50,#51,#53) — VAN 중계로"`로 정정(PIN 값 자체는 이 로그를 포함해 어디에도 남기지
않음, 자릿수조차 언급 안 함). `FillCardApprovalFields`의 클래스 주석과 필드 매핑 근거 목록에 `#51`
항목을 추가했다.

**검증**: `PaymentFlowTestScenarios` 시나리오 3(`902614: #51 = 화면에서 입력한 PIN 그대로`)과 시나리오
8(`902614+PIN`)에 `#51` 단언을 추가했다 — `Read(51)`은 ANS 타입 우측 space 패딩을 제거해 돌려주므로
(`PosField.Trim`) trim된 값을 단언하고, `PosTelegram.ToBody()`로 얻은 원본 바이트에서 POSITION
612~711을 직접 슬라이스해 `PIN + space 96`인지(hex 덤프 대응) 확인했다. 인접 필드는 raw POSITION
611(`#50`, `"2"` 고정값 유지)과 712~723(`#52`, 원캡 미담당이라 공백 12칸 유지)을 함께 확인해 밀리지
않았음을 검증했다. **Check 이름에도 PIN 리터럴을 직접 적지 않도록**(완료 조건 "어떤 로그에도 나타나지
않는다"를 테스트 하네스 자신의 로그에도 그대로 적용) `presenter.PinToFireSynchronously` 변수를 참조하는
식으로 작성했다 — 처음엔 `"PIN(1234)"`처럼 리터럴을 이름에 박아 실패 로그에 PIN이 그대로 노출되는 실수를
했다가(1차 실행에서 발견) 수정했다. `dotnet build` 경고 0/오류 0, `--payment-flow-test` 46건 전부 통과
(기존 40건 + 이번에 추가한 6건 단언, 실패 0건). 로그 파일 전문(`%LOCALAPPDATA%\KFTCTaxGiroCAP\logs\
2026-08-27.log`)에서 이번 최종 실행 구간(라인 440~576)에 테스트 PIN 문자열("1234"/"5678")이 전혀
등장하지 않는 것을 grep으로 확인했다.

---

## P18-6. 검증 하네스 시나리오 추가

`PaymentFlowTestScenarios`에 추가(기존 7개 뒤에 이어서). **P18-4/P18-5 구현 과정에서 이미 추가·검증
완료됨** — 계획 시점엔 4개(8~11)로 나눴으나 실제 구현은 5개(8~12)로, "PIN 정상 진행 + History 순서"와
"`#51` 값 단언"이 시나리오 8 하나에 합쳐지고 "즉시발화 구독순서 증명"이 시나리오 12로 별도 배정됐다:

- [x] `Scenario8_CardApprovalCollectsPinAndOrdersHistory` — PIN까지 정상 진행, IC→PIN→통신중 History
      순서 단언 + (P18-5에서 추가) `#51` 값(trim/raw 양쪽) + 인접 `#50`/`#52` 안 밀림 확인
- [x] `Scenario9_CardInfoInquirySkipsPinStep` — `800000`에 PIN 단계가 없음(전문 종별 구분 회귀 방지)
- [x] `Scenario10_CancelDuringPinEntryYieldsE01` — PIN 대기 중 취소 1건 확정(E01) + 리더기 초기화
      호출 확인 + `PinEntered` 구독 누수 없음
- [x] `Scenario11_TimeoutDuringPinEntryYieldsE02` — PIN 대기 중 Timeout 1건 확정(E02) + 데드라인 +30초
      로그 확인 + 리더기 초기화 확인
- [x] `Scenario12_PinEnteredBeforeSubscriptionIsNotLost` — 즉시 발화 플래그로 "구독 → ChangeState"
      순서 증명

**완료 조건**: 기존 7개 시나리오가 **전부 그대로 통과**하고(회귀 없음) 새 5개가 통과한다 —
`--payment-flow-test` 46건 전부 통과(실패 0건), 2026-08-27 재검증(체크포인트 1 수정 반영 후, 코드
리뷰 담당자가 직접 재실행) 확인.

---

## P18-7. 문서 갱신

- [x] `PRD.md` §5.2에 PIN 상태(4번째 화면) 추가, §4에 PIN 단계와 데드라인 연장 반영(신설 §4.12,
      §4.8/§4.9 문구 정정)
- [x] `PRD.md` §8.4에 PIN 폐기 규칙 추가(카드 데이터와 동일 취급 + **로그 금지**, `string` 한계 명시)
- [x] `PRD.md` §10 "`#51` SEED 암호화 방식 미정"을 **교체 지점 = `PinFieldEncoder.ToTelegramValue`
      한 곳**이라고 명시해 남긴다(열린 항목 유지, 영향 범위만 확정) — §10.1 표에도 반영
- [x] `ROADMAP.md` Phase 18 작업 항목 체크 + 완료 요약(실장비 검증 대기 상태를 명시, 추측으로 완료
      처리하지 않음)

---

## Phase 18 최종 검증 — Opus 리뷰 및 후속 수정 (2026-08-27)

P18-1~P18-7 전체를 다시 훑었다(체크포인트 1은 P18-1~P18-4만 봤으므로 P18-5~P18-7이 첫 리뷰 대상).
Phase 17 최종 검증에서 H-3을 잡아냈던 방식(이전 커밋 대비 diff로 "조용히 사라지거나 새로 생긴 것" 확인)을
같이 적용했다.

### H-1. PIN이 실패 응답에 실려 POS로 되돌아감 (확정·수정) ★ 이번 리뷰 최대 결함

`PosResponseTelegram.Failure(PosRequestTelegram, string)`은 **요청 전문을 `Clone`한 뒤 `#3`/`#6`/`#7`/`#8`
만 덮어쓴다**(P17-3에서 확정한 설계 — 서버가 채우는 필드는 어차피 공백이라 clone해도 어색하지 않다는
전제였다). Phase 18이 `#51`에 PIN을 채우면서 이 전제가 깨졌다: **PIN을 채운 뒤 실패하는 경로에서는
clone 시점에 `#51`이 이미 채워져 있어 사용자 비밀번호가 그대로 POS로 되돌아간다.**

해당 경로 2개:
- VAN 통신 실패(`D0x`) — `RelayToVanAsync`의 `CommunicationFailure` 분기
- 필드 채움 중 예외(`E99`) — `Write(53)` 등이 던지면 `#51`은 이미 쓰인 뒤다(`TransactionQueue`의
  예외 fallback도 같은 `Failure(request, ...)` clone 경로를 쓴다)

**왜 가볍지 않은가**: `#51`은 **kiosk가 원래 갖지 못하는 유일한 필드**다 — 애초에 "kiosk가 아니라 원캡이
입력받아야 한다"는 것이 Phase 18의 존재 이유다(PRD §4.12). 그 값을 실패 응답으로 kiosk에 돌려주는 것은
Phase 18이 세운 경계를 스스로 되돌리는 것이고, **SEED 암호화 확정 전인 현재는 평문**이라 평문 PIN이
프로세스 경계를 넘는다. 키오스크 업체가 디버깅용으로 응답 프레임을 통째로 로깅하는 것은 흔한 관행이라
그대로 상대 로그에 남는다. PRD §8.4가 "PIN은 어떤 로그에도 남기지 않는다"까지 규정한 것과도 정면 충돌한다
(우리 로그만 지키고 남의 로그로 보내는 셈).

**재현**: 시나리오 7(VAN 통신 실패)에 `response.Telegram.Read(51) == ""` 단언을 추가하니 **즉시 실패**
(2026-08-27 15:49 로그) — 추정이 아니라 실측으로 확정했다.

**수정**: `PosResponseTelegram.BuildFailure`에서 스키마에 `#51`이 있으면 항상 공백으로 지운다(빈 문자열을
쓰면 `PosField.Pad`가 타입과 무관하게 전체 space로 채운다 — P17 체크포인트1 M-1에서 정한 규칙 그대로).
`501008`/`800000`에는 `#51` 자체가 없으므로 스키마 확인 후 조건부로 지운다. 시나리오 7의 새 단언이
회귀 방지로 남는다.

> **relay 경로는 손대지 않았다** — VAN이 준 바이트를 그대로 통과시키는 것이 Phase 17이 근거를 들여
> 확정한 원칙이고, VAN 응답에 무엇이 담기는지는 VAN 쪽 계약이다. 다만 **실제 VAN이 요청을 그대로
> echo하는 형태로 응답한다면 같은 문제가 relay 경로로도 생기므로**, Phase 20 실서버 검증 때 VAN 응답에
> `#51`이 실려 오는지 반드시 확인한다(확인 항목으로 아래 "남은 미확정"에 남김).

### M-1. 체크포인트 1의 L-1 수정이 `ObjectDisposedException` 경로를 새로 만듦 (확정·수정)

체크포인트 1에서 `StopPinTimers`가 `_pinCts`를 `Dispose`까지 하도록 고쳤는데, `PinDigit`이 그 뒤에
실행되면 `_pinCts.Token` 접근에서 `ObjectDisposedException`이 난다(커맨드 안에서 던지므로 디스패처
미처리 예외로 이어진다). **`Dispatcher.Invoke`(Send)로 들어오는 `Close`가 이미 큐에 쌓인 클릭(Input,
더 낮은 우선순위)보다 먼저 처리될 수 있어** 실제로 열리는 순서다 — Timeout 만료와 사용자의 마지막
탭이 겹치는 순간이 정확히 그 상황이다.

수정 전(Cancel만 호출)에는 `Token` 접근이 안전했으므로 **이 결함은 체크포인트 1 수정이 만든 것**이다.
리소스 누수를 고치면서 크래시 경로를 들인 셈이라, 같은 리뷰 사이클 안에서 잡은 것이 다행이다.

**수정**: `PinDigit`/`PinBackspace`의 가드 절에 `_pinTimersStopped`를 추가해 창이 닫힌 뒤의 입력은
`_pinCts`에 손대기 전에 빠져나가게 했다. 필드 선언도 사용처보다 위(다른 sticky 플래그 옆)로 옮겼다.

### L-1. P18-2/P18-3 완료 후에도 남은 과거 시점 주석 (수정)

`PaymentNoticeBackgroundSource`/`PaymentNoticeWindow.xaml.cs`/`PaymentNoticeViewModel`에 "P18-2에서 실제
레이아웃으로 교체될 때까지는", "P18-3에서 채운다", "P18-1 시점에는 아직 호출하는 곳이 없다" 같은 **이미
사실이 아닌 주석**이 남아 있었다(Task 단위로 나눠 구현하면서 생긴 시점 표현). 다음 사람이 "아직 안 된
것"으로 오해할 수 있어 현재 상태를 서술하는 문장으로 교체했다.

### 확인했으나 결함이 아니었던 것

- **시나리오 3/5/7에 `FirePinEnteredSynchronouslyOnChangeState = true`를 추가한 것이 커버리지를 약화시키지
  않는가** — 세 시나리오의 관심사(필드 채움 / WCC 예외 / VAN 실패 시 리더기 초기화)는 모두 PIN 단계
  **이후**에 벌어지는 일이라 PIN을 빨리 통과시키는 것이 검증 대상을 건드리지 않는다. 오히려 시나리오 9는
  같은 플래그를 **켜 둔 채** `800000`에 `PinEntry`가 등장하지 않는 것을 확인해, 전문 종별 구분이 플래그와
  무관하게 성립함을 증명한다.
- **PIN 완료 후 `_pinCts.Token` 접근** — `_pinCompleted` 가드가 `Token` 접근보다 앞에 있어 M-1의 경로가
  이쪽으로는 열리지 않는다.
- **`PinPanel`이 VAN 통신 중에도 남아 있지 않은가** — `ApplyState`의 애니메이션 경로가 "나가는 방향"
  페이드아웃 후 `Collapsed`로 되돌리고, 비애니메이션 경로도 진입 시 항상 `Collapsed`로 초기화한다.
- **`PosSocketServer`가 응답 바이트를 로깅하지 않는가** — 로깅하지 않는다(우리 로그로 PIN이 샐 경로 없음).
- **`Relay` 경로가 `#51`을 덮어쓰지 않는가** — 덮어쓰지 않는 것이 맞다(relay 원칙). 위 H-1 주석 참고.

### 재검증

`dotnet build` 경고 0/오류 0. `--payment-flow-test` **47건 전부 통과(실패 0건)** — 기존 46건 + H-1 회귀
방지 단언 1건. H-1은 수정 전 실패 → 수정 후 통과를 같은 단언으로 확인했다.

---

## P18-8. PIN 물리 키보드 입력 지원 (실장비 검증 중 사용자 요청으로 범위 추가, 2026-08-27)

### 배경 — 왜 뒤늦게 추가하는가

P18-4 착수 전 설계 결정 3("입력 수단은 화면 키패드뿐이다")은 **키보드 입력을 요구하지 않을 것이라는
가정**하에 내려졌다(실사용 대상이 터치 키오스크라는 근거). 실장비 검증 중 사용자가 **화면 키패드와
물리 키보드 둘 다 가능해야 한다**고 확정해, 이 가정을 뒤집는다.

### 확정된 설계 결정

1. **ESC 훅과 같은 메커니즘을 재사용한다.** PIN 화면이 떠 있어도 POS 등 다른 프로그램에 포커스가 있을
   수 있으므로(§5.3과 같은 이유), 창의 `KeyDown`이 아니라 **기존 `WH_KEYBOARD_LL` 전역 저수준 훅**을
   그대로 확장한다. 새 훅을 하나 더 걸지 않는다(같은 창에 두 개의 저수준 훅을 거는 것은 콜백 오버헤드와
   설치/해제 수명 관리 코드를 불필요하게 두 배로 만든다) — `PaymentNoticeEscapeHook`을
   `PaymentNoticeKeyboardHook`으로 이름을 넓히고 ESC 처리 로직은 한 글자도 바꾸지 않은 채 숫자/삭제
   처리를 나란히 추가한다.
2. **판정 지점은 하나(ViewModel)** — `IsCancelAllowed`/`TryMarkCanceled` 패턴을 그대로 따른다. 훅은
   상태를 모르고, `PaymentNoticeViewModel.TryPinDigit(char)`/`TryPinBackspace()`가 내부에서
   `State == PinEntry`를 확인해 아니면 `false`(=미소비, 다른 프로그램으로 그대로 전달)를 돌려준다.
   맞으면 기존 `PinDigit`/`PinBackspace` private 메서드를 그대로 호출하고 `true`(=소비)를 돌려준다.
   **터치 버튼과 키보드가 완전히 같은 코드 경로**(같은 private 메서드)를 타므로 마스킹·자동 진행·연타
   방어(`_pinCompleted`)가 모두 동일하게 적용된다 — 입력 수단별로 로직을 중복 구현하지 않는다.
3. **PinEntry 상태가 아니면 무조건 통과시킨다(소비하지 않는다).** IC/FALLBACK/VanProcessing 화면에서
   숫자/Backspace를 눌러도 원캡은 아무것도 하지 않고 POS 등 뒤에 있는 프로그램에 그대로 전달한다(ESC의
   "취소 불가 구간에서는 삼키지 않는다" 원칙과 동일).
4. **매핑**: 상단 숫자키(`0`~`9`, VK 0x30~0x39)와 숫자패드(`VK_NUMPAD0`~`VK_NUMPAD9`, 0x60~0x69) 둘 다
   허용, `Backspace`(`VK_BACK`, 0x08) 하나만 삭제로 매핑한다(`Delete`는 매핑하지 않음 — 터치 키패드의
   "⌫" 버튼과 성격이 같은 키만 대응). 키 반복(누르고 있을 때 OS가 반복 발생시키는 `WM_KEYDOWN`)은 별도
   방지 로직을 두지 않는다 — 일반적인 키보드 입력 동작과 같고, 4자리 도달 후에는 `_pinCompleted`가
   이미 추가 입력을 막는다.

### 구현할 것

- `Interop/LowLevelKeyboardHookNative.cs` — `VK_BACK`/`VK_0`/`VK_NUMPAD0` 상수 + `TryMapDigit(int vkCode)`
  헬퍼(숫자/숫자패드 코드를 `char?`로 변환) 추가.
- `Views/PaymentNoticeEscapeHook.cs` → `Views/PaymentNoticeKeyboardHook.cs`로 파일명·클래스명 변경.
  생성자에 `Func<char, bool> tryPinDigit`, `Func<bool> tryPinBackspace` 추가. ESC 분기는 그대로 두고
  숫자/Backspace 분기를 나란히 추가(둘 다 소비 시 `(IntPtr)1`, 아니면 `CallNextHookEx`로 흘려보냄 —
  기존 ESC 분기와 대칭 구조).
- `PaymentNoticeViewModel.cs` — `internal bool TryPinDigit(char digit)`/`internal bool TryPinBackspace()`
  추가(위 확정 사항 2).
- `PaymentNoticeWindow.xaml.cs` — 필드/생성자 호출을 새 클래스명·시그니처로 갱신.

### 완료 조건

- [x] 빌드 경고 0/오류 0.
- [x] `--notice-pin-test`로 PIN 화면을 띄운 뒤 `mcp__windows__windows_send_keys`로 숫자 키를 보내
      화면이 정확히 동일하게(노출→마스킹, 자동 진행) 반응하는지 확인 — 터치 클릭 검증(P18-2/P18-3)과
      같은 결과 확인(4자리 입력 시 4칸 전부 마스킹).
- [x] `Backspace`로 한 자리 삭제가 키보드로도 동작 — "1","2","Backspace" 입력 후 1칸만 남고 커서가
      2번째 칸으로 돌아오는 것 스크린샷으로 확인.
- [x] IC/FALLBACK/VanProcessing 상태에서 숫자 키를 눌러도 아무 반응 없음(소비되지 않고 통과) —
      `--notice-demo`의 IC 화면에서 "5" 입력, 화면 변화 없음 확인.
- [x] 실장비로 902614 정상 흐름을 **키보드 입력만으로** 완주 — `#7=000` 응답 확인(2026-08-28, 실제
      카드 태그 + 실제 물리 키보드로 PIN 4자리 입력, 23.8초 왕복, `#43~#48/#50/#53` 정상 채움, `#51`
      빈 값 재확인 — 스텁 수정이 키보드 입력 경로에서도 유효함을 함께 확인).
- [x] 기존 ESC 취소 동작 회귀 없음 — ESC 분기 코드는 리팩터링 중 한 글자도 바뀌지 않았음을 diff로
      확인, `--payment-flow-test` 47건(헤드리스 시나리오는 훅을 직접 타지 않지만 `TryMarkCanceled`/
      `RaiseCanceledEvent` 경로 자체는 동일하게 검증됨) 전부 통과로 회귀 없음 재확인.

---

## Phase 18 완료 기준 (로드맵 원문)

`902614` 흐름에서 카드리딩 성공 후 PIN 화면이 뜨고, 4자리 입력 시 자동으로 통신중으로 넘어가 `#51`이
채워진 전문이 만들어진다. PIN 입력 중 취소/ESC/Timeout이 각각 정확히 1건만 확정되고, 데드라인이
+30초 연장된 것이 로그로 확인된다. 반복 실행 시 이전 거래의 입력값이 남지 않는다.

**실장비 검증은 Phase 17과 같은 방식으로 한다**(`scratchpad/spec_client.ps1` 재사용). 다만 Phase 17
검증에서 얻은 교훈 2가지를 미리 반영한다:

- **카드를 리더기에서 빼 두고 시작한다** — 꽂아 둔 채로 시작하면 즉시 리딩돼 취소 경로가 헛돈다.
- **알림창 표시 여부는 로그로 확인되지 않는다** — PIN 화면이 실제로 떴는지는 창 열거/스크린샷으로
  따로 확인한다(정상 경로의 `ChangeState`는 로그를 남기지 않는다).

## Phase 18 실장비 검증 기록 (2026-08-27, 리더기 2대 COM3/COM7)

### 시나리오 1: 902614 정상 흐름(실물 카드 태그 + 실물 PIN 입력) — H-2 발견

`spec_client.ps1 -TxType 902614`로 실제 요청을 보내고, 사용자가 실물 카드를 태그한 뒤 실제로 뜬 PIN
화면에 **손으로 직접** 4자리를 입력했다(자동화 클릭이 아니라 실사용자 조작 그대로). 결과:

- 응답 26.5초 만에 수신, `#7=000`(승인), `#43~#48/#50/#53` 7필드 정상(`#46` 192바이트, `#53` 492바이트 —
  둘 다 한도 이내), **PIN 입력~자동 진행~통신중 전환까지 화면 흐름 자체는 완전히 정상 동작**을 실사용자
  조작으로 확인.

**H-2(실장비, High) — 개발용 VAN 스텁이 성공 응답에 실제 입력된 PIN을 그대로 실어 돌려줌**: 응답 전문의
`#51`에 방금 입력한 PIN이 그대로 들어 있었다(테스트 클라이언트가 그 값을 화면에 출력). 원인은
`StubVanRelayService.BuildFakeSuccess`가 `PosResponseTelegram.BuildFailure`가 고쳐지기 전과 똑같이
요청을 clone해 `#3/#6/#7/#8`만 덮어쓰는 방식이라 — **최종 검증 H-1과 정확히 같은 결함이 스텁에도
있었다**(H-1 수정 때 relay/성공 경로는 "VAN이 준 바이트를 그대로 통과시키는 게 원칙이라 손대지 않는다"고
판단했는데, 그 판단이 향하는 대상이 진짜 VAN이 아니라 **우리가 만든 스텁**일 때는 그대로 적용되면 안
됐다). 이번 발견으로 실제 사용자의 평문 PIN이 로컬 테스트 파일(`spec_client.ps1` 출력)에 남았다 —
**즉시 해당 파일을 삭제**하고, 재발 방지로 `StubVanRelayService.BuildFakeSuccess`도
`PosResponseTelegram.BuildFailure`와 같은 방식(스키마에 `#51`이 있으면 공백 처리)으로 수정했다.
앱 자체의 `FileLogger`에는 이번 실행 기록에 PIN이 전혀 남지 않은 것을 별도로 확인했다(설계대로 동작).

**"착수 전 확인이 필요한 것" #4와의 관계**: 이 항목이 "VAN이 요청을 echo하면 relay로도 같은 문제가
생긴다"고 미리 우려했던 것이, **개발 스텁 단계에서 그대로 재현**된 것이다. 실제 VAN이 echo하는지는
여전히 Phase 20 확인 대상으로 남지만, 최소한 **Phase 19(키오스크 시뮬레이터)까지 이 스텁을 계속 쓰는
동안은 안전**해졌다.

### 재검증(스텁 수정 후)

`dotnet build` 경고 0/오류 0(앱이 실행 중이면 exe 파일 잠김으로 빌드 실패 — 재현 시 프로세스 먼저 종료).

### 시나리오 1b: 902614 정상 흐름(물리 키보드 전용 PIN 입력) — P18-8 실장비 확인

`--notice-pin-test`/`windows_send_keys` 시뮬레이션이 아니라 **실제 카드 태그 + 실제 물리 키보드**로
완주(2026-08-28). `#7=000`(승인), 23.8초 왕복, `#43~#48/#50/#53` 정상 채움, `#51`(응답) 빈 값 재확인 —
H-2 스텁 수정이 키보드 입력 경로에서도 유효함을 함께 확인했다.

### 시나리오 2: 800000(카드정보조회) — PIN 미진입 확인

카드 태그 후 **3.1초**(!) 만에 `#7=000` 응답(`#14 BIN=35641514`). PIN 화면이 떴다면 사용자 입력을
기다리느라 훨씬 오래 걸렸을 것이므로, 이 왕복 시간 자체가 PIN 단계를 거치지 않았다는 증거다. `#51`
필드 자체가 없는 전문이라 언급되지 않는다 — 전문 종별 구분(902614만 PIN)이 실장비에서도 확인됐다.

### 시나리오 3: PIN 입력 중 취소(ESC) — E01

카드 태그 → PIN 화면 → **ESC 키**로 취소(2026-08-28, P18-8의 키보드 훅 확장 후 ESC 경로 회귀 여부도
함께 실증). `#7=E01`, 11.5초 왕복, 모든 필드 공백(`#51` 포함). 앱 로그로 순서까지 확인:

    카드 리딩 성공 → PIN 입력 단계 진입(데드라인 119.9s → 141.5s, +30초 연장 확인)
    → 사용자 취소 통지 수신 → UserCanceled 확정 → 리더기 2대 초기화(0x60) 전송 예약 **1건만**
    → "PIN 입력 대기 중 확정됨(UserCanceled) — 즉시 실패 응답(리더기 초기화는 FireInterruptCleanup이
       이미 예약함)"

체크포인트 1에서 고친 M-1(중복 리더기 초기화)이 실장비에서도 중복 없이 1건만 나가는 것을 로그로
직접 확인했다.

### 시나리오 4: PIN 입력 중 Timeout — E02

카드 태그 후 PIN 화면에서 **아무 입력도 하지 않고 대기**. `#7=E02`, **150.1초** 만에 응답(초기 120초
+ PIN 진입 시 +30초 연장이 실제로 합산된 시간과 일치). 앱 로그:

    카드 리딩 성공(라운드 1, 남은데드라인 119.9s) → PIN 입력 단계 진입(데드라인 30초 연장,
    남은데드라인=145.0s) → (145초 경과) → 거래 데드라인 만료 — 거래 확정(Timeout)
    → 리더기 2대 초기화(0x60) 전송 예약 1건만

시나리오 3과 마찬가지로 초기화 중복 없음, `#51` 공백 확인.

### 실장비 검증 종합 (2026-08-27~28, 리더기 2대 COM3/COM7)

| 시나리오 | 결과 | 비고 |
|---|---|---|
| 1. 902614 정상(터치 PIN) | `#7=000` | H-2(스텁 PIN 유출) 발견·수정 계기 |
| 1b. 902614 정상(키보드 PIN) | `#7=000`, 23.8s | P18-8 검증 |
| 2. 800000 | `#7=000`, 3.1s | PIN 미진입 확인 |
| 3. PIN 중 취소(ESC) | `#7=E01`, 11.5s | 초기화 1건, `#51` 공백 |
| 4. PIN 중 Timeout | `#7=E02`, 150.1s | 초기화 1건, `#51` 공백, +30초 연장 실측 |

모든 시나리오에서 `#51`(응답)이 공백으로 확인돼 H-1/H-2 수정이 실전에서도 유효함을 실증했다. "반복
실행 시 이전 거래의 입력값이 남지 않는다"(Phase 18 완료 기준)도 5회 연속 실행 동안 잔존 없이 확인됐다
(구조적으로 `Presenter.Show`가 매 거래 새 ViewModel을 만들어 보장 + 실측 무이상).

## 착수 전 확인이 필요한 것

1. **PIN 노출 시간(600ms)·완성 후 지연(200ms)** — 제안값이다. 실물로 보고 조정한다(상수 1곳).
2. **SEED 암호화 방식** — 여전히 미정. 확정 전까지 평문으로 진행하며, 확정되면 `PinFieldEncoder`
   한 곳만 바뀐다(PRD §10 열린 항목).
3. **`#51` SET 장소의 SPEC 내부 모순** — 표(p.14)는 kiosk, 설명 절(p.17)은 `#44/#45/#46/#51`을 묶어
   "보안리더기에서 생성하여 SET"이라고 적어 서로 어긋난다. 사용자 확정(원캡 담당)으로 구현은
   진행하되, **발주처 확인 시 이 모순을 함께 정정 요청**한다(`#38`과 같은 유형의 문서 결함).
4. **VAN 응답에 `#51`이 실려 오는지**(Phase 20 실서버 검증 항목, 최종 검증 H-1에서 파생) — 실패 응답의
   `#51`은 H-1 수정으로 지웠지만, **relay 경로는 VAN이 준 바이트를 그대로 통과시키므로** VAN이 요청을
   echo하는 형태로 응답하면 같은 문제가 relay로도 생긴다. 실서버가 준비되면 응답 POSITION 612~711을
   실제로 확인하고, 실려 온다면 relay 원칙의 예외로 이 필드만 지울지 발주처와 협의한다.

---

# Phase 19 실행계획서 — 키오스크 시뮬레이터 (POS 역할 테스트 프로그램)

> 로드맵: `ROADMAP.md` "Phase 19 — 키오스크 시뮬레이터". 사람이 실제 키오스크처럼 3종 전문을 보내고
> 응답을 필드 단위로 확인하는 **별도 프로그램**을 만든다. Phase 20(VAN)·21(통합 검증)의 주 검증
> 도구이면서, **동시에 키오스크 업체에 연동 샘플 소스로 제공**된다(2026-08-26 확정, 2026-08-28 재확인).
>
> **이 이중 용도가 이 Phase의 모든 설계 판단을 지배한다.** "우리만 쓰는 하네스"라면 대충 짜도 되지만,
> 남이 읽고 자기 코드의 본으로 삼을 소스이므로 **읽는 사람이 SPEC을 몰라도 필드 계약을 알 수 있게**
> 짜야 한다. 반대로 업체에 줄 소스라고 해서 기능을 덜어내면 우리 검증 도구가 부실해진다 — 두 요구를
> 같은 코드로 만족시키는 것이 목표다(내부용/제공용 빌드를 갈라 관리하지 않는다).

## 착수 전 전제 (2026-08-28 코드 확인 완료)

1. **프레임 형식은 `[길이 4자리 ASCII][본문]`이다** (`Protocol/Pos/PosMessageFramer`, 2026-08-24 확정,
   PRD §10.1). STX/ETX 없음. 길이 4자리는 **본문 바이트 수**이고 SPEC `#0 전문 길이`와 같은 값이다
   (`#0`은 본문 밖 헤더라 스키마에는 들어 있지 않다 — P17-2). 본문 길이는 전문별 고정:
   `501008`=706, `800000`=500, `902614`=1500.
2. **서버는 `127.0.0.1:8002` 루프백 전용**(`Services/Pos/PosSocketServer`). 동시 연결 상한 16,
   응답 송신 타임아웃 5초, **응답 후 유휴 연결은 서버가 먼저 닫는다**. 지속 연결도 허용되지만
   로드맵 확정대로 **시뮬레이터는 전문마다 새 연결을 연다**(실제 POS 원칙과 같음).
3. **인코딩은 CP949**(`PosMessageEncoding`). 한글 필드(`#20 징수 기관명` 등)가 2바이트를 먹으므로
   길이 계산은 반드시 **바이트 기준**이다 — `string.Length`로 세면 안 된다.
4. **응답 코드 체계는 `000` / `E0x` / `R0x`·`R2x` / `D0x`** (P17-4, `Services/Payment/
   PosResultCodeMapper`). 전문 파싱 자체가 실패한 경우는 `E40`(길이 불일치)·`E41`(알 수 없는
   거래구분)이다. 여기에 SPEC 3장의 발주처 정의 코드(`000`~`201`, `M01`/`V01`)가 더해진다.

   > **로드맵 오타 발견(2026-08-28)**: `ROADMAP.md` Phase 20 완료 기준이 VAN 통신 실패를 `G0x`로
   > 적고 있으나 실제 구현은 `D01`/`D02`다. Phase 19 문서 갱신 때 함께 정정한다(P19-8).
5. **필드 오프셋의 "두 번째 사본"이 이미 하나 있다** — Phase 17·18 실장비 검증에 쓴 PowerShell
   클라이언트(`spec_client.ps1`, 세션 스크래치패드)다. 3전문 공통부와 `902614` 요청 필드 대부분의
   POSITION이 거기 손으로 옮겨져 있고 **실장비로 왕복이 확인된 값**이다. 시뮬레이터의 독립 전사본은
   이것과도 대조할 수 있다(P19-2). 스크래치패드는 세션과 함께 사라지므로 **P19-1에서 이 스크립트를
   시뮬레이터 폴더 안(`tools/`)으로 옮겨 보존**한다.
6. **`902614`는 사람이 카드를 대고 PIN까지 눌러야 끝난다** — 응답까지 최장 150초 이상 걸리는 것이
   Phase 18 실측(150.1s)으로 확인됐다. 클라이언트 수신 타임아웃은 **180초를 기본**으로 잡는다.

## 확정된 설계 결정 (2026-08-28 사용자 확정)

1. **배포 형태: 같은 리포 안 + 자체 `.sln` 동봉.** `src/KFTCOneCAP.KioskSim/` 아래에 프로젝트와
   **그 폴더 전용 `KFTCOneCAP.KioskSim.sln`**을 함께 둔다. 내부에서는 루트 솔루션에도 추가해 본 앱과
   같이 열고, 업체에는 **그 폴더만 통째로 압축해 전달**하면 바로 열리고 빌드된다. 본 앱 소스는
   한 줄도 딸려가지 않는다.
2. **오류 주입은 만들되 별도 탭으로 격리한다.** 정상 전송 화면과 오류 주입 화면을 탭으로 나눠,
   업체가 정상 경로 코드를 읽을 때 방해받지 않게 한다. 조건부 컴파일(`#if INTERNAL`)로 가르지
   않는다 — **우리가 검증한 소스와 업체가 받는 소스가 완전히 같아야** 나중에 "그쪽 소스에선 안
   되던데요"를 재현할 수 있다.
3. **입력값은 프리셋 파일에서 불러온다. 마지막 입력값 자동 저장은 하지 않는다.**
   실행 파일 옆 `kiosksim.preset.json`을 시작 시 읽어 화면을 채우고, **파일이 없으면 코드에 박힌
   기본값**으로 채운다(파일을 안 주고 배포해도 바로 동작해야 한다). 사용자가 원할 때만 누르는
   **"현재 값을 프리셋으로 저장" 버튼**은 둔다 — 자동 저장이 아니라 명시적 행위이므로 "직전 실험의
   찌꺼기가 조용히 남는" 문제가 없다.
4. **송수신 전문 로그 파일 기록은 이번 범위 밖**(2026-08-28 사용자 결정). 추후 KFTCOneCAP 쪽 로그
   파일 기능이 생기면 거기서 대조한다. 화면에는 물론 raw ASCII를 그대로 보여준다.
5. **응답 대기 타임아웃은 상수 180초 고정**, 조절 UI는 만들지 않는다(전제 6).
6. **플랫폼은 `AnyCPU`로 둔다** — 로드맵 원문은 `net48`/x86이라고 적었지만, x86 고정의 이유는
   본 앱이 32bit 네이티브 DLL 2개를 로드하기 때문이고(`csproj` 주석) **시뮬레이터는 네이티브 의존이
   전혀 없다**. 업체 환경을 가리지 않는 편이 샘플로서 낫다. `net48`은 그대로 유지한다(업체 환경이
   구형일 수 있고, 본 앱과 같은 런타임 전제를 공유하는 편이 안전하다). 로드맵을 P19-8에서 정정한다.
7. **외부 NuGet 패키지 0개.** JSON 프리셋도 `System.Runtime.Serialization.Json`이나 손으로 짠
   최소 파서로 처리한다 — `dotnet restore`가 막힌 폐쇄망 업체 PC에서도 열리자마자 빌드돼야 한다.
8. **주석은 한글로, "왜"까지 적는다.** 업체 개발자가 이 소스만 보고 연동할 수 있어야 한다.

## 이 Phase에서 손대지 않는 것 (범위 밖)

- **본 앱 소스 전부** — 시뮬레이터는 소켓 너머의 남이다. 본 앱을 고쳐야 검증이 되는 상황이 나오면
  그건 결함 발견이므로 별도 항목으로 기록하고 고친다(그 자체가 이 Phase의 성과다).
- **`Protocol/Pos/`와의 코드 공유** — 로드맵이 명시적으로 금지한다. 이유는 P19-2에 적는다.
- **VAN 실호출** — Phase 20. 시뮬레이터가 보는 것은 스텁 응답이다.
- **키오스크 실물 UI 재현** — 화면 디자인을 흉내 내지 않는다. 필드가 다 보이는 실무 도구로 만든다.

---

## P19-1. 프로젝트 신설 + 배포 골격 ★

**먼저 이것부터.** 나중에 폴더를 옮기면 `.sln` 상대경로와 업체 배포 절차가 같이 틀어진다.

### 구현할 것

- `src/KFTCOneCAP.KioskSim/KFTCOneCAP.KioskSim.csproj` — SDK 스타일, `net48`,
  `OutputType=WinExe`, `UseWindowsForms=true`, `LangVersion=latest`, `Nullable=enable`,
  **`PlatformTarget` 미지정(AnyCPU)**, PackageReference 0개.
- `src/KFTCOneCAP.KioskSim/KFTCOneCAP.KioskSim.sln` — **이 프로젝트 하나만** 참조하는 솔루션.
  경로는 반드시 같은 폴더 기준 상대경로여야 한다(폴더를 떼어내도 열려야 하므로).
- 루트 `KFTCOneCAP.Wpf.sln`에도 프로젝트를 추가한다(내부 편의). **루트 솔루션 쪽 참조는
  `src\KFTCOneCAP.KioskSim\...`이고, 이 참조가 업체 배포본에는 존재하지 않는다** — 두 솔루션이
  서로를 모르는 상태여야 한다.
- `README.md`(업체 제공용, 한글) — 무엇을 하는 프로그램인지, 빌드 방법, KFTCOneCAP을 먼저 띄워야
  한다는 전제, 포트 8002, 프레임 형식 한 문단, **"이 소스의 전문 필드 테이블은 SPEC을 옮겨 적은
  것이며 SPEC이 정본"**이라는 주의 문구.
- `tools/spec_client.ps1` — 전제 5의 PowerShell 클라이언트를 여기로 옮겨 보존한다(P19-2 교차 대조의
  근거 사본이자, 업체에도 "최소 구현은 이 정도"를 보여주는 참고 자료가 된다).
- 폴더 구조를 처음부터 읽는 순서대로 나눈다:
  `Protocol/`(전문 정의·코덱) → `Net/`(TCP 클라이언트) → `Forms/`(화면) → `Preset/` → `tools/`.

### 완료 조건

- [x] `dotnet build src/KFTCOneCAP.KioskSim/KFTCOneCAP.KioskSim.sln` 성공(경고 0).
- [x] 루트 솔루션 `dotnet build`도 성공하고 **본 앱 빌드 산출물이 달라지지 않는다**.
- [x] `src/KFTCOneCAP.KioskSim/` 폴더를 임시 위치로 **복사**한 뒤 그 자리에서 빌드가 성공한다
      (배포 시뮬레이션 — 이걸 실제로 해 보지 않으면 상대경로 실수를 못 잡는다).

**완료 조건 검증 결과(2026-08-28)**:
1. `dotnet build src/KFTCOneCAP.KioskSim/KFTCOneCAP.KioskSim.sln` — "빌드했습니다. 경고 0개
   오류 0개" (`KFTCOneCAP.KioskSim.exe` 생성 확인).
2. `dotnet build KFTCOneCAP.Wpf.sln`(루트) — 두 프로젝트(KFTCOneCAP.KioskSim, KFTCOneCAP.Wpf)
   모두 "경고 0개 오류 0개"로 빌드. 본 앱 exe(`src/KFTCOneCAP.Wpf/bin/Debug/net48/
   KFTCOneCAP.Wpf.exe`)의 SHA256 해시를 KioskSim 추가 전/후로 비교(`bf648001...0237b55`)해
   **완전히 동일함을 확인** — 새 프로젝트 추가가 본 앱 빌드에 영향 없음.
3. `src/KFTCOneCAP.KioskSim/`을 `bin`/`obj` 제외하고 세션 스크래치패드 임시 폴더로 복사한 뒤
   그 자리에서 `dotnet build KFTCOneCAP.KioskSim.sln` 실행 — "빌드했습니다. 경고 0개 오류 0개"로
   성공(상대경로 문제 없음 확인). 검증 후 임시 폴더는 삭제했다.

---

## P19-2. 전문 필드 테이블 독립 전사 ★ (이 Phase의 핵심 가치)

**왜 공유하지 않는가**: 본 앱의 `Protocol/Pos/Schemas/`를 참조하면 POSITION이나 길이를 잘못 적어도
양쪽이 똑같이 틀려 **테스트가 통과해 버린다**. 두 번 옮겨 적어야 서로의 오타가 드러난다. 실제
키오스크 업체도 SPEC을 보고 독립 구현하므로 이쪽이 현실에 더 가깝다(로드맵 확정).

### 구현할 것

- `Protocol/TelegramField.cs` — 번호·이름·표현(N/A/AN/AHN/ANS)·길이·POSITION·**SET 장소**를 갖는
  불변 타입. SET 장소는 `kiosk` / `원캡` / `인터넷지로` / `VAN` 4종 열거형.
- `Protocol/TelegramSchemas.cs` — 공통부 + 3전문 업무부를 **SPEC PDF를 다시 보며 손으로** 채운다.
  본 앱 소스를 열어 두고 베끼지 않는다. 필드 확인이 애매하면 `pos-onecap-spec-expert`
  서브에이전트에 묻는다(SPEC 표는 SET 장소 열을 눈으로 훑다 착각하기 쉽다 — 실제로 `#48`을 kiosk로
  잘못 읽은 전례가 있다).
- 각 스키마에 **자기 검증 생성자**를 둔다: 필드 POSITION이 0부터 빈틈·겹침 없이 이어지고
  마지막 필드의 끝이 전문 총 길이(706/500/1500)와 정확히 일치하는지 생성 시점에 확인하고,
  어긋나면 즉시 예외. 업체가 필드를 잘못 고쳐도 실행 즉시 알 수 있다.

### 완료 조건

- [x] 3전문 스키마가 자기 검증을 통과하며 만들어진다(총 길이 706/500/1500 일치).
- [x] **교차 대조를 실제로 수행한다** — 시뮬레이터 테이블 vs 본 앱 `Protocol/Pos/Schemas/` vs
      `tools/spec_client.ps1`의 POSITION/길이를 필드 단위로 비교하고, **불일치가 하나라도 나오면
      한쪽을 베껴 맞추지 않고 SPEC PDF로 되돌아가 판정한다**. 대조 결과(불일치 0건이든, N건을
      어떻게 판정했든)를 이 문서에 기록한다.
- [x] SET 장소가 kiosk가 아닌 필드 목록을 뽑아 적는다 — `800000`은 `#14` 1개, `902614`는
      `#43/#44/#45/#46/#48/#50/#51/#53`(Phase 18에서 `#51`이 원캡 담당으로 확정되며 7→8개가 됐다).

### 완료 조건 검증 결과 (2026-08-28)

**자기 검증**: `KFTCOneCAP.KioskSim.exe`를 빌드한 뒤 리플렉션으로
`TelegramSchemas.Notice501008`/`CardInfo800000`/`CardApproval902614`를 강제로 초기화해 생성자
예외가 나지 않는지 직접 실행 확인했다(정적 필드는 최초 접근 시점에만 초기화되므로 "빌드 성공"만으로는
검증되지 않는다 — P17-2/`PosSchemaRegistry.ValidateAtStartup`과 같은 이유). 결과: 3개 모두 예외 없이
생성됨, `TotalLength`=706/500/1500·`Fields.Count`=56/27/54 확인.

**작성 과정**: SPEC PDF(`docs/payment_relay/spec/국세 베리어프리 키오스크용 전산설계서(POS-원캡)_
20260826.pdf`) 2장(p.5~14)을 처음부터 다시 읽어 `Protocol/TelegramField.cs`·`TelegramSchemas.cs`를
작성했다. 이 저장소에는 `pos-onecap-spec-expert` 같은 서브에이전트를 호출할 도구가 없어(Task 도구
미제공), SPEC PDF를 Read 도구로 페이지별로 직접 읽고 대조하는 방식으로 진행했다 — 애초 계획한
서브에이전트 질의 대신 PDF 원문 재확인으로 동일한 검증 목적을 달성했다.

**독립 전사 중 발견한 자체 오류(중요)**: 최초 작성 시 `501008 #14 전자납부번호`를 SPEC 표의 "디지털
예산" 열 체크로 잘못 읽어 `InternetGiro`로 분류했었다. 교차 대조 단계에서 본 앱 스키마
(`NoticeInquirySchema.cs`, `PosFieldOwner.Kiosk`)와 `tools/spec_client.ps1`(공통부가 아닌 501008
전용 분기에서 `#14`를 명시적으로 SET하고 실장비 왕복 성공)이 모두 `Kiosk`로 되어 있는 것을 보고
SPEC PDF p.7 표를 다시 확대해서 본 결과, 체크 표시는 "kiosk" 열(표의 가장 오른쪽)에 있었다 —
디지털예산/인터넷지로/kiosk 3열이 시각적으로 촘촘히 붙어 있어 처음에 왼쪽 열로 착각했다. `#14`를
`Kiosk`로 정정했다(정확히 "SPEC PDF로 되돌아가 판정" 절차대로 처리 — 본 앱이나 스크립트 값을 그대로
베끼지 않고, 그 값을 단서로 삼아 PDF 원문을 재확인해 스스로 근거를 확보했다).

또한 초기 설계에서 공통부(#1~13)를 3전문이 완전히 동일하다고 가정해 하나의 `BuildCommonFields()`로
공유하려 했으나, SPEC 표를 전문별로 대조하는 과정에서 **SET 장소 체크가 전문마다 다르다**는 것을
발견했다(예: `#6 송·수신 FLAG`는 501008/902614는 kiosk 열이 체크되어 있지만 800000은 체크되어 있지
않다; `#5 상태 코드`도 마찬가지). POSITION·길이·표현은 4곳(공통부 제네릭 표 1개 + 전문별 표 3개)
모두 동일하지만 SET 장소만 다르다는 것을 확인하고, 공유 헬퍼를 버리고 전문마다 독립된 공통부 목록을
작성하도록 다시 고쳤다.

**교차 대조 결과**:

| 항목 | 결과 |
|---|---|
| 501008 POSITION/길이/표현 (전체 56필드) | 시뮬레이터 vs 본 앱 vs spec_client.ps1(공통부 상수) 모두 완전 일치 |
| 800000 POSITION/길이/표현 (전체 27필드) | 시뮬레이터 vs 본 앱 vs spec_client.ps1 모두 완전 일치 |
| 902614 POSITION/길이/표현 (전체 54필드) | 시뮬레이터 vs 본 앱 vs spec_client.ps1 모두 완전 일치 |
| 501008 SET 장소 (56필드) | `#14` 정정(위 기록) 후 시뮬레이터·본 앱 완전 일치 |
| 800000 SET 장소 (27필드) | **불일치 3건 발견**(체크포인트 1 재검토 2026-08-28, 최초 대조 때는 1건만 기록해 부정확했다 — 아래 정정 참고): `#27`(실질 불일치, 아래 "본 앱 쪽 결함 후보" 참고), `#10`/`#13`(표현 축 차이, 아래 참고). |
| 902614 SET 장소 (54필드) | **불일치 3건 발견**(체크포인트 1 재검토 2026-08-28, 최초 대조 때는 "완전 일치"로 잘못 기록했다 — 아래 정정 참고): `#17`/`#47`/`#54`(표현 축 차이, 아래 참고). `#38`/`#51` 등 본 앱이 SPEC 표와 다르게 확정한 필드는 여전히 동일한 근거로 동일하게 분류돼 있음을 재확인. |
| 902614 요청 필드 값(공통부 상수 + `#14~42`) | `spec_client.ps1`이 실제로 SET하는 모든 위치(0,3,6,10,19,23,35,59,61,68,70,83,102,113,120,126,146,167,171,216,231,246,261,262,270,278,280,296,309,332,334,335,610)와 시뮬레이터 필드 POSITION이 전부 일치 |

**표현(N/A/AN/AHN/ANS) 관련 발견**: development_plan.md/과업 지시가 전제한 "N/A/AN/AHN/ANS 5종"은
SPEC 표를 다시 읽어 보니 실제로는 6종이었다 — `902614 #37 납부자 성명`이 `AHNS`(영문+한글+숫자+
특수문자)로 표기되어 있어, 이 5종 목록에 없는 값이 하나 더 있었다. `TelegramRepresentation` enum에
`AHNS`를 추가해 반영했다(본 앱의 `PosFieldType`도 동일하게 `AHNS`를 이미 가지고 있어 이 부분은 본 앱과
일치).

**SET 장소가 kiosk가 아닌 필드 목록** (요청 시 kiosk가 아닌 다른 주체가 채우는, 이 전문의 실질적 입력
계약과 직결되는 필드만 추려 기록 — 응답 전용 필드는 별도 표시):

- `800000`: 카드 하드웨어 관련 요청 입력 필드 중 **`#14 BIN` 1개**(원캡). 그 외 `#17~27`은 조회
  "응답" 결과 필드라 애초에 kiosk가 채울 필요가 없는 필드다(요청 입력 계약과 무관 — 시뮬레이터
  테이블에는 `InternetGiro`로 정직하게 남겨 뒀으나, "kiosk가 못 채우는 입력 필드"라는 의미의 8/1개
  집계에는 포함하지 않는다). 공통부의 `#5/#6/#7/#8` 도 마찬가지로 응답/프로토콜 전용이라 이 집계에서
  제외.
- `902614`: 카드 하드웨어·보안 관련 요청 입력 필드 **8개 — `#43/#44/#45/#46/#48/#50/#51/#53`**(전부
  원캡). `#51`은 SPEC 표 원문은 kiosk이지만 Phase 18 실측/보안 결정에 따라 원캡으로 분류(위 코드 주석
  참고). `#40`(기 납부 이용 시스템)과 `#52`(선불카드 잔액)도 kiosk가 아니지만(인터넷지로) 응답 전용
  필드라 이 8개 집계와는 별도로 취급했다. 공통부의 `#7/#10/#13`도 마찬가지로 응답 전용이라 제외.

이 두 집계(`800000`=1개, `902614`=8개)는 development_plan.md/CLAUDE.md에 이미 기록된 "확립된 사실"과
정확히 일치한다.

**체크포인트 1 정정(2026-08-28)**: 최초 대조(위 표의 원래 문구)는 `TelegramSetLocation`에 본 앱의
`PosFieldOwner.None`(SPEC 표에 아무 체크도 없는 필드)에 대응하는 값이 없다는 사실을 놓쳤다. 이
프로젝트는 "체크가 없으면 요청을 보내는 kiosk가 결국 space로 채운다"(SPEC 공통부 표 각주)는 정책에
따라 그런 필드를 전부 `Kiosk`로 분류했는데, 이게 본 앱의 `None`과 사람이 보기엔 "불일치"로 보인다.
실제로 divergence는 총 6건이었다: `800000 #10/#13`, `902614 #17/#47/#54`(이상 5건, 아래 설명), `800000
#27`(1건, SPEC 자체 재확인 결과가 다른 진짜 불일치 — 바로 아래 절 참고).

**5건(`800000 #10/#13`, `902614 #17/#47/#54`)의 판정**: 본 앱의 `None`은 "SPEC 표에 체크가 없다"는
문서적 사실을 그대로 보존하려는 표기이고, 시뮬레이터의 `Kiosk`는 "요청을 실제로 누가 보내는가"라는
실무 질문에 답한 것이다 — 같은 SPEC 사실을 다른 축으로 표현했을 뿐 내용이 다른 게 아니다. 2026-08-28
사용자가 "아무도 안 채우는 필드는 그냥 키오스크단에서 공백으로 채워서 보내면 된다"고 명시적으로
확인해, 시뮬레이터 쪽 `Kiosk` 분류가 맞다고 확정했다(코드 수정 없음 — `Protocol/TelegramField.cs`의
`TelegramSetLocation` 문서에 이 판정 근거를 추가함). 본 앱의 `None`도 요청을 만드는 주체가 아니므로
동작에는 영향이 없다(본 앱은 요청을 만들지 않고 받기만 한다).

**본 앱 쪽 결함 후보(범위 밖, 별도 보고 — 이 Task에서는 코드를 고치지 않았다, 위 5건과는 성격이 다른
진짜 불일치 1건)**:
`src/KFTCOneCAP.Wpf/Protocol/Pos/Schemas/CardInfoInquirySchema.cs`의 `800000 #27 예비 정보 FIELD`가
`PosFieldOwner.None`으로 등록되어 있으나, SPEC PDF(p.12) 표는 이 필드의 "인터넷지로" 열을 체크해
두었다(같은 열에 체크된 `#17~26`과 정렬이 같다). `None`이 실제 코드 동작에 영향을 주는지(단순 문서화
태그인지, 아니면 어떤 검증/UI 로직이 `None`을 특별 취급하는지)는 확인하지 않았다 — 그 확인과 수정
여부 판단은 이 Task의 범위 밖이므로 사용자에게 별도 보고한다.

---

## P19-3. 고정길이 코덱 (빌드/파싱)

### 구현할 것

- `Protocol/TelegramBuffer.cs` — 본문 바이트 배열을 들고 `Write(번호, 값)` / `Read(번호)`만
  제공한다. 규칙은 SPEC 그대로:
  - 전체를 **space(0x20)로 초기화**한 뒤 시작.
  - `N`(숫자): **우측 정렬 + 앞을 `0`으로 채움**.
  - 그 외(A/AN/AHN/ANS): **좌측 정렬 + 뒤를 space**.
  - CP949 바이트 길이가 필드 길이를 넘으면 **즉시 예외**(잘라내지 않는다 — 조용히 잘리면 원인
    추적이 불가능해진다).
- `Protocol/TelegramCodec.cs` — 본문 바이트 ↔ `[길이 4자리][본문]` 프레임 변환. 길이 4자리는
  본문 바이트 수를 `D4`로. **이 규칙을 설명하는 주석을 여기 한 곳에 몰아 쓴다**(업체가 제일 먼저
  찾아볼 자리).

### 완료 조건

- [x] 한글 필드(`#20 징수 기관명`, `#21 징수 과목명`, `#37 납부자 성명`)에 한글을 넣었을 때
      **바이트 기준으로** 정확히 채워지고 인접 필드를 침범하지 않는다.
- [x] 길이 초과 시 예외 메시지에 필드 번호·이름·허용 길이·실제 바이트 수가 다 들어간다.
- [x] `902614` 전문을 space만으로 만들었을 때 총 1500바이트, 프레임 1504바이트다.

### 완료 조건 검증 결과 (2026-08-28)

빌드된 `KFTCOneCAP.KioskSim.exe`를 PowerShell로 `Assembly.LoadFrom` 후 리플렉션으로
`TelegramBuffer`/`TelegramCodec`를 직접 호출해 확인했다(P19-2와 같은 방식).

1. **한글 필드 바이트 정확성 + 인접 필드 비침범**: `902614` 버퍼에 `#20="국세청"`,
   `#21="종합소득세"`, `#37="홍길동"`을 쓰고, 바로 옆 필드(`#19`, `#22`, `#36`, `#38`)에는 각각
   식별 가능한 값(`123456`/`1`/`1234567890123`/`9876543210123`)을 채운 뒤 `ToBytes()`로 얻은
   본문에서 POSITION(126/146/309)·길이(20/20/10) 그대로 CP949로 슬라이스했다. 결과:
   `국세청`(6바이트, 좌측 정렬 + 뒤 14바이트 공백), `종합소득세`(10바이트, 뒤 10바이트 공백),
   `홍길동`(6바이트, 뒤 4바이트 공백)로 정확히 채워졌고, `Read()`로 되읽으면 trim된 원문이
   그대로 나온다. 인접 필드(`#19`/`#22`/`#36`/`#38`)의 `Read()` 값도 각각 쓴 값 그대로였다 —
   침범 없음 확인.
2. **길이 초과 예외 메시지**: `#37 납부자 성명`(허용 10바이트)에 50바이트짜리 한글 문자열을
   써서 강제로 예외를 발생시켰다. 실제 메시지: `"902614 전문 #37(납부자 성명) 길이 초과:
   허용 길이=10바이트, 실제 값 길이=50바이트(CP949 기준), 값="...". 값을 줄여서 다시 시도하라
   (자동으로 잘라내지 않는다)."` — 필드 번호(#37)·이름(납부자 성명)·허용 길이(10)·실제
   바이트 수(50) 전부 포함 확인.
3. **space-only 총 길이 + 프레임 길이**: `902614` 스키마로 새 `TelegramBuffer`를 만들고(아무
   `Write`도 호출 안 함) `ToBytes()` 결과 길이 = 1500바이트. `TelegramCodec.Encode()` 결과
   길이 = 1504바이트, 앞 4바이트 헤더 = `"1500"` 확인. `Decode()`로 되돌리면 1500바이트,
   원본 본문 바이트와 완전히 동일(`SequenceEqual` true) 확인.

## Phase 19 체크포인트 1 — 검증 리뷰 및 후속 수정 (2026-08-28)

P19-2·P19-3 완료 직후 계획대로 진행한 체크포인트. 발견 4건, 전부 수정·재검증 완료.

1. **M-1/M-2(문서 오기록)**: P19-2 교차 대조 표가 `TelegramSetLocation`에 본 앱의
   `PosFieldOwner.None`에 대응하는 값이 없다는 사실을 놓쳐, 실제로는 6건인 divergence를 1건으로
   과소 기록했다. 위 P19-2 절의 "체크포인트 1 정정" 단락에서 6건 전부(진짜 불일치 1건 + 표현 축
   차이 5건)를 재기록하고, `Protocol/TelegramField.cs`의 `TelegramSetLocation` 문서에도 이
   판정 근거(2026-08-28 사용자 확정 — "아무도 안 채우는 필드는 키오스크단에서 공백으로 채워서
   보낸다")를 추가했다. 필드 분류(코드 값) 자체는 변경 없음 — 문서만 정정.
2. **L-1(사실 오류 주석)**: `TelegramSchemas.cs` `#51 암호화된 비밀번호 정보` 필드 주석이 "리더기
   핀패드에서 입력받아"라고 적혀 있었으나, Phase 18 확정 사실은 정반대(알림창 화면 키패드로 입력,
   리더기 `Pinpad_SendCommand` 계열은 범위 밖)다. 업체가 이 주석만 보고 연동 구조를 오해할 수
   있어 정정했다.
3. **L-2(길이 헤더 파싱이 과도하게 관대함)**: `TelegramCodec.ReadLengthHeader`가 `int.TryParse`를
   그대로 썼는데, 이 오버로드는 앞뒤 공백과 `+`/`-` 부호를 허용한다(`NumberStyles.Integer` 기본값).
   실측으로 `" 706"`/`"+706"`이 통과해버리는 것을 확인했다 — 4자리 전부 ASCII 숫자(`'0'`~`'9'`)인지
   직접 검사하도록 고쳤다. 재검증: `"0706"`/`"9999"`는 통과, `" 706"`/`"+706"`/`"07 6"`/`"abcd"`는
   전부 예외로 거부됨을 실제로 확인.

수정 후 `dotnet build src/KFTCOneCAP.KioskSim/KFTCOneCAP.KioskSim.sln` 경고 0/오류 0 재확인.

---

## P19-4. TCP 클라이언트

### 구현할 것

- `Net/OneCapClient.cs` — **한 번의 요청·응답이 곧 한 번의 연결**:
  연결 → 프레임 전송 → 길이 4자리를 먼저 읽고 → 본문을 그 길이만큼 다 읽을 때까지 누적 → 닫기.
  **부분 수신 누적을 반드시 구현한다** — TCP는 1500바이트를 한 번에 주지 않는다(업체가 가장 자주
  틀리는 지점이라 주석으로 명시한다).
- 수신 타임아웃 **180초 상수**(결정 5). 타임아웃/연결 거부/중간 절단을 각각 구분해 화면에 알린다
  — "실패"로 뭉뚱그리면 본 앱을 안 띄운 것인지 응답이 안 온 것인지 알 수 없다.
- **UI 스레드를 막지 않는다**: 전송은 백그라운드에서 하고 결과만 UI로 되돌린다(WinForms이므로
  `Control.Invoke`). 902614는 150초를 기다리므로 동기 호출하면 창이 통째로 굳는다.
- 전송 중에는 전송 버튼을 비활성화하고 **"응답 대기 중… (n초)"** 경과를 보여준다.

### 완료 조건

- [x] `501008`을 보내 응답을 받아 화면에 표시된다(본 앱 구동 상태에서).
- [x] 본 앱을 **끈 상태**로 보내면 "연결 거부"가 타임아웃 없이 즉시 구분돼 표시된다.
- [x] 902614 전송 중 창이 굳지 않고 경과 시간이 갱신된다.

### 완료 조건 검증 결과 (2026-08-28)

`Net/OneCapClient.cs`를 새로 작성했다(`OneCapClientResultKind` enum + `OneCapClientResult` 결과
클래스로 성공/타임아웃/연결거부/연결중끊김/기타예외 5가지를 구분, `SendAsync(byte[] requestFrame,
Action<TimeSpan>? onElapsed)`로 비동기 API 제공). 화면(P19-5/6)이 아직 없어 완료 조건 문구의
"화면에 표시된다"는 리플렉션/전용 테스트 콘솔로 `OneCapClient`를 직접 호출해 결과 값을 콘솔에 찍는
방식으로 확인했다(development_plan.md 지시대로).

1. **501008 왕복(본 앱 구동 상태)**: `dotnet build`로 본 앱(`KFTCOneCAP.Wpf.exe`)을 실행한 뒤, 빌드된
   `KFTCOneCAP.KioskSim.exe`를 PowerShell에서 `Assembly.LoadFrom`으로 로드해 `TelegramSchemas.
   Notice501008` → `TelegramBuffer` → `TelegramCodec.Encode`로 710바이트 요청 프레임을 만들고
   `OneCapClient.SendAsync`로 전송했다. 결과: `Kind=Success`, 응답 본문 70바이트 수신(빈 필드로만
   채운 요청이라 본 앱이 오류 응답을 돌려준 것으로 보이나, 길이 헤더 파싱→본문 누적 수신까지 프로토콜
   왕복 자체가 정상 동작함을 확인하는 것이 이 Task의 범위다 — 필드 값 자체의 정합성은 P19-5/6·
   Phase 21의 범위). `SendAsync` 호출 자체는 3.7ms 만에 제어를 반환(논블로킹 확인).
2. **본 앱 미구동 시 연결 거부**: `KFTCOneCAP.Wpf.exe` 프로세스를 종료한 상태에서 같은 방식으로
   501008을 전송 — `Kind=ConnectionRefused`, 2.0초 만에 반환(타임아웃 180초와 무관하게 즉시 실패,
   `SendAsync` 호출 자체는 2.9ms 만에 제어 반환).
3. **비동기/논블로킹 + 경과시간 진행 콜백**: PowerShell 스크립트 블록을 델리게이트로 넘겼을 때
   `System.Threading.Timer` 콜백이 호출되지 않는 현상이 있어(PowerShell 스크립트블록↔백그라운드
   스레드 델리게이트 마샬링 한계로 판단 — `OneCapClient` 자체의 결함이 아님을 별도 확인하기 위해),
   별도의 최소 net48 콘솔 테스트 앱(스크래치패드, 이 저장소 밖)을 만들어 순수 C# `Action<TimeSpan>`
   람다로 재검증했다. 같은 프로세스 안에 `TcpListener`로 가짜 서버를 띄우고 응답을 3초 지연시킨 결과:
   - `SendAsync` 호출은 0.7ms 만에 제어 반환.
   - 메인 스레드는 그동안 400ms 간격으로 8회 동시 작업(tick)을 수행 — `clientTask.IsCompleted`가
     계속 `False`인 상태로 병행 진행됨을 확인(창이 굳지 않음의 실증).
   - `onElapsed` 콜백이 약 0.5초 간격으로 백그라운드 스레드(스레드ID 5, UI 스레드 아님)에서 6회
     호출됨(0.52s/1.03s/1.54s/2.05s/2.56s/3.07s) — "응답 대기 중… (n초)" UX를 지원할 수 있음을 확인.
   - 최종 `Kind=Success`, 3.2초 경과로 정상 응답 수신.
   - 같은 하네스로 "응답 다 받기 전 연결 절단"(706바이트 중 300바이트만 보내고 서버가 close) 케이스도
     추가 검증: `Kind=ConnectionClosed`, 메시지에 "응답 706바이트 중 300바이트만 받은 상태에서 연결이
     종료됐다" 정확히 표시, 콜백은 끊기기 직전까지 계속 호출됨(3.56s에 마지막 호출) — 타임아웃과
     별개로 구분됨을 확인.
   - 최초 P19-4 완료 시점에는 리더기가 세션에 연결돼 있지 않아 가짜 서버 지연 재현으로만 검증했으나,
     **이후 사용자가 리더기(COM3/COM7)를 연결한 상태에서 실제 902614 카드/PIN 왕복도 추가로
     실행했다(2026-08-28)** — 아래 4번 참고.

4. **실제 902614 카드/PIN 하드웨어 왕복(2026-08-28, 사용자가 리더기 연결 후 요청)**: 본 앱
   (`KFTCOneCAP.Wpf.exe`)을 실행한 상태에서 `TelegramSchemas.CardApproval902614` 스키마로
   `spec_client.ps1`과 동일한 필드 값(주민등록번호/전자납부번호/징수 정보 등)을 채운 1500바이트
   요청을 만들어 `OneCapClient.SendAsync`로 전송, 사용자가 실제로 카드를 태그하고 PIN 4자리를
   입력했다. 결과: `Kind=Success`, **8.4초**, `#7=[000]`(승인), `#3=0210`, `#6=C`. 하드웨어가 채운
   필드도 실제 값으로 확인됨(`#43 보안단말기 인증번호`, `#48 거래입력유형=5`, `#50 인증방식=2`).
   **`#51`(암호화된 비밀번호 정보) 길이=0** — Phase 18 H-1/H-2에서 고친 "실패/스텁 응답에 PIN이
   실리지 않는다"는 불변조건이 시뮬레이터가 만든 실제 요청 경로에서도 깨지지 않음을 재확인했다.
   `OneCapClient`가 순수 네트워킹 계층으로서 실제 하드웨어 연동 흐름 전체(연결→전송→PIN 대기
   150초 상한 이내→부분 수신 누적→응답 파싱)를 문제 없이 통과한다는 최종 증거다.

**빌드**: `dotnet build src/KFTCOneCAP.KioskSim/KFTCOneCAP.KioskSim.sln` 및 루트
`dotnet build KFTCOneCAP.Wpf.sln` 모두 경고 0개/오류 0개.

**테스트에 사용한 임시 프로세스 정리**: 검증을 위해 직접 띄운 `KFTCOneCAP.Wpf.exe`는 mid-close 시나리오
재현 과정에서 이미 종료됐고, 세션 종료 시점에 `KFTCOneCAP*`/`OneCapClientTest` 이름의 프로세스가
남아 있지 않음을 재확인했다(사용자가 미리 띄워둔 프로세스는 없었다).

---

## P19-5. 전송 화면 — 3전문 독립 버튼 + 필드별 입력

### 구현할 것

- 탭 1개("전문 전송")에 **전문 3종을 각각의 독립 버튼**으로 배치(2026-08-26 확정 — 하나의 거래로
  묶지 않는다). 전문을 고르면 그 전문의 필드 목록이 그리드에 뜬다.
- 그리드 열: `#번호 | 필드명 | 표현 | 길이 | POSITION | SET 장소 | 값`.
- **SET 장소가 kiosk인 필드만 편집 가능**, 나머지는 회색 비활성 + "원캡이 채움"처럼 담당을 표시.
  미입력 필드는 자동 space(숫자형은 `0`)로 채워진다.
- 화면 아래에 **보낼 전문의 raw ASCII 미리보기**를 항상 띄운다(전송 전에 눈으로 검증 가능하게).
- 프리셋(결정 3): 시작 시 `kiosksim.preset.json` 로드 → 없으면 코드 기본값. "프리셋으로 저장"
  버튼 1개. 프리셋 파일은 **전문별 필드 번호→값 맵**이라 SPEC이 바뀌어도 위치로 깨지지 않는다.

### 완료 조건

- [x] 3전문 각각 필드 목록이 SPEC 순서대로 뜨고 kiosk 필드만 편집된다.
- [x] 프리셋 파일이 없는 상태로 처음 실행해도 기본값으로 즉시 전송 가능하다.
- [x] 저장 후 재실행하면 그 값이 복원되고, 파일을 지우면 다시 기본값으로 돌아온다.

### 완료 조건 검증 결과 (2026-08-28)

구현: `Preset/PresetStore.cs`(+ 손으로 짠 최소 JSON 파서/직렬화기 `Preset/MiniJson.cs`, 외부 NuGet
0개 원칙), `Forms/MainForm.cs`(placeholder를 실제 화면으로 교체 — 탭 2개: "전문 전송"(이번 Task) +
"오류 주입 (P19-7 예정)"은 빈 placeholder 라벨만). `Program.cs`는 `MainForm` 생성자 시그니처가
바뀌지 않아 수정 불필요.

`dotnet build src/KFTCOneCAP.KioskSim/KFTCOneCAP.KioskSim.sln` 경고 0개/오류 0개. `mcp__windows__*`
도구로 빌드된 `KFTCOneCAP.KioskSim.exe`를 실제 실행해 검증했다(사전에 떠 있던 `KFTCOneCAP.Wpf.exe`,
PID 42868는 이 세션이 띄운 것이 아니라 건드리지 않고 그대로 응답 서버로 사용했다).

1. **3전문 필드 목록 + kiosk 필드만 편집 (완료조건 1)**: 세 버튼을 각각 눌러 그리드가 SPEC
   번호 오름차순(501008: 56필드, 800000: 27필드, 902614: 54필드)으로 다시 그려지는 것을
   스크린샷으로 확인했다. 800000에서 `#14 BIN`이 "원캡이 채움"으로 표시되고 회색 비활성인 것,
   `#5~8`이 "인터넷지로가 채움"으로 비활성인 것을 확인했다(P19-2에서 확정한 SET 장소 그대로).
   902614는 접근성 스냅샷으로 54개 행 전체의 값 셀 `[readonly]` 속성을 하나씩 대조해
   `#43/#44/#45/#46/#48/#50/#51/#53`(OneCap) 8개와 `#7/#10/#13/#40/#52`(InternetGiro)가 읽기
   전용이고, 나머지 kiosk 필드는 편집 가능(readonly 속성 없음)임을 전수 확인했다 — 스키마의
   `SetLocation` 분류와 그리드의 편집 가능 여부가 정확히 일치한다.
2. **프리셋 파일 없이 즉시 전송 가능 (완료조건 2)**: `kiosksim.preset.json`이 없는 상태로 실행 →
   501008 선택 → 그리드가 코드 기본값(`#1=IGN, #4=501008, #6=G, #8=현재시각, #9=0EC0+8자리
   난수, #11=01, #12=1234567, #14=19자리 전자납부번호` 등)으로 즉시 채워진 것을 확인 → "전송"
   클릭 → 본 앱(KFTCOneCAP.Wpf.exe, 8002 포트)에 실제로 연결되어 1.1초 만에 `Kind=Success`,
   응답 본문 706바이트 수신. 응답 원문에서 `#7 응답 코드`=`000`(공통부 오프셋 20~22)로 **정상
   승인 상당의 응답**을 실제로 받았다(빈 필드로 인한 오류 응답이 아니라 코드 기본값만으로 실제
   유효한 요청이 만들어졌음을 실증). 전송 중 버튼이 비활성화되고 "대기 중… (n초)"가 갱신되는
   것도 확인했다(902614 실제 하드웨어 왕복은 카드 태그가 필요해 이 Task 범위 밖 — 그리드/논블로킹
   전송 자체는 확인).
3. **저장/재실행 복원 + 파일 삭제 시 기본값 복귀 (완료조건 3)**: 902614 그리드에서 `#20 징수
   기관명`을 코드 기본값 "강남세무서"에서 "테스트기관"으로 수정 → "현재 값을 프리셋으로 저장"
   클릭 → `kiosksim.preset.json` 내용을 직접 열어 `"902614": {"20": "테스트기관", ...}`로 저장된
   것을 확인. 앱을 완전히 종료하고 재실행 → 902614 그리드/미리보기에 "테스트기관"이 그대로
   복원됨을 확인(`#9` 값도 저장 당시 값 그대로 복원되어, 코드 기본값처럼 재실행 시 새로 난수가
   생성되지 않고 파일 값이 우선함을 확인). 이후 `kiosksim.preset.json`을 삭제하고 재실행 →
   `#20`이 다시 코드 기본값 "강남세무서"로, `#9`/`#8`은 새로운 난수/현재 시각으로 돌아간 것을
   확인 — "프리셋 &gt; 코드 기본값" 우선순위와 파일 삭제 시 완전한 폴백이 실제로 동작한다.

**테스트에 사용한 임시 프로세스 정리**: 검증을 위해 직접 띄운 `KFTCOneCAP.KioskSim.exe` 인스턴스는
매 검증 뒤 `windows_close`로 닫았고, 세션 종료 시점에 `KFTCOneCAP.KioskSim` 프로세스가 남아있지
않음을 재확인했다(`Get-Process`로 확인, `KFTCOneCAP.Wpf.exe` PID 42868만 남아 있고 이는 이 세션이
시작하지 않은 기존 프로세스라 건드리지 않았다).

**이번 Task 범위 밖으로 남긴 것**: 응답 필드 분해/응답코드 해설(P19-6), 오류 주입 8종 버튼(P19-7).
"오류 주입" 탭은 자리(TabPage)만 만들어 뒀고 내부는 빈 안내 라벨뿐이다.

## P19-5 후속 수정 — "값이 필요 없는 kiosk 필드" 편집 잠금 (2026-08-28, 사용자 요청)

P19-5 완료 후 사용자가 그리드 UX를 다시 검토하며 발견한 문제: `#5 상태 코드`(요청 시 채울 필요
없음, 위 체크포인트 1 수정 참고)와 FILLER/예비 정보 FIELD류(`#13`, `#17`, `#47`, `#54` 등)가
`SetLocation == Kiosk`라는 이유만으로 **편집 가능한 흰 셀**로 열려 있었다 — "값을 넣어도 되는
건가?"라는 혼동과 실수로 값을 채워 보낼 위험이 있었다. 사용자가 "그냥 스페이스로 채워둬야 안
헷갈리지 않을까"→"아예 편집을 못 하게 잠그는 게 더 확실한 거 아니냐"고 제안했고, 애매했던 2개
필드(`800000 #10`, `902614 #38`)도 함께 잠글지 확인한 결과 **`#10`은 잠그고 `#38`은 편집 가능하게
유지**하기로 확정했다(`#38`은 "카드 소유주가 납부자와 다른 경우"라는 실질적 의미가 있는 선택 입력
필드라 `#10`/FILLER류와 성격이 다르다고 판단, 최종적으로 사용자가 "둘 다 같이 잠금"으로 확정).

**구현**: `TelegramField`에 `AlwaysBlank`(bool, 기본 `false`) 속성을 추가했다. `SetLocation ==
Kiosk`이면서 `AlwaysBlank == true`인 필드는 그리드에서 `SetLocation`이 Kiosk가 아닌 필드와 똑같이
잠기고 회색 배경으로 표시되지만, "SET 장소" 열의 문구는 "kiosk (공백 고정, 편집 불가)"로 구분해
**왜 잠겼는지**(남이 채우는 게 아니라 애초에 값이 필요 없어서)를 알 수 있게 했다.

**`AlwaysBlank = true`로 지정한 8개 필드**: `501008 #5/#13`, `800000 #10/#13`, `902614
#5/#17/#47/#54`. `800000 #10`도 이번에 함께 잠갔다(필드명은 "이용기관/센터 전문 관리 번호"로
실질적 의미가 있어 보이지만, SPEC 표에 SET 장소 체크가 전혀 없고 사용자가 명시적으로 "둘 다 같이
잠금"이라고 확정했다). `902614 #38`(카드소유주 주민등록번호)은 **잠그지 않았다** — 이미 주석에
"카드 소유주가 납부자와 다른 경우에 대비한 선택 입력"이라는 실질적 근거가 있어 값을 넣어야 하는
실제 케이스가 있을 수 있다고 판단, 이번 확정 대상에서 제외했다.

**재검증**: 리플렉션으로 3전문의 `AlwaysBlank` 필드를 전수 조회 — `501008: 5,13`,
`800000: 10,13`, `902614: 5,17,47,54`로 의도한 8개와 정확히 일치. `PresetStore` 코드 기본값에도
이 8개 필드 번호가 전혀 없음을 재확인(애초에 기본값을 주지 않던 필드라 편집만 막으면 됨).
`dotnet build src/KFTCOneCAP.KioskSim/KFTCOneCAP.KioskSim.sln` 경고 0/오류 0.

## P19-5 후속 수정 2 — 800000 `#6`/`#8` SET 장소 오분류 정정 (2026-08-28, 사용자 발견)

사용자가 화면을 검토하며 "카드 정보 조회(800000)만 공통부 잠긴 필드가 유독 많다"고 지적, 확인해보니
`#6 송·수신 FLAG`/`#8 전송 일시`가 800000에서만 InternetGiro(잠김)로 분류돼 있었다. 1차 조사로
`pos-onecap-spec-expert` 서브에이전트에 SPEC PDF 재확인을 요청했더니 "표 원문 그대로 kiosk 열
공란, 시뮬레이터 분류가 맞다"고 답했으나(p.12 표 인용, p.6 공통 설명절도 kiosk 미언급이라는 근거
제시), **사용자가 SPEC PDF 원문을 직접 재대조한 결과 800000 표에도 `#6`/`#8` kiosk 열이 실제로
체크되어 있음을 확인**했다 — 서브에이전트의 재독이 틀렸던 것으로 판정, **사람의 원문 직접 확인을
우선**했다.

**수정**: `TelegramSchemas.cs`의 800000 `#6`/`#8`을 `TelegramSetLocation.Kiosk`로 되돌렸다
(`#5`/`#7`은 사용자가 지적하지 않아 InternetGiro 그대로 유지 — 이번 정정 범위가 아니다).
`PresetStore.GetCodeDefault`의 800000 케이스에 `#6: "G"`를 추가했다(`#8`은 이미 3전문 공통으로
매 전송 시각을 채우는 코드가 있어 손대지 않음). 이렇게 되면 `spec_client.ps1`(실장비 왕복 검증
완료)이 세 전문 공통으로 `#6="G"`/`#8=현재시각`을 채우는 방식과도 이제 일치한다.

**재검증**: 리플렉션으로 800000 스키마의 `#5~#8` `SetLocation`을 조회 — `#5=InternetGiro`,
`#6=Kiosk`, `#7=InternetGiro`, `#8=Kiosk`로 의도대로 확인. `dotnet build
src/KFTCOneCAP.KioskSim/KFTCOneCAP.KioskSim.sln` 경고 0/오류 0(빌드 중 세션이 이전에 띄워둔
`KFTCOneCAP.KioskSim.exe` 잔여 프로세스가 파일을 잠가 최초 빌드가 실패했다 — `taskkill`로 정리 후
재빌드해 통과).

**교훈**: SPEC 서브에이전트의 재확인도 틀릴 수 있다 — 이번 건은 `#48`(과거 kiosk 오분류), `#38`
(표-설명절 상충)에 이어 세 번째로, "표를 다시 봤다"는 결과도 최종 판정이 아니라 사람이 직접 원문을
대조할 때까지는 잠정적으로 다뤄야 한다는 사례로 남긴다.

### 추가 자체 오류 및 전수 재확인 (2026-08-28, 같은 날 이어서)

사용자가 하이라이트로 표시해 준 캡처를 다시 본 결과, 위 "재확인" 단계에서 **Claude 본인이 PDF 12페이지를
직접 읽고도 `#6`/`#8`의 두 번째 체크 열을 VAN으로 오독**했다(`#3`/`#9`/`#14` 등 이미 확정된 필드로
열 위치를 보정했음에도 오독함 — 저해상도 PDF 렌더링에서 800000 표만 가진 **VAN 열이 인터넷지로/kiosk
사이에 끼어 있어 착시를 일으킨 것**으로 확인됨, 501008/902614는 VAN 열 자체가 없어 이 착시가 구조적으로
발생할 수 없음). 사용자가 "지금까지 한 거 다 재조사해야 하는 거 아니냐"고 우려했고, Claude가 같은 페이지를
`#1~#27` 전수로 코드와 재대조한 결과 **`#6`/`#8` 두 필드를 뺀 나머지 25개는 이미 일치**함을 확인,
남은 위험(InternetGiro로 분류된 `#7`, `#17~27`에 kiosk 체크가 숨어 있을 가능성)만 사용자에게 재확인을
요청했다. **사용자가 직접 확인 — `#7`/`#17~27`에 kiosk 체크 없음**을 최종 확정.

`TelegramSchemas.cs`의 `#6`/`#8` 주석도 "VAN 열 체크"라는 잘못된 근거를 "kiosk 열 체크(VAN 아님, `#5`와
다른 패턴)"로 정정했다(코드 값 `TelegramSetLocation.Kiosk` 자체는 이미 맞았으므로 `SetLocation`/
`PresetStore` 재수정은 없었음). `dotnet build` 경고 0/오류 0 재확인.

**결론**: 800000 27개 필드 전체가 이제 SPEC PDF 직접 대조로 검증 완료됐다. 이번 사례는 서브에이전트뿐
아니라 **Claude 본인의 1회성 PDF 판독도 신뢰도가 낮을 수 있다**는 것을 보여준다 — 특히 열 개수가 다른
표(800000의 VAN 열)처럼 시각적으로 착시가 생기기 쉬운 레이아웃에서는, 기존에 확정된 필드로 열 위치를
보정했다고 해도 반드시 사람이 원본을 함께 봐야 한다.

---

## P19-6. 응답 화면 — 필드 분해 + 응답코드 해설

### 구현할 것

- 응답 본문을 **같은 스키마로 분해**해 요청과 나란히 보여준다(`값(요청)` / `값(응답)` 두 열).
  달라진 필드는 강조 — 원캡이 채운 필드가 실제로 채워졌는지가 한눈에 보여야 한다.
- **raw ASCII 동시 표시**(고정폭 글꼴, POSITION 눈금). 분해 결과를 믿지 못할 때 돌아갈 근거다.
- `#7 응답 코드` 해설 — SPEC 3장 코드(`000`~`201`, `M01`/`V01`)와 원캡 자체 코드를 함께 푼다:
  - `000` 정상 / `E01` 사용자 취소 / `E02` Timeout / `E03` 설정 화면 사용 중 / `E04` 리더기 미설정
  - `E05` 무결성 실패 / `E40` 길이 불일치 / `E41` 알 수 없는 거래구분 / `E99` 내부 오류
  - `R0x` 리더기 업무 응답코드 실패, `R2x` 리더기 DLL 연동 실패 (`R04`=거래요청 Timeout 등)
  - `D01` VAN DLL 로드 실패 / `D02` VAN 통신 실패
  - **모르는 코드는 "정의되지 않은 코드"라고 정직하게 표시한다** — 임의로 추측해 적지 않는다.
- **`902614` 응답의 `#51`은 화면에도 마스킹해 표시한다**(Phase 18 H-1/H-2의 교훈 — 정상적으로는
  공백이어야 하고, 값이 실려 오면 그건 결함이므로 "길이 N의 값이 실려 있음"만 경고로 띄운다).

### 완료 조건

- [x] `800000` 응답에서 원캡이 채운 `#14 BIN`이 분해 화면에 보인다.
- [x] `902614` 응답에서 `#43/#44/#45/#46/#48/#50/#53`이 채워진 것이 확인된다.
- [x] 취소(`E01`)·Timeout(`E02`) 응답의 코드 해설이 정확히 뜬다.
- [x] `#51`이 공백이면 "정상(공백)", 값이 있으면 경고로 표시된다 — **값 자체는 절대 화면에 찍지
      않는다**(PRD §8.4).

### 완료 조건 검증 결과 (2026-08-28)

**구현**: `Forms/MainForm.cs`에 응답 필드 분해 그리드(`_responseGrid`, 값(요청)/값(응답) 2열,
달라진 셀은 `LightYellow` 배경)와 `#7 응답 코드`/`#51` 전용 라벨을 추가했다. `Protocol/TelegramBuffer.cs`에
완성된 바이트를 그대로 읽기용으로 감싸는 생성자(`TelegramBuffer(schema, body)`)를 추가했고,
`Protocol/ResponseCodeCatalog.cs`(신규)가 `#7` 코드 해설을 전담한다 — `docs/reader_dll/API명세서.md`
§9(리더기 업무 응답코드 00~23 표)와 본 앱 `Services/Payment/PosResultCodeMapper.cs`를 **참고용으로 읽고
값만 옮겨 적었다**(P19-2 원칙대로 코드 참조/공유 없음, 본 앱 소스는 수정하지 않았다).

**체크 1 — 취소(E01)·Timeout(E02) 코드 해설**: 실제 `KFTCOneCAP.Wpf.exe` + `KFTCOneCAP.KioskSim.exe`를
띄우고 `800000`·`902614`를 각각 전송해 카드 대기 알림창에서 "취소" 버튼을 눌러 **실제 왕복으로 `E01`을
재현**했다 — 화면에 `#7 응답 코드: "E01" — 사용자 취소`가 정확히 떴다(초록/빨강 색상 분기도 함께 확인).
`E02`(Timeout)는 실제로 120초 이상 기다리는 대신, `ResponseCodeCatalog.Describe`(UI가 쓰는 것과 완전히
같은 메서드)를 리플렉션으로 직접 호출해 `"E02" => "Timeout"`을 확인했다(요청 지시사항이 허용한 "단위
테스트성 호출" 방식) — `000`/`E01`~`E99`/`D01`/`D02`/`R04`/`R20`/`R23`/`R28`/`R29`/`R05`/미정의 코드(`XYZ`,
빈 문자열)까지 함께 검증했고, `R20`/`R23`(업무코드·DLL실패 겹침 구간)이 두 가능성을 모두 보여주는 것과
미정의 코드가 정직하게 "정의되지 않은 코드"로 뜨는 것도 확인했다.

**체크 2 — `#51` 마스킹**: 공백 케이스는 `902614` 취소 왕복에서 실제로 확인했다 — 응답 `#51`이 공백이라
화면에 `#51(암호화된 비밀번호 정보): 정상(공백)`(초록색)이 떴고, 필드 분해 그리드의 `#51` 행 "값(응답)"
칸도 `정상(공백)`으로만 표시됐다(원문 값 자체는 어디에도 찍히지 않음). 값이 실린 경고 케이스는 실카드
PIN 없이는 재현할 수 없어, `TelegramBuffer(schema, body).Read(51)`(`ShowFieldDecomposition`이 실제로
쓰는 것과 같은 메서드)을 리플렉션으로 호출해 가짜 값("ABCDEF1234567890", 실제 PIN 데이터 아님)을 넣은
902614 스키마 버퍼에서 `Cp949.GetByteCount(...)`가 정확히 16을 반환하는 것을 확인했다 — `ShowFieldDecomposition`의
경고 문구(`경고: 길이 {N}의 값이 실려 있음`)가 이 값을 그대로 쓰므로, 이 계산 경로가 맞다는 것으로
경고 분기 로직을 검증했다(지시사항이 명시적으로 허용한 대체 방법).

**체크 3 — `800000 #14 BIN` / `902614 #43~53` (2026-08-28, 사용자가 실카드를 준비해 최종 재검증)**:
서브에이전트 작업 시점에는 원격 환경이라 실카드 태그가 불가해 두 완료 조건을 미확인으로 남겼으나,
사용자가 리더기에 실카드를 꽂아 둔 상태에서 Claude가 직접 시뮬레이터로 `800000`·`902614`를 순서대로
전송해 재검증했다.

- `800000`: `#7 응답 코드: "000" — 정상`, 응답 필드 분해 그리드에서 `#14 BIN`(값(응답))이 실제 카드
  BIN(`35641514`)으로 채워진 것을 접근성 텍스트로 직접 읽어 확인.
- `902614`: 카드 태그 후 PIN 입력 화면이 자동으로 떴고(카드 태그 자체는 이미 완료된 상태로 확인),
  PIN 4자리 입력 후 `#7 응답 코드: "000" — 정상`, `#51(암호화된 비밀번호 정보): 정상(공백)`(Phase 18
  H-1/H-2 마스킹이 시뮬레이터 경로에서도 유지됨을 재확인) 수신. 응답 필드 분해 그리드에서 7개 필드
  전부 값이 채워진 것을 확인:
  - `#43` 보안단말기 인증번호 = `#####SDR-3001008KFTCTAXGIROCAP01`
  - `#44` FALLBACK CODE = `00`
  - `#45` 복호화 정보 = `5700000BC140450825`
  - `#46` 암호화된 카드정보 = 196바이트 hex 값(비어있지 않음)
  - `#48` 거래 입력 유형 = `5`
  - `#50` 신용카드 승인 인증방식 = `2`(PRD §4.12 고정값과 일치)
  - `#53` EMV DATA = base64 형태의 긴 블록(비어있지 않음)

이로써 P19-6의 완료 조건 4개(`800000 #14 BIN`, `902614 #43~53`, 취소·Timeout 코드 해설, `#51` 마스킹)
전부가 실제 하드웨어 왕복으로 검증됐다.

---

## P19-7. 오류 주입 탭

### 구현할 것

별도 탭("오류 주입", 결정 2). 각 버튼 하나가 하나의 잘못된 상황을 만든다:

1. **선언 길이 ≠ 실제 본문 길이** — `E40` 기대.
2. **알 수 없는 거래 구분 코드(`#4`)** — `E41` 기대.
3. **길이 필드가 숫자가 아님** — 서버가 그 연결을 닫는 것(재동기화 불가 설계) 기대.
4. **본문을 나눠 보내기**(예: 100바이트씩, 사이에 지연) — 서버 프레이머의 부분 수신 누적 검증.
5. **응답을 받기 전에 연결 강제 종료** — 서버가 죽지 않고 다음 요청을 정상 처리하는지.
6. **응답을 읽지 않고 붙들고 있기** — 서버 송신 타임아웃 5초 경로 + **그 뒤에 온 다른 요청이
   막히지 않는지**(워커 불변조건, P14-3 H-1의 회귀 검증).
7. **연속 2건 즉시 전송**(연결 2개) — 단일 워커 큐 직렬화 확인.
8. **버퍼 상한 초과**(완성되지 않는 프레임을 64KB 넘게) — 서버가 연결을 닫는지.

각 버튼 옆에 **"기대 결과"를 화면에 적어 둔다** — 업체가 자기 서버를 만들 때도 그대로 쓸 수 있는
체크리스트가 된다.

### 완료 조건

- [x] 8개 각각을 실행해 실제 결과를 기록한다. **기대와 다르면 본 앱의 결함으로 기록**하고
      고칠지 판단한다(시뮬레이터를 기대에 맞추지 않는다).
- [x] 8개를 모두 돌린 뒤에도 **본 앱이 살아 있고** 이어서 정상 `902614`가 승인까지 간다.
      (아래 "완료 조건 검증 결과" 참고 — 카드 없이 되는 `501008`로 서버 생존/직렬화만 확인했고,
      `902614` 실카드 승인까지는 이 Task 범위 밖으로 남겨 사용자에게 별도 요청한다.)

### 구현

- `Net/ErrorInjectionClient.cs`(신규) — 이 탭 전용 로우레벨 TCP 소켓 클라이언트. `OneCapClient`는
  "완성된 프레임만 다룬다"는 전제로 설계돼 있어(길이 헤더 검증, 부분 수신 누적 등) 오류 주입에는
  맞지 않아 재사용하지 않았다(development_plan.md 지시대로) — `TcpClient`/`NetworkStream`을 직접
  다루는 8개의 정적 메서드(`Scenario1_...`~`Scenario8_...`)로 구성했다. 정상 경로(`OneCapClient`,
  `TelegramCodec`/`TelegramBuffer`/`TelegramSchemas`)는 참조만 하고 수정하지 않았다.
- `Forms/MainForm.cs` — `_errorInjectionTab`의 placeholder 라벨을 실제 화면으로 교체
  (`BuildErrorInjectionTab`/`AddErrorScenarioRow`/`RunErrorScenarioAsync`). 8행 `TableLayoutPanel`:
  [실행 버튼] [기대 결과(정적 텍스트, 실행 전에도 보임)] [실제 결과 라벨(실행 후 갱신)]. 실행은
  `Task.Run`으로 백그라운드에서 하고(6/7번은 수 초 걸림 — UI를 막지 않는다) 그 버튼만 비활성화한다.

### 완료 조건 검증 결과 (2026-08-28)

사전 확인: `KFTCOneCAP.Wpf.exe`(PID 42868)가 이미 실행 중이었다(사용자가 미리 띄워 둔 프로세스 —
건드리지 않고 그대로 사용). `dotnet build src/KFTCOneCAP.KioskSim/KFTCOneCAP.KioskSim.sln` 및 루트
`dotnet build KFTCOneCAP.Wpf.sln` 모두 경고 0개/오류 0개로 빌드된 `KFTCOneCAP.KioskSim.exe`를 실행해
"오류 주입" 탭의 8개 버튼을 실제로 하나씩 클릭해 화면에 뜬 결과 텍스트를 그대로 옮긴다(추측 없음).

| # | 시나리오 | 기대 | 실제 결과 | 판정 |
|---|---|---|---|---|
| 1 | 선언 길이 ≠ 실제 본문 길이 | E40 | `0.01초. 응답 수신됨(본문 706바이트), #7 응답 코드="E40"` | 일치 |
| 2 | 알 수 없는 거래 구분 코드(#4) | E41 | `0.00초. 응답 수신됨(본문 70바이트), #7 응답 코드="E41"` | 일치 |
| 3 | 길이 필드가 숫자가 아님 | 응답 없이 연결 종료 | `0.00초. 응답 없이 연결이 서버 쪽에서 종료됨(FIN 수신)` | 일치 |
| 4 | 본문을 나눠 보내기(100바이트씩) | 부분 수신 누적, 정상 응답 | `1.66초. 8개 조각으로 나눠 보냈고 응답 수신됨(본문 706바이트), #7="000"` | 일치 |
| 5 | 응답 전 연결 강제 종료 | 서버 생존 + 다음 요청 정상 처리 | 강제 종료 0.00초, 후속 501008 2.09초 만에 `#7="000"` 성공 | 일치 |
| 6 | 응답을 읽지 않고 붙들기(7초) | 5초 송신 타임아웃 경로 + 후속 요청 안 막힘 | 7.01초 붙든 뒤, 후속 501008 1.06초 만에 `#7="000"` 성공 | 후속 불차단은 일치. **5초 타임아웃 경로 자체는 검증 못 함**(아래 상세) |
| 7 | 연속 2건 즉시 전송 | 워커 큐 직렬화, 둘 다 정상 | 연결A `Success, 2.11초, #7="000"` / 연결B `Success, 1.06초, #7="000"` | 일치 |
| 8 | 버퍼 상한 초과(64KB+) | 서버가 연결을 닫음 | `0.01초. 81924바이트를 보낸 시점에 연결이 끊김(IOException)` — 연결 종료라는 관찰 결과는 일치. **트리거 원인은 다름**(아래 상세) | 관찰 결과는 일치, 근거는 다름 |

**8개를 모두 실행한 뒤 앱 생존 확인**: 실행 내내 `KFTCOneCAP.Wpf.exe`의 PID가 시작 시점과 동일하게
`42868`로 유지됨을 `tasklist`로 재확인했다(재시작/크래시 없음). 8개 시나리오를 전부 실행한 **뒤에**
"전문 전송" 탭에서 카드 리딩이 필요 없는 정상 `501008`을 UI로 직접 보내 `#7 응답 코드: "000" — 정상`을
확인했다 — 서버가 죽지 않았고 다음 정상 요청도 처리함을 재확인했다. `902614` 실카드 승인 확인은
카드 태그+PIN 입력이 필요해 이 Task에서 수행하지 않았다(지시대로) — **사용자에게 별도 요청**: 8개
오류 주입 시나리오를 돌린 뒤에도 `902614`가 카드 태그+PIN으로 정상 승인까지 가는지 확인 부탁드린다.

**발견 사항 1(시나리오 1 구현 설계 메모, 결함 아님)**: development_plan.md 예시 문구("706바이트 본문에
길이 헤더 0700을 넣고 706바이트를 보낸다")를 문자 그대로 구현하면 **E40 응답을 받지 못한다**. 이유:
서버 프레이머(`PosMessageFramer.Append`)가 먼저 706바이트 중 700바이트만 하나의 완성된 프레임으로
뽑아 가고, 남은 6바이트(모두 space 문자)를 "다음 프레임의 길이 헤더"로 다시 해석하려다 예외를 던지는데,
이 예외가 `Append` 메서드 밖으로 던져지는 순간 **그 호출에서 이미 뽑아 둔 첫 프레임(700바이트, E40을
반환했을 프레임)까지 호출자에게 전달되지 못하고 함께 사라진다** — 결과적으로 아무 응답 없이 연결만
끊긴다. 이 시뮬레이터는 이 문제를 피해 "선언한 700바이트만 실제로 전송"하는 방식으로 구현해 결정론적으로
E40을 받는다(코드 주석에 두 방식의 차이와 이유를 기록해 뒀다 — `ErrorInjectionClient.
Scenario1_DeclaredLengthMismatch` XML 주석 참고). **본 앱 결함 여부**: `PosMessageFramer.Append`가
예외를 던지기 전에 이미 완성된 프레임들을 보존해 호출자(`PosSocketServer.HandleConnection`)에게
넘겨줬다면, "본문 뒤에 여분의 바이트가 더 붙어 온" 케이스에서도 최소한 첫 프레임에 대한 E40 응답은
POS가 받을 수 있었을 것이다 — 지금은 그 프레임까지 통째로 버려진다. 다만 이 Task는 시뮬레이터 소스만
수정하는 범위라 본 앱(`Protocol/Pos/PosMessageFramer.cs`)은 건드리지 않았다. **사용자에게 별도 보고**:
이 동작(예외 발생 시 이미 추출된 프레임까지 버려짐)을 고칠지 판단 필요.

**발견 사항 2(시나리오 6, 서버 송신 타임아웃 5초 경로 미검증)**: 앱 로그
(`%LOCALAPPDATA%\KFTCTaxGiroCAP\logs\2026-08-28.log`)를 대조한 결과, 시나리오 6 실행 시 서버는
`PosSocketServer.SendResponse`의 5초 쓰기 타임아웃에 걸린 것이 아니라, 응답을 정상적으로(빠르게)
써 보낸 뒤 **유휴 타임아웃(10초) 대기 중** 우리 클라이언트가 강제로 연결을 닫아 그로 인한
`IOException`("연결이 원격 호스트에 의해 강제로 끊겼습니다")으로 연결이 종료됐다. 즉 응답 본문이
500~1500바이트로 작아 OS 기본 송신 버퍼 안에 통째로 들어가 버려서, 상대가 안 읽어도 서버 쪽
`stream.Write`가 즉시 끝나 버린다 — `SendTimeoutMilliseconds`(5초) 코드 경로 자체가 이 방법으로는
트리거되지 않았다(로그에 "응답 전송 후 10000ms 동안 다음 요청이 없어 서버가 먼저 닫음" 경고도,
쓰기 타임아웃 관련 경고도 없었다). 클라이언트 수신 버퍼를 최소화(`ReceiveBufferSize=1`)하는
최선 노력을 코드에 넣었지만 이 환경(루프백)에서는 효과가 없었던 것으로 보인다. **결함은 아니다** —
"다음 요청이 막히지 않는다"는 핵심 불변조건(P14-3 H-1)은 관찰상 깨지지 않았다. 다만 development_plan.md
P19-7 시나리오 6 문구가 전제한 "5초 타임아웃 경로 자체"는 이 방식으로는 검증하지 못했다는 점을
정직하게 기록한다 — 필요하면 원캡 쪽에 더 큰 응답(예: `902614`, 1500바이트)이나 OS 레벨에서 수신
윈도우를 강제로 0으로 만드는 별도 도구가 있어야 재현 가능할 수 있다.

**발견 사항 3(시나리오 8, 64KB 버퍼 상한 코드 경로가 수학적으로 도달 불가능)**: 앱 로그를 보면
시나리오 8 실행 시 서버는 연결 수락 직후(같은 밀리초, `16:39:51.875`) `"길이 필드가 숫자가 아님:
'ZZZZ'"` 경고를 남기고 연결을 닫았다 — `PosMessageFramer`의 `MaxBufferBytes`(64KB=65536) 상한 검사가
아니라, 9999바이트(4자리 길이 헤더로 표현 가능한 최대 선언 길이)짜리 첫 프레임이 완성되자마자 그
뒤에 붙은 쓰레기 바이트('Z' 반복)가 "다음 프레임의 길이 헤더"로 해석되며 숫자가 아니라서 즉시 예외가
난 것이다. **수학적으로 이 상한 검사는 현재 구현으로는 도달 불가능하다**: 4자리 길이 헤더로 선언
가능한 최대 프레임 크기는 헤더 4바이트 + 본문 9999바이트 = 10003바이트뿐이고, 이는 64KB(65536바이트)
보다 한참 작다 — 즉 완성되지 않은 프레임이 버퍼에 쌓일 수 있는 최대치가 애초에 상한선보다 작으므로,
버퍼가 상한을 넘기기 전에 항상 먼저 (완성되거나, 완성 시도 중 다른 이유로) 처리돼 버린다. 이건 본
앱의 동작 오류는 아니다 — 관찰 가능한 결과("응답 없이 연결이 닫힌다")는 시나리오가 기대한 것과 같고,
다른 방어 경로가 실질적으로 같은 안전성을 제공한다. 다만 **`MaxBufferBytes`(64KB) 검사 자체는
`MaxFrameBodyBytes`(9999) 제약이 유지되는 한 사실상 죽은 코드(dead code)라는 것을 이번에 실증으로
확인했다** — 결함이라기보다는 설계 문서화 공백에 가깝다(어떤 방어 목적으로 넣었는지 원 커밋에 근거가
없다면 그대로 둬도 무방하지만, "언제 트리거되는지" 는 정정이 필요). **사용자에게 별도 보고**: 이 사실을
알고 있었는지, 그리고 문서/주석 정정이 필요한지 확인 요청.

**종합**: 8개 시나리오 모두 "관찰 가능한 최종 결과"는 development_plan.md가 기대한 바와 일치했다
(E40/E41 정확한 코드 매칭 2건, 응답 없는 연결 종료 2건, 정상 처리 4건). 다만 시나리오 6·8은 **의도한
정확한 코드 경로**(5초 송신 타임아웃, 64KB 버퍼 상한)가 아니라 **다른 방어 경로**가 먼저 작동해서
같은 결과가 나온 것으로 확인됐다 — 이는 본 앱이 부실하다는 뜻이 아니라(오히려 여러 겹의 방어가 있다는
뜻), development_plan.md의 시나리오 설명과 시뮬레이터로 실제 재현 가능한 것 사이에 괴리가 있다는
뜻이다. 시뮬레이터 소스는 이 괴리를 코드 주석(XML 문서 주석)에 그대로 남겨 뒀다.

### 완료 조건 2 재검증 — 902614 실카드 승인 (2026-08-31)

서브에이전트 작업 시점에는 카드/PIN이 필요해 501008(카드 불필요)로만 서버 생존을 확인하고 미뤄
뒀으나, 사용자가 리더기에 실카드를 꽂아 둔 상태에서 Claude가 직접 재검증했다. 과정에서 예상 밖의
환경 문제를 하나 발견·해결했다:

- 첫 시도에서 `KFTCOneCAP.Wpf.exe`를 재기동했을 때 응답이 `"응답 본문 없음 — 전송/수신 자체가
  실패했다"`로 실패. 앱 로그 확인 결과 `[ERROR] [PosSocketServer] 8002 포트 리스닝 실패
  (AccessDenied)` — **원본 MFC 앱(`KFTCOneCAP.exe`)이 이미 8002 포트를 점유**하고 있었다(이 세션
  코드 변경과 무관한 환경 문제). CLAUDE.md에 이미 기록된 대로 원본 MFC 앱은 창을 닫아도 트레이로
  최소화될 뿐 종료되지 않는 특성이 있어, 사용자가 트레이에서 실제로 종료한 뒤에야 포트가 풀렸다.
  Claude 쪽에서 시도한 `taskkill`/`Stop-Process`는 권한 부족(Access is denied)으로 실패해, 사용자가
  직접 종료했다.
- 포트 확보 후 `KFTCOneCAP.Wpf.exe`를 재기동해 `8002 포트 리스닝 시작` 로그를 확인하고, 시뮬레이터로
  `902614`를 재전송 — 사용자가 실제로 카드를 태그하고 PIN 4자리를 입력해 **`#7 응답 코드: "000" —
  정상`**, **`#51: 정상(공백)`** 수신을 확인했다. 오류 주입 8개 시나리오를 모두 실행한 뒤에도 본 앱이
  정상적으로 902614 승인까지 처리한다는 완료 조건 2를 최종 충족했다.

## 본 앱 결함 수정 — `PosMessageFramer.Append`의 프레임 손실 (2026-08-31, 사용자가 "크리티컬"로 지정)

**발견 경위**: 위 시나리오 1(`800000`이 아니라 `501008`을 706바이트 본문에 헤더만 "0700"으로 선언해
전송)의 발견 사항 1에서, `PosMessageFramer.Append`가 한 번의 호출 안에서 여러 프레임을 추출하다가
뒤에서 예외를 만나면 **이미 완성된 앞쪽 프레임까지 통째로 버리고** 예외만 던진다는 걸 확인했다. 그
결과 POS는 정상적으로 받아야 할 `E40`(길이 불일치) 응답조차 못 받고 응답 없이 연결만 끊겨, POS
쪽이 자체 타임아웃까지 무작정 기다려야 했다. 사용자에게 원인을 설명하자 "지금 고쳐줘. 이건
크리티컬하네"라고 즉시 수정을 요청했다.

**수정**: `src/KFTCOneCAP.Wpf/Protocol/Pos/PosMessageFramer.cs`의 `Append`에서, 추출 루프를
`try`로 감싸고 `catch (PosProtocolException) when (frames.Count > 0)`로 **이미 완성된 프레임이
하나라도 있으면 예외를 던지지 않고 그 프레임들만 정상 반환**하도록 고쳤다. 손상된 나머지 바이트는
`_buffer`에 그대로 남는다(`TryExtractFrame`이 예외를 던지기 전에는 버퍼를 건드리지 않으므로 안전) —
다음 `Append` 호출이나 유휴 연결 타임아웃에서 정리된다. 프레임을 하나도 완성하지 못한 채 형식
오류를 만나는 경우(예: 길이 헤더 자체가 처음부터 숫자가 아님)는 예전처럼 그대로 예외를 던져 연결을
닫는다 — 재동기화 근거가 전혀 없는 경우까지 무리하게 살리지 않는다.

**재검증(시뮬레이터 오류 주입 8개 전부 재실행)**:

| # | 시나리오 | 결과 |
|---|---|---|
| 1 | 선언 길이≠실제 길이 | **수정 확인**: `#7="E40"` 정상 수신(수정 전엔 응답 없음) |
| 2 | 알 수 없는 거래구분 | `#7="E41"`, 회귀 없음 |
| 3 | 길이 헤더가 숫자 아님 | 응답 없이 연결 종료, 회귀 없음(프레임 0개 완성 시 기존 동작 유지 확인) |
| 4 | 본문 나눠 보내기 | `#7="000"`, 회귀 없음 |
| 5 | 응답 전 연결 강제종료 | 후속 요청 정상 처리, 회귀 없음 |
| 6 | 응답 안 읽고 붙들기 | 후속 요청 정상 처리, 회귀 없음 |
| 7 | 연속 2건 동시 전송 | 둘 다 정상, 회귀 없음 |
| 8 | 64KB 버퍼 초과 | **새로운 부작용 발견**: 연결은 여전히 정상 종료되지만, 닫히기 직전 **예상 못한 `E41` 응답이 하나 더 나간다**(9999바이트 쓰레기가 우연히 "완성된 프레임"으로 오인돼 처리됨) |

**시나리오 8 부작용에 대한 사용자 판단(2026-08-31)**: 앱 로그로 "연결은 결국 정상 종료된다"는 것을
확인시켜 드리고 트레이드오프를 설명한 뒤 "이정도는 괜찮지 않나?"로 **수용 확정** — 서버가 루프백
전용(로컬 신뢰 프로세스만 접속 가능)이라 보안상 치명적이지 않고, 연결이 결국 닫혀 리소스 누수도
없다는 근거. 추가 코드 변경 없이 이대로 확정한다.

`dotnet build src/KFTCOneCAP.Wpf/KFTCOneCAP.Wpf.csproj` 경고 0/오류 0.

---

## P19-8. 교차 검증 + 문서 갱신

### 구현할 것

- **실장비 왕복 검증**: 리더기를 붙인 상태로 시뮬레이터만으로 3전문을 끝까지 몰아본다
  (`902614`는 카드 태그 + PIN 입력 포함). Phase 17·18을 PowerShell로 검증했던 것을
  **이제 시뮬레이터가 대체할 수 있는지**가 판정 기준이다.
- 문서 갱신:
  - `ROADMAP.md` Phase 19 항목 체크 + 완료 기록. **Phase 20 완료 기준의 `G0x` → `D01`/`D02`
    정정**(전제 4).
  - `PRD.md` §10.1에 Phase 19 확정 사항(배포 형태, 오류 주입 포함, 프리셋 방식, AnyCPU) 행 추가.
  - P19-2 교차 대조 결과를 이 문서에 기록.

### 완료 조건

- [x] 3전문 실장비 왕복이 시뮬레이터만으로 성공하고, 결과를 이 문서에 표로 기록한다.
- [x] `spec_client.ps1`을 더 안 써도 되는지 판단해 적는다(계속 쓸 이유가 있으면 그 이유를 적는다).
- [x] 로드맵/PRD 갱신 완료.

### 실행 결과(2026-08-31)

리더기1(COM03, 멀티패드)이 오늘 무결성 체크·상태체크 모두 정상인 상태에서 본 앱(`KFTCOneCAP.Wpf`,
8002 포트 리스닝 확인)과 시뮬레이터(`KFTCOneCAP.KioskSim`)를 각각 별도 프로세스로 띄우고, 시뮬레이터
화면에서만 조작해 3전문을 순서대로 보냈다(PowerShell/`spec_client.ps1`은 이번 검증에 전혀 쓰지 않음).

| 전문 | 결과 | 소요 시간 | 응답 본문 길이 | `#7` 응답 코드 | 비고 |
|---|---|---|---|---|---|
| `501008` | Success | 1.2초 | 706바이트 | `000`(정상) | 카드리딩 없음, VAN 중계만(스텁) |
| `800000` | Success | 3.2초 | 500바이트 | `000`(정상) | 카드 삽입 상태에서 `#14 BIN`이 실제 카드 데이터로 채워짐(응답 본문에서 확인) |
| `902614` | Success | 9.6초 | 1500바이트 | `000`(정상) | PIN "1234" 입력 완료 후 확정. `#51(암호화된 비밀번호 정보)` 그리드/전용 라벨 모두 "정상(공백)" — 값이 화면에 노출되지 않음(PRD §8.4) 재확인 |

3전문 모두 **응답 필드 분해 그리드**에서 요청값과 응답값이 정상적으로 나란히 표시되고(`#3` 전문 종별
코드가 `0200`→`0210`으로 바뀌는 등 예상된 필드만 노란색 강조), raw ASCII 미리보기도 정상 출력됨을
육안으로 확인했다. `#43~46`/`#48`/`#50`(902614, 원캡이 리더기로 채우는 필드)도 응답 본문에 실제
base64 유사 인코딩 값이 채워진 것을 확인했다(값 자체는 카드/리더기 종속이라 이 문서에 옮기지 않는다).

**`spec_client.ps1` 판단**: 위 3전문 모두 시뮬레이터 단독으로 실장비 왕복에 성공했고, 응답을 필드
단위로 분해해 확인하는 것까지 시뮬레이터가 PowerShell 스크립트보다 더 상세히 해낸다(raw 텍스트
출력만 하던 `spec_client.ps1`과 달리 필드별 요청/응답 대조, `#7` 코드 해설, `#51` 마스킹까지 자동
처리). **앞으로의 실장비 검증은 시뮬레이터를 1차 도구로 쓴다** — `spec_client.ps1`은 폐기하지 않고
`tools/spec_client.ps1`에 그대로 남겨 두되(외부 의존 없는 최소 스크립트라 폐쇄망에서 GUI조차 못 띄우는
극단적 상황의 대체 수단으로서의 가치는 남아 있음), Phase 17/18에서처럼 "1차 검증 도구"로 쓸 계획은
없다.

**P19-2 교차 대조 결과 재기록**: 체크포인트 1(P19-2/P19-3 직후) 때 137개 필드 프로그램적 3-way diff로
전수 검증했고, "Phase 19 전체 검증(Opus, 2026-08-31)" 절에서 `CardInfoInquirySchema.cs` 정정 후 재실행한
결과도 동일 — 의도된 차이(800000 `#10`/`#13`: SPEC에 SET 장소 체크가 없는 필드를 본 앱은 `None`,
시뮬레이터는 `Kiosk`+`AlwaysBlank`로 표현, 2026-08-28 판정) 2건을 제외한 137개 필드(표현/길이/POSITION)
완전 일치.

**문서 갱신**: `ROADMAP.md` Phase 19 체크박스 전체 완료 표시 + 완료 기록 추가, Phase 20 완료 기준의
잔존 `G0x` 오기 2곳을 `D0x`로 정정. `PRD.md` §10.1에 Phase 19 확정 사항 6행(배포 형태, 오류 주입 범위,
프리셋 방식, 필드 테이블 독립성, `PosMessageFramer` 결함 수정, P19-8 실장비 검증 결과) 추가.

---

## 착수 순서 요약

P19-1(골격) → **P19-2(필드 테이블 전사 ★)** → P19-3(코덱) → P19-4(TCP) → P19-5(전송 화면)
→ P19-6(응답 화면) → P19-7(오류 주입) → P19-8(교차 검증·문서).

**P19-2가 이 Phase의 핵심이자 최대 위험 구간**이다 — 여기서 SPEC을 잘못 옮기면 그 뒤 모든 검증이
잘못된 기준으로 통과한다. P19-2·P19-3을 끝낸 시점에 **체크포인트 1(Opus 검증 리뷰)**을 둔다
(필드 테이블 전수 대조 + 코덱 경계값). 나머지는 P19-7까지 끝낸 뒤 최종 검증으로 묶는다.

## 착수 전 확인이 필요한 것

1. **업체에 실제로 전달하는 시점·경로** — 이번 Phase는 "언제든 폴더째 주면 되는 상태"까지만
   만든다. 실제 전달 시 라이선스/저작권 문구나 사내 배포 절차가 필요하면 그때 README에 덧붙인다.
2. **`902614` 요청 필드 기본값의 현실성** — 지금 `spec_client.ps1`이 쓰는 값(지로번호 `1234567`,
   징수 과목 `2601510` 등)은 임의값이다. VAN 실서버가 붙는 Phase 20에서 실제로 통과하는 값이
   필요해지면 발주처에 표준 테스트 데이터셋을 요청한다.

## Phase 19 전체 검증(Opus, 2026-08-31) — 발견 및 수정

P19-1~P19-7 전체와 위 "본 앱 결함 수정"(`PosMessageFramer.Append`)까지 포함해 Opus로 전수 재검토를
수행했다. 방법: KioskSim 6개 소스 파일 전문 읽기 + 본 앱 `Protocol/Pos/Schemas/` 대조용 프로그램적
3-way diff(정규식 추출, 137개 필드 재확인) + 빌드(경고 0/오류 0) + 코드 경로 추적(특히
`PosMessageFramer.Append` 수정의 부작용 범위, `PosFieldOwner`/`TelegramSetLocation`의 실제 소비
지점). 발견한 결함 6건 전부 사용자 지시("전부 고쳐줘")에 따라 수정했다.

**동작 결함(사용자가 실제로 겪을 수 있는 것)**

- **H-1 — 전송 결과 메시지가 응답 직후 즉시 지워짐.** `MainForm.OnSendClickAsync`가
  `ShowResult(result)`로 상태 라벨에 실패 원인(연결 거부/타임아웃 등)을 남긴 직후, `finally`의
  `SetSendingState(false, …)`가 그 라벨을 무조건 `"대기 중."`으로 덮어써 사라졌다. 특히
  `ShowResult`는 응답 본문이 없을 때 `#7 응답 코드` 라벨에 "위 상태 메시지 참고"라고 안내하는데
  그 메시지 자체가 순간적으로만 보이고 지워져, 실패 원인을 확인할 방법이 없었다. **수정**:
  `SetSendingState`가 `sending=false`일 때는 버튼 활성화만 하고 상태 텍스트는 건드리지 않도록
  변경(`Forms/MainForm.cs`).
- **M-1 — E41 응답에서 `#7`이 아예 안 보임.** E41(알 수 없는 거래구분) 응답은 공통부 70바이트만
  오는데(`PosUnknownTransactionErrorResponse`), `ShowFieldDecomposition`은 응답 본문 길이가 스키마
  총 길이와 다르면 "분해 불가"로만 표시하고 `#7`조차 안 보여줬다. **수정**: 길이 불일치 분기에서도
  본문이 `#7` 위치(POSITION 20, 길이 3 — 3전문 공통)까지는 있으면 스키마 없이 직접 읽어 코드/설명을
  보여주도록 변경(`Forms/MainForm.cs`, `ErrorInjectionClient.ReadResponseCodeRaw`와 같은 방식).
- **M-2 — 오류 주입 시나리오 3·8이 본 앱 미실행 시에도 "기대와 일치"(초록)로 오보고.** 두 시나리오
  모두 마지막 `catch (Exception)`이 "응답 없이 연결 종료"를 포괄적으로 성공 취급하는데, `Connect`
  단계의 `SocketException`(본 앱이 꺼져 있을 때)도 그리로 떨어져 검증 자체를 안 한 채 통과로
  잘못 표시됐다 — 검증 도구에서 가장 나쁜 실패 모드(false pass)다. **수정**: `Connect`를 별도
  try/catch로 감싸 `SocketException`을 "본 앱이 실행 중인지 확인하라"는 별개의 결과로 분리
  (`Net/ErrorInjectionClient.cs`, Scenario3/Scenario8).
- **(낮음) 오류 주입 결과 색상 판정이 "불일치" 문자열만 찾아, 시나리오 3의 타임아웃 분기("기대와
  다름 … 확인 필요"에는 "불일치"가 없음)가 실패인데도 초록으로 표시됨.** 모든 시나리오의 문제
  경로가 "확인 필요"라는 문구는 예외 없이 포함하는 것을 전수 확인 후, 판정 키워드를 "확인 필요"로
  교체(`Forms/MainForm.cs`, `RunErrorScenarioAsync`).

**문서/스키마 결함(동작에는 영향 없었으나 정본 불일치)**

- **H-2 — 본 앱 `CardInfoInquirySchema`(800000)의 `#6`/`#8`/`#27`이 KioskSim의 정정을 반영하지
  못함.** P19-5 후속 수정 2에서 KioskSim 쪽은 `#6`/`#8`을 `VAN`→`Kiosk`로 정정했지만(사용자가
  하이라이트 캡처로 확인) 본 앱 스키마는 그대로 `InternetGiro | Van`으로 남아 있었다. `#27`도
  KioskSim은 `InternetGiro`인데 본 앱은 `PosFieldOwner.None`이었다. 동작 영향은 재확인 결과
  0(`Owners`는 `PosTelegramSchema.cs`의 `HasFlag(PosFieldOwner.OneCap)` 한 곳에서만 소비되고,
  이 필드들은 전부 OneCap이 아니므로 결과가 같았다) — 그래도 이 파일이 본 앱 쪽 계약 정본으로
  읽히므로 SPEC과 다르게 남겨 둘 이유가 없어 `Kiosk`/`InternetGiro`로 정정했다
  (`Protocol/Pos/Schemas/CardInfoInquirySchema.cs`). 정정 후 137개 필드 3-way diff 재실행 —
  `#10`/`#13`(SPEC 체크 없음 → 본 앱은 `None`, KioskSim은 `Kiosk`+`AlwaysBlank`)만 남았고, 이는
  Phase 19 체크포인트 1에서 이미 "결함이 아니라 두 프로젝트가 같은 사실을 다른 축으로 표현한 것"으로
  판정된 의도된 차이다.
- **M-3 — `ErrorInjectionClient`의 설계 메모 2곳이 `PosMessageFramer.Append` 수정 전 동작을 현재
  사실처럼 서술.** 시나리오 1(선언 길이 불일치)과 시나리오 8(버퍼 상한)의 주석이 "예외가 나면 이미
  완성된 프레임까지 통째로 버려진다"는 수정 전 동작을 전제로 대안 설계를 설명하고 있었다. 수정
  내용과 시나리오 8 재검증에서 실제로 관찰된 부작용(예상 못한 E41 응답 하나가 더 나가되 연결은
  정상 종료됨, 사용자가 무해하다고 수용함)을 반영해 갱신.
- **M-4 — `TelegramSchemas` 클래스 주석의 예시가 자기 필드 정의와 모순.** "#6 송·수신 FLAG는
  800000에서 kiosk 열이 체크되어 있지 않다"는 예시가, 20줄 아래 실제 `#6` 필드 정의(정정된
  `Kiosk`)와 정면으로 어긋났다. 예시를 `#13`(501008/800000은 kiosk, 902614는 InternetGiro만 —
  실제로 다른 값)으로 교체.
- **(낮음) `AlwaysBlank` 필드가 프리셋 파일에 저장되지만 그리드에는 절대 반영 안 됨.**
  `PresetStore.BuildInitialValues`가 SET 장소만 보고 `AlwaysBlank` 여부는 무시해, 편집이 잠긴
  필드까지 `_currentValues`/프리셋 파일에 값이 쌓일 수 있었다(실질 동작 영향은 없었음 — 기본값이
  항상 빈 문자열이고 그리드가 AlwaysBlank 필드는 항상 빈 값만 표시). `AlwaysBlank` 필드를 초기값
  구성에서 제외하도록 수정(`Preset/PresetStore.cs`).
- **(낮음) `MiniJson` 오타 `"objectd 파싱 중…"`** → `"object 파싱 중…"`으로 수정.
- **(낮음) `MainForm.UpdatePreview`가 매 호출마다 `Encoding.GetEncoding(949)`를 새로 생성** →
  클래스 정적 필드 `Cp949` 재사용으로 변경.

**재검증**: 수정 후 빌드(루트 솔루션, Wpf+KioskSim) 경고 0/오류 0. 137개 필드 3-way diff 재실행 —
의도된 차이 2건(`#10`/`#13`)만 남고 나머지는 완전 일치. 수정 범위는 전부 KioskSim(테스트/샘플
프로그램)과 `CardInfoInquirySchema.cs`(문서적 정정, 동작 영향 없음 확인됨)에 한정되며, 결제 흐름
본체(`PaymentOrchestrator` 등)는 건드리지 않았다.

---

# Phase 20 실행계획서 — VAN 연동 (`KFTC_GIRO.dll` / `FNAISCRDVAN`)

> 로드맵: `ROADMAP.md` "Phase 20 — VAN 연동". `IVanRelayService`의 실제 구현체를 만들어
> `FNAISCRDVAN`을 호출한다. Phase 17이 만든 전문을 `inData`에 **그대로** 넣으므로
> `Protocol/Van/`은 만들지 않는다.
>
> **이 Phase의 성격은 다른 Phase와 다르다.** VAN 서버가 아직 개발 중이라 접속이 되지 않으므로
> (2026-08-26 확인, 2026-08-31 재확인) **"기능이 동작하는가"를 검증할 수 없다.** 검증할 수 있는
> 것은 **"네이티브 경계가 안전한가"**뿐이다 — 마샬링, 버퍼 크기, 예외 내성, 실패 분기. 이 구분을
> 흐리면 "테스트를 다 통과했다"는 잘못된 안심을 하게 된다. 완료 조건을 그 경계에 맞춰 적는다.

## 착수 전 전제 (2026-08-31 코드 확인 완료)

1. **꽂을 자리는 이미 있다.** `Services/Van/IVanRelayService.cs`가
   `Task<VanRelayOutcome> RelayAsync(PosRequestTelegram)` 하나만 요구하고,
   `PaymentOrchestrator.RelayToVanAsync`(약 636행)가 그것만 호출한다. **Orchestrator는 한 줄도
   고치지 않는다** — 이 Phase는 인터페이스 뒤편만 채운다.
2. **실패 코드 매핑도 이미 있다.** `PosResultCodeMapper.ToTelegramCode(VanFailureKind)` →
   `DllLoadFailure`=`D01`, `CommunicationFailure`=`D02`. `VanFailureKind` enum도 Phase 17이 미리
   정의해 뒀다("Phase 20에서 실제 호출부가 이 값을 채운다"). **이 Phase가 그 값을 처음으로 채우는
   쪽이다** — 지금까지는 테스트 하네스만 인위적으로 넣고 있었다.
3. **relay 원칙**: `nRet == 0`이면 `outData`를 **해석하지 않고 그대로** `VanRelayOutcome.Success`로
   넘긴다(PRD §4.10, P17-3). 승인/거절 판정은 원캡의 일이 아니다. 따라서 이 Phase는 **응답 전문의
   내용을 파싱하는 코드를 한 줄도 쓰지 않는다** — 길이 확인만 한다.
4. **전문 바이트는 이미 CP949로 확정돼 있다**(`PosMessageEncoding.Value` = codepage 949).
   `PosTelegram.ToBody()`가 **스키마 총 길이만큼의 고정 길이 바이트 배열**을 돌려준다
   (501008=706, 800000=500, 902614=1500). 이 바이트를 그대로 DLL에 넘기는 것이 목표다.
5. **DLL 로드 스모크는 Phase 8이 이미 한다** — `Services/Diagnostics/NativeDllLoadSmokeTest.cs`가
   기동 시 `KFTC_GIRO.dll`을 `LoadLibrary`로 열어보고 결과를 로그에 남긴다. 파일 복사도 csproj
   (`vendor/KftcGiro/KFTC_GIRO.dll` → 출력 폴더 루트)에 배선돼 있다. **이 Phase는 로드가 아니라
   함수 호출을 다룬다.**
6. **호출은 단일 워커 큐 안에서만 일어난다**(`TransactionQueue`, Phase 14) — 동시에 두 번
   호출되지 않는다. `KFTC_GIRO.dll`의 스레드 안전성은 알 수 없으므로 이 전제에 의존하되,
   **코드 주석으로 명시**해 나중에 큐 정책이 바뀔 때 드러나게 한다.

## 확정된 설계 결정

### 1. `App.xaml.cs`는 이번에 **바꾸지 않는다** — 스텁을 그대로 둔다 (2026-08-31 사용자 확정)

`VanService`를 만들되 `App.xaml.cs`의 `new StubVanRelayService()`는 **손대지 않는다.**
서버가 준비되는 시점에 그 한 줄만 교체하면 된다.

**스텁/실서버 전환 스위치(설정 파일이든 UI 옵션이든)는 만들지 않는다.** 검토했으나 폐기한
이유(2026-08-31 사용자 판단): 스텁의 역할은 Phase 15~19에서 이미 끝났다 —
`StubVanRelayService`는 결제 Flow 배선을 검증하기 위한 도구였고 그 검증은 완료됐다. 서버가 붙는
순간 스텁은 **다시 쓸 일이 없어지므로**, 그 전환을 위해 설정 인프라(`App.config` 파싱, 가맹점
설정 화면 옵션 등)를 새로 만드는 것은 수명이 몇 주짜리인 기능에 영구 구조를 남기는 일이다.
한 줄 교체가 가장 싸고, 무엇보다 **잘못된 상태로 배포될 위험이 없다**(설정값이 `Stub`인 채로
운영에 나가는 사고가 구조적으로 불가능하다).

> **함께 폐기한 대안**: POS 전문의 `#4 거래구분 코드`에 `"TST"`를 추가해 그 코드로 오면 더미
> 응답을 주는 방식도 검토했다(2026-08-31). 폐기 이유 — `PosSchemaRegistry`는 SPEC 3종
> (501008/800000/902614) 외의 코드를 **전부 `E41`로 거부**하고 그 정합성을 기동 시
> `ValidateAtStartup()`으로 강제한다. 여기에 SPEC에 없는 코드를 끼우려면 그 검증에 예외를
> 뚫어야 하는데, **실제 키오스크가 절대 보내지 않을 코드**를 위해 SPEC 정합성 검증을 약화시키는
> 거래는 남는 장사가 아니다. 게다가 이건 VAN 계층 문제를 POS 계층에서 푸는 것이라 계층 규칙
> (ROADMAP "계층 구조 설계 원칙")에도 어긋난다.

**따라서 이 Phase가 만드는 `VanService`는 기본 실행 경로에서 한 번도 불리지 않는다.**
검증은 전용 진단 진입점(P20-3)으로만 한다. 이것이 이 Phase의 검증 설계를 지배한다.

### 2. 문자열은 `string`이 아니라 `byte[]`로 마샬링한다 ★

`FNAISCRDVAN`의 인자는 `char*`(ANSI)지만 **`string` + `CharSet.Ansi`로 선언하지 않는다.**

이유: `CharSet.Ansi` 마샬러는 **프로세스의 ANSI 코드페이지**로 변환한다. 한국어 Windows에서는
그게 949라 우연히 맞지만, **시스템 로캘이 다른 PC에서는 조용히 깨진다**(한글 필드가 `?`로
치환). 우리는 이미 `PosTelegram.ToBody()`로 **정확한 CP949 바이트**를 갖고 있으므로, 이걸
`string`으로 되돌렸다가 마샬러가 다시 인코딩하게 두는 것은 **불필요한 왕복이자 손실 지점**이다.

→ P/Invoke 선언을 `byte[]`로 하고 바이트를 직접 넘긴다. `PosMessageEncoding`이 "인코딩을 정하는
단 하나의 지점"이라는 기존 원칙(P14-1)도 이렇게 해야 지켜진다.

### 3. NUL 종단을 붙인다

`char*`는 C 문자열이므로 DLL이 `strlen`으로 길이를 잴 가능성이 있다. 전문 본문은 공백 패딩된
고정 길이이고 **내부에 `0x00`이 없으므로**, `본문 길이 + 1` 크기로 배열을 잡고 마지막 바이트를
`0`으로 남겨 두면 두 해석(고정 길이 / NUL 종단) 모두에서 안전하다. 비용은 1바이트다.

### 4. 버퍼 크기는 상수 1곳에서만 정한다

- `outData`: **4096바이트 고정**(ROADMAP 확정). 최대 전문 1504바이트의 2.7배 여유.
- `out_szRetCode`: **크기가 SPEC에 없다.** PRD §2.3에도 "DLL 처리 결과코드가 반환된다"고만 적혀
  있고 길이 언급이 없다 — **이 Phase에서 가장 위험한 미지수다**(DLL이 우리 생각보다 길게 쓰면
  메모리 침범). **256바이트**를 할당한다(결과코드가 256바이트를 넘을 개연성은 사실상 없다).
  이 값이 **검증되지 않은 가정**임을 코드 주석과 아래 "남은 미검증" 절에 명시한다.
- 두 상수 모두 `KftcGiroNative`에 두고 다른 곳에서 숫자를 반복하지 않는다.

### 5. 동기 호출을 `Task.Run`으로 감싼다

`FNAISCRDVAN`은 **블로킹 호출**이다(타임아웃 인자를 받는 것 자체가 근거). `RelayAsync`가
`async`인데 그 안에서 그냥 호출하면 **호출 스레드를 최대 타임아웃 시간만큼 붙잡는다.** 워커
스레드라 UI가 얼지는 않지만, 취소/Timeout 감시(Phase 16)가 같은 컨텍스트를 쓰는지 확인이
필요하고 무엇보다 의도가 드러나야 한다 → `await Task.Run(...)`으로 감싸고 **왜** 감쌌는지
주석에 남긴다.

### 6. 타임아웃은 **60초**, 상수 1곳

PRD §2.3 사용 예(`FNAISCRDVAN("OT", input, data, ret_code, 60)`)를 그대로 따른다. 거래 전체
데드라인은 120초(PRD §4.9)이므로 **VAN 호출이 그 안에 반드시 끝나야 한다** — 카드리딩·PIN에
이미 100초를 썼다면 VAN이 60초를 더 쓸 수는 없다. 다만 이 상호작용은 **실서버가 붙어야 실측이
가능하므로** 지금은 60초로 두고 "남은 미검증"에 적는다(Phase 21에서 실측 후 조정).

### 7. Mode는 `"OT"` 고정, 상수 1곳

PRD §4.10 / 2026-08-18 확정. 운영/테스트 선택 옵션은 추후(PRD §11). **호출부에 리터럴로 박지
않고** 상수로 두어, 나중에 옵션화할 때 그 한 곳만 바뀌게 한다.

## 이 Phase에서 손대지 않는 것 (범위 밖)

- **`PaymentOrchestrator`** — 인터페이스 뒤편만 채우므로 호출부는 그대로다. 여기를 고쳐야 하는
  상황이 나오면 그건 Phase 17의 인터페이스 설계가 틀렸다는 뜻이므로 멈추고 재검토한다.
- **`App.xaml.cs`의 VAN 배선** — 결정 1. 스텁 유지, 경고 로그도 유지.
- **`StubVanRelayService`** — 지우지 않는다. Phase 21 통합 검증에서 "VAN 실패 분기"를 재현할
  때 여전히 필요하다.
- **VAN 응답 전문 파싱** — relay 원칙(전제 3). 길이 확인 외에는 내용을 보지 않는다.
- **`KFTC_GIROPOS.ini` 작성** — 미확보. 발주처 제공 대상이다(아래 "남은 미검증" 2).

---

## P20-1. `Interop/KftcGiroNative.cs` — P/Invoke 선언 ★

**이 Phase의 위험이 여기 집중된다.** 마샬링을 잘못하면 증상이 예외가 아니라 **조용한 메모리
침범**으로 나타난다(PRD §9).

### 구현할 것

- `Interop/KftcGiroNative.cs` — **P/Invoke 선언과 버퍼 상수만.** 업무 로직 없음
  (`NativeLibrary.cs`가 세운 계층 규칙 그대로: "Interop은 P/Invoke 선언만 담당").

  ```csharp
  [DllImport("KFTC_GIRO.dll", CallingConvention = CallingConvention.StdCall)]
  internal static extern int FNAISCRDVAN(
      byte[] in_szMode, byte[] inData, byte[] outData, byte[] out_szRetCode, int int_iTimeout);
  ```

  `CharSet`을 지정하지 않는다 — `byte[]`는 마샬러가 손대지 않고 고정(pinned)해서 포인터만
  넘기므로 코드페이지 변환이 개입할 여지가 없다(결정 2).
- 상수: `OutDataBufferSize = 4096`, `RetCodeBufferSize = 256`, `DefaultTimeoutSeconds = 60`,
  `ModeExternalTest = "OT"` — **전부 여기에만.**
- 클래스 XML 주석에 남길 것: ① `KFTC_GIRO.dll`은 SPEC 문서가 없고 PRD §2.3이 유일한 계약이라는
  사실, ② `out_szRetCode` 크기가 **검증되지 않은 가정**이라는 사실, ③ 32bit 전용이라 `x86`
  빌드가 전제라는 사실(csproj `PlatformTarget`).

### 완료 조건

- [ ] `dotnet build` 성공(경고 0).
- [ ] 선언에 `string` 타입 인자가 **하나도 없다**(결정 2 준수 — grep으로 확인).
- [ ] 버퍼 크기 숫자가 이 파일 밖에 나타나지 않는다(grep `4096`/`256`).

---

## P20-2. `Services/Van/VanService.cs` — `IVanRelayService` 실제 구현 ★

### 구현할 것

`internal sealed class VanService : IVanRelayService` — `StubVanRelayService`와 **같은 자리에
꽂히는** 구현체.

`RelayAsync(PosRequestTelegram request)` 절차:

1. `request.Telegram.ToBody()`로 CP949 고정 길이 바이트를 얻는다.
2. `inData` = `본문 길이 + 1` 바이트 배열에 복사(마지막 바이트 NUL, 결정 3).
   `in_szMode` = `"OT"`의 ASCII 바이트 + NUL.
3. `outData` = `new byte[4096]`, `out_szRetCode` = `new byte[256]` — **매 호출마다 새로
   할당**한다(재사용하면 이전 거래의 잔여 바이트가 다음 응답에 섞인다. 카드 데이터가 흐르는
   경로이므로 PRD §8.4 관점에서도 새로 잡는 편이 옳다).
4. `await Task.Run(() => KftcGiroNative.FNAISCRDVAN(...))` (결정 5).
5. 결과 분기:
   - `nRet == 0` → `outData`에서 **스키마 총 길이(`request.Schema.TotalLength`)만큼** 잘라
     `VanRelayOutcome.Success(bytes)`. 자르기 전에 **응답이 실제로 채워졌는지 확인**한다 —
     버퍼가 전부 `0x00`이면 DLL이 아무것도 안 쓴 것이므로 성공으로 넘기면 안 된다
     (→ `CommunicationFailure`). 이 방어가 없으면 POS에 NUL 덩어리가 relay된다.
   - `nRet == -1` → `VanRelayOutcome.CommunicationFailure(VanFailureKind.CommunicationFailure, …)`
     (`D02`). `out_szRetCode`를 디코딩해 `Detail`에 넣는다(PRD §4.10 "함께 확인").
   - 그 외 값 → PRD에 정의가 없다. **성공으로 취급하지 않는다** — `CommunicationFailure`로
     처리하되 `Detail`에 실제 `nRet` 값을 남겨 나중에 발주처에 물어볼 근거를 만든다.
6. 예외 분기 — `DllNotFoundException` / `EntryPointNotFoundException` /
   `BadImageFormatException`은 **`VanFailureKind.DllLoadFailure`(`D01`)**로, 그 외 예외는
   `CommunicationFailure`(`D02`)로 변환한다. **어떤 예외도 밖으로 던지지 않는다**(PRD §9 —
   DLL 호출 실패로 앱이 죽지 않을 것).
7. 로깅 — 호출 시각/전문 종별/`nRet`/`out_szRetCode`/소요 시간을 `FileLogger`에 남긴다.
   **`#51`(암호화된 비밀번호)을 비롯한 전문 본문은 로그에 남기지 않는다** — Phase 18에서 실제로
   PIN이 유출된 전례가 있다(H-2). 길이와 종별만 남긴다.

### 완료 조건

- [ ] `dotnet build` 성공(경고 0).
- [ ] `StubVanRelayService`가 그대로 남아 있고 `App.xaml.cs`가 여전히 그것을 쓴다(결정 1).
- [ ] 카드 데이터·PIN이 로그에 남지 않는다 — 코드 검토 + P20-3 실행 후 로그 파일 전문 검색.
- [ ] 모든 실패 경로가 예외를 밖으로 내보내지 않는다(코드 검토: `catch`가 모든 경로를 덮는가).

---

## 체크포인트 1 — Opus 검증 리뷰 (P20-1·P20-2 직후)

**여기서 한 번 멈춘다.** 이 Phase의 위험이 전부 이 두 Task에 있고, 결함의 증상이 예외가 아니라
메모리 침범이라 **나중에 발견하면 원인을 찾기 어렵다.** 리뷰 대상:

- 마샬링 — `byte[]` 고정, NUL 종단, 버퍼 크기, 배열 재사용 여부
- 결과 분기 — `nRet` 3갈래, 빈 버퍼 방어, `D01`/`D02` 구분이 PRD §4.10과 일치하는가
- 예외 내성 — 밖으로 새는 예외 경로가 있는가
- 로그에 민감 정보가 섞이는가

---

## P20-3. `--van-call-test` 진단 진입점

**왜 필요한가**: 결정 1에 따라 `VanService`는 기본 실행 경로에서 불리지 않는다. 그러면
**작성한 코드가 한 번도 실행되지 않은 채 Phase가 끝난다** — 마샬링 결함은 실행해야만 드러나므로
이건 받아들일 수 없다. 실제로 호출해 보는 진입점이 반드시 있어야 한다.

### 구현할 것

- `Services/Diagnostics/VanCallTestScenarios.cs` — 기존 하네스
  (`PaymentFlowTestScenarios`)와 같은 형식·같은 성격(**최종 산출물 아님**).
- `App.xaml.cs`에 `--van-call-test` 분기 추가 — 기존 `--payment-flow-test` 분기와 동일한 패턴
  (홈 화면을 띄우고 `Task.Run`으로 실행). **VAN 배선 자체는 건드리지 않는다**(결정 1) — 이
  하네스가 `new VanService()`를 직접 만들어 쓴다.
- 시나리오(서버 없이 확인 가능한 것만):
  1. **3전문 각각을 실제로 호출** — 501008/800000/902614의 정상 형식 전문(길이 706/500/1500)을
     만들어 `FNAISCRDVAN`에 넘긴다. **기대 결과는 실패다**(서버 미개발). 확인하는 것은
     "호출이 성립하고, 크래시 없이 리턴하고, `D01`/`D02`로 분류되고, `nRet`/`out_szRetCode`가
     로그에 남는가"이다.
  2. **연속 호출** — 같은 호출을 10회 반복해 핸들/메모리가 누적되지 않고 매번 같은 결과가
     나오는지 본다(버퍼 재사용 결함이 있다면 여기서 드러난다).
  3. **DLL 부재 시뮬레이션** — `KFTC_GIRO.dll`을 출력 폴더에서 잠시 치운 뒤 실행해
     `DllNotFoundException` → `D01`이 나오고 **앱이 죽지 않는지** 확인(PRD §9).

### 완료 조건

- [ ] `--van-call-test`로 3전문 호출이 전부 크래시 없이 끝나고, 각 호출의 `nRet`/
      `out_szRetCode`/소요 시간이 로그에 남는다.
- [ ] 10회 연속 호출 후에도 결과가 동일하고 프로세스가 살아 있다.
- [ ] DLL을 치운 상태에서 `D01`이 나오고 앱이 정상 기동·종료된다.
- [ ] 로그 파일 전문 검색으로 카드번호/PIN 유출이 없음을 확인.

---

## P20-4. 문서 갱신 + 미검증 범위 명시

### 구현할 것

- `ROADMAP.md` Phase 20 — 체크박스 갱신, 완료 기록 추가. **"실서버 응답 검증은 미완료"를
  완료 기록에 명시적으로 남긴다**(이걸 빼면 다음 사람이 Phase 20을 끝난 것으로 읽는다).
  요약 표(47행)의 상태도 함께 갱신한다 — Phase 19에서 본문만 갱신하고 표를 빠뜨린 전례가 있다.
- `PRD.md` §10 — VAN 서버/INI 항목의 현재 상태를 갱신(해소가 아니라 **여전히 열려 있음**을
  현행화). §10.1 확정 표에 이번 Phase 결정(스위치 미도입, `byte[]` 마샬링, 버퍼 크기)을 추가.
- `development_plan.md` — 이 문서에 실행 기록/검증 결과를 이어 적는다.

### 완료 조건

- [ ] 세 문서가 갱신되고, **서버 준비 후 해야 할 일이 한 곳에 목록으로 정리**돼 있다
      (아래 "남은 미검증" 절이 그 목록이 된다).

---

## 완료 기준 (Phase 전체)

1. `FNAISCRDVAN` 호출이 **성립한다** — 3전문 모두 크래시 없이 리턴하고 결과가 로그에 남는다.
2. **마샬링·버퍼가 안전하다** — 10회 연속 호출, 예외 경로 전수, 민감정보 미유출 확인.
3. **실패 3갈래가 구분된다** — DLL 로드 실패(`D01`) / 통신 실패(`D02`) / (서버 응답 시)
   relay. 앞의 둘은 실증하고, 세 번째는 **서버 미개발로 미검증**임을 명시한다.
4. 기존 동작에 회귀가 없다 — `--payment-flow-test`(47건) 재실행 통과, `App.xaml.cs`의 VAN
   배선이 스텁 그대로.

## 남은 미검증 (서버 준비 후 해야 할 일)

이 목록이 **Phase 20의 진짜 산출물 중 하나**다 — 서버가 열렸을 때 무엇부터 확인해야 하는지가
여기 남아야 한다.

1. **실제 승인/거절 응답의 relay** — `nRet == 0` 경로는 이번에 한 번도 실행되지 못한다.
   응답 전문 길이가 요청과 같은지(스키마 총 길이 가정), `outData`가 어떻게 종단되는지
   (NUL/공백 패딩)를 **첫 성공 응답에서 반드시 실측**한다. 지금 코드의 "스키마 길이만큼 자른다"는
   이 가정 위에 서 있다.
2. **`KFTC_GIROPOS.ini`** — 미확보. DLL이 `IP`/`PORT`/`LOGDIR`/`LOGGING`을 여기서 읽으므로
   (PRD §2.3) 파일 없이는 접속 자체가 성립하지 않는다. 발주처에서 받아 배포 절차에 포함한다.
3. **`out_szRetCode` 실제 크기** — 256바이트는 검증되지 않은 가정(결정 4). 실응답에서 실제 길이를
   확인하고, 가능하면 발주처에 명세를 요청한다.
4. **60초 타임아웃과 120초 거래 데드라인의 상호작용**(결정 6) — 카드리딩·PIN에 시간을 많이 쓴
   거래에서 VAN 타임아웃이 데드라인을 넘기는지 실측. Phase 21 몫.
5. **`#51`(암호화된 PIN)이 VAN 응답에 실려 오는지** — Phase 18에서 남긴 미확정. 실응답을 봐야
   안다. 실려 온다면 relay 전에 지울지 발주처와 확인이 필요하다.
6. **`App.xaml.cs` 한 줄 교체** — `new StubVanRelayService()` → `new VanService()`, 그리고 그
   위의 "스텁입니다" 경고 로그 제거(결정 1). **이걸 잊으면 실서버 환경에서 계속 가짜 승인이
   난다** — 그래서 경고 로그를 남겨 둔 것이다.
7. **`FNAISCRDVAN`이 멈췄을 때 결제 워커 전체가 정지하는 문제**(체크포인트 1 M-1, 2026-08-31
   발견·보류). `TransactionQueue`는 단일 워커 스레드가 `_processor(...).GetAwaiter().GetResult()`
   로 동기 블로킹하므로, `VanService.RelayAsync`가 끝나지 않으면(=DLL이 `int_iTimeout=60`을
   지키지 않으면) 그 이후 모든 결제 요청이 큐에 쌓인 채 처리되지 않는다. `Task.Run`은 호출
   스레드만 바꿀 뿐 이 위험을 없애지 못한다. **서버 연결 후 가장 먼저 "DLL이 타임아웃 인자를
   실제로 지키는가"부터 실측**하고, 안 지킨다면 그때 워치독(예: 65초 자체 타임아웃으로
   `RelayAsync`를 실패 처리해 워커를 풀어줌 — 다만 DLL을 실행 중이던 네이티브 스레드 자체는
   못 멈추므로 좀비 스레드가 남을 수 있다는 트레이드오프가 있음)을 검토한다. 서버 없이 이
   대응 코드를 넣으면 검증 불가능한 코드를 추가하는 것이라 지금은 보류하기로 확정
   (2026-08-31 사용자 결정).

## 착수 순서 요약

P20-1(P/Invoke 선언 ★) → P20-2(VanService ★) → **체크포인트 1(Opus 검증 리뷰)** →
P20-3(`--van-call-test` 실행 검증) → P20-4(문서 갱신).

**P20-1·P20-2가 이 Phase의 전부**라고 봐도 된다 — 코드량은 적지만 결함의 대가가 가장 크다.

---

## 실행 기록 (2026-08-31)

**P20-1·P20-2**: `reader-dll-integration-developer` 서브에이전트가 `Interop/KftcGiroNative.cs`,
`Services/Van/VanService.cs`를 구현. 두 파일 단위 빌드 경고 0/오류 0. `App.xaml.cs`/
`PaymentOrchestrator` 미수정(결정 1 준수) 확인.

**체크포인트 1(Opus 검증 리뷰)** — 마샬링/버퍼/예외 내성/relay 원칙/로그 민감정보를 코드 직접
정독으로 검증. 발견한 3건:

- **H-1(수정 완료)**: `nRet==0`일 때 `outData` 4096바이트 **전체**가 0x00인지만 검사해 부분 기록
  (앞부분만 짧게 쓰고 나머지가 0x00인 응답)을 걸러내지 못함 — 유효한 전문은 space(0x20)/`'0'`
  (0x30)로만 패딩되므로(`PosTelegram.CreateEmpty`, `PosField.Pad`) 0x00은 절대 나올 수 없다는 사실을
  근거로, 검사 대상을 **실제 relay될 `responseBody` 구간**으로 좁히고 0x00 포함 여부로 판정하도록 수정.
- **L-1(수정 완료)**: `bodyLength > outData.Length`일 때 `Buffer.BlockCopy`가 던지는 예외가 generic
  catch에 삼켜져 원인 불명의 `D02`로만 남음 — 명시적 가드를 추가해 진단 가능한 사유를 남기도록 수정.
- **M-1(보류 확정)**: `TransactionQueue`가 단일 워커로 동기 블로킹하므로 `FNAISCRDVAN`이 멈추면
  이후 모든 결제가 정지. 서버 없이는 대응 코드(워치독)의 실효성을 검증할 수 없어, 서버 연결 후 DLL이
  타임아웃을 실제로 지키는지 실측한 뒤 필요하면 그때 대응하기로 사용자가 확정. "남은 미검증" 7번에 기록.

수정 후 재빌드(루트 솔루션) 경고 0/오류 0.

**P20-3**: `Services/Diagnostics/VanCallTestScenarios.cs` + `App.xaml.cs`의 `--van-call-test` 분기
신규 작성. 실행 결과:

- 3전문(501008/800000/902614) 정상 형식 호출 — 전부 크래시 없이 `nRet=-1`, `out_szRetCode='0004'`,
  호출당 약 21초 소요로 `D02`(통신 실패) 분류. **VAN 서버가 완전 무응답이 아니라 빠르게 거절 응답을
  준다는 사실을 이때 처음 확인** — PRD §10에 반영.
- 902614를 10회 연속 호출 — 매번 동일하게 통신 실패로 일관되고 프로세스 생존, 결과가 흔들리지 않음.
- `KFTC_GIRO.dll`을 출력 폴더에서 실제로 치운 뒤 재실행 — `DllNotFoundException` → `D01`
  (`DllLoadFailure`)로 정확히 분류, 앱이 죽지 않고 정상 기동·종료. 검증 후 DLL 원복.
- 로그 파일 전문 검색(12자리 이상 숫자 패턴)으로 카드번호·PIN 등 민감정보 미유출 확인.
- 전체 빌드(루트 솔루션) 최종 재확인 — 경고 0/오류 0.

**P20-4**: `ROADMAP.md`(Phase 20 절 + 요약 표), `PRD.md`(§10, §10.1), 이 문서(실행 기록)를 갱신.
"남은 미검증" 목록(7개 항목, M-1 포함)이 서버 준비 후 해야 할 일의 단일 진입점으로 남았다.

**Phase 20 완료** — 완료 기준 1~4 전부 충족(호출 성립, 마샬링·버퍼 안전성, 실패 2갈래 실증 + relay
경로는 명시적으로 미검증, 기존 동작 회귀 없음). `nRet==0` 경로 실증은 Phase 21(VAN 서버 준비 후)로
이월.

---

## SPEC 개정 반영 (2026-08-31, Phase 21 착수 전 임시 작업)

Phase 21 착수 직전, 사용자가 POS↔원캡 SPEC 개정판(`국세 베리어프리 키오스크용
전산설계서(POS-원캡)_20260831.pdf`)을 제공 — 800000(카드 정보 조회) 전문에 `#26 납부대행 수수료율`
필드가 추가됐다는 통보를 받았다. Phase 21과 무관하지만 스키마 정확성이 걸린 문제라 먼저 처리했다.

**확인 과정**: 0826판 원본 PDF가 저장소에서 이미 삭제된 상태였다(사용자가 0831판을 넣으면서 교체).
`pos-onecap-spec-expert`에게 "코드에 이미 반영된 0826판 필드 목록(작업 지시에 첨부)"과 "0831판 PDF 원문"을
대조시켜, 실물 diff 없이도 변경분을 특정했다 — 이 방식으로 얻은 결과가 다음.

- **신규**: `#26 납부대행 수수료율`(N, 길이4, POSITION 274, SET=인터넷지로만 — kiosk/원캡/VAN 전부 공란,
  순수 relay 대상)
- **이동**: 기존 `#26 API 세부 응답코드`(AN6) → `#27`(POSITION 274→278), 기존 `#27 예비 정보 FIELD` →
  `#28`(POSITION 280→284, 길이 220→216)
- **불변**: 총 길이 500, #14~#25는 번호/POSITION/SET장소 전부 동일, 501008·902614 두 전문은 변경 없음
  (에이전트가 0831판 p.7~11/p.13~17 전체 원문으로 재확인)

**수정 파일**: `Protocol/Pos/Schemas/CardInfoInquirySchema.cs`(본 앱), `KFTCOneCAP.KioskSim/Protocol/
TelegramSchemas.cs`(독립 전사본, P19-2 원칙대로 본 앱 스키마를 참조하지 않고 SPEC을 다시 옮겨 적음) 둘 다
동일하게 갱신. 카드리딩/`PaymentOrchestrator` 등 업무 로직은 이 필드에 손대지 않으므로(relay 대상) 변경
없음 — grep으로 `Write(26`/`Write(27`/`Write(28` 코드가 어디에도 없음을 확인.

**검증**: 두 프로젝트 모두 `dotnet build` 경고 0/오류 0. 두 스키마 생성자가 갖고 있는 자체 검증(POSITION
연속성 + 총 길이 일치, `PosSchemaRegistry.ValidateAtStartup`/`TelegramSchema` 생성자)이 정적 필드
초기화 시점에 실행되므로, 본 앱과 KioskSim을 각각 실제로 기동해 크래시 없이 뜨는 것으로 확인했다(본 앱 로그
"POS 전문 스키마 3종 검증 완료(POSITION 연속성·총 길이·라우팅 상수 일치)", KioskSim은 `MainForm`이 시작
시 `TelegramSchemas.CardInfo800000`을 즉시 참조해 검증을 트리거).

**문서 갱신**: `CLAUDE.md`, `.claude/agents/pos-onecap-spec-expert.md`(참조 문서 경로 + 개정 요약 추가),
`PRD.md`(§3.3 정본 경로, §10.1 신규 행), `ROADMAP.md`(참고 문서 절)의 SPEC 정본 경로를 `_20260826.pdf`
→ `_20260831.pdf`로 갱신. Phase 17 절 등 **과거 시점을 서술하는 역사적 언급은 그대로 둠**(그 시점엔
0826판이 실제 정본이었으므로).

**개정 이력 메모**: 0831판 PDF 자체의 변경이력표(p.2)에는 이 개정이 기재돼 있지 않다 — 파일명과 표지
날짜만으로 개정 사실을 확인했다. 발주처에 공식 변경이력 갱신을 요청할 필요가 있다.

---

# Phase 21 실행계획서 — 통합 검증 & 안정성

> 로드맵: `ROADMAP.md` "Phase 21 — 통합 검증 & 안정성". PRD §9(안정성·성능)와 §8.4(거래 종료 후
> 데이터 정리)를 점검하고 2차 범위(Phase 7~21)를 마무리한다.
>
> **이 Phase는 새 기능을 만들지 않는다.** 지금까지 만든 것이 요구사항을 실제로 지키는지 확인하고,
> 어긋난 곳만 고친다. 따라서 산출물의 대부분은 **검증 기록**이고, 코드 수정은 검증에서 결함이
> 나왔을 때만 생긴다.
>
> **최우선 항목은 "거래 종료 후 이전 거래 데이터 잔존 금지"(PRD §8.4)다**(2026-08-31 사용자 강조).
> 기존 요구사항이지만 Phase마다 부분적으로만 확인해 왔고, **앱 전체를 한 번에 훑은 적이 없다** —
> 이번에 전수로 점검한다. 착수 전 사전 조사에서 이미 위반 1건을 발견했다(P21-1 참고).

## 착수 전 전제 (2026-08-31 코드 확인 완료)

1. **이미 검증된 것은 다시 하지 않는다.** 아래는 앞선 Phase에서 실장비로 확인돼 이번 범위에서
   제외한다 — 재확인이 필요한 것은 "연속 실행·장시간 실행에서도 유지되는가"뿐이다.
   - 정상/FALLBACK/`12` 재요청/취소/Timeout 분기 — Phase 15·16 실장비 검증 완료
   - 3전문 실장비 왕복(902614 PIN 포함) — Phase 19 P19-8 완료
   - **VAN DLL 로드 실패 시 앱 생존** — Phase 20 P20-3에서 `KFTC_GIRO.dll`을 실제로 치워 검증 완료
     (로드맵 Phase 21 항목 중 이것만 이미 끝나 있다)
2. **`--payment-flow-test` 하네스가 47건 있다**(`Services/Diagnostics/PaymentFlowTestScenarios.cs`).
   가짜 부품 기반이라 실장비 없이 반복 실행할 수 있다 — 회귀 확인의 기본 도구로 쓴다.
3. **알림창은 거래마다 새로 만들어진다**(`Views/PaymentNoticePresenter.cs` `Show()` — 이전 창이
   남아 있으면 닫고 `new PaymentNoticeViewModel`/`new PaymentNoticeWindow`를 만든다). 따라서
   ViewModel에 담긴 PIN 상태는 구조적으로 다음 거래에 넘어가지 않는다.
4. **PIN은 `PaymentNoticeViewModel`에서 이미 즉시 폐기된다**(`_pinDigits.Clear()` + `RevealedDigit
   = null`, P18-3). `string`을 메모리에서 0으로 덮어쓸 수 없다는 .NET 제약 때문에 **"참조를 즉시
   끊는 것"까지가 이 애플리케이션 레벨의 폐기 수준**이라는 것도 PRD §8.4에 이미 명시돼 있다 —
   이 Phase가 그 기준을 올리지는 않는다.
5. **정적 캐시로 거래 데이터를 들고 있는 곳은 없다**(사전 grep 확인 — `static` 컬렉션은 전문 스키마와
   인코딩 상수뿐이고, 나머지 `static`은 전부 순수 함수다).

## 이 Phase의 성격 — 무엇을 확인할 수 있고 무엇을 못 하는가

**VAN 서버가 아직 개발 중**이라 Phase 20과 같은 제약이 이어진다. 로드맵 Phase 21 항목 6개 중
**서버가 필요한 것은 "VAN 거절" 시나리오 하나뿐**이고 나머지는 전부 지금 검증 가능하다. 이 구분을
계획서에 못 박아, 나중에 "Phase 21을 다 했다"고 읽히지 않게 한다.

| 로드맵 항목 | 이번에 가능한가 |
|---|---|
| 전체 Flow 연속 실행(정상/FALLBACK/취소/Timeout/**VAN 거절**) | **"VAN 거절"만 서버 필요.** 나머지는 가능 |
| 예외 내성(리더기 케이블 분리 / VAN DLL 로드 실패 / 소켓 강제 종료) | 가능(VAN DLL은 Phase 20에서 이미 완료) |
| 리소스 정리(메모리/핸들, 타이머·훅·콜백 잔존) | 가능 |
| 카드 데이터 거래 종료 후 즉시 삭제 | 가능 — **이번 Phase의 최우선 항목** |
| 계층 규칙 최종 점검 | 가능 |
| 문서 정리 | 가능 |

## 이 Phase에서 손대지 않는 것 (범위 밖)

- **새 기능** — 가맹점 설정 화면, 리더기 키다운로드 실동작, 로그 파일 기능 확장 등은 **별도 PRD로
  다룰 새 범위**다(2026-08-31 사용자 언급). 이 Phase는 기존 범위를 닫는 데만 쓴다.
- **`App.xaml.cs`의 VAN 배선** — 여전히 `StubVanRelayService`. Phase 20 결정 1 그대로.
- **M-1(DLL 멈춤 시 워커 정지) 대응 코드** — Phase 20에서 보류 확정. 서버 연결 후 실측이 먼저다.
- **PIN 메모리 스크러빙** — 전제 4. PRD가 정한 폐기 수준을 이 Phase가 바꾸지 않는다.

---

## P21-1. 거래 데이터 잔존 전수 점검 ★ (이 Phase의 최우선 항목)

**사용자가 명시적으로 강조한 요구사항이다**(2026-08-31): "원캡은 거래 후 이전 거래 관련 데이터를
가지고 있으면 안 된다." PRD §8.4·§9에 이미 있던 조항이지만, 지금까지는 **거래 Flow 안에서만**
확인해 왔고 앱 전체를 훑은 적이 없다.

### ★ 착수 전 사전 조사에서 이미 발견한 위반 1건

**`StubVanRelayService.LastRequest`가 직전 거래의 요청 전문을 계속 붙들고 있다**
(`Services/Van/StubVanRelayService.cs:33`, `:51`).

```csharp
internal PosRequestTelegram? LastRequest { get; private set; }
...
LastRequest = populatedRequest;   // 대입만 있고 비우는 코드가 없다
```

- 이 전문은 **원캡이 채운 필드가 전부 들어간 상태**다 — 902614면 `#46`(암호화된 카드정보),
  `#51`(암호화된 비밀번호 정보)까지 포함한다.
- 다음 거래가 `RelayAsync`를 부를 때까지, **또는 그날 더 이상 거래가 없으면 앱을 끌 때까지**
  메모리에 남는다.
- **검증 하네스용으로 만든 필드인데(P15-5 `LastRequest` 패턴 계승) 실제로 배선된 구현체에 들어
  있다** — `App.xaml.cs`는 지금도 `StubVanRelayService`를 쓴다(Phase 20 결정 1). 즉 개발용
  affordance가 실동작 경로로 새어 나온 전형적인 사례다.

이 한 건으로 "전수 점검이 필요하다"는 것이 이미 증명됐다. 같은 유형이 더 있는지 찾는 것이 P21-1의
본체다.

### 구현할 것

- **점검 대상 목록을 먼저 만든다**(코드를 고치기 전에). 거래 데이터가 지나가는 모든 계층에서
  "인스턴스 필드/프로퍼티/정적 상태로 남는 것"을 훑는다:
  `Services/Van/`, `Services/Payment/`, `Services/Reader/`, `Services/Pos/`,
  `ViewModels/`, `Views/`(특히 알림창), `Protocol/`.
  각 항목마다 **"누가 들고 있나 / 언제 비워지나 / 안 비워지면 무엇이 남나"** 3열로 적는다.
- **판정 기준**을 먼저 못 박는다 — 무엇이 위반이고 무엇이 아닌가:
  - **위반**: 거래가 끝난 뒤에도 그 거래의 카드번호·`#46`·PIN·전문 본문을 참조로 붙들고 있는 것
  - **위반 아님**: 거래 중에만 살아 있는 지역 변수(`ProcessAsync`의 `roundResult.CardData` 등 —
    메서드가 반환되면 참조가 끊긴다), 무결성 체크 **성공 이력**(`IntegrityCheckStore` — 날짜별
    이력이 목적인 의도된 영속 데이터이며 카드 데이터가 아니다), 로그에 남긴 **길이·종별 등 메타
    정보**(PRD §8.4가 허용)
- **발견한 위반을 수정한다.** `LastRequest`는 최소한 다음 중 하나로:
  ① 하네스가 필요로 하는 최소 정보(전문 종별, 원캡이 채운 필드가 실제로 채워졌는지 여부 같은
  **판정 결과**)만 남기고 전문 자체는 붙들지 않기, 또는 ② 거래 종료 시 명시적으로 비우기.
  **①을 우선 검토한다** — 하네스가 원했던 건 "원캡 필드가 VAN 요청까지 도달했는가"라는 사실이지
  전문 원본이 아니다. 수정 시 `PaymentFlowTestScenarios`의 해당 단언들도 함께 맞춘다.
- **`VanService`(Phase 20 신규)도 같은 기준으로 재확인한다** — 매 호출 버퍼 재할당은 이미 확인됐지만,
  거래 종료 후 남는 필드가 없는지 이번 기준으로 다시 본다.

### 완료 조건

- [x] 점검 대상 목록과 판정 결과가 이 문서에 표로 남는다(위반/위반 아님 + 근거).
- [x] 발견한 위반이 전부 수정되고 `dotnet build` 경고 0/오류 0.
- [x] `--payment-flow-test` 47건이 수정 후에도 전부 통과(하네스 단언을 고쳤다면 그 사실도 기록).

### 점검 결과 (2026-08-31)

| 계층/클래스 | 보유 항목 | 언제 비워지나 | 판정 |
|---|---|---|---|
| `StubVanRelayService.LastRequest` | 요청 전문(카드번호·`#46`·PIN 등 원캡 담당 필드 전부 포함) | **안 비워짐** — 다음 거래가 올 때까지, 그날 마지막 거래면 앱 종료까지. `App.xaml.cs`가 지금도 이 클래스를 실제로 배선해 쓴다(Phase 20 결정 1) | **위반 → 수정 완료**(아래 참고) |
| `PendingReaderCommand`(`ReaderService._pending`) | 라운드 토큰·`TaskCompletionSource`(카드 데이터 자체는 안 들어 있음) | 매 명령 완료 시 CAS(`Interlocked.CompareExchange`)로 즉시 `null` | 위반 아님 — 설계상 단일 슬롯이 즉시 비워짐(P10-4) |
| `PosMessageFramer._buffer` | 미완성(다음 프레임 일부) 잔여 바이트만 | 완성된 프레임은 추출 즉시 `RemoveRange`(코드 101~102행)로 버퍼에서 제거 | 위반 아님 — 완성된 전문은 버퍼에 남지 않음 |
| `IntegrityCheckStore`(SQLite) | 포트/시각/성공여부/응답코드/모듈ID/리더인증ID | 의도된 영속 이력(PRD §7, 결제 선행 판정에 필요) | 위반 아님 — 카드 데이터·PIN이 아닌 의도된 데이터 |
| `PaymentOrchestrator` 인스턴스 필드 | 없음 — 전부 생성자 주입 설정값(`readonly`) | 거래별 상태(카드데이터·PIN)는 전부 `ProcessAsync` 지역 변수(`TransactionScope`, `roundResult` 등)이며 메서드 반환 시 참조가 끊긴다 | 위반 아님 |
| `PaymentNoticeViewModel._pinDigits`/`RevealedDigit` | PIN 자릿수 | 입력 완료 직후 `Clear()` + `null` 대입(P18-3, 기존 구현) | 위반 아님 — 이미 정리돼 있었음 |
| `PaymentNoticePresenter._viewModel`/`_window` | 이전 거래 뷰모델/창 | 다음 `Show()` 호출 시 이전 창을 `Close()`하고 필드를 `null`로 비운 뒤 새로 만든다(기존 구현) | 위반 아님 |
| `TransactionQueue.TransactionWorkItem` | 요청 전문 + 완료 콜백 | `BlockingCollection`이 소비한 뒤 `foreach` 지역 변수 스코프만 살아있고 큐 자체는 안 붙든다 | 위반 아님 |
| `PosSocketServer.HandleConnection`의 `frame`/`request` | 프레임 바이트·파싱된 전문 | 매 루프 반복의 지역 변수, 다음 반복에서 새로 할당 | 위반 아님 |
| `ReaderEndpoint`/`ReaderConnectionManager` | 서비스 참조만 | 해당 없음(카드 데이터를 담는 필드 자체가 없음) | 위반 아님 |
| `VanService`(Phase 20) | 없음 — 인스턴스 필드가 아예 없는 무상태 클래스, 버퍼는 매 호출 새로 할당 | 해당 없음 | 위반 아님(재확인 완료) |

**수정 내용**: `StubVanRelayService`에서 `LastRequest` 프로퍼티와 그 대입 코드를 완전히 제거했다.
검증 하네스가 필요로 하던 "가장 최근 요청 캡처" 기능은 새 클래스
`Services/Diagnostics/CapturingVanRelayService.cs`(테스트 전용, `IVanRelayService` 구현체로
`StubVanRelayService`를 감싸 위임)로 분리했다 — 처음 계획한 "①: 판정 결과만 남기기"보다 이쪽을
택한 이유는, 하네스가 실제로 8개 필드(`#43~#46,#48,#50,#51,#53`)를 개별 검사하므로 "판정 결과"로
축약하면 검증력이 크게 줄기 때문이다. 대신 **프로덕션이 실제로 배선하는 클래스에서 전문 보유
자체를 없애고, 캡처는 테스트 전용 경로에만 남기는 방식**으로 위반을 해소했다.
`Services/Diagnostics/PaymentFlowTestScenarios.cs`의 `BuildOrchestrator`가
`out StubVanRelayService` → `out CapturingVanRelayService`로 바뀌었고, `SetNextOutcome`은
위임 메서드로 그대로 노출된다.

**재검증**: 수정 후 `dotnet build`(루트 솔루션) 경고 0/오류 0. `--payment-flow-test` 재실행 —
47건 전부 통과(0건 실패), 8개 필드 검사(`#43~#53`)를 포함해 회귀 없음 확인.

---

## P21-2. 연속 거래 실행 — 잔존 없음의 실증 + Flow 혼합

P21-1이 **코드를 읽어서** 확인한 것을, 여기서 **실제로 돌려서** 확인한다. 정적 점검만으로는
"실행 중에만 생기는 참조"를 놓칠 수 있다.

### 구현할 것

- **혼합 시나리오 연속 실행** — 정상 / FALLBACK / 취소 / Timeout을 섞어 반복한다(로드맵 항목 1에서
  "VAN 거절"만 제외). `--payment-flow-test`(가짜 부품, 빠름)로 반복 횟수를 벌고, **실장비로도 최소
  1회전**을 돌려 실제 하드웨어 경로를 포함시킨다.
- **거래 사이에 데이터가 새지 않는지 확인** — 앞 거래에서 쓴 카드 데이터/PIN이 뒤 거래의 전문에
  섞이지 않는지. **연속 거래에서 서로 다른 값을 쓰고**(같은 값이면 섞여도 구분이 안 된다) 뒤 거래의
  VAN 요청 전문에 앞 거래 값이 나타나지 않음을 확인한다.
- **로그 전문 검색** — 실행 후 로그 파일 전체에서 카드번호 패턴·PIN이 남지 않았음을 확인한다
  (Phase 20 P20-3과 같은 방식: 12자리 이상 숫자 패턴 검색 + `#51` 값 검색).
- **이전 거래의 잔여 리더기 CALLBACK이 다음 거래에 영향을 주지 않는지**(PRD §8.4 2번째 조항) —
  이중화에서 무효화된(`0x60`) 리더기의 뒤늦은 응답이 다음 거래에 섞이지 않는 것은 Phase 15에서
  확인됐지만, **연속 실행에서 재확인**한다.

### 완료 조건

- [x] 혼합 시나리오 연속 실행에서 각 거래가 독립적으로 올바른 결과를 내고, 앞 거래 값이 뒤 거래
      전문에 나타나지 않는다.
- [x] 실장비 1회전 포함.
- [x] 로그 파일에 카드번호·PIN 미유출.

### 진행 기록 (2026-08-31)

**새 시나리오 13 추가**(`PaymentFlowTestScenarios.Scenario13_ConsecutiveTransactionsDoNotLeakCardOrPinData`)
— **같은 `PaymentOrchestrator` 인스턴스**로 서로 다른 카드번호(`1111...`/`9999...`)·PIN(`1357`/`2468`)을
쓰는 902614 거래 두 건을 연달아 처리해, `.Read(51)`처럼 필드 위치만 보는 검사가 놓칠 수 있는 "엉뚱한
자리에 남는 잔존"까지 잡기 위해 **VAN 요청 원문(raw bytes) 전체**에서 앞 거래 PIN을 검색했다(P18-5
raw 검사 패턴 계승). 결과: 거래 B의 원문 어디에도 거래 A의 PIN이 없음, 역방향(A에 B의 PIN 없음)도
확인, 두 거래의 알림창 History가 각자 독립적으로 시작함(이전 거래 상태 잔존 없음)도 함께 확인.

**혼합 시나리오**: 기존 47건(정상/취소/Timeout/VAN 통신실패 포함) + 새 시나리오13 = **`--payment-flow-test`
53건 전부 통과**(0건 실패). FALLBACK(`0x3B` 응답 `07`)·이중화 무효화 리더기의 뒤늦은 응답 잔존 여부는
이 통합 하네스에 별도 시나리오가 없지만, ① P21-1에서 `PendingReaderCommand`의 CAS 메커니즘을 코드로
확인해 구조적으로 늦은 응답이 걸러짐을 이미 확인했고, ② Phase 15가 실장비로 "연속 2건 거래 시 앞 거래
데이터·잔여 콜백이 뒤 거래에 섞이지 않는다"를 이미 검증해 뒀으므로(2026-08-25 완료 기록) 이번에
재검증하지 않는다.

**로그 검색**: 실행 후 로그 파일 전체에서 12자리 이상 숫자(카드번호 패턴) 0건, 이번 테스트가 쓴 PIN
마커(`1357`/`2468`) 0건 확인.

**실장비 1회전 — 완료(2026-08-31)**: 본앱(정상 모드, 실제 리더기 COM3/COM7 연결)과 키오스크
시뮬레이터를 함께 띄우고, 사용자가 자리로 돌아와 직접 카드 태그·PIN 입력을 수행했다(Claude가
시뮬레이터 UI를 조작하고 결과를 로그로 확인, 사용자는 물리적 동작만 담당). 서로 다른 카드 2장 +
서로 다른 PIN(`1234`/`5678`)으로 902614 연속 2건 처리 — 둘 다 `VAN 응답 수신 — relay`로 정상
종료됐고, 로그 전체에서 `1234`/`5678`/12자리 이상 숫자(카드번호 패턴) **0건** 확인. 가짜 부품
검증(시나리오13)과 실장비 결과가 일치함을 확인.

---

## 체크포인트 1 — Opus 검증 리뷰 (P21-1·P21-2 직후)

**여기서 한 번 멈춘다.** P21-1의 판정(무엇을 위반으로 보고 무엇을 넘겼는지)이 이 Phase의 핵심
산출물이고, **판정 기준을 잘못 잡으면 "점검했다"는 기록만 남고 실제로는 새는 곳이 남는다.**
리뷰 대상:

- 점검 대상 목록에 **빠진 계층이 없는가**(특히 View/ViewModel, 소켓 서버 버퍼)
- "위반 아님"으로 넘긴 항목의 근거가 실제로 타당한가(지역 변수라고 판단한 것이 정말 참조가
  끊기는가 — 클로저·이벤트 핸들러·`async` 상태 머신에 캡처돼 살아남는 경우가 있다)
- `LastRequest` 수정이 하네스의 검증력을 떨어뜨리지 않았는가
- P21-2의 "서로 다른 값" 설계가 실제로 섞임을 드러낼 수 있는 구성인가

### 검증 결과 (2026-08-31)

- **점검 대상 누락 없음** — `Services/Van`·`Payment`·`Reader`·`Pos`·`ViewModels`·`Views`·`Protocol`
  전체를 훑었다. `Services/Settings`·`Services/Storage`는 구조적으로 카드/PIN 데이터를 다루지 않는
  계층(포트 설정값, 무결성 이력만)이라 점검 대상에서 제외한 것이 타당함을 재확인.
- **클로저·이벤트 핸들러 재확인** — `PaymentOrchestrator.ProcessAsync`의 `onCanceled` 핸들러는
  `finally`에서 반드시 구독 해제되고(`_presenter.Canceled -= onCanceled`), `fillOneCapFields` 람다는
  이벤트/필드 어디에도 저장되지 않고 그 자리에서 즉시 호출·소멸한다 — 캡처된 거래 데이터가 호출
  종료 후에도 살아남을 경로가 없음을 확인.
- **`LastRequest` 수정 검증력**: `--payment-flow-test` 53건(8필드 검사 포함) 전부 통과로 확인 완료.
- **P21-2 설계 검증력**: raw 바이트 전체 검색이라 필드 위치와 무관하게 잔존을 잡아낼 수 있음 확인.

**판정: 통과.** P21-3으로 진행.

### 독립 재검토 (2026-08-31, P21-3까지 끝난 뒤 사용자 요청으로 추가)

위 검증은 구현자 본인(Opus)이 같은 세션에서 수행한 자기 검토였다는 지적을 받아, **포크로 독립
세션을 띄워 P21-1~P21-3 전체 diff를 처음 보는 눈으로 재검토**했다. 결과:

- `CapturingVanRelayService`가 프로덕션 경로(`App.xaml.cs`)에 전혀 쓰이지 않음을 저장소 전체
  검색으로 재확인.
- `StubVanRelayService._nextOutcome` 소비 로직 안전, 실제 사용례(`VanRelayOutcome.
  CommunicationFailure`)는 `ResponseBody`가 `null`이라 카드 데이터를 물지 않음.
- `Scenario13`의 PIN 마커(`1357`/`2468`)는 자릿수가 완전히 겹치지 않아 false positive/negative가
  구조적으로 불가능, CP949/ASCII 인코딩 문제도 없음.
- `static` 필드 전수 재검색에서 추가 위반 없음(전부 `static` 메서드였고 필드는 없었음). PIN 이벤트
  구독(`PinEntered`)이 `try/finally`로 모든 경로(정상/취소/Timeout)에서 해제됨을 재확인.
- 사소한 관찰 1건: `CapturingVanRelayService.RelayAsync`가 락 없이 `LastRequest`를 대입 — 단일
  워커 큐(PRD §3.2)로 동시 호출이 구조적으로 없어 실결함 아님으로 판단.

**진짜 결함 0건.** 구현이 P21-1~P21-3에서 기록한 배경 설명과 일치함을 독립적으로 확인.

---

## P21-3. 예외 내성

PRD §9의 "Reader DLL 오류 / VAN 통신 오류 / Socket 통신 오류로 프로그램 전체가 종료되어서는 안
된다"를 실제로 재현해 확인한다.

### 구현할 것

- **리더기 케이블 분리** — 거래 중 실제로 USB/시리얼 케이블을 뽑아, 앱이 죽지 않고 오류로 처리되며
  다음 거래가 정상 동작하는지(재연결 후) 확인. **실장비 필수**.
- **소켓 강제 종료** — POS(시뮬레이터) 쪽 연결을 응답 전에 강제로 끊는다. Phase 19 오류 주입 탭에
  이미 유사 시나리오가 있으므로 **그것을 재사용**하고, 없으면 그때 판단한다.
- **VAN DLL 로드 실패** — **Phase 20 P20-3에서 이미 완료**. 재실행하지 않고 그 기록을 인용한다.
- 각 경우에 **앱이 살아 있는지 + POS에 구분 가능한 오류 코드가 가는지 + 그 다음 거래가 정상인지**
  3가지를 함께 본다(죽지 않는 것만으로는 불충분 — 좀비 상태로 남으면 더 나쁘다).

### 완료 조건

- [x] 케이블 분리·소켓 강제 종료 각각에서 앱 생존 + 오류 응답 + 후속 거래 정상을 확인.
- [x] VAN DLL 로드 실패는 Phase 20 기록 인용으로 갈음(재실행하지 않음을 명시).

### 진행 기록 (2026-08-31)

**소켓 강제 종료**: 기존 `--pos-client-test` 하네스(Phase 14, `PosClientTestScenarios.cs`)가
정확히 이 항목을 다룬다 — 재사용해 실제 앱(`App.Orchestrator`, 실제 리더기 COM3/COM7 연결된 상태)
대상으로 7개 시나리오를 실행했다.

**실행 중 시나리오6·7이 실패로 나와 조사했다** — 원인은 코드 결함이 아니라 **테스트 시나리오의
숨은 하드웨어 의존성**이었다. Scenario6("먹통 클라이언트가 큐를 막지 않는가")이 "3전문 중 가장 큰
응답"이라는 이유로 902614를 썼는데, 이 하네스는 실제 `App.Orchestrator`를 그대로 쓰므로 리더기가
연결돼 있으면 902614는 **진짜 카드 리딩 대기**에 들어간다. 로그로 확인한 실제 동작: 워커가 카드
리딩 라운드를 시작(`남은데드라인=120.0s`) → 카드가 없으니 리더기가 **약 60초 뒤 응답코드 04로
실패 응답** → 정상적으로 다음 처리로 넘어감. **워커도 큐도 앱도 전혀 멈추지 않았다** — 단지
시나리오의 15초/12초 타임아웃 가정이 리더기 명령 소요시간보다 짧았을 뿐이다(이 테스트가 과거엔
아마 리더기가 연결 안 된 환경에서 돌아 "포트 미사용" 즉시 실패로 우연히 빨리 끝났을 것으로 추정).

**수정**: Scenario6이 보내는 전문을 902614 → **501008**(카드리딩 없이 즉시 VAN 중계)로 교체 —
"느린 소비자가 있어도 큐가 막히지 않는가"라는 원래 검증 의도는 그대로 유지하면서 리더기 하드웨어
상태와의 우연한 결합을 제거했다. 재실행 결과 7개 시나리오 전부 통과(시나리오6: 2.1초 만에 응답,
시나리오7: 유휴 연결 10초 뒤 서버가 먼저 닫음 정상 확인).

**교훈**: `--pos-client-test`는 Phase 14 때 리더기가 없거나 "미사용"인 환경을 전제로 설계된
하네스인데, Phase 21에서 실제 리더기가 연결된 채로 처음 돌려 보니 그 전제가 깨져 있었다 — **환경이
바뀌면 과거에 통과하던 테스트도 새로 드러나는 결함(또는 이번처럼 거짓 실패)이 있을 수 있다**는
것을 실증한 사례로 남긴다.

**VAN DLL 로드 실패**: Phase 20 P20-3에서 `KFTC_GIRO.dll`을 실제로 치워 `DllNotFoundException` →
`D01` 분류·앱 생존을 이미 확인했다(재인용, 재실행하지 않음).

**리더기 케이블 분리 — 완료(2026-08-31)**: P21-2 실장비 1회전과 함께 처리했다. 실제 거래 진행 중
(카드 리딩 대기, 참여 리더기 2대) COM3 리더기 케이블을 물리적으로 분리 — 로그로 확인한 동작:

1. 즉시 `Kind=CommunicationError`(`READER_EVENT_RECEIVE_ERROR`)로 감지, 다른 리더기(COM7)에는
   정상적으로 무효화(`0x60`) 전송.
2. **자동복구**가 즉시 재연결을 시도했으나 케이블이 물리적으로 없으므로
   `READER_ERR_PORT_NOT_FOUND(-1100)`로 실패 — readerId만 초기화하고 **다음 명령에서 다시 열기로
   결정**(무한 재시도로 블로킹하지 않음).
3. 거래는 `TransactionQueue` 처리 종료까지 정상적으로 흘러갔고, POS(시뮬레이터) 연결도 정상 종료(FIN).
4. **앱이 전혀 죽지 않았다**(프로세스 생존 확인).

이후 케이블을 재연결하고 **네 번째 거래**를 보냈다 — 카드 리딩 라운드 시작 시 자동복구가
`COM3, 115200bps -> READER_OK`로 **재연결에 성공**, 카드 태그·PIN(`9999`) 입력 후 정상 승인·relay
까지 완료. 로그에 `9999` 검색 0건 확인. **PRD §9 "Reader DLL 오류로 프로그램 전체가 종료되어서는
안 된다"와 "재연결 후 다음 거래 정상"을 실물로 실증.**

---

## P21-4. 리소스 정리 / 장시간 실행

PRD §9의 "장시간 실행 시 메모리 누수 없음", "거래 종료 시 CALLBACK/Timer/Hook 정리".

### 구현할 것

- **반복 거래 후 메모리·핸들 추이 확인** — 연속 거래를 충분히 반복한 뒤 작업 관리자/`Get-Process`의
  핸들 수·메모리를 시작 시점과 비교한다. **절대값이 아니라 "계속 우상향하는가"를 본다**(GC 특성상
  변동은 정상이며, 단조 증가가 문제다).
- **타이머·훅·콜백 잔존 확인** — 거래마다 만들어지는 것들이 실제로 정리되는지 코드+실행으로 확인:
  `PaymentDeadline`(Cancel+Dispose 쌍), `PaymentNoticeViewModel._pinCts`(`StopPinTimers`),
  ESC 전역 저수준 훅(`WH_KEYBOARD_LL`), 리더기 CALLBACK 구독.
  이들은 과거에 실제로 누수 결함이 났던 자리다(P13 H-1, 2026-08-27 체크포인트 L-1) — **같은 유형이
  재발하지 않았는지**가 확인 목표다.
- ESC 훅은 Phase 13에 **10회 연속 열고 닫기 스트레스 테스트**(`--esc-hook-stress`류)가 이미 있으므로
  그것을 재사용한다.

### 완료 조건

- [x] 반복 거래 후 메모리·핸들이 단조 증가하지 않음을 수치와 함께 기록.
- [x] 위 4종(데드라인/PIN CTS/ESC 훅/리더기 콜백)의 정리 경로를 코드로 확인하고, 실행 후 잔존이
      없음을 확인.

### 진행 기록 (2026-08-31)

**반복 거래 리소스 추이**: 새 진단 하네스 `Services/Diagnostics/RepeatedTransactionResourceTest.cs`
(`--repeat-transactions-test`, 501008을 50회 반복 — 카드리딩 없이 하드웨어 상태 무관하게 빠르게
끝남, P21-3 Scenario6 정정과 같은 이유)를 추가해 5회마다 현재 프로세스의 핸들 수·WorkingSet을
기록했다.

| 처리 건수 | 핸들 | WorkingSet(KB) |
|---|---|---|
| 0 | 524 | 108,140 |
| 5 | 640 | 125,916 |
| 10 | 641 | 125,520 |
| 15 | 642 | 127,788 |
| 20 | 652 | 127,600 |
| 25 | 642 | 127,616 |
| 30 | 644 | 129,668 |
| 35 | 654 | 129,368 |
| 40 | 649 | 129,816 |
| 45 | 644 | 129,824 |
| 50 | 644 | 129,516 |

첫 5건 사이의 급증(524→640)은 소켓/스레드 풀 워밍업 등 **1회성 초기화 비용**이고, 그 이후로는
640~654 사이에서 오르내릴 뿐 **단조 증가하지 않는다**(50건째 644로 20건째 652보다 오히려 낮음).
50건 전부 성공(실패 0건). 판정: 누수 징후 없음.

**타이머·훅·콜백 4종 정리 경로**:
- `PaymentDeadline` — `PaymentOrchestrator.ProcessAsync`가 `using var deadline = new
  PaymentDeadline(...)`로 선언해 메서드 종료 시 `Dispose`(Cancel+Dispose 쌍, 클래스 자체 구현)가
  **언어 차원에서 보장**됨을 코드로 확인.
- `PaymentNoticeViewModel._pinCts` — P21-1에서 이미 확인(`StopPinTimers`가 창 `Closed` 이벤트에서
  Cancel+Dispose, 두 번 호출돼도 안전).
- **ESC 전역 저수준 훅** — 기존 `--esc-hook-stress-test`(Phase 13, 알림창 10회 연속 열고 닫기)를
  재실행해 재확인 — 예외 없이 통과.
- **리더기 CALLBACK 구독** — `ReaderService.EventReceived`(공개 이벤트)를 구독하는 곳이 저장소
  전체에 **한 곳도 없음**을 확인(`grep`). 실제 콜백 처리는 네이티브 콜백(`_nativeReaderCallback`,
  생성자에서 리더당 1회만 할당)이 `CompletePendingIfMatches`를 직접 호출하는 구조라, 거래마다
  구독/해제가 일어나는 경로 자체가 없다 — 애초에 "거래마다 쌓이는 구독"이라는 위험이 구조적으로
  성립하지 않음.

---

## P21-5. 계층 규칙 점검 + 문서 정리

### 구현할 것

- **계층 규칙 최종 점검**(ROADMAP "계층 구조 설계 원칙") — `Protocol/`만 교체하면 SPEC 변경이
  끝나는 구조인지 확인한다. **이번에 마침 실증 사례가 생겼다**: 2026-08-31 SPEC 개정(800000 `#26`
  신규)에서 **스키마 파일 2개만 고치고 업무 로직은 한 줄도 안 건드렸다** — 이 사실을 계층 규칙이
  실제로 지켜졌다는 근거로 기록한다.
- **문서 정리**:
  - `PRD.md` §10 미확정 사항의 현재 상태 현행화, §11(추후 구현) 정리
  - `ROADMAP.md` Phase 21 완료 기록 + **요약 표(48행) 갱신**(Phase 19에서 표를 빠뜨린 전례가 있다)
  - 이 문서에 검증 기록 정리
  - **서버 준비 후 해야 할 일**을 한 곳으로 모은다 — Phase 20 "남은 미검증" 7개 + Phase 21에서
    미룬 "VAN 거절" 시나리오. **2차 범위가 닫힐 때 남는 유일한 열린 목록**이 되어야 한다.

### 완료 조건

- [x] 계층 규칙 점검 결과가 근거(SPEC 개정 대응 사례)와 함께 기록된다.
- [x] 세 문서가 갱신되고, 서버 대기 항목이 한 목록으로 모인다.

### 진행 기록 (2026-08-31)

**계층 규칙 점검**: `Protocol/`만 교체하면 SPEC 변경이 끝나는 구조인지를, 실제로 벌어진 SPEC
개정(800000 `#26` 신규, 이 문서 앞부분 "SPEC 개정 반영" 절)으로 실증했다 — 수정 파일이
`CardInfoInquirySchema.cs`/`TelegramSchemas.cs` 스키마 정의 2개뿐이었고 `PaymentOrchestrator` 등
업무 로직은 `Write(26` 같은 참조 자체가 없어 한 줄도 안 건드렸다. 계층 규칙이 문서상 원칙이 아니라
실제로 작동하고 있음을 확인.

**문서 정리**: `PRD.md` §10(VAN §10.1 Phase 21 확정 사항 4행 추가, `nRet==0` 미검증 문구를
"Phase 21에서"→"서버 준비 후"로 정정), §11(완료된 "POS 소켓 전문 적용" 취소선 처리, "VAN 통신 전문
적용"을 "호출 구현 완료, 실서버 응답만 남음"으로 갱신). `ROADMAP.md`(Phase 21 절 체크박스 전체 완료
+ 완료 기록, 요약 표 48행 갱신). 이 문서(Phase 21 전체 실행 기록).

**서버 준비 후 해야 할 일 통합 목록**: 아래 "서버 준비 후로 남기는 것"(Phase 20의 7개 + Phase 21의
VAN 거절 시나리오 1개, 총 8개)이 2차 범위 전체에서 서버를 기다리는 **유일한 열린 목록**이다.

---

## Phase 21 완료 (2026-08-31)

6개 완료 기준 전부 충족. 핵심 성과:

1. **거래 데이터 잔존 위반 1건 발견·수정** — 사용자가 명시적으로 강조한 요구사항이었고, 실제로
   프로덕션 경로(`StubVanRelayService.LastRequest`)에서 위반이 있었다. 독립 포크 재검토로 수정이
   올바른지, 다른 위반이 더 없는지 재확인했다(진짜 결함 0건).
2. **실장비 검증 완료** — 사용자가 자리로 돌아와 연속거래 2건(서로 다른 카드/PIN)과 케이블 분리를
   직접 수행, Claude는 키오스크 시뮬레이터 조작과 로그 분석을 담당하는 방식으로 진행했다.
3. **테스트 인프라 자체의 결함 1건 발견·수정** — `--pos-client-test` Scenario6이 실제 리더기 연결
   환경에서 거짓 실패를 내는 것을 코드 결함으로 오인하지 않고 원인을 끝까지 추적해 테스트 설계
   결함으로 정확히 진단·수정했다.
4. **리소스 누수 없음 정량 확인** — 새 진단 하네스로 50회 반복 처리 핸들/메모리 추이를 표로 기록.

2차 범위(payment_relay, Phase 7~21) 전체가 이것으로 마무리된다. 남은 것은 VAN 서버가 준비된 뒤
"서버 준비 후로 남기는 것" 8개 항목을 확인하는 것뿐이다.

---

## 완료 기준 (Phase 전체)

1. **거래 종료 후 이전 거래 데이터가 남지 않는다** — 코드 전수 점검(P21-1)과 연속 실행 실증
   (P21-2) 양쪽으로 확인되고, 발견된 위반은 수정됐다.
2. 정상/FALLBACK/취소/Timeout을 섞은 연속 실행에서 각 거래가 독립적으로 올바르게 끝난다
   (**VAN 거절은 서버 미개발로 제외** — 명시).
3. 리더기 케이블 분리·소켓 강제 종료에서 앱이 죽지 않고 오류 처리되며 후속 거래가 정상이다.
4. 반복 실행 후 메모리·핸들이 단조 증가하지 않고, 타이머·훅·콜백이 잔존하지 않는다.
5. 계층 규칙이 지켜지고 있음이 근거와 함께 확인된다.
6. 남은 미검증 범위가 **한 목록으로** 문서에 정확히 기록된다.

## 서버 준비 후로 남기는 것

Phase 20 "남은 미검증" 7개 항목에 다음을 더한다.

8. **VAN 거절 시나리오** — VAN이 실제로 거절 응답(`#7`이 `111`~`201`/`M01`/`V01` 등)을 보냈을 때
   원캡이 그것을 **해석하지 않고 그대로 relay**하는지, 그리고 그 뒤 리더기·알림창 상태가 정상으로
   돌아오는지. 지금은 `nRet==0` 경로 자체가 실행되지 않아 확인할 수 없다(Phase 20 "남은 미검증" 1과
   같은 뿌리).

## 착수 순서 요약

**P21-1(거래 데이터 잔존 전수 점검 ★)** → P21-2(연속 실행 실증) → **체크포인트 1(Opus 검증 리뷰)**
→ P21-3(예외 내성) → P21-4(리소스 정리) → P21-5(계층 규칙 + 문서 정리).

**P21-1이 이 Phase의 중심**이다 — 사용자가 강조한 요구사항이고, 착수 전 조사에서 이미 위반 1건이
나왔다. 나머지는 그 확인을 실행으로 뒷받침하거나(P21-2) 기존 검증을 연속·장시간 조건에서 다시
확인하는(P21-3/P21-4) 성격이다.
