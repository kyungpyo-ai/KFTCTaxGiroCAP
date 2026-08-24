using System;
using System.Windows.Threading;
using KFTCOneCAP.Wpf.Services.Diagnostics;
using KFTCOneCAP.Wpf.Services.Payment;
using KFTCOneCAP.Wpf.ViewModels;

namespace KFTCOneCAP.Wpf.Views;

/// <summary>
/// <see cref="IPaymentNoticePresenter"/>의 구현체(docs/payment_relay/development_plan.md P13-6).
/// Phase 15의 결제 워커(백그라운드 스레드)가 이 클래스만 통해 알림창을 다룬다 — 창/뷰모델 생성,
/// 상태 전환, 닫기를 전부 <see cref="_dispatcher"/>로 UI 스레드에 마샬링한다.
///
/// ★ <see cref="_window"/>/<see cref="_viewModel"/> 필드는 오직 UI 스레드 위에서만 읽고 쓴다 —
/// <see cref="RunOnUiThread"/>가 모든 진입점을 UI 스레드로 몰아주므로, 별도 락 없이도 안전하다
/// (호출 스레드가 이미 UI 스레드면 그대로 동기 실행, 아니면 <see cref="Dispatcher.Invoke(Action)"/>로
/// 동기 마샬링 — 어느 쪽이든 필드 접근은 항상 UI 스레드에서만 일어난다).
/// </summary>
public sealed class PaymentNoticePresenter : IPaymentNoticePresenter
{
    private readonly Dispatcher _dispatcher;
    private PaymentNoticeWindow? _window;
    private PaymentNoticeViewModel? _viewModel;

    public event EventHandler? Canceled;

    /// <summary>운영 코드에서는 인자 없이 생성 — 앱의 UI 스레드 Dispatcher를 사용한다.</summary>
    public PaymentNoticePresenter() : this(System.Windows.Application.Current.Dispatcher)
    {
    }

    /// <summary>테스트/개발용 — 임의의 Dispatcher를 주입할 수 있게 내부에 노출.</summary>
    internal PaymentNoticePresenter(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public void Show(PaymentNoticeState state)
    {
        RunOnUiThread(() =>
        {
            if (_window != null)
            {
                // 이미 떠 있으면 새 창을 또 만들지 않고 상태만 갱신한다(중복 Show 방지).
                _viewModel!.State = state;
                return;
            }

            _viewModel = new PaymentNoticeViewModel { State = state };
            _viewModel.Canceled += OnViewModelCanceled;

            _window = new PaymentNoticeWindow(_viewModel);
            _window.Closed += OnWindowClosed;
            _window.Show();
        });
    }

    public void ChangeState(PaymentNoticeState state)
    {
        RunOnUiThread(() =>
        {
            if (_viewModel is null)
            {
                FileLogger.Warn($"PaymentNoticePresenter.ChangeState({state}): 알림창이 열려 있지 않아 무시됨");
                return;
            }

            _viewModel.State = state;
        });
    }

    public void Close()
    {
        RunOnUiThread(() =>
        {
            if (_window is null)
            {
                FileLogger.Warn("PaymentNoticePresenter.Close: 알림창이 열려 있지 않아 무시됨");
                return;
            }

            _window.Close(); // OnWindowClosed에서 정리(취소/완료 등 어떤 경로로 닫히든 여기로 모임)
        });
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.Canceled -= OnViewModelCanceled;
        }

        if (_window != null)
        {
            _window.Closed -= OnWindowClosed;
        }

        _window = null;
        _viewModel = null;
    }

    private void OnViewModelCanceled(object? sender, EventArgs e) => Canceled?.Invoke(this, EventArgs.Empty);

    private void RunOnUiThread(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _dispatcher.Invoke(action);
        }
    }
}
