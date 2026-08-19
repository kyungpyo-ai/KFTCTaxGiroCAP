using System;
using System.Runtime.InteropServices;

namespace KFTCOneCAP.Wpf.Interop;

/// <summary>
/// 네이티브 경계 — Win32 LoadLibrary/FreeLibrary P/Invoke 선언만. 업무 로직 없음
/// (docs/payment_relay/ROADMAP.md "계층 구조" — Interop은 P/Invoke 선언만 담당).
///
/// Phase 8(development_plan.md P8-4)에서 두 DLL(ReaderSerial.dll/KFTC_GIRO.dll)의 로드 가능 여부만
/// 확인하는 스모크 테스트용. 실제 함수 호출(Reader_*, FNAISCRDVAN)은 Phase 9/17에서 별도로
/// Interop/ReaderSerialNative.cs, Interop/KftcGiroNative.cs에 선언한다.
/// </summary>
internal static class NativeLibrary
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FreeLibrary(IntPtr hModule);
}
