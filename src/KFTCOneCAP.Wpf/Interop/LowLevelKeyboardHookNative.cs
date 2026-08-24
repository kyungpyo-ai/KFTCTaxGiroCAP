// LowLevelKeyboardHookNative.cs — 전역 저수준 키보드 훅(WH_KEYBOARD_LL) P/Invoke 선언.
// docs/payment_relay/development_plan.md P13-5(결제 알림창 ESC 전역 후킹, PRD §5.3). 훅의 설치/해제
// 수명 관리는 Views/PaymentNoticeEscapeHook.cs가 맡는다 — 이 파일은 순수 네이티브 선언만 담는다
// (기존 Interop/ReaderSerialNative.cs와 동일한 계층 원칙: Interop은 P/Invoke 선언만, 정책은 상위 계층).
using System;
using System.Runtime.InteropServices;

namespace KFTCOneCAP.Wpf.Interop
{
    internal delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct KBDLLHOOKSTRUCT
    {
        public int vkCode;
        public int scanCode;
        public int flags;
        public int time;
        public IntPtr dwExtraInfo;
    }

    internal static class LowLevelKeyboardHookNative
    {
        internal const int WH_KEYBOARD_LL = 13;
        internal const int WM_KEYDOWN = 0x0100;
        internal const int WM_SYSKEYDOWN = 0x0104;
        internal const int VK_ESCAPE = 0x1B;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern IntPtr GetModuleHandle(string? lpModuleName);
    }
}
