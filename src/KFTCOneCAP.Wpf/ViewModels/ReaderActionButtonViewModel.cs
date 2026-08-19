using System;
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
/// 바인딩으로 함께 잠긴다.
///
/// 실제 리더기 통신은 대부분 이번 Phase 범위 밖(원본도 스텁)이라 3초 딜레이 후 항상 성공 처리한다
/// (development_plan.md P7-4 "스텁은 스텁대로 유지"). 다만 Phase 9(P9-3) 파일럿에서 "초기화" 버튼
/// 1개(Reader1)만 <paramref name="customExecute"/>로 실제 0x60/0x70 왕복에 임시 연결됐다 — 정식
/// 배선·문구·실패 처리는 Phase 12 몫이며, 여기서는 최소한으로만 둔다(ReaderSetupViewModel 참고).
/// </summary>
public sealed partial class ReaderActionButtonViewModel : ObservableObject
{
    private readonly ReaderSetupViewModel _owner;
    private readonly string _normalContent;
    private readonly string _loadingContent;
    private readonly Func<Task>? _customExecute;

    public ReaderActionButtonViewModel(ReaderSetupViewModel owner, string normalContent, string loadingContent, Func<Task>? customExecute = null)
    {
        _owner = owner;
        _normalContent = normalContent;
        _loadingContent = loadingContent;
        _customExecute = customExecute;
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

        if (_customExecute != null)
        {
            await _customExecute();
        }
        else
        {
            await Task.Delay(3000);
        }

        Content = _normalContent;
        IsLoading = false;
        _owner.IsBusy = false;
    }
}
