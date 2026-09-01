using System;
using System.IO;
using System.Text;

namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 8(docs/payment_relay/development_plan.md P8-3) 최소 파일 로깅.
///
/// - 로깅 프레임워크(NLog/Serilog 등)를 도입하지 않는다 — "언제 무슨 일이 있었는지" 한 줄씩 남기는
///   것 이상의 요구사항(구조화 로그, 원격 전송, 동적 레벨 변경)이 PRD에 없다.
/// - 기록 위치는 C:\KFTC_PosAgent\KFTCTaxLog\ 다(Phase 22, docs/operations/development_plan.md P22-0,
///   PRD.md §1.1.1, 2026-09-01 확정 — 이전 %LOCALAPPDATA%\KFTCTaxGiroCAP\logs\에서 이전. 탐색기
///   기본 숨김 폴더라 현장 기사가 찾기 어려운 문제 해결). 이 경로는 사용자 프로필 밖(C:\ 루트)이라
///   일반 권한으로는 쓰기가 안 될 수 있어, app.manifest의 requireAdministrator로 앱을 항상 관리자
///   권한으로 실행한다 — SQLite DB 경로(IntegrityCheckStore)는 이 변경과 무관하게 그대로 둔다.
/// - 스레드 안전해야 한다 — Reader CALLBACK이 리더기별 수신 스레드에서 호출되므로(Phase 9부터)
///   UI 스레드와 동시에 로그를 쓸 수 있다. 파일 핸들을 매 호출마다 열고 닫는 대신, 프로세스 전체에서
///   하나의 lock으로 직렬화한다(동시 다발 기록에도 줄이 섞이지 않도록).
/// - 로깅 실패가 앱을 죽이면 안 된다 — 디스크 가득참/권한 문제 등은 조용히 무시한다(로깅 목적 자체가
///   진단이므로, 로깅 실패로 앱이 죽으면 본말이 전도된다).
/// </summary>
public static class FileLogger
{
    private static readonly object SyncRoot = new();

    private static string LogDirectory => @"C:\KFTC_PosAgent\KFTCTaxLog";

    public static void Info(string message) => Write(LogLevel.Info, message);

    public static void Warn(string message) => Write(LogLevel.Warn, message);

    public static void Error(string message) => Write(LogLevel.Error, message);

    private static void Write(LogLevel level, string message)
    {
        try
        {
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{LevelText(level)}] {message}{Environment.NewLine}";
            string filePath = Path.Combine(LogDirectory, $"{DateTime.Now:yyyy-MM-dd}.log");

            lock (SyncRoot)
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(filePath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // 로깅 실패(디스크 가득참/권한 문제 등)가 앱 동작에 영향을 주면 안 된다 — 조용히 무시.
        }
    }

    private static string LevelText(LogLevel level) => level switch
    {
        LogLevel.Info => "INFO",
        LogLevel.Warn => "WARN",
        LogLevel.Error => "ERROR",
        _ => "INFO",
    };
}
