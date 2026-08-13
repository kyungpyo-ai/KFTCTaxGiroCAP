# ROADMAP: KFTCOneCAP WPF 재구현

> 이 문서는 `docs/PRD_WPF.md`(무엇을 만들지)를 기준으로, **어떤 순서로 얼마나 작은 단위로 구현할지**를 정의한다.
> 각 Phase는 "빌드되고 눈으로 확인 가능한 상태"로 끝나는 것을 원칙으로 한다 — 다음 Phase로 넘어가기 전 반드시 실행/캡처로 검증한다.

## 작업 방식 (바이브코딩 규칙)

1. Phase는 순서대로 진행한다. 이전 Phase의 "완료 기준"을 통과하지 못하면 다음으로 넘어가지 않는다.
2. 각 Phase 종료 시: `dotnet build` 성공 + 화면 실행 + (필요시) `docs/screenshots/`의 원본 캡처와 육안 대조.
3. PRD와 실제 구현이 어긋나는 지점을 발견하면, 코드보다 먼저 `PRD_WPF.md`를 갱신한 뒤 구현을 맞춘다(문서-코드 불일치 방지).
4. 각 Phase는 되도록 하나의 커밋 단위로 마무리한다(사용자가 커밋을 요청할 때).
5. 막히거나 PRD에 없는 결정이 필요하면 즉시 사용자에게 확인 — 임의로 스펙을 정하지 않는다.

## 진행 상태

| Phase | 내용 | 상태 |
|---|---|---|
| 0 | 프로젝트 스캐폴딩 | ✅ 완료 |
| 1 | 디자인 시스템(색상/폰트/공용 컨트롤 스타일) | ✅ 완료 |
| 2 | 홈 화면 — 정적 레이아웃 | ✅ 완료 |
| 3 | 홈 화면 — 인터랙션 & 트레이 | ✅ 완료 |
| 4 | 리더기 설정 — 정적 레이아웃 | ⬜ 대기 |
| 5 | 리더기 설정 — 비즈니스 로직(스텁) | ⬜ 대기 |
| 6 | 리더기 설정 — 포트열기 토글 & 레지스트리 | ⬜ 대기 |
| 7 | 리더기 설정 — 실제 COM 포트 연동 | ⬜ 대기 |
| 8 | 통합 검증 & 마무리 | ⬜ 대기 |

(상태 값: ⬜ 대기 / 🔄 진행중 / ✅ 완료 / ⏸ 보류)

---

## Phase 0 — 프로젝트 스캐폴딩

**목표**: 빈 WPF 앱이 빌드되고 실행되는 상태.

- [x] `.NET` 버전 결정 → **.NET Framework 4.8** (`net48`)로 확정. **Windows 7(SP1) 지원 요구사항** 때문에 .NET 6/8 등 최신 .NET(Core 계열)은 Windows 7에서 실행 자체가 불가하여 제외 — Win7~Win11까지 폭넓게 지원되는 .NET Framework 4.8이 유일한 현실적 선택지. (최초 스캐폴딩은 net8.0-windows로 시작했다가 요구사항 확인 후 net48로 재작업함 — 상세: PRD 1.4)
- [x] `KFTCOneCAP.Wpf.sln` + `src/KFTCOneCAP.Wpf/KFTCOneCAP.Wpf.csproj` (WPF, net48)
- [x] 폴더 구조: `src/KFTCOneCAP.Wpf/Views/`, `ViewModels/`, `Themes/`, `Assets/Icons/`, `Assets/Images/`
- [x] `App.xaml` / `App.xaml.cs`, `MainWindow.xaml`(빈 창 껍데기, 템플릿 기본값)
- [x] MVVM 기반 골격 → **CommunityToolkit.Mvvm 8.4.2** 추가 (표준적이고 소스 제너레이터 기반이라 보일러플레이트 최소화)
- [x] `dotnet build` 성공, 빈 창 실행 확인 (`mcp__windows__windows_launch` + `windows_screenshot`으로 실측 검증, net8.0-windows/net48 양쪽 모두 검증)

**완료 기준**: 빈 창이 뜬다. 스타일/로직 없음. — **통과 (2026-08-13, net48 기준 최종)**

---

## Phase 1 — 디자인 시스템

**목표**: PRD 2장(색상/타이포그래피/공용 컨트롤)을 리소스로 코드화.

- [x] `Themes/Colors.xaml` — PRD 2.1 색상 팔레트 전체를 `SolidColorBrush` 리소스로 정의 (홈 화면/리더기설정 화면 토큰 전체, 결과 정상/오류 칩 포함)
- [x] `Themes/Typography.xaml` — Pretendard/Malgun Gothic 폰트 리소스 + 화면별 크기/굵기 `Style` (일반 모드 기준값 우선, 컴팩트 분기는 Phase 8 이후 별도 검토). PRD 표의 pt 값은 96dpi 기준 `pt × 96/72` 로 환산해 WPF `FontSize`(px 단위)에 적용
- [x] Pretendard 폰트 파일 확보 및 포함 — 원본 `C:\Project\MerchantSetup_OnPaintIcons_Clean_CP949\fonts\`의 Regular/Medium/Bold 3종을 `src/KFTCOneCAP.Wpf/Assets/Fonts/`에 복사, `Resource` 빌드 액션으로 csproj에 포함(OFL 라이선스라 임베딩 문제 없음). **ExtraBold(800) 파일이 원본에 없어 홈 타이틀은 Bold(700)로 폴백** — 추후 ExtraBold 파일이 확보되면 `Typography.xaml`의 `HomeTitleTextStyle`만 교체하면 됨
- [x] `Themes/Buttons.xaml` — `CModernButton` 대응 `Button` Style: `AutoButtonStyle`/`DefaultButtonStyle`/`PrimaryButtonStyle`/`ReaderButtonStyle` 4종, 호버/눌림 Trigger 포함
- [x] `Themes/ComboBox.xaml` — `CSkinnedComboBox` 대응 라운드(6px) 콤보 Style, 포커스 시 2px 파란 보더로 전환하는 커스텀 `ControlTemplate`
- [x] `Themes/ToggleSwitch.xaml` — `CModernToggleSwitch` 대응 커스텀 토글(좌측 라벨 + 우측 노브, `BackEase` 슬라이드 애니메이션). `IsPending` 스피너는 이번 Phase 필수 아니라 시각 요소(회전 링 Border)만 템플릿에 남겨두고 트리거 배선은 보류
- [x] `Themes/TextBox.xaml` — `CSkinnedEdit` 대응. 홈/리더기설정 화면에는 없지만(가맹점 설정 화면용으로 선구축) ComboBox와 동일한 KFTCInputTheme(hover/focus 보더)을 재사용
- [x] 위 리소스를 모아보는 임시 "스타일 갤러리" 창(`Views/StyleGalleryWindow.xaml`, 개발용, 최종 산출물 아님)으로 육안 확인 — `App.xaml.cs`의 시작 창이 **임시로 갤러리 창으로 변경되어 있음**(다음 Phase에서 `MainWindow`로 되돌리거나 정리 필요)

**완료 기준**: 스타일 갤러리 창에서 버튼/콤보/토글/텍스트박스가 PRD 색상·폰트와 일치하는 것을 스크린샷 대조로 확인 — **통과 (2026-08-13)**. 색상 스와치 22종 HEX 일치, 타이포그래피 12종 크기/굵기 확인, 버튼 4변형 호버·눌림 동작, 콤보 hover/활성 보더 전환, 토글 슬라이드+눌림 애니메이션, 텍스트박스 클리핑 없음 모두 육안 확인됨.

**Phase 1 진행 중 발견/수정된 버그 및 트렌드 개선 요약** (전부 2026-08-13, 사용자 실사용 테스트 피드백 기반 — 각 항목의 근본 원인·수정 상세는 해당 `Themes/*.xaml` 파일 상단 주석에 기록되어 있으므로 여기서는 결과만 요약):
- **콤보박스/토글 hover 트리거 누락**: 원본(`ModernUI.h/cpp`)엔 hover 색상이 있는데 WPF 이식 시 누락되어 있었음 → 원본 실측 색상값 그대로 추가.
- **버튼/콤보/토글 클릭 피드백 부족**: 전 컨트롤에 `IsPressed` 시 살짝 축소(scale)되는 공통 클릭 피드백 추가.
- **콤보박스 "선택 후에도 클릭 상태가 계속 유지됨" (실버그)**: 원인은 `IsKeyboardFocusWithin` 트리거 — `IsDropDownOpen` 기준으로 교체.
- **토글 "OFF해도 파란색 유지됨" (실버그)**: 두 개의 `ColorAnimation` Storyboard가 같은 속성을 동시에 건드려 우선순위 충돌 — 색상 전환을 전부 즉시 반영 Setter로 교체.
- **텍스트박스 텍스트 잘림 (실버그)**: `TextBox`는 `ComboBox`와 달리 폰트 전체 줄높이(ascent+descent)를 예약하는데 기존 `Height=36`이 부족 → `MinHeight=44` 기본값으로 교체.
- **콤보박스 드롭다운 항목의 Windows 기본 파란 하이라이트**: `SkinnedComboBoxItemStyle` 추가로 라운드/플랫 디자인 통일.
- **hover에서 배경까지 칠하는 건 트렌드 아님**: 입력 컨테이너류(텍스트박스/닫힌 콤보박스)는 보더 색상만 변경, 배경 틴트는 제거(버튼/드롭다운 항목처럼 클릭 가능 영역 전체를 보여주는 요소는 배경 hover 유지).
- **레이아웃 흔들림 버그 (실버그)**: hover/focus에서 `BorderThickness`를 바꾸는 게 실제 렌더링 크기를 키워 아래 요소를 밀어냄 → 두께를 1.5px로 고정하고 상태는 색상만으로 표현.
- **버튼/토글 클릭 시 텍스트박스 포커스 표시가 안 풀림 (실버그)**: `Focusable="False"`라 키보드 포커스가 실제로 옮겨가지 않던 것이 원인 → `Focusable="True"` + `FocusVisualStyle="{x:Null}"`로 교체.

이로써 버튼/콤보박스(닫힌 상태+드롭다운 항목)/토글/텍스트박스 전 컨트롤이 호버(색상만, 두께 고정)·눌림(스케일)·비활성(반투명) 3단계 상호작용 패턴으로 통일됨. **Phase 1 최종 완료.**

---

## Phase 2 — 홈 화면: 정적 레이아웃

**목표**: `docs/screenshots/home_screen.png`과 레이아웃이 일치하는 정적 화면(애니메이션/트레이 제외).

> **컴팩트 모드 대응 방침 (2026-08-13 확정, PRD 미확정 사항 #6)**: 원본처럼 세로 해상도 ≤800px에서 별도 치수 세트로 전환하는 기능을 최종적으로 구현한다(전환 메커니즘: 런타임 작업영역 높이 감지 + `ResourceDictionary` 스왑 — Phase 8에서 배선). 실제 배선/컴팩트 값 확정은 Phase 8 몫이지만, **Phase 2부터 지금** 지켜야 할 규칙이 하나 있다 — 홈/리더기설정 화면에서 쓰는 폭/높이/폰트크기 등 치수는 XAML에 리터럴로 박지 말고 `Themes/Layout.xaml`(신규, 이번 Phase에서 생성) 같은 전용 리소스 딕셔너리에 일반 모드 값으로 키를 만들어 참조한다. 이렇게 해두면 Phase 8에서 컴팩트 세트를 가진 두 번째 딕셔너리를 만들어 스왑하는 것만으로 끝나고, Phase 2~7에서 이미 만든 XAML을 다시 고칠 필요가 없다.

- [x] 창 크롬: 고정 크기(1104×567, `ResizeMode=NoResize`), 흰 타이틀바(Win10 1809+ `DwmSetWindowAttribute` 조건부 적용, 미지원 OS는 no-op), 타이틀 "KFTCOneCAP Plus Ver 3.0.9 | 모듈 Ver 524" — **PRD 3.2/6장 #7 미확정**: 버전 조합 규칙(런타임에 앱버전/모듈버전을 어디서 읽어와 조합하는지)은 원본 소스에서도 확인 불가(빌드-소스 불일치, PRD #7) → 스크린샷 실측 문자열을 정적 리터럴로 임시 적용, TODO 주석으로 남김
- [x] 헤더: 로고(`Assets/Images/img_ci_mark.png` 실제 자산, 아래 "추가 보정" 참고) + "KFTCOneCAP" + "Plus" 뱃지 + 서브타이틀
- [x] 우상단: 로그 전송(132×36) / 최신 버전 업데이트(205×36) 버튼 — 정적 배치만, 클릭 동작 없음(Phase 3)
- [x] 카드 4개 정적 배치: 아이콘(PRD 3.4 벡터 형태를 `Path`/`GeometryGroup`(EvenOdd cutout)으로 구현) + 제목 + 설명
- [x] 하단: 구분선 + 최소화(184×40)/프로그램 종료 버튼 (정적) — 버튼 문구는 PRD 표 리터럴("종료") 대신 원본 소스(`SetWindowText`)·스크린샷 실측과 동일한 "프로그램 종료"로 반영(스크린샷/소스 우선 원칙)
- [x] PRD 3.3 레이아웃 수치(marginX=50/cardGap=18/cardVisualH=260 등)를 전부 `Themes/Layout.xaml` 리소스 키로 정의 후 참조(리터럴 미사용)

**완료 기준**: 실행 화면을 캡처해 `docs/screenshots/home_screen.png`과 나란히 비교 — **통과 (2026-08-13)**. 창 크기 1104×567 픽셀 단위까지 원본과 정확히 일치. 헤더(로고/타이틀/Plus뱃지/서브타이틀), 카드 4개(아이콘/제목/설명 위치), 헤더·하단 버튼 텍스트/배치를 크롭 비교한 결과 레이아웃·텍스트·색상이 육안상 사실상 동일. 전체 픽셀 diff(3px 샘플링, 임계값 60)로는 ~6% 차이가 나왔지만 크롭 확대 대조 결과 대부분 폰트 안티앨리어싱/헤일로 차이이고 실제 레이아웃 밀림은 아님을 확인.

**임의 판단/근사 처리 항목** (PRD 미확정 또는 원본 자산 부재로 인해):
- ~~헤더 로고~~ — **해결 (아래 "추가 보정" 참고)**: 사용자가 실제 `img_ci_mark.png` 자산을 제공해 `Image`로 교체 완료, 벡터 근사는 제거됨.
- 타이틀바/서브타이틀 버전 문자열: PRD 6장 #7과 동일한 사유로 동적 조합 규칙 미확정 → 스크린샷 리터럴 그대로 정적 적용, TODO 주석 남김.
- 카드 아이콘 벡터 좌표: PRD 3.4의 치수 설명(예: "19×23.5, radius 3")을 28×28 유닛 좌표로 직접 환산해 구현 — 원본 실제 벡터 좌표(원본은 GDI+ 커스텀 드로잉)와 1px 단위로 동일하지는 않으나 스크린샷 크롭 비교 결과 형태가 충분히 유사함을 확인.

**추가 보정 (2026-08-13, 사용자 피드백)**:
- 헤더 로고를 실제 자산(`Assets/Images/img_ci_mark.png`, csproj `Resource`로 포함)을 로드하는 `Image`로 교체 — 위 "임의 판단" 항목의 Path 벡터 근사 제거.
- 헤더 로그전송/업데이트, 하단 최소화/종료 4개 버튼의 테두리·배경을 원본(`ModernUI.cpp CModernButton::DrawItem`, isLogTransfer/isUpdate/isExit 분기) 및 스크린샷 실측 기준으로 재검증 — 평상시 테두리·배경 없음(호버 시에만 옅은 배경)으로 `Buttons.xaml`의 `DefaultButtonStyle`/`UpdateButtonStyle`/`AutoButtonStyle` 수정, 로그전송/업데이트 아이콘은 호버 무관 고정색(파랑/초록) 유지하도록 Content를 아이콘+텍스트 2개 `TextBlock`으로 분리.
- 카드 내부 아이콘-제목 사이 행이 `*`(가변)이라 카드 높이가 고정된 상태에서 제목/설명이 카드 하단까지 밀려 내려가던 버그 수정(`Layout.xaml`에 `HomeCardIconTitleGapGridLength=24` 고정값 리소스 추가, 4개 카드 모두 반영).

---

## Phase 3 — 홈 화면: 인터랙션 & 트레이

**목표**: 카드 호버/눌림 애니메이션, 카드 클릭 시 서브 창 오픈, 트레이 최소화/메뉴.

- [x] 카드 호버 애니메이션(리프트 + 글로우) — PRD 3.4, `Storyboard`/`EasingFunction`으로 근사
- [x] 카드 눌림 애니메이션(축소 + 배경색 보간 + 아이콘 색 반전)
- [x] 카드 클릭 → 해당 서브 창 `ShowDialog()` (리더기 설정만 우선 연결, 나머지는 Phase 8 이전까지 비활성/플레이스홀더 — PRD 미확정 사항 #5 확인)
- [x] 최소화 버튼 → 트레이로 이동 (`System.Windows.Forms.NotifyIcon` interop)
- [x] 트레이 우클릭 커스텀 메뉴(PRD 3.6: 열기/리더기 설정/가맹점 설정/구분선/종료)
- [x] 트레이 더블클릭 → 창 복원

**완료 기준**: 카드 호버/클릭 체감이 원본과 유사, 트레이 최소화·복원·메뉴 동작 확인 — **통과 (2026-08-13)**.

**구현 요약**:
- `Themes/Buttons.xaml` `HomeCardButtonStyle`: `ControlTemplate`에 카드별 로컬(비공유) `SolidColorBrush` 2개(Bd 배경/테두리) + `TransformGroup`(Scale+TranslateY) + `DropShadowEffect`를 추가하고, `IsMouseOver`/`IsPressed` 트리거에 `Storyboard`(EnterActions/ExitActions)로 리프트(-5px, back-ease-out)/글로우(Opacity 0→0.35)/눌림(Y+6px, Scale 0.96, 배경 `CardFillPressed`로 보간, 테두리 Opacity 0)을 구현. 공유 리소스 브러시를 직접 애니메이션 타겟으로 잡으면 카드 4개가 색을 공유해버리는 문제가 있어, 템플릿 내부에 카드별 로컬 브러시를 새로 만드는 방식으로 우회(주석에 근거 기록).
- 아이콘 배경/글리프 반전(PRD 3.4)은 별도 `HomeCardIconBoxStyle`/`HomeCardIconGlyphStyle`(`Buttons.xaml`)로 분리 — 카드 콘텐츠의 `Border`/`Path`가 `ControlTemplate` 밖(ContentPresenter로 주입되는 콘텐츠)이라 템플릿 트리거로 직접 건드릴 수 없어, `RelativeSource AncestorType=Button`로 상위 카드의 `IsPressed`를 참조하는 `DataTrigger`로 구현.
- `Views/HomeWindow.xaml.cs`: 카드 4개 `Click` 핸들러 배선. 리더기 설정 카드는 `MessageBox`로 "Phase 4에서 구현 예정" 안내(코드 주석에 TODO로 실제 `ShowDialog()` 교체 지점 명시). 나머지 3개 카드(가맹점 설정/결제/전표 설정)는 PRD 6장 #5 미확정 상태라 임의로 비활성화하지 않고 "구현 범위 밖" 안내 `MessageBox`로 통일(카드 자체는 계속 클릭 가능/애니메이션 동작 — 이 판단은 아래 "임의 판단" 항목 참고).
- 트레이: `System.Windows.Forms.NotifyIcon`(csproj에 `UseWindowsForms=true` 추가) + `ContextMenuStrip`(열기/리더기 설정/가맹점 설정/구분선/프로그램 종료). WPF `ContextMenu` 대신 WinForms `ContextMenuStrip`을 택함(NotifyIcon에 WPF ContextMenu를 붙이려면 `SetForegroundWindow` 포커스 트릭이 별도로 필요해 안정성이 떨어짐 — 작업 지시사항에서 "완전히 동일한 커스텀 스타일까지는 불필요"를 명시적으로 허용해 표준적인 방식 채택). 더블클릭(`NotifyIcon.DoubleClick`) 시 `Show()`+`WindowState=Normal`+`Activate()`로 복원. 최소화 버튼은 `Hide()`로 작업표시줄/Alt-Tab에서까지 완전히 사라지도록 처리(원본 "트레이로 이동" 동작과 일치). 창 닫기(X)/프로그램 종료 시 `NotifyIcon.Dispose()`로 트레이 아이콘 잔상 방지.
- 트레이 아이콘 그래픽은 전용 `.ico` 자산이 아직 없어 실행 파일 내장 기본 아이콘(`Icon.ExtractAssociatedIcon`)을 임시로 재사용(TODO 주석 남김, Phase 3 범위에 아이콘 자산 제작 불포함).

**검증 결과**:
- `dotnet build` 성공(경고 0/오류 0).
- 카드 4개 클릭 → 각각 올바른 안내 메시지박스가 뜨는 것을 실측 확인(리더기 설정: "Phase 4부터 구현 예정" / 나머지 3개: "구현 범위 밖").
- 최소화 버튼 클릭 → `mcp__windows__windows_list_windows` 결과에서 창이 완전히 사라짐(Alt-Tab/작업표시줄에서 숨김, 트레이로 이동 성공) 확인. 동시에 `Get-Process`로 프로세스가 계속 실행 중임을 확인해 `NotifyIcon` 생성 과정에서 예외가 나지 않았음을 간접 검증.
- 프로그램 종료 버튼 및 타이틀바 X 버튼 각각 클릭 후 `Get-Process`로 프로세스가 완전히 종료되고(exit code 1 = 프로세스 없음) 트레이 아이콘 잔상이 남지 않음을 확인.
- **검증 못한 부분(명시)**: 실제 시스템 트레이 아이콘 자체(우클릭 메뉴 열기, 더블클릭 복원)는 이 저장소의 `mcp__windows__*` 도구가 시스템 알림 영역(overflow chevron 포함) UI를 접근성 트리로 잡지 못해 실제 마우스 우클릭/더블클릭으로 재현하지 못했다 — 코드 리뷰(표준 `NotifyIcon`+`ContextMenuStrip` API 패턴, `Show`/`WindowState`/`Activate` 표준 복원 코드)와 "최소화 후 프로세스 생존+창 숨김" 간접 신호로 대체 검증함(작업 지시사항에서 사전에 허용한 대체 검증 방식).
- 카드 호버(`IsMouseOver`) 리프트/글로우 애니메이션은 자동화 도구(accessibility Invoke)로는 실제 마우스 enter/press 이벤트가 발생하지 않아 스크린샷 재현이 불가능했으나, **사용자가 실제 마우스로 직접 확인 — 정상 동작함(2026-08-13)**.

**임의 판단**: 가맹점 설정/결제/전표 설정 카드를 `IsEnabled=false`로 비활성화하지 않고 계속 클릭 가능하게 두어 "구현 범위 밖" 안내만 띄우도록 처리(PRD 6장 미확정 사항 #5). 비활성화 시 시각적으로 원본과 달라 보일 위험(원본은 모든 카드가 정상 동작하는 것처럼 보임)이 더 크다고 판단했으나, 이는 PM 확인이 필요한 임의 결정이다. 트레이 메뉴의 "가맹점 설정" 항목도 동일한 안내로 통일.

---

## Phase 4 — 리더기 설정: 정적 레이아웃

**목표**: `docs/screenshots/reader_setup.png`과 일치하는 정적 화면.

- [ ] 창 크롬: 고정 크기, 타이틀 "리더기 설정"
- [ ] 헤더(아이콘+제목+부제) — 공용 `ModernUIHeader` 대응 컴포넌트로 홈 화면과 스타일 공유 검토
- [ ] "포트 설정" 섹션 카드: 리더기1/2 카드 2개 정적 배치(콤보, 버튼 5개, 토글, info 버튼) — PRD 4.4
- [ ] "무결성 체크 정보" 섹션: 조회기간 콤보 + 조회 버튼 + 리스트(컬럼 헤더는 PRD 4.6 실측값 사용)
- [ ] 하단 확인/취소 버튼

**완료 기준**: 실행 화면 캡처를 `docs/screenshots/reader_setup.png`과 대조.

---

## Phase 5 — 리더기 설정: 비즈니스 로직 (스텁)

**목표**: PRD 4.7~4.9, 4.12의 로직을 스텁 수준으로 이식(원본도 실통신 미구현).

- [ ] 버튼 클릭(초기화/상태체크/키다운로드/무결성체크/업데이트) → 로딩 상태(스피너+텍스트) → 3초 후 자동 완료 (원본 동작 재현, `Task.Delay` 기반 비동기로 — UI 스레드 블로킹 금지)
- [ ] 조회 버튼 → 로딩(2초) → 더미 데이터로 리스트 갱신 (조회기간별 행 수: 오늘 3 / 7일 5 / 30일·100일 10)
- [ ] AOP 제약 로직(PRD 4.11): `INTERLOCK` 값에 따른 활성/비활성 상태 전이
- [ ] 스냅샷/dirty-check(PRD 4.13): 콤보1/2 + 멀티패드1/2 추적, 취소 시 확인창
- [ ] 확인(OK) 검증: TRANSINFO_AOP 모드 포트 미지정 시 저장 차단

**완료 기준**: 각 버튼 클릭 시 로딩→완료 흐름 확인, AOP 시나리오별(레지스트리 값 수동 변경) 활성화 상태 육안 확인.

---

## Phase 6 — 포트 열기 토글 & 레지스트리 연동

**목표**: PRD 4.8 포트 열기 토글의 확인창 → 백그라운드 처리 → 타임아웃 → 레지스트리 저장 흐름, 정보 팝오버.

- [ ] 포트 열기 토글 클릭 → 확인창(문구는 PRD 미확정 사항 #2 확정 후 반영) → 되돌림/재적용
- [ ] 비동기 처리(2.5초 시뮬레이션) + 10초 타임아웃
- [ ] 성공 시 리더기2 강제 비활성화(단일 포트 인터락, TRANSINFO_AOP 예외)
- [ ] 레지스트리 저장(`PORT_ALWAYSOPEN` 반전 인코딩 — PRD 5장 참고, 레지스트리 공유 여부는 미확정 사항 #1 확인 후 최종 확정)
- [ ] 정보 팝오버(포트열기/멀티패드) — PRD 4.10 문구 그대로

**완료 기준**: 토글 ON/OFF 흐름이 원본과 동일하게 동작, 레지스트리 값 확인.

---

## Phase 7 — 실제 COM 포트 연동

**목표**: 더미 콤보 대신 실제 시스템 COM 포트 목록 사용.

- [ ] `System.IO.Ports.SerialPort.GetPortNames()`로 포트 열거, "미사용" 항목 최상단 고정
- [ ] 저장된 포트가 목록에 없으면 `"<port>(사용불가)"` 형태로 유지 (PRD 4.13)

**완료 기준**: 실제 COM 포트 연결/해제 시 콤보 목록이 갱신되는지 확인(가능한 환경에서).

---

## Phase 8 — 통합 검증 & 마무리

- [ ] 홈 화면 + 리더기 설정 화면 전체를 원본과 나란히 스크린샷 비교
- [ ] PRD 6장 "미확정 사항" 전체 재확인 — 남은 항목 있으면 명시적으로 보류 처리
- [ ] 컴팩트 모드(≤800px 높이) 대응 구현 — 방침은 확정됨(PRD 미확정 사항 #6, Phase 2 안내 참고): 일반/컴팩트 두 번째 `Themes/Layout.xaml` 세트 작성 + 런타임 작업영역 높이 감지로 시작 시(및 필요시 화면 전환 시) `ResourceDictionary` 스왑 배선
- [ ] 코드 정리 (사용하지 않는 리소스/스타일 정리)

---

## 참고 문서
- `docs/PRD_WPF.md` — 요구사항 정의
- `docs/screenshots/home_screen.png`, `docs/screenshots/reader_setup.png` — 원본 실측 캡처
