# ROADMAP: KFTCOneCAP WPF 재구현

> 이 문서는 `PRD_WPF.md`(무엇을 만들지)를 기준으로, **어떤 순서로 얼마나 작은 단위로 구현할지**를 정의한다.
> 각 Phase는 "빌드되고 눈으로 확인 가능한 상태"로 끝나는 것을 원칙으로 한다 — 다음 Phase로 넘어가기 전 반드시 실행/캡처로 검증한다.

## 작업 방식 (바이브코딩 규칙)

1. Phase는 순서대로 진행한다. 이전 Phase의 "완료 기준"을 통과하지 못하면 다음으로 넘어가지 않는다.
2. 각 Phase 종료 시: `dotnet build` 성공 + 화면 실행 + (필요시) `screenshots/`의 원본 캡처와 육안 대조.
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
| 4 | 리더기 설정 — 정적 레이아웃 | ✅ 완료 |
| 5 | 리더기 설정 — 비즈니스 로직(스텁) + 레지스트리 저장/dirty-check (AOP 제약·TRANSINFO_AOP 검증·포트열기 토글은 보류) | ✅ 완료 |
| 6 | 통합 검증 & 마무리 | ✅ 완료 |

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
- [x] `Themes/Typography.xaml` — Pretendard/Malgun Gothic 폰트 리소스 + 화면별 크기/굵기 `Style` (일반 모드 기준값 우선, 컴팩트 분기는 Phase 6 이후 별도 검토). PRD 표의 pt 값은 96dpi 기준 `pt × 96/72` 로 환산해 WPF `FontSize`(px 단위)에 적용
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

**목표**: `screenshots/home_screen.png`과 레이아웃이 일치하는 정적 화면(애니메이션/트레이 제외).

> **컴팩트 모드 대응 방침 (2026-08-13 확정, PRD 미확정 사항 #6)**: 원본처럼 세로 해상도 ≤800px에서 별도 치수 세트로 전환하는 기능을 최종적으로 구현한다(전환 메커니즘: 런타임 작업영역 높이 감지 + `ResourceDictionary` 스왑 — Phase 6에서 배선). 실제 배선/컴팩트 값 확정은 Phase 6 몫이지만, **Phase 2부터 지금** 지켜야 할 규칙이 하나 있다 — 홈/리더기설정 화면에서 쓰는 폭/높이/폰트크기 등 치수는 XAML에 리터럴로 박지 말고 `Themes/Layout.xaml`(신규, 이번 Phase에서 생성) 같은 전용 리소스 딕셔너리에 일반 모드 값으로 키를 만들어 참조한다. 이렇게 해두면 Phase 6에서 컴팩트 세트를 가진 두 번째 딕셔너리를 만들어 스왑하는 것만으로 끝나고, Phase 2~5에서 이미 만든 XAML을 다시 고칠 필요가 없다.

- [x] 창 크롬: 고정 크기(1104×567, `ResizeMode=NoResize`), 흰 타이틀바(Win10 1809+ `DwmSetWindowAttribute` 조건부 적용, 미지원 OS는 no-op), 타이틀 "KFTCOneCAP Plus Ver 3.0.9 | 모듈 Ver 524" — **PRD 3.2/6장 #7 미확정**: 버전 조합 규칙(런타임에 앱버전/모듈버전을 어디서 읽어와 조합하는지)은 원본 소스에서도 확인 불가(빌드-소스 불일치, PRD #7) → 스크린샷 실측 문자열을 정적 리터럴로 임시 적용, TODO 주석으로 남김
- [x] 헤더: 로고(`Assets/Images/img_ci_mark.png` 실제 자산, 아래 "추가 보정" 참고) + "KFTCOneCAP" + "Plus" 뱃지 + 서브타이틀
- [x] 우상단: 로그 전송(132×36) / 최신 버전 업데이트(205×36) 버튼 — 정적 배치만, 클릭 동작 없음(Phase 3)
- [x] 카드 4개 정적 배치: 아이콘(PRD 3.4 벡터 형태를 `Path`/`GeometryGroup`(EvenOdd cutout)으로 구현) + 제목 + 설명
- [x] 하단: 구분선 + 최소화(184×40)/프로그램 종료 버튼 (정적) — 버튼 문구는 PRD 표 리터럴("종료") 대신 원본 소스(`SetWindowText`)·스크린샷 실측과 동일한 "프로그램 종료"로 반영(스크린샷/소스 우선 원칙)
- [x] PRD 3.3 레이아웃 수치(marginX=50/cardGap=18/cardVisualH=260 등)를 전부 `Themes/Layout.xaml` 리소스 키로 정의 후 참조(리터럴 미사용)

**완료 기준**: 실행 화면을 캡처해 `screenshots/home_screen.png`과 나란히 비교 — **통과 (2026-08-13)**. 창 크기 1104×567 픽셀 단위까지 원본과 정확히 일치. 헤더(로고/타이틀/Plus뱃지/서브타이틀), 카드 4개(아이콘/제목/설명 위치), 헤더·하단 버튼 텍스트/배치를 크롭 비교한 결과 레이아웃·텍스트·색상이 육안상 사실상 동일. 전체 픽셀 diff(3px 샘플링, 임계값 60)로는 ~6% 차이가 나왔지만 크롭 확대 대조 결과 대부분 폰트 안티앨리어싱/헤일로 차이이고 실제 레이아웃 밀림은 아님을 확인.

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
- [x] 카드 클릭 → 해당 서브 창 `ShowDialog()` (리더기 설정만 우선 연결, 나머지는 Phase 6 이전까지 비활성/플레이스홀더 — PRD 미확정 사항 #5 확인)
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

### 알려진 이슈 (해결, 2026-08-14): 카드 클릭 후 그 카드만 hover 리프트 재발동 안 됨 → 리프트 효과 자체 제거

**증상**: 카드 4개 중 하나에 처음 마우스를 올리면 리프트(-5px)+글로우가 정상 발동한다. 그 카드를 **한 번이라도 클릭**하면(모달 `MessageBox` 오픈→닫힘), **그 카드에서만** 이후 마우스를 올려도 리프트가 다시 발동하지 않는다. 색상/눌림 등 다른 반응, 그리고 클릭하지 않은 다른 카드는 계속 정상.

**시도했다가 실패한 접근들** (전부 되돌림, `Themes/Buttons.xaml`은 Phase 3 커밋 상태 그대로):
1. `Window.Activated`에서 `Mouse.Synchronize()` 호출(모달로 인한 히트테스트 stuck 가설) — 효과 없음.
2. `IsMouseOver`/`IsPressed` 두 Trigger가 진입할 때 서로의 `BeginStoryboard`를 `<StopStoryboard>`로 정지(애니메이션 클록 충돌 가설) — 클록이 로컬 기본값으로 순간 스냅되는 새 결함만 만들고 되돌림.
3. 호버 애니메이션을 `IsMouseOver` Trigger에서 빼서 `MouseEnter`/`MouseLeave`(진단 로그로 100% 신뢰성 확인됨) 코드비하인드에서 `Button.BeginStoryboard(...)`로 직접 구동 — `Storyboard.TargetName`이 Button이 아니라 `ControlTemplate` 내부 네임스코프에 속해 `ResolveTargetName`에서 `InvalidOperationException` 크래시. `Storyboard.Begin(button, button.Template, true)`로 수정했으나 사용자 재현 결과 여전히 이상함.
4. `Storyboard`를 완전히 걷어내고 `Template.FindName(...)`으로 찾은 `TranslateTransform`/`ScaleTransform`/`DropShadowEffect`에 `DependencyObject.BeginAnimation(...)`을 직접 호출(가장 저수준 API, 클록 트리/타겟이름 해석을 전혀 거치지 않음) — 이것도 사용자 재현 결과 여전히 이상함.

**진단으로 확정된 사실** (임시 로그로 실측, 이후 제거): `IsMouseOver` 프로퍼티와 raw `MouseEnter`/`MouseLeave` 이벤트는 모달 오픈/닫기 이후에도 매번 정확하게 True/False를 오간다 — 즉 프로퍼티/이벤트 레벨은 문제가 없다.

**현재 평가**: 애니메이션을 "발동시키는" 트리거/이벤트 경로는 여러 방식으로 교체해봐도 전부 실패했다는 것은, 문제가 발동 코드 자체가 아니라 **모달(`MessageBox`)이 닫힌 뒤 그 특정 카드의 렌더링/컴포지션(예: `DropShadowEffect`나 `RenderTransform`의 화면 갱신)이 깨지는 쪽**일 가능성이 높다는 뜻이다. 이 가설은 아직 검증 전이며, 다음에 다시 붙잡을 때는 애니메이션 발동 코드보다 렌더링/컴포지션 쪽(`InvalidateVisual`, `RenderOptions`, `CacheMode` 등)을 먼저 의심할 것.

**현재 상태(당시)**: 사소한 시각적 흠으로 남겨두고 보류. 기능(클릭/색상/눌림)에는 영향 없음. Phase 4로 진행.

---

**최종 해결 (2026-08-14, Phase 5 이후, 사용자 지시)**: 원인 규명을 여러 차례 시도했으나(위 4가지 접근 모두 실패) 근본 원인을 찾지 못한 상태가 이어지자, 사용자가 "리프트 효과 자체를 없애는 게 낫겠다"고 판단 — `Themes/Buttons.xaml`의 `HomeCardButtonStyle`에서 호버 시 `CardTranslate`(TranslateTransform Y, -5px 리프트) 애니메이션을 `IsMouseOver` Enter/Exit 트리거에서 완전히 제거했다. 호버 시 미세 확대(scale 1.005)와 글로우(그림자 opacity 0→0.35)는 그대로 유지(둘 다 이 버그와 무관하게 정상 동작했음). 눌림(`IsPressed`) 애니메이션의 6px 아래로 밀림(push-down)은 리프트와 별개의 효과라 유지하되, 눌림 해제 시 복귀 좌표만 기존 `-5`(리프트 상태 가정)에서 `0`(리프트가 없어진 기본 위치)으로 수정했다. 리프트 트랜스폼 자체가 사라졌으므로 "그 트랜스폼이 재생되지 않는" 버그도 자연히 재현 불가능해졌다 — 근본 원인 규명이 아니라 문제가 되는 효과 자체를 제거하는 방식의 해결.

**검증**: `dotnet build` 성공(경고 0/오류 0). 홈 화면 카드에 마우스를 올렸을 때 더 이상 위로 뜨지 않고 미세 확대+글로우만 나타나는 것을 확인. 리더기 설정 카드를 클릭해 모달을 열었다 닫은 뒤에도(이전엔 이 시점부터 그 카드만 리프트가 재발동 안 했음) 호버 시 확대/글로우가 계속 정상 동작하는지는 실제 마우스 호버가 필요해 자동화 도구로는 재현 확인이 어려우나, 애초에 리프트 트랜스폼 자체가 없어졌으므로 해당 증상의 재발 여지가 구조적으로 사라졌다.

---

## Phase 4 — 리더기 설정: 정적 레이아웃

**목표**: `screenshots/reader_setup.png`과 일치하는 정적 화면.

- [x] 창 크롬: 고정 크기, 타이틀 "리더기 설정"
- [x] 헤더(아이콘+제목+부제) — 공용 `ModernUIHeader` 대응 컴포넌트로 홈 화면과 스타일 공유 검토
- [x] "포트 설정" 섹션 카드: 리더기1/2 카드 2개 정적 배치(콤보, 버튼 5개, info 버튼) — PRD 4.4. **포트 열기 토글은 이번 Phase에서 만들지 않음**(2026-08-14 기준 Phase 5 범위에서도 제외되어 별도 단계로 보류 — 아래 Phase 5 상단 안내 참고).
- [x] "무결성 체크 정보" 섹션: 조회기간 콤보 + 조회 버튼 + 리스트(컬럼 헤더는 PRD 4.6 실측값 사용)
- [x] 하단 확인/취소 버튼

**완료 기준**: 실행 화면 캡처를 `screenshots/reader_setup.png`과 대조 — **통과 (2026-08-14)**. 홈 화면의 "리더기 설정" 카드 클릭 → 새 `ReaderSetupWindow`가 `ShowDialog(owner=this)`로 열리는 것을 실측 확인. 헤더(아이콘/제목/부제), "포트 설정"/"무결성 체크 정보" 섹션 제목(블루 바), 리더기1/2 카드(번호 뱃지, 라벨, 콤보, 액션 버튼 5개, 멀티패드 토글+info), 무결성 리스트(조회기간 콤보+조회 버튼, 컬럼 헤더 6개, 빈 상태 문구), 하단 확인/취소 버튼까지 스크린샷 크롭 대조 결과 레이아웃·문구·색상이 원본과 육안상 사실상 동일. 확인/취소 버튼 클릭 시 예외 없이 다이얼로그가 닫히고 홈 화면으로 정상 복귀하는 것도 확인(재오픈까지 2회 반복 테스트).

**구현 요약**:
- `Views/ReaderSetupWindow.xaml`/`.xaml.cs` 신규 생성. 창 크기는 PRD 4.2 "최초 1회 레이아웃 계산 후 창 크기 확정" 동작을, 홈 화면처럼 전체 Width/Height를 리터럴로 고정하는 대신 `SizeToContent="WidthAndHeight"` + `ResizeMode="NoResize"` + 내부 컴포넌트별 고정 치수(콤보 178, 액션버튼 100 등)로 재현 — 컴포넌트 치수가 자연스럽게 전체 창 크기를 한 번 계산해서 고정시키는 방식이라 원본 동작 취지에 더 부합한다고 판단.
- 흰색 타이틀바 적용 로직(`DwmSetWindowAttribute`, OS 버전 체크)은 `HomeWindow.xaml.cs`와 동일한 코드를 그대로 복제 — 서브 창이 이 화면 하나뿐이라 공용 헬퍼로 추출하는 것은 과도한 추상화로 판단해 보류(주석에 사유 명시, 필요 시 Phase 6 정리 단계에서 재검토 가능).
- 헤더 아이콘: 홈 화면 "리더기 설정" 카드의 글리프(`GeometryGroup`, 카드리더 단말기 형태)를 완전히 동일하게 재사용하고 배경만 진한 블루(`Blue500Brush`) 고정 + 글리프 흰색으로 바꿔 두 화면 간 스타일을 공유했다(ROADMAP 체크리스트의 "공용 ModernUIHeader 대응 컴포넌트로 스타일 공유 검토" 항목 반영). 별도 `UserControl`로 분리하지는 않음(1곳에서만 사용 — 과도한 추상화 방지 원칙).
- `Themes/Buttons.xaml`에 `InfoButtonStyle` 신규 추가(PRD 2.3 `CInfoIconButton` 대응). PRD 원문은 "i" 아이콘이라고 되어 있으나 `screenshots/reader_setup.png` 실측 결과 물음표(?) 글리프였음 — 스크린샷을 우선하는 프로젝트 원칙(CLAUDE.md)에 따라 "?" 로 구현.
- `Themes/Layout.xaml`에 리더기 설정 화면 전용 리소스 키 대거 추가(메인/서브 카드 radius·padding, 헤더 아이콘 56, 카드 높이 128/간격 12, 뱃지 34, 콤보 178, 액션버튼 100×36/간격 8, info버튼 20, 리스트 높이 166 등 — 전부 PRD 4.3~4.6 수치). `RowDefinition.Height`/`Margin`에 바인딩할 값은 기존 `HomeCardVisualHGridLength`/`HomeHeaderMargin` 패턴과 동일하게 `GridLength`/`Thickness` 완제품 리소스를 별도로 마련(WPF는 속성 값 하나에 `{StaticResource}`와 리터럴 텍스트를 섞어 쓸 수 없어 `"{StaticResource X},0,0,0"` 같은 표현이 `XamlParseException`을 던짐 — 개발 중 직접 겪고 수정).
- `Views/HomeWindow.xaml.cs`: `ReaderSetupCardButton_Click`/트레이 메뉴의 "리더기 설정" 항목이 기존 플레이스홀더 `MessageBox` 대신 `new ReaderSetupWindow { Owner = this }.ShowDialog()`를 호출하도록 교체.
- 확인/취소 버튼은 지시대로 실제 검증/저장 로직 없이 `DialogResult` 설정 후 `Close()`만 수행(Phase 5에서 레지스트리 저장, dirty-check 확인창 배선 예정. TRANSINFO_AOP 검증은 2026-08-14 범위 조정으로 별도 단계로 보류 — 아래 Phase 5 상단 안내 참고 — 코드에 TODO 주석 남김).

**"포트 열기" 토글 처리 (임의 판단)**: 작업 지시에서 허용한 두 옵션(빈 공간 남기기 / 자리 자체를 아예 안 넣기) 중 **빈 공간을 남기는 쪽**을 선택했다 — 리더기1 카드의 "포트 열기" 자리에 라벨/토글/info로 구성된 `StackPanel`을 `Visibility="Hidden"`으로 렌더링해 폭(`ReaderPortOpenPlaceholderWidth=148`, 스크린샷 실측 근사값)만 차지하게 했다. 이렇게 하면 리더기1/2 카드의 "멀티패드 여부" 토글이 좌우로 동일한 x 위치에 정렬되어(`screenshots/reader_setup.png` 실측과 동일한 정렬) 시각적으로 원본에 더 가깝다고 판단했다. "멀티패드 여부" 토글 자체는 PRD 4.9(비동기 처리 없는 단순 UI 토글)를 근거로 이번 Phase에 시각 요소로 포함했다(클릭 가능하지만 상태 변경이 어디에도 반영되지 않는 정적 배치 — Phase 5에서 실제 저장 로직 배선).

**기타 임의 판단/근사 처리 항목**:
- 콤보박스 항목은 PRD 4.13(실제 COM 포트 열거)이 이 저장소 범위 밖(외부 DLL 연동, 별도 PRD — 위 Phase 6 상단 안내 참고)이라 정적 더미 항목("COM 01"/"미사용")만 하드코딩.
- 리더기1/2 카드 번호 뱃지 색상(활성 Blue500 / 비활성 회색 `#BEC7D1`)은 PRD 4.11(AOP 제약)의 최종 상태가 아니라 "AOP 미적용 기본값" 스크린샷 실측을 그대로 반영 — INTERLOCK 값에 따른 동적 전환(AOP 제약 로직)은 2026-08-14 범위 조정으로 Phase 5에서 제외되어 별도 단계로 보류(아래 Phase 5 상단 안내 참고).
- 무결성 체크 리스트는 항상 빈 상태로 고정 표시(조회 버튼 클릭 시 동작 없음 — Phase 5 범위). `ScrollViewer`로 감싸 Phase 5에서 실제 행이 채워질 때 가시 3행 고정 요구사항(PRD 4.3/4.6)을 그대로 살릴 수 있게 미리 구조를 잡아둠.

---

### Phase 4 보정 (2026-08-14, 사용자 실행 화면 피드백 기반)

사용자가 Phase 4 산출물을 직접 실행해 `screenshots/reader_setup.png`와 대조한 결과 4가지 수정 요청이 있었고, 전부 반영 완료:

1. **헤더 아이콘 교체 + 창 세로 길이/폰트 축소**: 기존에는 홈 화면 "리더기 설정" 카드의 상세 벡터(본체+화면 컷아웃+키패드+슬롯 라인)를 그대로 재사용했으나, 스크린샷 확대 실측 결과 실제 헤더 아이콘은 3열×2행 둥근 사각형 6개로 구성된 단순 그리드 형태였다 — `Views/ReaderSetupWindow.xaml`의 `GeometryGroup`을 그리드 6개로 교체(더 이상 홈 화면과 아이콘을 공유하지 않음). 또한 직전 구현이 스크린샷 대비 세로로 약 13% 더 길었던 것(전 772×1005, 참고 캡처 다이얼로그 실측 약 762×875, 세로/가로 비율 1.30 vs 1.15)을 `Themes/Layout.xaml`(메인/서브 카드 패딩, 헤더 아이콘 46(기존 56), 카드 높이 112(기존 128), 뱃지 30(기존 34), 액션버튼/조회/하단버튼 높이 32(기존 36), 리스트 높이 142(기존 166) 등)과 `Themes/Typography.xaml`/`Themes/ComboBox.xaml`의 리더기 화면 전용 폰트 크기(헤더 타이틀 24→19, 서브타이틀 17.33→13.33, 섹션 제목 20→15.33, 라벨/본문/콤보 18.67→14.67, 작은 텍스트 16→12.67) 축소로 보정. 실행 캡처 결과 744×824(비율 1.108)로 참고 캡처(비율 1.148)와 육안상 사실상 동일한 비율까지 근접.
2. **액션 버튼(초기화/상태체크/키다운로드/무결성체크/업데이트)/"조회" 버튼 색상 재작업**: `Themes/Buttons.xaml`의 `ReaderButtonStyle`을 흰 배경+회색 보더 고정(hover 시에만 파랑)에서 IsEnabled 기준 2단계 상시 톤(활성: 연한 파랑 `#DCEBFF` 배경+Blue500 텍스트 / 비활성: 연한 회색 `#F1F3F5` 배경+회색 `#9AA5B1` 텍스트, 테두리 없음, `IsEnabled=False`에서도 반투명 처리하지 않음)으로 전면 재작성. "조회" 버튼도 `PrimaryButtonStyle`(진한 파랑 solid) 대신 동일한 `ReaderButtonStyle`로 교체. 기존에 이 스타일을 공유하던 "취소" 버튼은 원본이 흰 배경+회색 보더 중립 톤이라 별도 `ReaderSecondaryButtonStyle`을 신설해 분리.
3. **COM 콤보 "미사용" ↔ 카드 버튼/토글 활성화 연동**: `Views/ReaderSetupWindow.xaml.cs`에 리더기1/2 각각의 콤보 `SelectionChanged`(코드비하인드, `Window.Loaded` 시점에 이벤트 구독 — XAML에서 바로 구독하면 `SelectedIndex="0"` 초기화 중 `x:Name` 필드가 아직 연결되지 않아 `NullReferenceException` 위험이 있어 회피) 핸들러를 추가해, 콤보가 "미사용"이면 해당 카드의 액션 버튼 5개(`StackPanel`)와 멀티패드 토글의 `IsEnabled`를 false로, 아니면 true로 설정. `Loaded`에서 최초 1회도 동일하게 반영. 레지스트리/AOP 연동은 이번 수정 범위가 아님(Phase 5). "포트 열기" 토글은 아직 만들지 않았으므로(Hidden 자리만 존재) 연동 대상에서 제외.
4. **콤보박스 눌림(scale) 효과 제거**: `Themes/ComboBox.xaml`의 `SkinnedComboBoxStyle`에서 `PART_Toggle`의 `IsPressed=True`를 감지해 `PressScale`을 0.97로 축소하던 `DataTrigger`를 제거(버튼과 달리 콤보박스는 누른다고 눌리는 시각 효과가 있으면 안 된다는 피드백). 이 스타일은 앱 전역 공용 리소스라 다른 화면의 콤보박스에도 동일하게 반영됨(의도된 변경). 겸사겸사 `SkinnedComboBoxStyle`의 전역 `FontSize`도 18.67→14.67로 축소(리더기 설정 화면 외 사용처가 없음을 확인 후 적용).

**검증**: `dotnet build` 성공(경고 0/오류 0). 홈 화면 → 리더기 설정 카드 클릭 → 캡처 결과를 참고 스크린샷과 크롭 대조(헤더 아이콘 그리드 형태, 창 세로 비율, 액션 버튼 활성/비활성 색상, "취소" 버튼 중립 톤)하여 일치 확인. 리더기1 콤보를 "COM 01"→"미사용"으로 바꾸면 리더기1 카드의 버튼 5개+멀티패드 토글이 즉시 비활성화(연한 회색 톤)되고, 리더기2 콤보를 "미사용"→"COM 01"로 바꾸면 반대로 활성화(연한 파랑 톤)되는 것을 실제 클릭으로 재현 확인. 콤보 드롭다운을 열고 닫는 과정에서 콤보 자체의 크기/스케일이 변하지 않는 것을 스크린샷으로 확인(이전의 눌림 효과 제거 확인).

---

### Phase 4 가독성 개선 (2026-08-14, 사용자 피드백: "전체적인 화면 개선 가독성 개선")

사용자가 "각 섹션의 글씨크기/굵기를 최신 트렌드에 맞게 개선해달라"고 요청. 현재 화면을 `screenshots/reader_setup.png`와 크롭 대조한 결과, 대부분의 텍스트 스타일(헤더 타이틀, 섹션 제목, 라벨/본문, 리스트 헤더/빈 상태 문구)은 이미 참고 스크린샷과 크기·굵기가 일치했다. 유일하게 어긋난 부분은 헤더 서브타이틀("리더기 연결 및 제어 설정을 관리합니다")이었다 — 참고 스크린샷에서는 타이틀(Bold)과 뚜렷이 대비되는 가는 굵기(Regular)로 렌더링되어 있는데, 기존 구현은 `ReaderHeaderSubtitleTextStyle`이 `FontWeight="Bold"`로 되어 있어 타이틀과 위계 구분이 되지 않았다.

- **수정**: `Themes/Typography.xaml`의 `ReaderHeaderSubtitleTextStyle`을 `FontWeight="Bold"` → `FontWeight="Normal"`로 변경(크기 13.33px, 색상 `SubTextBrush`는 유지). 최신 국내 UI 트렌드(제목=크고 굵게, 설명/부제=가는 굵기+연한 회색으로 명확히 대비)와도 부합.
- 라벨(`ReaderLabelTextStyle`, Bold 14.67)과 본문(`ReaderBodyTextStyle`, Normal 14.67)은 크기는 같지만 굵기가 이미 다르고(라벨 Bold vs 본문 Normal), 참고 스크린샷의 실제 렌더링과 크롭 대조해도 육안상 위계 구분이 충분해 보여 변경하지 않음. 리스트 헤더/빈 상태 텍스트(`ReaderSmallTextStyle`, 12.67px)도 참고 스크린샷과 크기가 사실상 동일해 변경하지 않음. 버튼 텍스트 굵기(`ReaderButtonStyle`)는 지시대로 건드리지 않음.
- 폰트 크기 변경이 없어(굵기만 변경) 카드 높이/버튼 폭 등 `Layout.xaml` 치수는 영향 없음 — 레이아웃 리소스는 수정하지 않았다.

**검증**: `dotnet build` 성공(경고 0/오류 0). 실행 중이던 프로세스 종료 후 재빌드/재실행, 홈 화면 → 리더기 설정 카드 클릭 → 캡처해 수정 전/후를 참고 스크린샷과 나란히 크롭 대조 — 수정 후 서브타이틀이 참고 스크린샷과 동일하게 가는 굵기로 렌더링됨을 확인. 홈 화면(다이얼로그 뒤 배경)은 이번 변경으로 영향받지 않음을 스크린샷으로 확인(홈 화면 타이포그래피는 이번 작업 범위 밖).

---

### Phase 4 트렌디 타이포그래피 개선 (2026-08-14, 사용자 요청: "꼭 원본말고 더 트랜디하고 가독성있게 한번 분석 및 개선해줘")

바로 위 항목이 `screenshots/reader_setup.png`(원본 MFC 실측)과의 픽셀 일치가 목적이었다면, 이번 요청은 **원본 제약에서 벗어나** 최신 국내 핀테크 UI(토스/카카오/네이버 등) 트렌드에 맞춰 타이포그래피 자체를 개선하는 것이 목적. 실행 화면을 직접 캡처해 분석한 결과, 아래 2가지 지점이 "원본과는 같지만 트렌드상 개선 여지가 있는" 부분으로 확인되어 반영했다(작업 범위: `Views/ReaderSetupWindow.xaml` 전용, `Themes/Typography.xaml`/`Themes/ComboBox.xaml`만 수정, 홈 화면은 손대지 않음, 버튼 스타일도 지시대로 유지):

1. **라벨(캡션) vs 실제 값(콘텐츠)의 위계 평면화 문제**: 카드 라벨("리더기1 - COM 포트", "멀티패드 여부" — `ReaderLabelTextStyle`, 기존 Bold 14.67)과 실제 선택값(콤보박스 "COM 01"/"미사용" 텍스트, 기존 Medium 14.67)이 크기가 완전히 같아 "무엇이 캡션이고 무엇이 실제 데이터인지"가 구분되지 않았다. 토스/카카오 등은 캡션을 작고 연하게 낮추고, 실제 값을 더 크고 진하게 강조하는 패턴을 일관되게 쓴다.
   - `ReaderLabelTextStyle`: `Bold 14.67 / TitleTextBrush(#191F28)` → `Medium 폰트 + Normal 굵기 / 12.67px / CardTitleTextBrush(#333D4B)`로 축소·완화(완전히 옅은 `SubTextBrush`까지는 낮추지 않음 — 토글 옆 라벨은 조작 대상을 바로 읽을 수 있어야 해서 최소 대비는 유지).
   - `Themes/ComboBox.xaml`의 `SkinnedComboBoxStyle`: `Medium 14.67 / Normal` → `실제 Bold 웨이트 폰트 + 15.33px`로 반대로 키움. 라벨은 작게/연하게, 값(콤보 선택값)은 크게/진하게 — 값이 실제로 도드라지도록 대비를 강화했다. 이 콤보 스타일은 현재 리더기 설정 화면에서만 쓰이는 것을 재확인 후 전역 리소스에 바로 적용(다른 화면에 콤보 사용처가 새로 생기면 재검토 필요, `Buttons.xaml`의 `ReaderButtonStyle` 관련 기존 판단과 동일한 논리).
   - 굵기 대비(Bold 남용) 대신 크기+색상 대비로 위계를 표현한 것도 최신 트렌드에 더 부합한다는 판단.
2. **리스트 헤더/빈 상태 텍스트가 다소 빽빽함**: `ReaderSmallTextStyle`(리스트 컬럼 헤더 "체크일시"/"포트"/... 및 빈 상태 문구 "조회된 무결성 체크 정보가 없습니다.")을 `12.67px → 13.33px`로 소폭 확대해 가독성을 개선했다(여전히 본문/값 14.67px보다는 작게 유지해 "표는 보조 정보"라는 위계는 유지).
3. **(검토했으나 미적용) 헤드라인 자간(letter-spacing) 타이트닝**: 헤더 타이틀/섹션 제목에 음수 자간(`CharacterSpacing=-10`)을 줘서 토스류 헤드라인 특유의 "타이트하고 정제된" 인상을 추가하려 했으나, `TextBlock.CharacterSpacing`은 WPF .NET Core 포트에서 추가된 속성으로 **.NET Framework 4.8(`net48`)에는 존재하지 않아** 빌드 에러(`MC4005`)가 발생 — 이 프로젝트의 타겟 프레임워크 제약(CLAUDE.md, Windows 7 지원) 때문에 채택 불가로 확인, 되돌림.

라벨 크기 변경(14.67→12.67)과 콤보 폰트 변경(14.67 Medium→15.33 Bold)은 텍스트 자체 치수만 바뀌고 `Layout.xaml`의 카드 `MinHeight`(112, `Min`이라 자동 확장 가능)/콤보 `Width`(178/110, 고정) 등 레이아웃 리소스는 그대로 두었다 — 콤보박스 드롭다운(COM 01/미사용, 오늘/7일/30일/100일)을 실제로 열어 텍스트 잘림이 없는지 확인 완료.

**검증**: `dotnet build` 성공(경고 0/오류 0). 실행 중이던 프로세스 종료 후 재빌드/재실행, 홈 화면 → 리더기 설정 카드 클릭 → 캡처 확인 — 라벨("리더기1 - COM 포트", "멀티패드 여부")이 작고 연한 캡션으로, 콤보 선택값("COM 01", "미사용", "오늘")이 더 크고 진한 값으로 뚜렷이 구분되어 보이는 것을 확인. 리더기1/2 콤보 및 조회기간 콤보 드롭다운을 실제로 열어(`windows_click`) 항목(`COM 01`/`미사용`, `오늘`/`7일`/`30일`/`100일`) 텍스트가 잘리지 않고 정상 표시되는 것을 확인. 확인/취소 버튼 클릭 시 예외 없이 다이얼로그가 정상적으로 닫히는 것도 재확인. 홈 화면(작업 범위 밖)은 변경하지 않음.

---

### Phase 4 헤더/카드 아이콘 재작업 (2026-08-14, 사용자 피드백 3회 연속: "아이콘이 이상해" → "리더기 모양이 맞다, 홈 화면 버튼이랑 통일해달라" → "부자연스럽다, 더 이쁘고 자연스럽게")

바로 위 "Phase 4 보정" 섹션(1번 항목)에서 헤더 아이콘을 "3열×2행 둥근 사각형 6개짜리 단순 그리드"로 교체했다고 기록했는데, 이후 사용자 피드백을 거치며 그 형태 자체가 여러 차례 다시 바뀌어 **해당 기록은 더 이상 최신 상태가 아니다** — 최종 형태는 아래 3차 수정 결과이며, `Views/HomeWindow.xaml`(`ReaderSetupCardButton`)과 `Views/ReaderSetupWindow.xaml`(헤더) 두 곳이 완전히 동일한 지오메트리를 공유한다.

1. **1차(아이콘이 이상함)**: 그리드 6블록 형태를 `HomeWindow.xaml`의 "결제"(`TransCardButton`) 카드 아이콘 지오메트리(본체+화면 컷아웃+키패드)로 교체 — 그러나 이는 의미상 "결제/계산기" 아이콘이지 "리더기" 아이콘이 아니었다(홈 화면 캡처를 다시 실측해 확인: 리더기 설정 카드와 결제 카드는 원래부터 서로 다른 벡터를 쓰고 있었는데, 실수로 결제 카드 쪽을 재사용한 것).
2. **2차(리더기 모양이 맞다, 통일 요청)**: 홈 화면 자체의 "리더기 설정" 카드(`ReaderSetupCardButton`)가 이미 올바른 카드리더 단말기 벡터(세로형 본체+화면+3×2 키패드+하단 슬롯 라인)를 쓰고 있음을 재확인하고, 헤더 아이콘을 그 지오메트리로 교체해 두 화면을 통일.
3. **3차(부자연스럽다, 더 예쁘게 + "너무 작아짐" 후속 피드백)**: 기존 지오메트리는 화면/키패드 6개의 간격·크기가 미세하게 비대칭이고, 하단 슬롯이 얇은 선(`Rect 8,23,11,0.9`)만 남아 있어 붕 뜬 잔여물처럼 보였다. 본체(`Rect 4.5,2.25,19,23.5 r3`)·화면(`Rect 7.5,5.25,13,8 r1.5`, 반경 확대)·키패드 6개(각 `3.4×2.6 r0.7`, 좌우/행간 간격 균등)·하단 슬롯(`Rect 8.5,23,11,1.2 r0.6`, 얇은 선 대신 라운드 필)을 모두 좌우 대칭·동일 간격으로 재설계. 재설계 직후 전체 크기를 줄였다가("본체 16×22") 사용자가 "아이콘이 너무 작아졌다"고 지적해 원래 크기(본체 19×23.5, 원본과 동일 스케일)로 되돌리고 비율만 대칭으로 다듬은 최종안을 적용했다.

**검증**: 매 단계마다 `dotnet build` 성공 확인 후 홈 화면/리더기 설정 창을 열어 `mcp__windows` 스크린샷으로 직접 대조(홈 화면 4개 카드 아이콘 중 "리더기 설정"과 "결제"가 서로 다른 벡터임을 확대 크롭으로 재확인, 헤더 아이콘 확대 크롭으로 화면/키패드/슬롯 대칭 확인). 두 파일(`HomeWindow.xaml`/`ReaderSetupWindow.xaml`)의 `GeometryGroup` 좌표가 문자 그대로 동일함을 최종 확인.

---

## Phase 5 — 리더기 설정: 비즈니스 로직(스텁) + 레지스트리 저장/dirty-check

> **2026-08-14 범위 조정(사용자 지시)**: 기존에 별도 Phase였던 "비즈니스 로직(스텁)"과 "포트열기 토글 & 레지스트리 연동"을 하나의 Phase로 합쳤다가, 포트 열기 토글은 아직 미구현(Phase 4에서 자리만 Hidden으로 남겨둠) 상태라 이번 Phase 범위에서 다시 제외했다. 아래 항목들은 이번 Phase에서 **명시적으로 제외**하고 이후 별도 단계로 미룬다:
> 1. AOP 제약 로직(PRD 4.11) — `INTERLOCK` 값에 따른 컨트롤 활성/비활성 전이. 미구현(레지스트리 `INTERLOCK` 값 읽기·카드 뱃지 표시 등 선행 작업이 얽혀 있어 별도로 다루는 게 안전하다는 판단).
> 2. 확인(OK) 검증 중 TRANSINFO_AOP 모드 포트 미지정 시 저장 차단. 미구현.
> 3. **포트 열기 토글(PRD 4.8)** — 확인창 → 백그라운드 처리(2.5초 시뮬레이션) → 10초 타임아웃 → 레지스트리 저장(`PORT_ALWAYSOPEN`) → 성공 시 리더기2 강제 비활성화(단일 포트 인터락) 흐름 전체. 미구현. 리더기1 카드의 토글 자리는 여전히 `Visibility="Hidden"`으로 비워둔 상태 그대로.

**목표**: PRD 4.7, 4.9, 4.13의 로직을 스텁 수준으로 이식(원본도 실통신 미구현) + 콤보/멀티패드 값의 레지스트리 저장 및 dirty-check.

- [x] 버튼 클릭(초기화/상태체크/키다운로드/무결성체크/업데이트) → 로딩 상태(스피너+텍스트) → 3초 후 자동 완료 (원본 동작 재현, `Task.Delay` 기반 비동기로 — UI 스레드 블로킹 금지). **실제 리더기 통신 로직은 이 Phase의 범위가 아니며, 원본도 미구현이라 이후에도 별도 단계로 다룰 예정**(사용자 지시).
- [x] 조회 버튼 → 로딩(2초) → 더미 데이터로 리스트 갱신 (조회기간별 행 수: 오늘 3 / 7일 5 / 30일·100일 10)
- [x] 레지스트리 저장: `COMPORT1_FIELD`/`COMPORT2_FIELD`, `MULTIPAD1_FIELD`/`MULTIPAD2_FIELD`(반전 인코딩), 경로 `HKCU\Software\KFTC_VAN\KFTCTaxGiroCAP\SERIALPORT` — PRD 5장 참고(2026-08-14 확정: 원본과 레지스트리 공유하지 않고 별도 앱 이름 사용)
- [x] 정보 팝오버(멀티패드) — PRD 4.10 문구 그대로
- [x] 스냅샷/dirty-check(PRD 4.13): 콤보1/2 + 멀티패드1/2 추적, 취소 시 확인창

**완료 기준**: 각 액션 버튼/조회 버튼 클릭 시 로딩→완료 흐름 확인. 콤보/멀티패드 값 레지스트리 저장 확인. dirty-check(콤보/토글 변경 후 취소 시 확인창) 동작 확인. (AOP 시나리오 검증, TRANSINFO_AOP 저장 차단, 포트 열기 토글은 위 범위 조정에 따라 이번 완료 기준에서 제외 — 별도 단계에서 다룸) — **통과 (2026-08-14)**.

**구현 요약**:
- `Views/ReaderSetupWindow.xaml.cs`: 액션 버튼 10개(리더기1/2 × 초기화/상태체크/키다운로드/무결성체크/업데이트)가 공용 `ActionButton_Click` 핸들러 하나를 공유한다 — 각 `Button`의 `Tag`에 로딩 문구(예: "초기화중...")를 XAML에서 미리 심어두고, 핸들러는 클릭된 버튼의 `Content`를 그 문구로 바꾼 뒤 `await Task.Delay(3000)`, 완료 후 원래 `Content`로 되돌리는 방식(스피너 애니메이션은 이번 Phase 필수 아님으로 텍스트만 변경 — 아래 "임의 판단" 참고). 조회 버튼(`QueryButton_Click`)도 동일 패턴으로 2초 딜레이 후 더미 데이터를 반영한다.
- "동시에 하나의 작업만 진행 가능"(PRD 4.7)은 `_isBusy` bool 플래그 + `SetGlobalEnabled(bool)` 헬퍼로 구현 — 작업이 시작되면 리더기1/2 콤보·액션버튼패널·멀티패드토글·조회기간콤보·조회버튼·확인·취소 버튼 전체를 한 번에 잠그고(개별 카드 단위가 아니라 화면 전체 단위), 완료 후 전체를 풀고 나서 기존 `ApplyReaderCardEnabled`(Phase 4에 이미 있던 "미사용" 콤보 기준 활성/비활성 로직)를 리더기1/2 양쪽에 재적용한다. `_isBusy`가 true인 동안의 클릭은 핸들러 최상단에서 즉시 return(추가로 컨트롤 자체도 disabled라 이중 방어).
- 무결성 체크 리스트: 기존 "항상 빈 상태"였던 정적 `Grid`를 `ItemsControl`(x:Name="IntegrityListItemsControl")로 교체하고, 빈 상태 문구(`IntegrityEmptyText`)/로딩 문구(`IntegrityLoadingText`, "조회 중입니다...")와 함께 같은 `Grid`에 겹쳐 놓은 뒤 코드비하인드에서 `Visibility`로 세 상태를 전환한다. 각 행의 `DataTemplate`은 헤더와 동일한 6열 비율(20/11/8/18/23/20)의 `Grid`를 재사용하고, 결과 칩(정상/오류)은 `Models/IntegrityCheckRow.cs`에 새로 만든 모델이 `Themes/Colors.xaml`의 기존 `ResultOkBgBrush`/`ResultErrorBgBrush` 등 리소스를 `Application.Current.Resources[...]`로 그대로 참조해 리터럴 색상 중복 없이 바인딩한다. 더미 데이터는 `BuildDummyRows(period)`가 조회기간에 따라 3/5/10행을 생성하고, 4번째 행마다 결과코드 "01"(오류)을 섞어 정상/오류 칩이 둘 다 보이도록 구성했다.
- 정보 팝오버(PRD 4.10): 리더기1/2의 멀티패드 info 버튼("?" 아이콘) 2개가 XAML에 하나만 선언한 공용 `Popup`(`MultipadInfoPopup`, `StaysOpen="False"`)을 공유한다. `MultipadInfoButton_Click`이 클릭된 버튼을 `PlacementTarget`으로 지정해 여는데, 같은 버튼을 다시 클릭하면(= 이미 그 버튼을 대상으로 열려 있으면) 닫고, 다른 버튼을 클릭하면 `PlacementTarget`만 바뀌면서 자연스럽게 이전 팝오버가 닫히고 새 팝오버가 그 자리에 뜬다. "포트 열기" info 버튼은 자리 자체가 `Visibility="Hidden"`이라 배선하지 않음(지시대로).
- 레지스트리 저장(PRD 5장/4.12): `SaveToRegistry()`가 `Registry.CurrentUser.CreateSubKey(@"Software\KFTC_VAN\KFTCTaxGiroCAP\SERIALPORT")` 하위에 `COMPORT1_FIELD`/`COMPORT2_FIELD`(콤보 선택 텍스트 그대로)와 `MULTIPAD1_FIELD`/`MULTIPAD2_FIELD`(반전 인코딩: `IsChecked==true` → `"0"`, 아니면 `"1"`)를 저장하고, `ConfirmButton_Click`이 저장 직후 `DialogResult=true; Close()`.
- 스냅샷/dirty-check(PRD 4.12/4.13): `Loaded` 핸들러 마지막에 콤보1/2 선택 텍스트와 멀티패드1/2 `IsChecked` 값을 필드 4개에 저장해두고, `CancelButton_Click`이 열려있는 팝오버를 먼저 닫은 뒤 현재 값과 스냅샷을 비교 — 하나라도 다르면 `MessageBox.Show(..., YesNo)`로 "변경된 내용이 있습니다.\n저장하지 않고 종료하시겠습니까?"를 띄우고, "아니요"면 `return`(창 유지), "예"거나 애초에 변경사항이 없으면 `DialogResult=false; Close()`.

**임의 판단/근사 처리 항목** (PRD/작업 지시에 명시되지 않아 직접 결정한 세부사항):
- **스피너 없음**: 지시사항이 "여력 되면 작은 회전 스피너 추가"로 선택지를 열어뒀는데, `Themes/ToggleSwitch.xaml`에 이미 있는 회전 링 패턴을 액션 버튼(`ReaderButtonStyle`, `Themes/Buttons.xaml`)에 새로 이식하는 것은 버튼 콘텐츠가 `Content="{TemplateBinding Content}"`를 텍스트 문자열로 직접 바꿔치기하는 현재 코드비하인드 구조와 잘 맞지 않아(스피너를 넣으려면 버튼 템플릿을 아이콘+텍스트 복합 구조로 다시 짜야 함) 이번 Phase에서는 텍스트 전환만으로 로딩 상태를 표현했다. 필요 시 별도 후속 작업으로 추가 가능.
- **화면 전체 잠금 범위**: PRD 4.7 원문은 "해당 리더기 카드의 콤보/토글/나머지 버튼 4개를 비활성화"라고 카드 단위로 적혀 있지만, 작업 지시가 "화면 전체(리더기1/2 액션버튼 + 조회버튼 전부 포함)에서 하나의 작업만"이라고 명시적으로 확장했으므로 이를 그대로 따라 리더기1/2 카드 전체 + 조회 영역 + 확인/취소까지 전부 잠그도록 구현했다(확인/취소까지 잠그는 것은 작업 지시에 명시되지 않았으나, 비동기 작업 도중 창이 닫히는 경합 상황을 피하기 위한 안전장치로 포함시켰다).
- **팝오버 스타일**: "화살표 포인터까지는 필수 아님, 심플한 카드형 팝업이면 충분"이라는 지시에 따라 흰 배경 + 얇은 회색 보더 + 라운드 10px + `DropShadowEffect` 카드형 `Popup`으로 구현(화살표 포인터 없음). `Placement="Bottom"`으로 info 버튼 바로 아래 뜨도록 함.
- **더미 데이터 생성 규칙**: PRD/지시에 정확한 알고리즘이 없어 임의로 결정 — 기준 시각(`2026-03-08 09:12:34`, 원본 PRD 4.6에 언급된 예시 타임스탬프와 동일한 날짜)에서 행마다 `-(i*37)분 -(i*11)초`씩 당겨 서로 다른 값을 만들고, 포트는 짝/홀 인덱스로 "COM 01"/"COM 02"를 번갈아, 결과코드는 4번째 행마다("i % 4 == 3") "01"(오류)을 섞어 정상/오류 칩이 둘 다 렌더링되도록 했다. 모듈ID/리더기식별번호/POS식별번호는 `MD-1000`/`RDR-100000`/`POS-200000` 형태의 순번 문자열로 생성.

**검증 결과**:
- `dotnet build` 성공(경고 0/오류 0).
- 액션 버튼(리더기1 "초기화") 클릭 → 버튼 텍스트가 "초기화중..."으로 바뀌고 리더기1/2 카드 전체(콤보/토글/나머지 버튼) + 조회 영역 + 확인/취소가 모두 비활성화되는 것을 `mcp__windows__windows_snapshot`으로 확인. 로딩 중 다른 버튼("상태체크") 클릭 시 무시됨(비활성 상태 유지)을 확인. 3초 후 텍스트가 "초기화"로 원복되고 전체 컨트롤이 재활성화되며, 리더기2는 콤보가 여전히 "미사용"이라 `ApplyReaderCardEnabled` 재적용으로 계속 비활성 상태로 유지되는 것을 확인.
- 조회 버튼 클릭(조회기간 기본값 "오늘") → 버튼 텍스트가 "조회중..."으로 바뀌고 리스트 영역이 "조회 중입니다..."로 전환된 뒤 2초 후 더미 행 3개(체크일시/포트/결과칩/모듈ID/리더기식별번호/POS식별번호, 결과 "정상" 초록 칩)가 헤더와 컬럼 정렬이 맞게 표시되는 것을 스크린샷으로 확인. 조회기간을 "7일"로 바꿔 재조회 → 5행 표시(가시 3행 고정 + 세로 스크롤바 등장) 및 4번째 행이 "오류" 빨강 칩으로 렌더링되는 것을 확인.
- 리더기1 멀티패드 토글 ON → info 버튼("?") 클릭 → "멀티패드 여부" 제목과 PRD 4.10 문구(ON/OFF 설명 + 스캐너 각주) 그대로인 팝오버가 버튼 아래에 뜨는 것을 스크린샷으로 확인. 같은 버튼 재클릭 → 팝오버가 닫히는 것을 스크린샷으로 확인.
- 멀티패드 토글을 켠 상태에서 취소 클릭 → "변경된 내용이 있습니다.\n저장하지 않고 종료하시겠습니까?" 확인창(예/아니요)이 뜨는 것을 확인, "아니요" 클릭 시 창이 닫히지 않고 유지되는 것을 확인(윈도우 목록으로 재확인).
- 같은 상태에서 확인 클릭 → 창이 닫히고, PowerShell `Get-ItemProperty -Path HKCU:\Software\KFTC_VAN\KFTCTaxGiroCAP\SERIALPORT`로 조회한 결과 `COMPORT1_FIELD=COM 01`, `COMPORT2_FIELD=미사용`, `MULTIPAD1_FIELD=0`(켜짐→반전 "0"), `MULTIPAD2_FIELD=1`(기본 꺼짐)이 실제로 저장된 것을 확인.
- 새로 리더기 설정 창을 열어(레지스트리 로드는 이번 Phase 범위 아니므로 XAML 기본값으로 리셋된 상태) 값을 전혀 바꾸지 않고 바로 취소 클릭 → 확인창 없이 즉시 창이 닫히는 것을 확인.

---

### Phase 5 보완 (2026-08-14, 사용자 피드백 2건: "레지스트리 값 로드 누락" + "로딩 스피너 없음")

위 Phase 5 1차 구현은 레지스트리 **저장**만 있고 창을 열 때 저장된 값을 다시 **불러오는** 부분이 없었다(항상 XAML 기본값 `SelectedIndex="0"`으로 시작 — 즉 리더기1은 매번 "COM 01"이 기본으로 보임). 또한 액션/조회 버튼의 로딩 상태가 텍스트 전환뿐이라 시각적 스피너가 없었다. 두 가지를 보완했다.

**1) 레지스트리 값 로드 (PRD 4.13/5장)**:
- `Views/ReaderSetupWindow.xaml.cs`에 `LoadFromRegistry()`를 추가하고 `ReaderSetupWindow_Loaded`의 가장 첫 단계(콤보 `SelectionChanged` 구독/`ApplyReaderCardEnabled`/dirty-check 스냅샷 캡처보다 먼저)에서 호출한다 — `HKCU\Software\KFTC_VAN\KFTCTaxGiroCAP\SERIALPORT`의 `COMPORT1_FIELD`/`COMPORT2_FIELD`/`MULTIPAD1_FIELD`/`MULTIPAD2_FIELD`를 읽어 콤보 선택값/토글 `IsChecked`에 반영한다. 이 순서 덕분에 이후의 활성화 연동과 dirty-check 스냅샷이 "로드된 값" 기준으로 정확히 잡힌다(스냅샷 캡처가 로드 이후 시점이라 순서가 꼬이지 않음을 코드 리뷰로 확인).
- `SelectComboValue(ComboBox, string?)` 헬퍼: 저장된 값과 문자열이 일치하는 `ComboBoxItem`이 있으면 그 항목을 선택하고, 값이 비어있거나(키/값 없음) 콤보 항목에 없는 값이면 안전하게 "미사용" 항목으로 폴백한다.
- 토글은 반전 인코딩 규칙 그대로(`MULTIPAD{N}_FIELD == "0"`일 때만 켜짐, 그 외/없음은 꺼짐)를 역방향으로 적용해 `IsChecked`를 초기화한다.
- 레지스트리 접근 자체(`Registry.CurrentUser.OpenSubKey`)를 `try/catch`로 감싸 권한 문제 등으로 예외가 나도 창 밖으로 전파하지 않고 조용히 기본값(미사용/꺼짐)으로 폴백하도록 했다.
- `Views/ReaderSetupWindow.xaml`의 `Reader1PortCombo`/`Reader2PortCombo`에 있던 `SelectedIndex="0"` 하드코딩을 제거했다 — 이제 최초 선택은 전적으로 `LoadFromRegistry()`(있으면 저장값, 없으면 "미사용")가 담당한다.

**2) 로딩 스피너 (PRD 4.7 "스피너+텍스트")**:
- `Controls/ButtonLoadingHelper.cs`(신규) — `Button`에 새 속성을 얹을 수 없어 WPF 표준 패턴인 첨부 속성(`DependencyProperty` `IsLoading`)으로 구현.
- `Themes/Buttons.xaml`의 `ReaderButtonStyle` `ControlTemplate`을 리팩터링 — 기존에는 `ContentPresenter` 하나만 있던 콘텐츠 영역을 가로 `StackPanel`(스피너 `Ellipse` + `ContentPresenter`)로 재구성하고, `controls:ButtonLoadingHelper.IsLoading=True`일 때만 스피너가 보이며 `RotateTransform` + 무한 반복(`RepeatBehavior="Forever"`) `Storyboard`로 회전한다. `ToggleSwitch.xaml`의 기존 "스피너"(균일한 색의 완전한 원형 `Border` 테두리)는 대칭 도형이라 회전해도 육안상 정지해 보이는 문제가 있어 그대로 재사용하지 않고, `Ellipse.StrokeDashArray="2,1.4"`로 점선 원을 만들어 회전이 실제로 눈에 보이도록 새로 설계했다(주석에 판단 근거 기록).
- `Views/ReaderSetupWindow.xaml.cs`의 `ActionButton_Click`/`QueryButton_Click`에서 로딩 시작/종료 시점에 각각 `ButtonLoadingHelper.SetIsLoading(button, true/false)`를 호출해 텍스트 전환과 스피너 표시를 함께 토글한다.
- 로딩 중에는 해당 버튼이 속한 `ActionButtonsPanel` 전체가 `SetGlobalEnabled(false)`로 비활성화되므로(클릭된 버튼 포함), 스피너/텍스트도 `IsEnabled=False` 트리거의 회색 팔레트를 그대로 따라간다(회색 배경 위에 회색 점선 스피너 — 대비는 충분히 확인됨, 아래 검증 스크린샷 참고).

**검증 결과**:
- `dotnet build` 성공(경고 0/오류 0).
- (a) 레지스트리에 `COMPORT1_FIELD=COM 01`, `COMPORT2_FIELD=COM 01`, `MULTIPAD1_FIELD=0`, `MULTIPAD2_FIELD=0`을 PowerShell로 미리 저장한 뒤 리더기 설정 창을 열자 두 콤보 모두 "COM 01"로 선택되고 두 멀티패드 토글 모두 ON, 리더기2 액션 버튼 5개도 활성(연한 파랑 톤)으로 뜨는 것을 `mcp__windows__windows_snapshot`/스크린샷으로 확인.
- (b) 레지스트리 키(`SERIALPORT`) 자체를 삭제한 뒤 다시 열자 두 콤보 모두 "미사용", 두 토글 모두 OFF, 액션 버튼 전부 비활성(연한 회색 톤)으로 뜨는 것을 확인 — 예외 없이 정상 폴백.
- (c) 액션 버튼(리더기1 "초기화", 이어서 리더기2 "초기화") 클릭 → 버튼 텍스트가 "초기화중..."으로 바뀌는 것과 동시에 텍스트 좌측에 작은 점선 원(스피너)이 표시되는 것을 스크린샷으로 확인(회전 애니메이션 자체는 스크린샷 정지 이미지로는 프레임 하나만 보이지만, `RotateTransform` + `Forever` 반복 `Storyboard`가 트리거에 정상 연결되어 있음을 XAML 리뷰로 확인했고, 스피너 도형 자체가 대칭이 아닌 점선 원이라 실제로 회전 시 시각적 변화가 발생하는 구조임을 확인). 3초 후 텍스트가 원래대로 돌아오며 스피너도 함께 사라지는 것을 확인.
- 값 로드 후(레지스트리에 저장된 값 그대로) 아무 것도 바꾸지 않고 취소 클릭 → 확인창 없이 즉시 닫힘(dirty-check 스냅샷이 "로드된 값" 기준으로 정확히 잡혀 있음을 재확인).

---

## Phase 6 — 통합 검증 & 마무리

> **2026-08-14 범위 조정(사용자 지시)**: 기존 "Phase 6 — 실제 COM 포트 연동"(더미 콤보 대신
> `System.IO.Ports.SerialPort.GetPortNames()`로 실제 COM 포트 열거, PRD 4.13)을 이 저장소의
> 로드맵에서 제거했다. 실제 리더기 연동은 **외부 DLL을 붙이는 방식으로 진행될 예정이며, 별도
> PRD 문서로 범위/요구사항을 새로 정의해서 진행**하기로 했다 — 이 프로젝트(KFTCTAXGIROCAP)는
> "리더기 설정 화면의 UX/UI 구현"까지가 목표이고, 실제 하드웨어/DLL 연동은 그 후속 작업(별도
> 문서·별도 단계)으로 명확히 분리한다. 그래서 원래 Phase 7이었던 "통합 검증 & 마무리"가 이
> 저장소 기준 마지막 단계(Phase 6)가 된다.

- [x] 홈 화면 + 리더기 설정 화면 전체를 원본과 나란히 스크린샷 비교
- [x] PRD 6장 "미확정 사항" 전체 재확인 — 남은 항목 있으면 명시적으로 보류 처리
- [x] 컴팩트 모드(≤800px 높이) 대응 구현 — 방침은 확정됨(PRD 미확정 사항 #6, Phase 2 안내 참고): 일반/컴팩트 두 번째 `Themes/Layout.xaml` 세트 작성 + 런타임 작업영역 높이 감지로 시작 시(및 필요시 화면 전환 시) `ResourceDictionary` 스왑 배선
- [x] 코드 정리 (사용하지 않는 리소스/스타일 정리)

**완료 기준**: 홈/리더기 설정 화면이 원본 스크린샷과 대조해 의도치 않은 레이아웃 깨짐 없이 렌더링되고, PRD 6장 미확정 사항이 전부 보류/확정 처리되고, 컴팩트 모드가 실제로 리소스 스왑을 통해 동작하며, 죽은 리소스가 정리된 상태 — **통과 (2026-08-14)**.

**1) 스크린샷 대조 결과**: `screenshots/home_screen.png`/`reader_setup.png`를 실행 화면과 크롭 대조한 결과 Phase 2~5에서 이미 여러 차례 검증/보정을 거쳐온 상태 그대로 레이아웃·색상·문구가 육안상 일치했고, 이번 Phase에서 새로 발견된 의도치 않은 깨짐/잘림/색상 오류는 없었다(Phase 4~5의 트렌디 개선으로 인한 원본과의 의도된 차이는 그대로 유지). 홈 화면 카드 4개, 헤더/하단 버튼, 리더기 설정 화면의 헤더/포트 설정/무결성 체크 정보 섹션, 확인/취소 버튼까지 재확인.

**2) PRD 6장 미확정 사항 정리**: `PRD_WPF.md` 6장의 #2/#3/#4를 "외부 DLL 연동 + 별도 PRD" 범위로 이동됐음을 반영해 명시적으로 보류 처리(취소선 추가), #5(트레이 메뉴 가맹점 설정 진입)는 Phase 3의 임의 판단("비활성화하지 않고 안내 메시지만 표시")을 "잠정 채택(PM 재확인 시 변경 가능)"으로 PRD에 반영, #7(소스-빌드 불일치)은 CLAUDE.md의 "스크린샷 우선 원칙"이 프로젝트 전 구간에서 실제로 적용되어 왔음을 근거로 "정책적으로 해소됨"으로 명시. #1/#6은 기존 확정 상태 유지.

**3) 컴팩트 모드(≤800px 높이) 구현**:
- `Themes/Layout.Compact.xaml`/`Themes/Typography.Compact.xaml`(신규) — `Themes/Layout.xaml`/`Themes/Typography.xaml`(일반 모드)과 정확히 동일한 `x:Key` 셋을 가진 컴팩트 전용 리소스 딕셔너리. 값 산출은 PRD가 직접 컴팩트 리터럴을 제공하는 항목(홈 화면 3.3장: 창 840×420, marginX 36, cardGap 12, cardVisualH 210, 헤더/하단 버튼 치수 등)은 그 값을 그대로 채택하고, PRD가 컴팩트 값을 안 주거나(리더기 설정 화면 다수 항목) 현재 "일반" 모드 값 자체가 Phase 4~5 트렌디 보정으로 PRD 리터럴과 달라져 있는 항목은 "PRD가 제시하는 그 항목의 컴팩트/일반 비율을 구해 현재 실제 적용값에 곱하는" 방식으로 근사했다(각 파일 상단 주석에 산출 근거 기록). 리더기 설정 창 크기(`ReaderWindowWidth/Height`)는 일반 모드와 동일한 방법론(컴포넌트 치수를 `SizeToContent="WidthAndHeight"`로 1회 렌더링 후 `GetWindowRect`로 실측해 고정)으로 722×749를 확정 — 액션 버튼 폭(100)이 PRD상 컴팩트 값이 없어 양쪽 모드 동일하게 유지되는 바람에 카드 폭의 하한선 역할을 해 창 폭 자체는 일반 모드(744) 대비 소폭만 줄어듦.
- `App.xaml`/`App.xaml.cs` — 리소스 딕셔너리 병합 전체를 XAML 선언(`Application.Resources`)에서 `App.OnStartup` 코드비하인드로 옮겼다. 이유: `Buttons.xaml`이 `Layout.xaml`의 키(`HomeCardCornerRadius`, `ReaderInfoButtonSize`)를 자기 파싱 시점에 `StaticResource`로 즉시 참조하므로, 일반/컴팩트 중 하나를 런타임에 골라 병합하려면 `Buttons.xaml`이 파싱되기 *전에* 그 선택이 끝나 있어야 한다 — 정적 XAML 선언(`InitializeComponent`가 `OnStartup`보다 먼저 실행됨)으로는 이 순서를 제어할 수 없어 전부 코드로 옮겼다. `OnStartup`이 `SystemParameters.PrimaryScreenHeight` 기준으로 판정한 뒤 `Colors → Typography(일반/컴팩트) → Layout(일반/컴팩트) → Buttons → ComboBox → ToggleSwitch → TextBox` 순서로 `Resources.MergedDictionaries`를 채우고 `base.OnStartup(e)`를 호출 — 그 시점에 `StartupUri`(`HomeWindow.xaml`)가 리소스가 모두 갖춰진 상태로 생성된다.
- **임의 판단(컴팩트 모드 판정 기준)**: `SystemParameters.WorkArea.Height`(작업표시줄 등을 제외한 가용 영역) 대신 `SystemParameters.PrimaryScreenHeight`(모니터 해상도 자체의 높이)를 채택했다. PRD 원문이 "화면 높이(screen height) ≤800px"라고 명시하고 있어 "이 모니터 자체가 저해상도인가"를 묻는 것에 더 가깝다고 판단했고, `WorkArea`는 작업표시줄 위치/두께에 따라 매번 달라져 같은 모니터에서도 판정이 오락가락할 수 있다는 점도 근거로 삼았다(판단 근거는 `App.xaml.cs` 주석에도 기록).
- **검증 방식**: 개발 환경 모니터가 800px 이하가 아니라 실제 자동 감지를 재현할 수 없어, `App.xaml.cs`의 `isCompact` 판정 직후에 `isCompact = true;`를 임시로 추가해 강제한 뒤 빌드/실행 → 홈 화면(840×420, 축소된 카드/폰트)과 리더기 설정 화면(컴팩트 `Layout.Compact.xaml`/`Typography.Compact.xaml` 반영, 722×749, 헤더·포트 설정·무결성 체크 정보·확인/취소 버튼까지 클리핑 없이 전부 표시)을 `mcp__windows__windows_screenshot`으로 캡처해 확인 — 이 과정에서 `ReaderSetupWindow.xaml`의 `Width`/`Height`도 일시적으로 `SizeToContent="WidthAndHeight"`로 바꿔 `GetWindowRect`(PowerShell `EnumWindows`/`GetWindowRect` P/Invoke)로 실측한 뒤 722×749를 `Layout.Compact.xaml`에 고정값으로 반영했다. 검증 후 강제 플래그와 `SizeToContent` 임시 변경을 모두 되돌리고 재빌드 → 일반 모드(1104×567 홈, 744×820 리더기 설정)가 이전과 동일하게 렌더링되는 것을 재확인해 회귀가 없음을 검증했다.

**4) 코드 정리**:
- 시작 창: `App.xaml`의 `StartupUri`가 이미 `Views/HomeWindow.xaml`로 되어 있음을 확인(Phase 1 노트의 "임시로 갤러리 창으로 변경되어 있음" 상태는 이미 이전 Phase에서 정리 완료된 상태였음 — 이번 Phase에서 추가로 변경할 것 없음).
- 죽은 리소스: `Themes/*.xaml`에 정의된 모든 `x:Key`가 프로젝트 내 다른 곳에서 `{StaticResource ...}`/`{DynamicResource ...}`로 실제 참조되는지 전수 조사(`grep`) — `HomeCardGap`/`HomeCardIconInset`/`HomeCardVisualH`/`HomeFooterBtnGap`/`HomeHeaderTop`/`HomeMarginX`/`ReaderActionButtonGap`/`ReaderBottomBtnGap`/`ReaderCardGap`/`ReaderListHeaderHeight`/`ReaderListHeight` 11개가 어디서도 참조되지 않는 것을 확인했다. 이들은 전부 "값의 출처를 문서화하려고 남겨둔 `sys:Double` 버전"으로, `Margin`/`RowDefinition.Height` 등에는 그 값을 반영한 `Thickness`/`GridLength` 완제품 리소스(`HomeHeaderMargin`, `ReaderCardGapMargin`, `ReaderListHeightGridLength` 등)만 실제로 쓰이고 있었다 — `Themes/Layout.xaml`과 신규 `Themes/Layout.Compact.xaml` 양쪽에서 제거하고, 값 자체는 남아있는 주석/완제품 리소스에 그대로 반영해 정보 손실 없이 정리했다. `Views/StyleGalleryWindow.xaml`(개발용 스타일 갤러리)은 `Buttons.xaml`/`ComboBox.xaml`/`ToggleSwitch.xaml`/`TextBox.xaml`/`Colors.xaml`의 스타일들을 참조하는 채로 남겨뒀으나 프로덕션 시작 창이 아니므로 참조처로만 취급하고 삭제하지 않았다.
- 정리 후 재검증: `grep`으로 `Themes/*.xaml` 전체 `x:Key`를 다시 스캔해 미참조 항목이 0개임을 확인했고, `Layout.xaml`/`Layout.Compact.xaml`과 `Typography.xaml`/`Typography.Compact.xaml` 각각의 키 집합이 `diff`로 완전히 동일함을 확인했다(컴팩트 스왑이 항상 안전하게 동작하기 위한 전제 조건).

**검증 결과**:
- `dotnet build` 성공(경고 0/오류 0) — 리소스 병합 방식을 코드비하인드로 전면 교체하고 죽은 리소스를 제거한 뒤에도 유지.
- 일반 모드(이 개발 환경 모니터 기준, `PrimaryScreenHeight` > 800): 홈 화면(1104×567)과 리더기 설정 화면(744×820) 모두 이번 Phase 작업 전과 픽셀 단위로 동일하게 렌더링됨을 스크린샷으로 재확인(회귀 없음).
- 컴팩트 모드(임시 강제): 홈 화면 840×420, 리더기 설정 화면 722×749로 축소되어 렌더링되고, 리더기 설정 화면의 모든 섹션(헤더/포트 설정 카드 2개/무결성 체크 정보/확인·취소 버튼)이 클리핑 없이 전부 표시되는 것을 확인.
- 카드 클릭 → 리더기 설정 모달 오픈, 콤보 활성/비활성 연동, 취소 버튼 클릭 시 정상 종료까지 일반 모드에서 재확인해 리소스 병합 방식 변경(App.xaml→App.xaml.cs)이 기존 인터랙션에 영향을 주지 않았음을 확인.

---

### Phase 6 컴팩트 모드 버그 수정 (2026-08-14, 사용자 실측 피드백: 1024×768 해상도)

사용자가 실제 1024×768(컴팩트 모드 대상) 환경에서 직접 실행해본 결과 3가지 문제를 보고했다:
1. 홈 화면 하단에 "선이 하나 추가된 것처럼 보임"
2. 홈 화면 헤더의 로그 전송/최신 버전 업데이트 버튼이 너무 크고 서로 붙어 있음
3. 리더기 설정 창이 화면을 꽉 채울 정도로 크고, 특히 COM 포트 콤보박스만 비정상적으로 큼

원인 조사 결과, 위 Phase 6 최초 구현 당시 놓친 **리터럴 하드코딩 3곳**이 원인이었다(전부 "치수/폰트는 항상 리소스 키를 경유한다"는 프로젝트 규칙을 어긴 지점 — 컴팩트 스왑 대상에서 빠져 있었음):
- `Themes/ComboBox.xaml`의 `SkinnedComboBoxStyle`이 `FontSize="15.33"`/`Padding="10,6"`을 리터럴로 고정하고 있어, 다른 모든 요소가 컴팩트 값으로 줄어드는데 콤보박스만 일반 모드 크기 그대로 렌더링됨(문제 3의 직접 원인).
- `Views/HomeWindow.xaml`의 헤더/하단 버튼 텍스트 6곳이 `FontSize="16"` 리터럴이라 컴팩트 모드에서도 축소되지 않아, 줄어든 버튼 박스(125×32/180×32)에 일반 크기 텍스트가 들어가면서 버튼이 커 보이고 두 버튼이 붙어 보임(문제 2의 원인). `Themes/Typography.Compact.xaml`에 이미 `HomeFooterButtonTextStyle`(13.33px)을 만들어뒀지만 정작 View에서 참조하지 않고 있었다.
- `Views/HomeWindow.xaml`의 하단 최소화/종료 버튼 행 여백이 `Margin="0,20,0,20"` 리터럴이라 컴팩트 모드에서도 그대로라, 위 구분선(Border, 실제로 있음 — 사용자가 본 "추가된 선"은 새로 생긴 요소가 아니라 이 기존 구분선이 좁아진 간격 탓에 버튼과 붙어 보인 것)과 버튼 사이 간격이 압축되어 눈에 띄게 좁아 보임(문제 1의 원인).

**수정**:
- `Themes/ComboBox.xaml`: `FontSize`/`Padding`을 새 리소스 키(`ReaderComboFontSize` — `Typography.xaml`/`.Compact.xaml`, `ReaderComboPadding` — `Layout.xaml`/`.Compact.xaml`)로 분리해 참조하도록 변경(일반 15.33/10,6, 컴팩트 13.14/8,4 — 콤보 폭 축소 비율과 동일한 "일반 텍스트" 0.857 비율 적용).
- `Views/HomeWindow.xaml`: 6곳의 `FontSize="16"` 리터럴을 전부 `FontSize="{StaticResource HomeFooterButtonFontSize}"`로 교체. **주의**: 처음엔 기존 `HomeFooterButtonTextStyle`(Foreground까지 포함한 `Style`)을 통째로 적용하려 했으나, 이 버튼들(`AutoButtonStyle`/`DefaultButtonStyle`/`UpdateButtonStyle`)은 `IsMouseOver` 트리거가 `Button.Foreground`를 바꾸고 내부 `TextBlock`이 그 값을 상속받는 구조라, `TextBlock`에 `Foreground`가 포함된 `Style`을 걸면 그 상속이 끊겨 호버 시 색이 안 바뀌는 회귀가 생긴다는 것을 뒤늦게 인지 — `HomeFooterButtonTextStyle`(Style)은 삭제하고 `FontSize` 전용 `sys:Double` 키(`HomeFooterButtonFontSize`, 일반 16/컴팩트 13.33)만 남겨 `Foreground`는 손대지 않도록 수정.
- `Themes/Layout.xaml`/`Layout.Compact.xaml`: `HomeFooterAreaMargin`(일반 `0,20,0,20`/컴팩트 `0,10,0,10`) 신규 추가, `Views/HomeWindow.xaml`의 하단 버튼 `StackPanel` `Margin`을 이 리소스로 교체.
- `Layout.Compact.xaml`의 `ReaderWindowHeight`: 콤보박스 버그가 있는 채로 측정됐던 최초값(749)이 실제보다 19px 컸음이 드러나, 버그 수정 후 같은 방법론(`SizeToContent="WidthAndHeight"`로 1회 렌더 → `GetWindowRect` 실측)으로 재측정해 **730**으로 갱신(`ReaderWindowWidth`(722)는 변화 없음 — 액션 버튼 폭이 하한선이라 콤보 폭 변화의 영향을 받지 않음).

**검증**: `dotnet build` 성공(경고 0/오류 0). `App.xaml.cs`의 `isCompact` 판정과 `ReaderSetupWindow.xaml`의 `Width`/`Height`를 다시 임시로 강제/`SizeToContent`로 바꿔 재현 → 수정 후 스크린샷에서 (1) 하단 구분선과 최소화/종료 버튼 사이에 명확한 간격이 확보되고 창 하단 가장자리에 이상 요소가 없음, (2) 로그 전송/최신 버전 업데이트 버튼이 분리되어 표시됨, (3) COM 포트 콤보박스가 라벨/버튼과 비례하는 크기로 축소됨, (4) 리더기 설정 창이 722×730으로(19px 추가 축소) 렌더링됨을 확인. 검증 후 임시 변경 모두 원복, 일반 모드(1104×567 홈, 744×820 리더기 설정)에서 콤보/버튼 크기·hover 색상 전환에 회귀가 없음을 재확인.

**알려진 한계(범위 밖)**: PRD 4.2가 요구하는 "작업영역 클램프"(창이 화면보다 크면 자동으로 줄이거나 위치 조정)는 여전히 미구현 상태다. 722×730은 1024×768 화면의 작업표시줄 제외 가용 영역(대략 720~730px 높이)에 거의 맞닿아 있어, 작업표시줄 두께/위치에 따라 여전히 빠듯하게 느껴질 수 있다 — 클램프 로직 자체는 이번 수정 범위가 아니며 별도로 다룰 사안(위 Phase 6 완료 기준에는 포함되지 않았던 항목).

---

### Phase 6 컴팩트 모드 폰트 교체: Pretendard → Malgun Gothic (2026-08-14, 사용자 실측 피드백)

바로 위 버그 수정을 반영한 뒤에도 사용자가 1024×768 실기에서 "원본 MFC도 이 해상도에서 Pretendard 글씨가 살짝 깨져 보이는 현상이 있었는데 지금도 있다"고 재확인했다. WPF `TextOptions.TextFormattingMode="Display"`(정수 픽셀 스냅) 적용을 먼저 시도했으나 실제 화면에서 체감 차이가 없어 되돌렸다(`Views/HomeWindow.xaml`/`Views/ReaderSetupWindow.xaml`에 잠깐 추가했다가 제거).

근본 원인은 폰트 자체다 — Pretendard는 작은 픽셀 크기(10~13px대)에서 최적화된 TrueType 힌팅 명령어가 없어 획이 많은 한글에서 안티앨리어싱이 거칠어 보이기 쉽고, 이건 원본(GDI 렌더링)과 WPF 둘 다 같은 폰트 파일을 쓰는 한 공통으로 겪는 한계다. 그런데 **`PRD_WPF.md` 1.5가 애초에 "컴팩트 화면에서도 Malgun Gothic"이라고 명시**하고 있었음에도, `Themes/Typography.Compact.xaml` 구현 시 이 부분을 놓치고 일반 모드와 동일하게 Pretendard를 먼저 쓰도록 해뒀던 것이 확인되어, PRD 원문대로 바로잡았다.

**수정**:
- `Themes/Typography.Compact.xaml`: `PretendardFontFamily`/`PretendardMediumFontFamily` 리소스 값을 임베디드 Pretendard(`pack://.../Assets/Fonts/#Pretendard...`)에서 시스템 폰트 `Malgun Gothic`으로 교체(일반 모드 `Themes/Typography.xaml`은 변경 없음 — Pretendard 유지). Windows 내장 폰트라 ClearType 힌팅이 잘 되어 있어 작은 크기에서도 획이 깨끗하다.
- **연쇄 버그 발견 및 수정**: 폰트를 바꾸자 확인/취소 버튼("확인"→"화이", "취소"→"최소"로 보일 만큼 아래쪽이 잘림)이 깨져 보였다. 처음엔 버튼 자체의 `Padding`/`FontSize` 문제로 의심해 `Themes/Buttons.xaml` `PrimaryButtonStyle`/`ReaderSecondaryButtonStyle`의 리터럴 `FontSize="14"`를 리소스화(`ReaderBottomBtnFontSize`, 일반 14/컴팩트 12.44)하고 `Padding`도 리소스화(`ReaderBottomBtnPadding`)해 `Views/ReaderSetupWindow.xaml`의 두 버튼에 명시적으로 적용했으나 — 재현 결과 증상이 전혀 변하지 않아, `ModernButtonBase`의 `ControlTemplate`을 다시 보니 애초에 `ContentPresenter`가 `Padding`을 `TemplateBinding`하지 않아 `Padding`은 이 두 버튼에서 처음부터 아무 효과가 없었다는 것도 함께 확인됨(그대로 남겨둠 — 무해하고 리터럴 제거 자체는 여전히 유효한 정리). 진짜 원인은 창 전체 높이(`ReaderWindowHeight`, 컴팩트 730)가 Malgun Gothic의 더 큰 줄 높이를 반영하지 못해 다이얼로그의 맨 아래 요소(확인/취소 버튼 행)가 창 경계에서 그대로 잘린 것이었다 — `SizeToContent="WidthAndHeight"`로 재실측한 결과 **742**로 갱신해 해결했다(`Layout.Compact.xaml`).

**검증**: `dotnet build` 성공(경고 0/오류 0). `isCompact`를 임시로 강제하고 홈/리더기 설정 화면을 캡처 — 폰트가 Malgun Gothic으로 렌더링되는 것을 확인했고, "확인"/"취소" 버튼 텍스트가 잘림 없이 완전하게 표시되는 것을 확대 대조로 확인했다. 검증 후 임시 강제 코드를 원복하고 일반 모드(1104×567 홈, 744×820 리더기 설정, Pretendard 유지)에서 회귀가 없음을 재확인했다. **참고**: 실제로 이 폰트가 Pretendard보다 1024×768 환경에서 더 선명하게 보이는지는 (a) 자동화 스크린샷으로는 서브픽셀/ClearType 수준의 차이가 잘 안 잡히고 (b) 사용자가 언급한 원본 MFC의 동일 현상이 폰트 자체의 한계(작은 크기 힌팅 부재)에서 기인한다는 분석에 근거해 PRD가 원래 지시한 대로 되돌린 것 — 실기에서 사용자가 직접 재확인 필요.

---

### Phase 6 컴팩트 모드 실측 2차 보정: 리더기 설정 창 높이 + 홈 화면 구분선 위치 (2026-08-18, 실제 1024×768 PC 실측 피드백)

사용자가 실제 1024×768 해상도 PC에서 컴팩트 모드를 실측하고 2가지 문제를 보고했다:
1. 리더기 설정 창의 아래쪽이 "살짝 안 보임" — 창 자체(722×742)가 1024×768 화면의 작업표시줄 제외 가용 영역(전형적으로 720~730px)보다 커서 하단이 화면 밖으로 잘려 보임.
2. 홈 화면 하단의 구분선(카드와 최소화/종료 버튼 사이 가로선)이 "너무 위에 붙어있다" — `Views/HomeWindow.xaml`의 헤더-카드 간격(Row1)/카드-구분선 간격(Row3)이 둘 다 리터럴 `"*"`로 고정돼 항상 1:1로 나뉘어, 컴팩트 모드에서 남는 공간이 좁아지자 구분선이 시각적으로 카드에 가깝게(위로 치우쳐) 보였다.

**수정 1(리더기 설정 창 높이 축소)**: `Themes/Layout.Compact.xaml`의 세로 방향 여백류를 축소하고 재실측:
- `ReaderMainCardPadding` 상하 18→12
- `ReaderSubCardPadding` 상하 11→8
- `ReaderCardPadding` 상하 9→7
- `ReaderCardGapMargin` 5→4
- `ReaderListHeightGridLength` 118→100

기존 방법론(`Views/ReaderSetupWindow.xaml`을 임시로 `SizeToContent="WidthAndHeight"`, `App.xaml.cs`의 `isCompact`를 임시로 `true`로 강제 → 빌드/실행 → PowerShell P/Invoke `GetWindowRect`/`GetDpiForWindow`로 실측)로 재측정한 결과 `ReaderWindowHeight`가 **742 → 691**로 축소됨을 확인(`ReaderWindowWidth`(722)는 변화 없음). 확인/취소 버튼 텍스트 잘림 없음, 카드/리스트 간격도 부자연스럽지 않음을 스크린샷 대조로 확인 후 값을 고정 반영, 임시 변경(SizeToContent/isCompact 강제) 모두 원복.

**수정 2(홈 화면 구분선 위치 보정)**: `Views/HomeWindow.xaml`의 Row1(헤더-카드 간격)/Row3(카드-구분선 간격) `RowDefinition.Height="*"` 리터럴 2곳을 각각 신규 리소스 키 `HomeHeaderCardGapGridLength`/`HomeCardDividerGapGridLength`로 교체. `Themes/Layout.xaml`(일반 모드)에는 기존 동작과 완전히 동일하도록 둘 다 `*`(1:1 비율)로 추가해 회귀가 없도록 했고, `Themes/Layout.Compact.xaml`에는 `1*`/`2*`(Row3 비중을 2배로)로 추가해 구분선이 카드-푸터버튼 사이 공간의 중앙 쪽으로 내려오도록 했다.

**검증**: `dotnet build` 성공(경고 0/오류 0, 실행 중이던 프로세스는 `taskkill`로 종료 후 빌드). `isCompact`를 임시로 강제해 컴팩트 모드로 실행 → 홈 화면 스크린샷에서 구분선이 카드-푸터 공간 중앙에 가깝게 이동한 것을 확인, 리더기 설정 창(722×691, 최종 고정값)을 열어 헤더/포트 설정 카드 2개/무결성 체크 정보/확인·취소 버튼 모두 클리핑 없이 표시됨을 확인. 이후 `isCompact` 강제를 원복하고 일반 모드(1104×567 홈, 744×820 리더기 설정)로 재실행해 두 화면 모두 이번 수정 전과 픽셀 단위로 동일하게 렌더링됨(회귀 없음)을 재확인.

---

### Phase 6 마무리 보정: 홈 카드 호버 애니메이션 + 조회 버튼 폭 + 구분선 비율 재확정 (2026-08-18)

이 프로젝트의 마지막 보정 라운드. 세 가지를 다뤘다.

**1) 홈 카드 호버 시 글자 흔들림/흐림 + 반응 지연**: `Themes/Buttons.xaml`의 `HomeCardButtonStyle`이 호버 시 카드 전체를 1.005배로 미세 확대하는 `ScaleTransform` 애니메이션을 갖고 있었는데, 이 확대가 매 프레임 카드 하위 트리(텍스트 포함)를 벡터로 다시 래스터라이즈해 ClearType 서브픽셀 위치가 흔들리는 현상("글자가 꿀렁거림", 컴팩트 모드는 폰트가 작아 더 두드러짐)과 그 재계산 비용으로 인한 호버 반응 지연을 함께 유발했다.
- 1차 시도: `CacheMode="BitmapCache"`(`RenderAtScale="2"`까지 조정)로 스케일 애니메이션을 GPU 비트맵 확대로 대체 — 흔들림은 해결됐으나, 캐싱된 비트맵을 확대하는 과정에서 항상 약간의 리샘플링이 남아 호버 시 글자가 흐려 보이는 새 문제가 생겼고, 캐싱 자체가 평상시(호버 전) 렌더링에도 영향을 줘 대기 상태에서까지 살짝 흐려 보이는 부작용이 발견됐다.
- 최종 해결: 1.005배 확대는 육안으로 거의 인지되지 않는 수준의 효과였으므로(과거 hover-lift 효과를 제거했을 때와 같은 판단), `BitmapCache`를 완전히 제거하고 호버 시 확대 애니메이션 자체를 삭제 — 글로우(그림자 `Opacity` 애니메이션)만 남겼다. 텍스트가 전혀 다시 그려지지 않으므로 흔들림/흐림/반응 지연이 구조적으로 발생할 수 없다. 눌림(press) 시 축소(0.96) 애니메이션은 그대로 유지하되, 마우스를 뗄 때 복귀 목표를 기존 `1.005`에서 `1`로 수정(호버 확대가 없어졌으므로).

**2) 리더기 설정 화면 "조회" 버튼 로딩 텍스트 잘림**: `QueryButton_Click`(코드비하인드)이 로딩 중 버튼 `Content`를 `"조회중..."`(6자)으로 바꾸고 스피너(`ReaderButtonStyle`)까지 표시하는데, 평상시 라벨 `"조회"`(2자) 기준으로 잡아둔 `ReaderQueryButtonWidth`(일반 72/컴팩트 61)가 로딩 상태 폭엔 부족해 글자가 잘렸다. 같은 스타일을 쓰는 액션 버튼(`ReaderActionButtonWidth=100`)은 이보다 긴 로딩 문구("다운로드중..." 7자)도 잘림 없이 들어가는 것을 근거로, `ReaderQueryButtonWidth`를 일반 72→88, 컴팩트 61→75로 확대(`Themes/Layout.xaml`/`Layout.Compact.xaml`).

**3) 홈 화면 구분선 위치 비율 재확정**: 바로 위(2026-08-18 2차 보정)에서 `HomeCardDividerGapGridLength`를 `1*:2*`로 도입했으나, 실기 스크린샷을 나란히 대조한 결과 변화폭이 육안으로 거의 구분되지 않을 만큼 미미했다(전체 여유 공간 자체가 작아 비율을 소폭 바꾸는 정도로는 체감 차이가 거의 없음). `1*:2.8*`로도 시도했으나 마찬가지였고, `1*:6*`으로 크게 올려서야 카드-구분선 간격이 뚜렷하게 벌어지는 것을 스크린샷으로 확인해 이 값으로 최종 확정(`Themes/Layout.Compact.xaml`, `Themes/Layout.xaml`의 일반 모드 `1:1`은 변경 없음).

**검증**: `dotnet build` 성공(경고 0/오류 0). `isCompact`를 임시로 강제해 컴팩트 모드로 재현 → 홈 화면 호버 애니메이션이 확대/흔들림 없이 글로우만 표시됨을 확인, 조회 버튼 로딩 상태("조회중..."+스피너)가 잘림 없이 표시됨을 확인(리더기 설정 창 722×691 유지, 추가 높이 변경 없음), 구분선이 `1:6` 비율에서 카드-구분선 간격이 뚜렷하게 벌어짐을 확인. 이후 강제 코드 원복, 일반 모드(1104×567 홈, 744×820 리더기 설정)에서 호버 애니메이션(글로우만)과 조회 버튼 폭 변경 모두 의도대로 반영되고 다른 회귀가 없음을 재확인.

**프로젝트 상태**: 이 커밋을 끝으로 1차 범위(홈 화면 + 리더기 설정 화면의 UX/UI 재구현, Phase 0~6)를 완료한다. 다음 기능 추가(실제 리더기 하드웨어/COM 포트 연동 등)는 별도 PRD 문서를 새로 작성해 진행한다.

> **2026-08-18 갱신**: 위 문장은 원래 "이 ROADMAP.md에 Phase 7부터 이어서 추가한다"였으나, 2차 개발이 `docs/payment_relay/`라는 독립 폴더(PRD·실행계획서·DLL·이미지 자산)를 갖추게 되면서 **로드맵도 그 폴더로 분리**했다 — 폴더명(`home_reader_setup`)이 1차 범위 화면을 가리키는데 결제 중계 계획이 그 안에 있으면 이름과 내용이 어긋나고, 3단 문서 세트(PRD → ROADMAP → 실행계획서)가 두 폴더에 흩어지기 때문이다. **Phase 번호를 이어간다는 원래 취지는 그대로 유지**된다(여기 0~6, `payment_relay/ROADMAP.md` 7~18). "작업 방식" 규칙도 그 문서에 동일하게 옮겨 적용한다.

---

# 2차 개발(결제 중계 기능)은 별도 문서로 이어집니다

Phase 7부터의 계획은 **`docs/payment_relay/ROADMAP.md`**에 있습니다. 이 문서(`home_reader_setup/`)는 1차 범위
— 홈 화면과 리더기 설정 화면의 UX/UI 재구현(Phase 0~6) — 의 계획과 이력만 다룹니다.

Phase 번호는 두 문서에 걸쳐 이어집니다(여기 0~6, 저기 7~). 같은 앱(`KFTCOneCAP.Wpf`)을 계속 확장하는 것이라
번호를 새로 시작하지 않습니다.

---

## 참고 문서
- `PRD_WPF.md` — 1차 범위(홈 화면·리더기 설정 화면 UX/UI) 요구사항 정의
- `screenshots/home_screen.png`, `screenshots/reader_setup.png` — 원본 실측 캡처
- `docs/payment_relay/PRD.md` / `ROADMAP.md` — 2차 범위(결제 중계 기능)
