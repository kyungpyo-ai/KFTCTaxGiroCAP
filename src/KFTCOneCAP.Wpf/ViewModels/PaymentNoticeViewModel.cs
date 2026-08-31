using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KFTCOneCAP.Wpf.Services.Diagnostics;
using KFTCOneCAP.Wpf.Services.Payment;

namespace KFTCOneCAP.Wpf.ViewModels;

/// <summary>
/// 결제 알림창(Views/PaymentNoticeWindow.xaml)의 ViewModel.
/// <see cref="State"/>에서 화면의 이미지/문구/카드 애니메이션을 전부 파생시킨다
/// (<see cref="Views.PaymentNoticeWindow"/>의 "배경 소스 단일 지점" 규칙).
///
/// 이 클래스는 <c>Visibility</c> 등 WPF 타입을 다루지 않는다(P7-3에서 정립한 원칙 — UI 상태는
/// View/컨버터가 이 열거값에서 파생시킨다).
///
/// (docs/payment_relay/development_plan.md P13-2) 취소는 정확히 한 번만 나가야 한다 — 취소 버튼
/// 연타/ESC 연타(P13-5에서 연결)/버튼+ESC 동시 입력 어느 경우에도 <see cref="Canceled"/>는 1회만
/// 발생한다. <c>IPaymentNoticePresenter</c> 제어 진입점(P13-6)은 Phase 15에서 이어서 다룬다.
/// </summary>
public sealed partial class PaymentNoticeViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCancelAllowed))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private PaymentNoticeState _state = PaymentNoticeState.IcCardRequest;

    private bool _canceled;

    /// <summary>취소가 정확히 1회 발생했을 때 통지된다. Phase 15가 결제 워커에 중계한다.</summary>
    public event EventHandler? Canceled;

    /// <summary>
    /// PIN 4자리 입력이 완료됐을 때 통지된다(<see cref="IPaymentNoticePresenter.PinEntered"/>가 이
    /// 이벤트를 그대로 중계 — <see cref="Views.PaymentNoticePresenter"/> 참고). 실제 발화(키패드 입력
    /// 처리·자동 진행·연타 방어)는 아래 P18-3 구역(<see cref="PinDigit"/>/<see cref="CompletePinAsync"/>)이
    /// 담당하며, 발화는 <see cref="RaisePinEnteredEvent"/> 한 곳을 통해서만 이뤄진다.
    /// </summary>
    public event EventHandler<PinEnteredEventArgs>? PinEntered;

    /// <summary>
    /// 취소 가능 여부를 판정하는 유일한 지점(development_plan.md P13-2). 이미 취소했거나
    /// <see cref="PaymentNoticeState.VanProcessing"/> 상태면 취소할 수 없다 — VAN 요청이 나간 뒤
    /// 취소를 받으면 VAN 승인과 POS 응답이 어긋날 수 있기 때문(PRD §4.8/§5.3).
    /// </summary>
    public bool IsCancelAllowed => !_canceled && State != PaymentNoticeState.VanProcessing;

    [RelayCommand(CanExecute = nameof(IsCancelAllowed))]
    private void Cancel()
    {
        if (TryMarkCanceled())
        {
            RaiseCanceledEvent();
        }
    }

    /// <summary>
    /// (Opus 검증 리뷰 2026-08-24, H-3) 취소 가능 여부 판정과 <see cref="_canceled"/> 확정을
    /// 동기·원자적으로 수행한다(이벤트 통지는 하지 않음). ESC 전역 훅처럼 "지금 이 순간 삼킬지"를
    /// 결정하는 동시에 취소를 확정해야 하는 호출자를 위한 것이다.
    ///
    /// 원래는 훅 콜백이 삼킬지 여부만 동기로 정하고 실제 취소 실행은
    /// <see cref="System.Windows.Threading.Dispatcher.BeginInvoke(Delegate)"/>(Normal 우선순위)로
    /// 미뤘는데, 그 사이 Phase 15 워커가 <c>ChangeState(VanProcessing)</c>을
    /// <see cref="System.Windows.Threading.Dispatcher.Invoke(Delegate)"/>(Send 우선순위, Normal보다
    /// 먼저 처리됨)로 부르면 먼저 처리되어, 뒤늦게 실행된 취소가 이미 VanProcessing으로 바뀐
    /// <see cref="IsCancelAllowed"/>를 보고 조용히 무시되는 결함이 있었다 — ESC는 이미 삼켜져
    /// POS에도 전달되지 않았는데 취소는 일어나지 않는, 결제 시스템에서 가장 위험한 무증상 실패였다.
    /// 상태 전환(플래그 확정)은 여기서 동기로 끝내고, 무거울 수 있는 외부 구독자 통지
    /// (<see cref="RaiseCanceledEvent"/>)만 호출자가 별도로 지연시킨다.
    /// </summary>
    internal bool TryMarkCanceled()
    {
        if (!IsCancelAllowed)
        {
            return false;
        }

        _canceled = true;
        OnPropertyChanged(nameof(IsCancelAllowed));
        CancelCommand.NotifyCanExecuteChanged();
        return true;
    }

    /// <summary>
    /// <see cref="TryMarkCanceled"/>로 취소가 이미 확정된 뒤 구독자에게 통지한다 — 반드시 먼저
    /// <see cref="TryMarkCanceled"/>가 true를 반환한 경우에만 호출한다.
    /// </summary>
    internal void RaiseCanceledEvent() => Canceled?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// PIN 4자리 입력이 완료됐을 때 <see cref="PinEntered"/>를 통지한다(<see cref="RaiseCanceledEvent"/>와
    /// 같은 분리 패턴). "정확히 한 번"은 <see cref="_pinCompleted"/> sticky 플래그가 보장하며, 유일한
    /// 호출처는 <see cref="CompletePinAsync"/>다.
    /// </summary>
    internal void RaisePinEnteredEvent(string pin) => PinEntered?.Invoke(this, new PinEnteredEventArgs(pin));

    // ── P18-3: PIN 입력 로직 ────────────────────────────────────────────────

    /// <summary>PIN 노출 시간(development_plan.md P18-3 제안값) — 숫자를 누르면 이 시간만큼 잠깐
    /// 숫자로 보였다가 점으로 바뀐다.</summary>
    internal const int PinRevealDurationMs = 600;

    /// <summary>4자리 완성 후 자동 진행까지의 지연(P18-3 제안값) — 마지막 자리가 채워진 것이 화면에
    /// 보이도록 잠깐 기다린 뒤 <see cref="PinEntered"/>를 발화한다.</summary>
    internal const int PinCompleteDelayMs = 200;

    private const int PinMaxLength = 4;

    private readonly List<char> _pinDigits = new(PinMaxLength);

    // 창이 닫힐 때(Close 경로) 반드시 취소한다 — Phase 13 Opus 리뷰 H-1(데모 DispatcherTimer가 창을
    // 닫아도 계속 발화하며 창/뷰모델을 붙들던 누수)과 같은 함정을 Task.Delay + 토큰 취소로 피한다.
    private readonly CancellationTokenSource _pinCts = new();

    // 숫자를 누를 때마다 증가한다. 노출→마스킹 지연 작업이 완료됐을 때 "그사이 다른 숫자가 눌려
    // RevealedDigit이 이미 바뀌었는지"를 이 세대 번호로 판정한다(값 비교보다 안전 — 같은 숫자를
    // 연속으로 눌러도 오작동하지 않는다).
    private int _pinRevealGeneration;

    // 4자리 완성 시점에 true로 굳는다(Canceled의 _canceled와 같은 sticky 플래그) — 이후 숫자/삭제
    // 입력을 전부 무시해 PinEntered가 반드시 1회만 발화하게 한다(연타 방어).
    private bool _pinCompleted;

    // StopPinTimers가 실행됐는지(=창이 닫혀 _pinCts가 Dispose됐는지). 입력 커맨드도 이 플래그를 보고
    // 즉시 빠져나간다 — 자세한 이유는 PinDigit 주석 참고(최종 검증 M-1).
    private bool _pinTimersStopped;

    [ObservableProperty]
    private int _pinLength;

    /// <summary>지금 막 입력돼 잠깐 숫자로 보여줄 자리의 값. 없으면(마스킹 상태) <c>null</c>.</summary>
    [ObservableProperty]
    private string? _revealedDigit;

    /// <summary>
    /// P18-8(2026-08-27, 실장비 검증 중 사용자 확정 — 물리 키보드도 지원) — 전역 키보드 훅
    /// (<see cref="Views.PaymentNoticeKeyboardHook"/>)이 숫자키를 여기로 넘긴다. <c>IsCancelAllowed</c>/
    /// <c>TryMarkCanceled</c>와 같은 패턴: **판정 지점은 이 메서드 하나**다 — <see cref="State"/>가
    /// <see cref="PaymentNoticeState.PinEntry"/>가 아니면 아무것도 하지 않고 <c>false</c>(=미소비, 훅이
    /// 다른 프로그램으로 그대로 흘려보낸다)를 돌려준다. 맞으면 터치 키패드와 **완전히 같은**
    /// <see cref="PinDigit(string)"/>을 호출해 마스킹·자동 진행·연타 방어가 입력 수단과 무관하게
    /// 동일하게 적용되게 한다(입력 수단별로 로직을 중복 구현하지 않는다).
    /// </summary>
    internal bool TryPinDigit(char digit)
    {
        if (State != PaymentNoticeState.PinEntry)
        {
            return false;
        }

        PinDigit(digit.ToString());
        return true;
    }

    /// <summary>P18-8 — <see cref="TryPinDigit"/>와 같은 패턴의 Backspace 판정 지점.</summary>
    internal bool TryPinBackspace()
    {
        if (State != PaymentNoticeState.PinEntry)
        {
            return false;
        }

        PinBackspace();
        return true;
    }

    [RelayCommand]
    private void PinDigit(string digit)
    {
        // _pinTimersStopped(창이 이미 닫힘)를 여기서 함께 막는다 — 아래에서 _pinCts.Token에 접근하는데,
        // StopPinTimers가 CTS를 Dispose한 뒤라면 그 접근이 ObjectDisposedException을 던진다
        // (2026-08-27 Phase 18 최종 검증 M-1). Dispatcher.Invoke(Send)로 들어오는 Close가 이미 큐에
        // 쌓인 클릭(Input, 더 낮은 우선순위)보다 먼저 처리될 수 있어 실제로 열리는 순서다.
        if (_pinTimersStopped || _pinCompleted || string.IsNullOrEmpty(digit) || _pinDigits.Count >= PinMaxLength)
        {
            return;
        }

        _pinDigits.Add(digit[0]);
        PinLength = _pinDigits.Count;

        int generation = ++_pinRevealGeneration;
        RevealedDigit = digit;
        _ = RevealThenMaskAsync(generation, _pinCts.Token);

        if (_pinDigits.Count == PinMaxLength)
        {
            // sticky 플래그는 여기서 즉시 세운다 — 실제 PinEntered 발화는 아래에서 지연되지만, 그
            // 사이의 연타(숫자/삭제)는 이 시점부터 이미 전부 무시된다.
            _pinCompleted = true;
            _ = CompletePinAsync(_pinCts.Token);
        }
    }

    [RelayCommand]
    private void PinBackspace()
    {
        // PinDigit과 같은 이유로 _pinTimersStopped를 함께 막는다(창이 닫힌 뒤 큐에 남은 클릭 방어).
        if (_pinTimersStopped || _pinCompleted || _pinDigits.Count == 0)
        {
            return;
        }

        _pinDigits.RemoveAt(_pinDigits.Count - 1);
        PinLength = _pinDigits.Count;
        RevealedDigit = null;
        // 진행 중이던 노출→마스킹 지연 작업이 나중에 완료돼도 세대 번호가 달라 아무 효과가 없다.
        _pinRevealGeneration++;
    }

    private async Task RevealThenMaskAsync(int generation, CancellationToken token)
    {
        try
        {
            await Task.Delay(PinRevealDurationMs, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested || generation != _pinRevealGeneration)
        {
            return;
        }

        RevealedDigit = null;
    }

    private async Task CompletePinAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(PinCompleteDelayMs, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        string pin = new(_pinDigits.ToArray());
        // 거래 간 잔존 금지(P18-3) — 인스턴스가 들고 있던 자릿수 상태를 즉시 비운다. string 자체는
        // 메모리에서 0으로 덮어쓸 수 없지만(불변+인터닝), 참조를 즉시 끊는 것까지가 이 단계의 폐기
        // 수준이다(PRD §8.4).
        _pinDigits.Clear();
        RevealedDigit = null;
        FileLogger.Info("PaymentNoticeViewModel: PIN 4자리 입력 완료");

        RaisePinEnteredEvent(pin);
    }

    /// <summary>창이 닫힐 때 반드시 호출한다 — 진행 중인 PIN 노출/자동 진행 지연 작업을 전부
    /// 취소해, 닫힌 뒤에도 타이머가 계속 발화해 이미 닫힌 창/뷰모델을 붙드는 누수(P13 H-1과 같은
    /// 종류)를 막는다. <see cref="_pinCts"/>를 <c>Dispose</c>까지 해야 완전하다(2026-08-27 체크포인트
    /// 리뷰 L-1 — <see cref="PaymentDeadline"/>이 Cancel+Dispose 쌍으로 이미 겪은 것과 같은 종류의
    /// 누수를 Cancel만 하고 남겨 뒀었다). 이 메서드는 항상 UI 스레드(창의 <c>Closed</c> 이벤트)에서만
    /// 호출되므로 <c>_disposed</c> 플래그에 락이 필요 없다(이 클래스의 <see cref="_canceled"/>/
    /// <see cref="_pinCompleted"/> sticky 플래그와 같은 전제). 두 번 호출돼도 안전하다.</summary>
    internal void StopPinTimers()
    {
        if (_pinTimersStopped)
        {
            return;
        }

        _pinTimersStopped = true;
        _pinCts.Cancel();
        _pinCts.Dispose();
    }
}
