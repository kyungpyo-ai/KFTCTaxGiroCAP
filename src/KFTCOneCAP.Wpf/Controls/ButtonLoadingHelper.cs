using System.Windows;

namespace KFTCOneCAP.Wpf.Controls;

/// <summary>
/// 2026-08-14 추가(Phase 5 보완, 사용자 피드백: "로딩 스피너가 없다"). `Button.Content`를
/// 로딩 문구로 바꿔치기하는 기존 방식(Views/ReaderSetupWindow.xaml.cs)은 유지하면서, 로딩 중
/// 여부만 별도로 표시하기 위한 첨부 속성. `Themes/Buttons.xaml`의 `ReaderButtonStyle`
/// `ControlTemplate`이 이 값을 `Trigger`로 참조해 회전 스피너의 표시/애니메이션을 제어한다
/// (버튼 자체에 새 DependencyProperty를 만들 수 없어 첨부 속성으로 구현 — WPF 표준 패턴).
/// </summary>
public static class ButtonLoadingHelper
{
    public static readonly DependencyProperty IsLoadingProperty =
        DependencyProperty.RegisterAttached(
            "IsLoading",
            typeof(bool),
            typeof(ButtonLoadingHelper),
            new PropertyMetadata(false));

    public static bool GetIsLoading(DependencyObject obj) => (bool)obj.GetValue(IsLoadingProperty);

    public static void SetIsLoading(DependencyObject obj, bool value) => obj.SetValue(IsLoadingProperty, value);
}
