using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using KFTCOneCAP.Wpf.ViewModels;

namespace KFTCOneCAP.Wpf.Views;

/// <summary>
/// 가맹점 설정 화면.
/// Phase 23(docs/operations/development_plan.md P23-3) — MVVM으로 만든다(PRD.md §0.2). 검증/저장은
/// <see cref="ShopSetupViewModel"/>이 맡고, 이 코드비하인드는 <see cref="Window.Close"/>/
/// <see cref="Window.DialogResult"/>/<see cref="MessageBox"/> 호출과 경합 게이트
/// (<see cref="App.SetupScreenGate"/>) 등록/해제만 담당한다 — <c>ReaderSetupWindow.xaml.cs</c>와
/// 같은 역할 분담이다.
///
/// 워밍업 인스턴스가 없다(development_plan.md P23-4) — 컨트롤 6개뿐이라 최초 오픈 비용이 문제되는
/// 화면이 아니고, <c>ReaderSetupWindow.IsWarmupInstance</c> 같은 분기를 또 만들 이유가 없다.
///
/// 2026-09-02 Opus 리뷰(CP1) 개선권장 9(사용자 확정) — "dirty-check 확인창을 만들지 않는다"던 이전
/// 결정을 뒤집었다. 사용자가 "같은 설정창인데 당연히 있어야지"라고 확정해, <see cref="ReaderSetupWindow"/>와
/// 동일한 UX(취소/X/Alt+F4 모두 <see cref="ShopSetupViewModel.IsDirty"/>를 확인해 변경사항이 있으면
/// 확인창을 띄우고, "아니오"면 창을 유지한다)로 맞췄다. <c>확인</c> 경로는 이 확인창과 무관하다.
/// </summary>
public partial class ShopSetupWindow : Window
{
    public ShopSetupViewModel ViewModel { get; } = new();

    public ShopSetupWindow()
    {
        InitializeComponent();
        DataContext = ViewModel;
        ViewModel.ResultMessageReady += ViewModel_ResultMessageReady;
    }

    /// <summary>
    /// Phase 23(P23-4) — 이 창이 열려 있는 동안 결제 Flow가 카드 리딩을 시도하지 않도록
    /// <see cref="App.SetupScreenGate"/>에 등록한다(리더기 설정 화면과 카운터 공유, PRD.md §2.7).
    /// <c>Closed</c>에서 반드시 해제한다 — <c>Closing</c>이 아니라 <c>Closed</c>에 두는 이유는
    /// <see cref="ReaderSetupWindow"/>와 동일하다(<c>Closing</c>은 <see cref="CancelEventArgs.Cancel"/>로
    /// 취소될 수 있어 "실제로 닫혔다"를 보장하지 못한다 — 개선권장 9로 이 창도 이제 <c>Closing</c>을
    /// 취소하는 경로(dirty-check 확인창)가 생겼으므로 더더욱 <c>Closed</c>에 둬야 카운터가 정확하다).
    ///
    /// 2026-09-02 Opus 리뷰(CP1) L-3 — <see cref="ReaderSetupWindow"/>의 <c>_registeredInGate</c>
    /// 가드와 동일한 목적. 이 창엔 워밍업 인스턴스가 없어 <c>IsWarmupInstance</c> 분기는 필요 없지만,
    /// <c>Loaded</c>가 이론상 재진입하는 경우(비주얼 트리 재부착 등) 이중 등록으로 카운터가 영구히
    /// 새면 이후 모든 POS 결제가 <c>E03</c>으로 거부되는 실패 모드를 막는다.
    /// </summary>
    private bool _registeredInGate;

    private void ShopSetupWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // PRD 4.2와 같은 취지 — 초기 포커스는 확인(OK) 버튼.
        ConfirmButton.Focus();

        if (!_registeredInGate)
        {
            App.SetupScreenGate.Register();
            _registeredInGate = true;
        }
    }

    private void ShopSetupWindow_Closed(object? sender, EventArgs e)
    {
        if (_registeredInGate)
        {
            App.SetupScreenGate.Unregister();
            _registeredInGate = false;
        }
    }

    /// <summary>
    /// P23-3 — ViewModel은 MessageBox를 직접 호출하지 않는다(계층 규칙). 검증 실패/저장 실패 문구가
    /// 준비되면 이 이벤트로 알려오고, 여기서만 모달로 보여준다.
    /// </summary>
    private void ViewModel_ResultMessageReady(object? sender, string message) =>
        MessageBox.Show(this, message, "가맹점 설정", MessageBoxButton.OK, MessageBoxImage.Warning);

    /// <summary>
    /// 2026-09-02 Opus 리뷰(CP1) C-1 — <c>DialogResult</c> setter는 이 창이 <c>ShowDialog()</c>로
    /// 모달로 뜬 경우에만 유효하다(그 즉시 <c>Close()</c>까지 겸한다). 운영 경로
    /// (<c>HomeWindow.OpenShopSetup()</c>)는 항상 <c>Owner</c>를 지정해 모달로 열지만,
    /// <c>--shop-setup</c> 진단 인자 경로(<c>App.xaml.cs</c>의 <c>StartupUri</c>, 즉 <c>Show()</c>)는
    /// <c>Owner</c>가 <c>null</c>이라 모달이 아니다 — 그 상태에서 <c>DialogResult</c>를 설정하면
    /// <c>InvalidOperationException</c>이 발생하고, 이 앱엔 전역 예외 핸들러가 없어 그대로 프로세스가
    /// 죽는다(실기 재현됨, Windows 이벤트 로그에 <c>System.Windows.Window.set_DialogResult</c> 스택
    /// 확인). <c>Owner</c> 유무로 모달 여부를 판단해 비모달이면 <c>Close()</c>만 호출한다.
    /// </summary>
    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.TryConfirm())
            return; // 검증/저장 실패 — 창 유지(PRD.md §2.6)

        // 확인 경로는 dirty-check 확인창과 무관하다(개선권장 9) — 이미 저장을 마쳤으므로
        // ShopSetupWindow_Closing이 중복으로 dirty 확인을 띄우지 않도록 먼저 처리 완료를 표시한다.
        _closeHandled = true;

        if (Owner != null)
            DialogResult = true; // 모달이면 이 한 줄이 Close()까지 겸한다
        else
            Close();
    }

    /// <summary>
    /// 2026-09-02 개선권장 9 — X/Alt+F4 등 취소 버튼을 거치지 않는 모든 닫기 경로를 가로채기 위한
    /// 플래그. <see cref="ReaderSetupWindow"/>의 <c>_closeHandled</c>와 동일 목적 — 취소 버튼이
    /// 이미 자신의 정상 경로(dirty 확인 → Close)로 처리를 마쳤으면 <see cref="ShopSetupWindow_Closing"/>이
    /// 같은 확인을 중복 실행하지 않는다.
    /// </summary>
    private bool _closeHandled;

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        // PRD.md §2.6 — 취소는 아무것도 저장하지 않는다.
        if (!ConfirmDiscardIfDirty())
            return; // 창 유지

        _closeHandled = true;

        // 위 ConfirmButton_Click 주석 참고 — Owner가 없는 비모달 경로(--shop-setup)에서는
        // DialogResult 대신 Close()만 호출해야 크래시가 나지 않는다.
        if (Owner != null)
            DialogResult = false;
        else
            Close();
    }

    /// <summary>
    /// 2026-09-02 개선권장 9 — <see cref="ReaderSetupWindow.ReaderSetupWindow_Closing"/>과 동일
    /// 원칙. 취소 버튼을 거치지 않은 닫기(X, Alt+F4)를 가로채 같은 dirty-check를 적용한다.
    /// <see cref="_closeHandled"/>면(취소 버튼이 이미 처리) 아무것도 하지 않는다.
    /// </summary>
    private void ShopSetupWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_closeHandled)
            return;

        if (!ConfirmDiscardIfDirty())
        {
            e.Cancel = true;
            return;
        }

        _closeHandled = true;
    }

    /// <summary>
    /// PRD.md §2.6 취소 흐름 — 열려있는 팝오버를 먼저 닫고, 변경사항 판단은
    /// <see cref="ShopSetupViewModel.IsDirty"/>에 위임한다. dirty면 확인창을 띄우고 "아니오" 선택 시
    /// false(창 유지)를 반환한다. <see cref="CancelButton_Click"/>과 <see cref="ShopSetupWindow_Closing"/>
    /// 둘 다 이 메서드 하나를 공유한다(중복 구현 금지, <c>ReaderSetupWindow.ConfirmDiscardIfDirty</c>와
    /// 동일 패턴).
    /// </summary>
    private bool ConfirmDiscardIfDirty()
    {
        if (FieldInfoPopup.IsOpen)
            FieldInfoPopup.IsOpen = false;

        if (!ViewModel.IsDirty())
            return true;

        var result = MessageBox.Show(
            this,
            "변경된 내용이 있습니다.\n저장하지 않고 종료하시겠습니까?",
            "가맹점 설정",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        return result == MessageBoxResult.Yes;
    }

    // ===================== 필드 안내 info 팝오버 =====================
    // 필드 6개(금융결제원 서버/키오스크 고유번호/카드입력 타임아웃/자동 리부팅/자동 업데이트/
    // 결제 화면 잠금)가 단일 Popup(FieldInfoPopup)을 공유한다 — ReaderSetupWindow.xaml.cs
    // MultipadInfoButton_Click과 동일 패턴(같은 버튼 재클릭 시 닫히고, 다른 버튼 클릭 시
    // PlacementTarget과 내용만 바뀐다). 문구 출처: ShopSetupDlg.cpp(원본 MFC) 5개 + PRD.md §2.3
    // 근거 신규 1개(키오스크 고유번호) — development_plan.md P23-3 절 참고.
    // Popup의 PlacementTarget/IsOpen, TextBlock.Inlines 조작은 시각 요소 배치이며 ViewModel이 다룰
    // 데이터가 아니므로 View에 남는다(ReaderSetupWindow.xaml.cs 클래스 상단 주석과 같은 원칙).

    private void FieldInfoButton_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        if (FieldInfoPopup.IsOpen && ReferenceEquals(FieldInfoPopup.PlacementTarget, button))
        {
            FieldInfoPopup.IsOpen = false;
            return;
        }

        var (title, lines) = GetFieldInfo((string)button.Tag);
        FieldInfoTitleText.Text = title;
        FieldInfoBodyText.Inlines.Clear();
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
                FieldInfoBodyText.Inlines.Add(new LineBreak());
            FieldInfoBodyText.Inlines.Add(new Run(lines[i]));
        }

        FieldInfoPopup.PlacementTarget = button;
        FieldInfoPopup.IsOpen = true;
    }

    private static (string Title, string[] Lines) GetFieldInfo(string key) => key switch
    {
        // ShopSetupDlg.cpp 원본 문구(2026-09-02 CP949 복원). 콤보 항목명("운영 서버")과 다르지만
        // 원본 문구를 그대로 재현하는 것이 목적(작업 지시 확정 사항).
        "VanMode" => ("금융결제원 서버", new[]
        {
            "금융결제원 서버 선택",
            "· 실제 거래 서버 : 운영 환경 (기본값)",
            "· 테스트 서버 : 승인 테스트용",
            "· 테스트 서버(내부용) : 개발/검증용",
        }),
        // PRD.md §2.3 근거 신규 항목(원본 MFC에 없던 항목이라 원본 문구 없음).
        // 2026-09-02 §2.3.2 재확정(사용자 최종 결정) — 빈 값도 거부(E06)로 정책이 뒤집혀 문구를
        // 함께 정정했다. "비워두면 안전하다"는 인상을 주면 안 된다 — 설치 시 반드시 입력해야 한다.
        "KioskId" => ("키오스크 고유번호", new[]
        {
            "장애 보고에서 가맹점을 특정하는 유일한 키",
            "· 20자 이내 — 반드시 입력해야 한다(비어 있으면 모든 결제가 거부됨, E06)",
            "· POS 요청 값과 다르면 결제 거부(E06)",
        }),
        // ShopSetupDlg.cpp 원본 문구는 "100초"지만 실제 동작은 0=120초(PRD §2.4,
        // ShopSettingsService) — 혼란 방지를 위해 실제 동작을 마지막 줄에 덧붙인다.
        "CardReadTimeout" => ("카드입력 Timeout", new[]
        {
            "카드 입력 대기 시간 (초 단위)",
            "· 권장값: 100초 / 0 입력 시 자동 100초 설정",
            "(이 화면은 0 입력 시 120초로 동작합니다)",
        }),
        "AutoReboot" => ("자동 리부팅", new[]
        {
            "일일 단위 KFTCOneCAP 자동 리부팅 여부",
            "· 기본값 : 사용",
        }),
        // 2026-09-02 Opus 리뷰(CP1) 개선권장 7 — 원본 문구는 그대로 두고, 실제 이 화면의 기본값
        // (ShopSettings.AutoUpdate 기본 false, PRD.md §2.5)이 다르다는 보충 문구를 덧붙였다
        // (CardReadTimeout 항목의 "(이 화면은 …로 동작합니다)" 패턴과 동일).
        "AutoUpdate" => ("자동 업데이트", new[]
        {
            "프로그램 시작 시 kftc_updater.exe 자동 실행 여부",
            "· 기본값 : 사용",
            "(이 화면의 기본값은 미사용입니다)",
        }),
        "KeyinDim" => ("결제 화면 잠금", new[]
        {
            "결제 입력창 표시 시 배경 화면을 어둡게 잠금 처리",
            "· 기본값 : 미사용",
        }),
        _ => (string.Empty, Array.Empty<string>()),
    };
}
