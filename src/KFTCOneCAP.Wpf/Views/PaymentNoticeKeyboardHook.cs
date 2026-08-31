using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using KFTCOneCAP.Wpf.Interop;
using KFTCOneCAP.Wpf.Services.Diagnostics;

namespace KFTCOneCAP.Wpf.Views;

/// <summary>
/// 결제 알림창(<see cref="PaymentNoticeWindow"/>) 전용 전역 키보드 훅
/// (docs/payment_relay/development_plan.md P13-5, PRD §5.3 — ESC / P18-8 — PIN 숫자·Backspace).
/// POS 등 다른 프로그램에 포커스가 있어도 이 키들을 감지해야 하므로 창의 <c>KeyDown</c>이 아니라
/// <c>WH_KEYBOARD_LL</c>(전역 저수준 키보드 훅)을 사용한다.
///
/// ★ 이 인스턴스가 <see cref="_proc"/> 델리게이트를 필드로 들고 있는 동안에만 훅이 안전하다 — 지역
/// 변수로 넘기면 네이티브 쪽은 계속 그 주소를 참조하는데 관리 객체는 GC가 수거해버려 랜덤한 시점에
/// 프로세스가 죽는다(Phase 9 P9-2에서 리더기 CALLBACK에 대해 세운 것과 정확히 같은 규칙).
///
/// (Opus 검증 리뷰 2026-08-24, H-3) "삼킬지 판정"과 "취소 확정"을 <see cref="_tryCancel"/> 한 호출로
/// **동기·원자적으로** 처리한다 — 이 훅은 자신을 설치한 UI 스레드 위에서 호출되므로(WH_KEYBOARD_LL의
/// 표준 동작), 이 호출이 끝나기 전까지는 다른 UI 스레드 작업(Phase 15 워커의
/// <c>Dispatcher.Invoke(ChangeState)</c> 등)이 끼어들 수 없다. 예전에는 판정만 동기로 하고 실제 취소
/// 실행을 <see cref="Dispatcher.BeginInvoke(Delegate)"/>로 미뤘는데, 그 사이 Send 우선순위로 들어온
/// <c>ChangeState(VanProcessing)</c>이 먼저 처리되면 ESC는 이미 삼켰는데 취소는 조용히 무시되는
/// 결함이 있었다. 무거울 수 있는 외부 통지(<see cref="_notifyCanceled"/>)만 계속
/// <see cref="Dispatcher.BeginInvoke(Delegate)"/>로 미룬다 — 저수준 훅 콜백이 느리면 OS가 훅을 강제로
/// 떼어내기 때문에, 상태 확정처럼 빠른 것만 동기로 하고 나머지는 미루는 원칙은 유지한다.
///
/// <b>P18-8(PIN 물리 키보드 입력, 2026-08-27 실장비 검증 중 사용자 확정)</b>: 숫자키/Backspace는 ESC와
/// 판정 방식이 정확히 같다 — <see cref="_tryPinDigit"/>/<see cref="_tryPinBackspace"/>가
/// <c>PaymentNoticeViewModel.State == PinEntry</c>일 때만 <c>true</c>(소비)를 돌려주고, 그 안에서
/// 기존 터치 키패드가 쓰는 바로 그 private 메서드(<c>PinDigit</c>/<c>PinBackspace</c>)를 그대로
/// 호출한다 — 입력 수단(터치/키보드)별로 로직을 중복 구현하지 않는다. PIN 입력이 동기로 화면 프로퍼티만
/// 바꾸므로(무거운 외부 통지 없음) ESC의 <c>_notifyCanceled</c> 같은 지연 단계가 필요 없다. 같은 창에
/// 두 번째 저수준 훅을 걸지 않고 기존 ESC 훅(옛 이름 <c>PaymentNoticeEscapeHook</c>)을 그대로 넓혀
/// 쓰는 이유는 콜백 오버헤드와 설치/해제 수명 관리 코드를 두 배로 만들지 않기 위해서다.
/// </summary>
internal sealed class PaymentNoticeKeyboardHook : IDisposable
{
    private readonly LowLevelKeyboardProc _proc;
    private readonly Func<bool> _tryCancel;
    private readonly Action _notifyCanceled;
    private readonly Func<char, bool> _tryPinDigit;
    private readonly Func<bool> _tryPinBackspace;
    private readonly Dispatcher _dispatcher;
    private IntPtr _hookId = IntPtr.Zero;

    public PaymentNoticeKeyboardHook(
        Func<bool> tryCancel, Action notifyCanceled,
        Func<char, bool> tryPinDigit, Func<bool> tryPinBackspace,
        Dispatcher dispatcher)
    {
        _tryCancel = tryCancel ?? throw new ArgumentNullException(nameof(tryCancel));
        _notifyCanceled = notifyCanceled ?? throw new ArgumentNullException(nameof(notifyCanceled));
        _tryPinDigit = tryPinDigit ?? throw new ArgumentNullException(nameof(tryPinDigit));
        _tryPinBackspace = tryPinBackspace ?? throw new ArgumentNullException(nameof(tryPinBackspace));
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
            FileLogger.Error($"결제 알림창 키보드 전역 훅 설치 실패 (Win32Error={Marshal.GetLastWin32Error()})");
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

            if (data.vkCode == LowLevelKeyboardHookNative.VK_ESCAPE && _tryCancel())
            {
                // 취소는 위 _tryCancel() 안에서 이미 동기로 확정됐다 — 여기서는 통지만 미룬다.
                _dispatcher.BeginInvoke(_notifyCanceled);
                // 우리가 취소로 소비했으므로 다른 프로그램(POS 등)에 이중으로 전달하지 않는다
                // (development_plan.md P13-5 "ESC를 삼킬 것인가" 확정 사항). VanProcessing 등
                // 취소를 처리하지 않는 구간은 _tryCancel()이 false를 반환해 이 분기에 들어오지 않으므로
                // 아래로 흘러 CallNextHookEx.
                return (IntPtr)1;
            }

            char? digit = LowLevelKeyboardHookNative.TryMapDigit(data.vkCode);
            if (digit is { } d && _tryPinDigit(d))
            {
                // PinEntry 상태가 아니면 _tryPinDigit이 false를 돌려주므로 이 분기에 들어오지 않고
                // 아래로 흘러 CallNextHookEx — ESC와 동일한 "취소 불가 구간에서는 삼키지 않는다" 원칙.
                return (IntPtr)1;
            }

            if (data.vkCode == LowLevelKeyboardHookNative.VK_BACK && _tryPinBackspace())
            {
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
