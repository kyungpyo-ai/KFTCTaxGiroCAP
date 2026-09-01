using System;
using System.IO;
using System.Runtime.InteropServices;
using KFTCOneCAP.Wpf.Interop;

namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 8(docs/payment_relay/development_plan.md P8-4) — 두 네이티브 DLL(ReaderSerial.dll,
/// KFTC_GIRO.dll)의 LoadLibrary 수준 로드 스모크 테스트.
///
/// - 여기서는 "로드만" 한다. 실제 함수 호출은 Phase 9(Reader)/Phase 17(VAN) 몫이다.
/// - 로드 실패해도 앱은 정상 기동해야 한다(PRD §9) — 이 클래스는 결과를 로그로만 남기고 예외를
///   던지지 않는다.
/// - KFTC_GIRO.dll은 MFC42.DLL/MSVCRT.dll/WSOCK32.dll에 의존한다(PRD §2.3). 파일은 존재하는데
///   의존 DLL이 없으면 LoadLibrary가 실패하며 Marshal.GetLastWin32Error()가 126
///   (ERROR_MOD_NOT_FOUND)을 반환한다 — 이 경우와 "파일 자체가 없음"(ERROR_FILE_NOT_FOUND=2)을
///   구분해서 로그에 남긴다.
/// </summary>
internal static class NativeDllLoadSmokeTest
{
    private const int ErrorFileNotFound = 2;
    private const int ErrorModNotFound = 126;

    public static void RunAll(string baseDirectory)
    {
        TryLoad(Path.Combine(baseDirectory, "ReaderSerial.dll"));
        TryLoad(Path.Combine(baseDirectory, "KFTC_GIRO.dll"));
    }

    private static void TryLoad(string dllPath)
    {
        string fileName = Path.GetFileName(dllPath);

        if (!File.Exists(dllPath))
        {
            FileLogger.Error(LogCategory.App, $"DLL 로드 스모크 실패: {fileName} — 파일이 출력 폴더에 없음 (경로: {dllPath})");
            return;
        }

        IntPtr handle = IntPtr.Zero;
        try
        {
            handle = NativeLibrary.LoadLibrary(dllPath);
            if (handle == IntPtr.Zero)
            {
                int errorCode = Marshal.GetLastWin32Error();
                string reason = errorCode switch
                {
                    ErrorFileNotFound => "ERROR_FILE_NOT_FOUND(2) — 파일 경로 문제(존재 확인은 통과했으나 로드 시점에 사라짐)",
                    ErrorModNotFound => "ERROR_MOD_NOT_FOUND(126) — 의존 DLL 누락 가능성 (예: MFC42.DLL/MSVCRT.dll/WSOCK32.dll, PRD §2.3)",
                    _ => $"Win32 오류 코드 {errorCode}",
                };
                FileLogger.Error(LogCategory.App, $"DLL 로드 스모크 실패: {fileName} — {reason}");
                return;
            }

            FileLogger.Info(LogCategory.App, $"DLL 로드 스모크 성공: {fileName} (핸들 획득)");
        }
        catch (Exception ex)
        {
            // LoadLibrary 자체는 예외를 던지지 않는 게 정상이지만, 방어적으로 감싼다
            // (PRD §9 — 로드 실패가 앱을 죽이면 안 된다).
            FileLogger.Error(LogCategory.App, $"DLL 로드 스모크 중 예외 발생: {fileName} — {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                NativeLibrary.FreeLibrary(handle);
            }
        }
    }
}
