# 실행계획서: 결제 중계 기능 (Phase 7~13)

> `PRD.md`(무엇을) → `ROADMAP.md`(어떤 순서로) → **이 문서(Task 단위로 무엇을 어떻게, 어디까지 하면 끝인지)**.
> 실제 코드 작성은 이 문서의 Task를 순서대로 따라간다.
>
> **Phase 14~18은 아직 작성하지 않았다** — 앞 Phase의 실장비 검증 결과에 따라 뒤쪽 계획이 조정될 여지가
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

**완료 조건**
- [ ] 홈 화면이 최소화(트레이)된 상태에서 알림창을 띄움 → 홈 화면이 복원되지 않음
- [ ] 홈 화면이 전면에 떠 있는 상태에서 알림창을 띄우고 닫음 → 홈 화면이 다시 전면에 오지 않음
- [ ] 알림창을 닫은 뒤 포커스가 직전 프로그램(예: 메모장)으로 돌아감

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

**완료 조건**
- [ ] 포커스가 **메모장에 있는 상태**에서 ESC → 알림창이 취소로 반응(전역 훅이 실제로 동작함을 입증)
- [ ] 알림창을 닫은 뒤 메모장에서 ESC가 정상 동작(훅이 해제되어 우리 앱이 관여하지 않음)
- [ ] ESC 이외의 키(한글 입력, Ctrl+C 등)가 알림창이 떠 있는 동안에도 다른 프로그램에서 정상 동작
- [ ] 알림창을 **10회 연속** 열고 닫은 뒤에도 훅이 남아 있지 않음
- [ ] 창 X/Alt+F4/강제 종료 등 **모든 경로**에서 해제가 일어남을 로그로 확인
- [ ] 훅 콜백 델리게이트가 필드로 보관되어 있음(코드 확인)

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

**완료 조건**
- [ ] `Services/Payment/` 아래에 WPF 타입 참조가 없음(grep으로 확인)
- [ ] 백그라운드 스레드에서 `Show`/`ChangeState`/`Close`를 호출해도 예외 없이 동작
- [ ] 닫힌 뒤 `ChangeState`/`Close` 호출 시 예외 없이 무시되고 로그만 남음
- [ ] `Canceled` 이벤트가 취소 1회당 정확히 1번 발생

## P13-7. 단독 검증 (Flow 없이)

Phase 15가 없어 결제 요청으로 알림창을 띄울 수 없으므로 **개발용 임시 트리거**를 만든다.

- 기존 `Views/StyleGalleryWindow`(개발용 화면)에 트리거를 붙이거나, 그에 준하는 임시 진입점을 둔다.
- **최종 산출물이 아님을 코드 주석에 명시**하고, Phase 15에서 실제 Flow가 연결되면 제거 여부를 재검토한다.

**검증 매트릭스** — 아래를 실제로 실행해 확인한다.

| # | 시나리오 | 확인 |
|---|---|---|
| 1 | IC → FALLBACK → PROCESSING 순차 전환 | 창 재생성/깜빡임 없음, 배경·문구가 크로스페이드로 부드럽게 전환, 카드 반복 애니메이션 정상 |
| 2 | 메모장을 띄운 상태에서 알림창 표시 | 항상 위에 보임 |
| 3 | 포커스가 메모장에 있을 때 ESC | 취소로 반응 |
| 4 | 알림창 닫은 뒤 메모장에서 ESC | 정상 동작(훅 해제됨) |
| 5 | 취소 버튼 연타 + ESC 연타 | 취소 통지 1회 |
| 5-2 | `VanProcessing` 상태에서 취소 버튼 클릭 + ESC | 취소 발생 안 함, 버튼이 비활성으로 보임, ESC는 메모장에 전달됨 |
| 6 | 홈 화면 최소화 상태에서 표시/닫기 | 홈 화면 복원 안 됨 |
| 7 | 홈 화면 전면 상태에서 표시/닫기 | 홈 화면이 다시 전면에 오지 않음 |
| 8 | 열고 닫기 10회 반복 | 핸들/메모리 증가 없음, 훅 잔존 없음 |
| 9 | 백그라운드 스레드에서 제어 호출 | 예외 없이 동작 |

**완료 조건**
- [ ] 위 9개 시나리오를 모두 실행하고 결과를 이 Task 아래에 기록(확인 못 한 것은 이유를 적고 체크하지 않는다)
- [ ] `dotnet build` 경고 0/오류 0
- [ ] 기존 두 화면(홈/리더기 설정)에 회귀 없음 — 특히 **전역 훅이 리더기 설정 화면의 키 입력을 방해하지
      않는지** 확인

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

## Phase 13 완료 후

- 알림창 정교화(세부 각도/타이밍/자산 교체) 재개 시점은 사용자가 다시 지시한다. 재개 시 위 "시행착오
  기록"부터 먼저 읽는다 — 같은 실수(카드 기울기 vs 화살표 방향 혼동, 원 애니메이션의 뒤쪽 절반 노출)를
  반복하지 않기 위함이다.
- P13-2(취소 1회 제한)/P13-4(표시 정책)/P13-5(ESC 전역 훅)/P13-6(제어 진입점 계약)/P13-7(9개 시나리오
  검증)은 아직 시작 전이다.
- Opus 전체 검증 리뷰 → Sonnet 수정(프로젝트 워크플로우)은 P13-2~7까지 마친 뒤 진행한다.
- Phase 14(소켓 서버 + 단일 워커 Queue) 실행계획서를 그때 작성한다. 착수 전에 **POS↔앱 소켓 전문**이
  여전히 미확정이면(ROADMAP "남은 미확정 사항" #2) 예정대로 임시 테스트 전문으로 진행한다.
