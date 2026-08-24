using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using KFTCOneCAP.Wpf.Interop;
using KFTCOneCAP.Wpf.Services.Diagnostics;

namespace KFTCOneCAP.Wpf.Views;

/// <summary>
/// 결제 알림창(<see cref="PaymentNoticeWindow"/>) 전용 전역 ESC 훅
/// (docs/payment_relay/development_plan.md P13-5, PRD §5.3). POS 등 다른 프로그램에 포커스가 있어도
/// ESC를 감지해야 하므로 창의 <c>KeyDown</c>이 아니라 <c>WH_KEYBOARD_LL</c>(전역 저수준 키보드 훅)을
/// 사용한다.
///
/// ★ 이 인스턴스가 <see cref="_proc"/> 델리게이트를 필드로 들고 있는 동안에만 훅이 안전하다 — 지역
/// 변수로 넘기면 네이티브 쪽은 계속 그 주소를 참조하는데 관리 객체는 GC가 수거해버려 랜덤한 시점에
/// 프로세스가 죽는다(Phase 9 P9-2에서 리더기 CALLBACK에 대해 세운 것과 정확히 같은 규칙).
///
/// 콜백 안에서는 "삼킬지 말지"를 정하는 데 필요한 최소 동기 판정(<see cref="_isCancelAllowed"/> 호출 —
/// 필드 읽기 수준)만 하고, 실제 취소 처리(<see cref="_onEscapeCancel"/>)는
/// <see cref="Dispatcher.BeginInvoke(Delegate)"/>로 미뤄 즉시 반환한다 — 저수준 훅 콜백이 느리면 OS가
/// 훅을 강제로 떼어내기 때문이다(그 처리 자체는 가볍더라도, 콜백 안에서 직접 실행하지 않는다는 원칙을
/// 지킨다).
/// </summary>
internal sealed class PaymentNoticeEscapeHook : IDisposable
{
    private readonly LowLevelKeyboardProc _proc;
    private readonly Func<bool> _isCancelAllowed;
    private readonly Action _onEscapeCancel;
    private readonly Dispatcher _dispatcher;
    private IntPtr _hookId = IntPtr.Zero;

    public PaymentNoticeEscapeHook(Func<bool> isCancelAllowed, Action onEscapeCancel, Dispatcher dispatcher)
    {
        _isCancelAllowed = isCancelAllowed ?? throw new ArgumentNullException(nameof(isCancelAllowed));
        _onEscapeCancel = onEscapeCancel ?? throw new ArgumentNullException(nameof(onEscapeCancel));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _proc = HookCallback; // 필드로 보관 — GC 보호(위 클래스 주석 참고)
    }

    public void Install()
    {
        if (_hookId != IntPtr.Zero)
        {
            return;
        }

        _hookId = LowLevelKeyboardHookNative.SetWindowsHookEx(
            LowLevelKeyboardHookNative.WH_KEYBOARD_LL,
            _proc,
            LowLevelKeyboardHookNative.GetModuleHandle(null),
            0);

        if (_hookId == IntPtr.Zero)
        {
            FileLogger.Error($"결제 알림창 ESC 전역 훅 설치 실패 (Win32Error={Marshal.GetLastWin32Error()})");
        }
    }

    public void Uninstall()
    {
        if (_hookId == IntPtr.Zero)
        {
            return;
        }

        LowLevelKeyboardHookNative.UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsKeyDown(wParam))
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (data.vkCode == LowLevelKeyboardHookNative.VK_ESCAPE && _isCancelAllowed())
            {
                _dispatcher.BeginInvoke(_onEscapeCancel);
                // 우리가 취소로 소비했으므로 다른 프로그램(POS 등)에 이중으로 전달하지 않는다
                // (development_plan.md P13-5 "ESC를 삼킬 것인가" 확정 사항). VanProcessing 등
                // 취소를 처리하지 않는 구간은 이 분기에 들어오지 않으므로 아래로 흘러 CallNextHookEx.
                return (IntPtr)1;
            }
        }

        return LowLevelKeyboardHookNative.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static bool IsKeyDown(IntPtr wParam)
    {
        long msg = wParam.ToInt64();
        return msg == LowLevelKeyboardHookNative.WM_KEYDOWN || msg == LowLevelKeyboardHookNative.WM_SYSKEYDOWN;
    }

    public void Dispose() => Uninstall();
}
