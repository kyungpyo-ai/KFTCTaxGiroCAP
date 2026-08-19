using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace KFTCOneCAP.Wpf.ViewModels;

/// <summary>
/// 리더기1/2 카드의 액션 버튼(초기화/상태체크/키다운로드/무결성체크/업데이트) 1개의 상태.
/// Phase 7(docs/payment_relay/development_plan.md P7-2/P7-3): 코드비하인드가
/// <c>button.Content</c>/<c>ButtonLoadingHelper.SetIsLoading</c>을 직접 대입하던 방식을,
/// 버튼 1개당 이 ViewModel 1개로 옮겼다 — 클릭된 버튼만 로딩 문구/스피너로 바뀌고(PRD 4.7),
/// 나머지 버튼은 상위 패널의 IsEnabled(<see cref="ReaderSetupViewModel.Reader1CardEnabled"/> 등)
/// 바인딩으로 함께 잠긴다. 실제 리더기 통신은 이번 Phase 범위 밖(원본도 스텁)이라 3초 딜레이 후
/// 항상 성공 처리한다(development_plan.md P7-4 "스텁은 스텁대로 유지").
/// </summary>
public sealed partial class ReaderActionButtonViewModel : ObservableObject
{
    private readonly ReaderSetupViewModel _owner;
    private readonly string _normalContent;
    private readonly string _loadingContent;

    public ReaderActionButtonViewModel(ReaderSetupViewModel owner, string normalContent, string loadingContent)
    {
        _owner = owner;
        _normalContent = normalContent;
        _loadingContent = loadingContent;
        content = normalContent;
        ExecuteCommand = new AsyncRelayCommand(ExecuteAsync);
    }

    [ObservableProperty]
    private string content;

    [ObservableProperty]
    private bool isLoading;

    public IAsyncRelayCommand ExecuteCommand { get; }

    private async Task ExecuteAsync()
    {
        // PRD 4.7 "동시에 하나의 작업만 진행 가능" — 소유자(ReaderSetupViewModel)의 busy 상태를
        // 기준으로 판단한다. 패널 IsEnabled 바인딩이 이미 클릭 자체를 막아주지만, 기존
        // 코드비하인드(ActionButton_Click)와 동일하게 방어적으로 한 번 더 확인한다.
        if (_owner.IsBusy)
            return;

        _owner.IsBusy = true;
        IsLoading = true;
        Content = _loadingContent;

        await Task.Delay(3000);

        Content = _normalContent;
        IsLoading = false;
        _owner.IsBusy = false;
    }
}
