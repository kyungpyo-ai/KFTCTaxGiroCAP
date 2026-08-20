using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using KFTCOneCAP.Wpf.Services.Diagnostics;

namespace KFTCOneCAP.Wpf.Services.Storage;

/// <summary>
/// Phase 11(docs/payment_relay/development_plan.md, PRD §7) — 무결성 체크 이력을 SQLite에
/// 저장/조회하는 계층. ROADMAP.md "계층 구조"의 <c>Services/Storage/</c> 위치 — SQL을 직접 다루는
/// 것은 Storage의 책임이지 Protocol이 아니며, WPF 타입(Visibility/Dispatcher 등)은 알지 못한다.
///
/// - 패키지: <c>Microsoft.Data.Sqlite</c>(P11-1에서 채택 — 이유는 development_plan.md 참고).
/// - DB 파일 위치: <c>%LOCALAPPDATA%\KFTCTaxGiroCAP\</c> — Phase 8의 <see cref="FileLogger"/>가
///   로그를 두는 폴더와 같은 규칙(설치 폴더는 쓰기 권한 문제로 부적합).
/// - 모든 공개 메서드는 예외를 밖으로 던지지 않는다(P11-4, PRD §9) — DB 파일 손상/잠김/디스크
///   문제가 있어도 앱이 죽지 않아야 한다. 실패는 반환값으로 표현하고 <see cref="FileLogger"/>에
///   원인을 남긴다.
/// - 저장 실패와 무결성 체크의 업무 결과(성공/실패)는 서로 다른 축이다(2026-08-20 사용자 확정,
///   <see cref="IntegrityCheckSaveResult"/> 참고) — 이 클래스는 "저장이 됐는지"만 책임지고, 그
///   결과를 놓고 결제를 계속할지는 호출자(Phase 15)가 판단한다.
/// - 조회 실패 시 "이력 없음"으로 간주한다(P11-4) — 무결성 체크를 건너뛰는 것보다 다시 체크하는
///   쪽이 안전하기 때문이다.
/// </summary>
public sealed class IntegrityCheckStore
{
    /// <summary>PRD §2.1 — POS 식별번호는 설정 항목이 아니라 코드에 하드코딩된 상수다.</summary>
    public const string PosId = "KFTCTAXGIROCAP01";

    /// <summary>
    /// 저장 형식 "yyyy-MM-dd HH:mm:ss.fff"(로컬 시각) — 문자열 그대로 사전식 정렬해도 시간순
    /// 정렬/범위 비교가 성립하도록 고정 자리수 ISO 8601 스타일로 통일한다(P11-2).
    /// </summary>
    private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff";

    private readonly string _connectionString;

    public IntegrityCheckStore()
        : this(DefaultDatabasePath())
    {
    }

    /// <summary>테스트/진단 하네스가 임시 경로를 지정할 수 있도록 내부 생성자를 열어 둔다.</summary>
    internal IntegrityCheckStore(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
        }.ToString();
    }

    private static string DefaultDatabasePath()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KFTCTaxGiroCAP");
        return Path.Combine(dir, "integrity_check.db");
    }

    /// <summary>
    /// 무결성 체크 이력 1건을 저장한다. 실패해도 예외를 던지지 않고
    /// <see cref="IntegrityCheckSaveResult.Failed(string)"/>를 반환한다(P11-4).
    /// </summary>
    public IntegrityCheckSaveResult Save(IntegrityCheckRecord record)
    {
        try
        {
            using var connection = OpenConnection();
            EnsureSchema(connection);

            using var command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO IntegrityCheckHistory
    (CheckedAtLocal, ComPort, IsSuccess, ResponseCode, ModuleId, ReaderAuthId, PosId)
VALUES
    ($checkedAt, $comPort, $isSuccess, $responseCode, $moduleId, $readerAuthId, $posId);";
            command.Parameters.AddWithValue("$checkedAt", record.CheckedAt.ToString(TimestampFormat, CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$comPort", record.ComPort);
            command.Parameters.AddWithValue("$isSuccess", record.IsSuccess ? 1 : 0);
            command.Parameters.AddWithValue("$responseCode", (object?)record.ResponseCode ?? DBNull.Value);
            command.Parameters.AddWithValue("$moduleId", (object?)record.ModuleId ?? DBNull.Value);
            command.Parameters.AddWithValue("$readerAuthId", (object?)record.ReaderAuthId ?? DBNull.Value);
            command.Parameters.AddWithValue("$posId", PosId);
            command.ExecuteNonQuery();

            return IntegrityCheckSaveResult.Ok();
        }
        catch (Exception ex)
        {
            FileLogger.Error($"무결성 체크 이력 저장 실패: {ex.GetType().Name} - {ex.Message}");
            return IntegrityCheckSaveResult.Failed(ex.Message);
        }
    }

    /// <summary>
    /// 리스트 표시용 조회(리더기 설정 화면 §4.6) — <paramref name="fromInclusive"/>일
    /// 00:00:00.000부터 <paramref name="toInclusive"/>일 23:59:59.999까지(날짜 경계 양쪽 포함),
    /// 최신순으로 반환한다. 조회 실패 시 빈 목록을 반환한다(P11-4 — "이력 없음"으로 간주). 원본
    /// 값을 그대로 담은 DTO를 반환하며, 화면 표시용 서식·변환은 호출자(ViewModel) 책임이다
    /// (계층 규칙, <see cref="IntegrityCheckHistoryEntry"/> 참고).
    /// </summary>
    public List<IntegrityCheckHistoryEntry> GetHistory(DateTime fromInclusive, DateTime toInclusive)
    {
        try
        {
            using var connection = OpenConnection();
            EnsureSchema(connection);

            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT CheckedAtLocal, ComPort, IsSuccess, ResponseCode, ModuleId, ReaderAuthId, PosId
FROM IntegrityCheckHistory
WHERE CheckedAtLocal >= $from AND CheckedAtLocal < $toExclusive
ORDER BY CheckedAtLocal DESC;";
            command.Parameters.AddWithValue("$from", fromInclusive.Date.ToString(TimestampFormat, CultureInfo.InvariantCulture));
            // 날짜 경계 오류를 피하기 위해 "종료일 다음 날 00:00:00.000 미만"으로 배타적 상한을 둔다
            // (종료일 23:59:59.999를 리터럴로 넣는 것보다 자정 경계 실수가 적다).
            DateTime toExclusive = toInclusive.Date.AddDays(1);
            command.Parameters.AddWithValue("$toExclusive", toExclusive.ToString(TimestampFormat, CultureInfo.InvariantCulture));

            var entries = new List<IntegrityCheckHistoryEntry>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string checkedAtLocal = reader.GetString(0);
                string comPort = reader.GetString(1);
                bool isSuccess = reader.GetInt64(2) != 0;
                string? responseCode = reader.IsDBNull(3) ? null : reader.GetString(3);
                string? moduleId = reader.IsDBNull(4) ? null : reader.GetString(4);
                string? readerAuthId = reader.IsDBNull(5) ? null : reader.GetString(5);
                string posId = reader.GetString(6);

                DateTime checkedAt = DateTime.TryParseExact(
                    checkedAtLocal, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                    ? parsed
                    : DateTime.MinValue;

                entries.Add(new IntegrityCheckHistoryEntry(checkedAt, comPort, isSuccess, responseCode, moduleId, readerAuthId, posId));
            }

            return entries;
        }
        catch (Exception ex)
        {
            FileLogger.Error($"무결성 체크 이력 조회 실패: {ex.GetType().Name} - {ex.Message}");
            return new List<IntegrityCheckHistoryEntry>();
        }
    }

    /// <summary>
    /// 결제 선행 판정용(PRD §4.2) — 금일(로컬 자정 기준) 해당 COM 포트의 성공 이력이 있는지.
    /// 조회 실패 시 false를 반환한다(P11-4 — "이력 없음"으로 간주해 무결성 체크를 다시 수행하는
    /// 쪽이 안전하다).
    /// </summary>
    public bool HasSuccessToday(string comPort)
    {
        try
        {
            using var connection = OpenConnection();
            EnsureSchema(connection);

            DateTime todayStart = DateTime.Now.Date;
            DateTime tomorrowStart = todayStart.AddDays(1);

            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT COUNT(1)
FROM IntegrityCheckHistory
WHERE ComPort = $comPort
  AND IsSuccess = 1
  AND CheckedAtLocal >= $todayStart
  AND CheckedAtLocal < $tomorrowStart
LIMIT 1;";
            command.Parameters.AddWithValue("$comPort", comPort);
            command.Parameters.AddWithValue("$todayStart", todayStart.ToString(TimestampFormat, CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$tomorrowStart", tomorrowStart.ToString(TimestampFormat, CultureInfo.InvariantCulture));

            var count = (long)(command.ExecuteScalar() ?? 0L);
            return count > 0;
        }
        catch (Exception ex)
        {
            FileLogger.Error($"금일 무결성 체크 성공 이력 조회 실패({comPort}): {ex.GetType().Name} - {ex.Message}");
            return false;
        }
    }

    private SqliteConnection OpenConnection()
    {
        var builder = new SqliteConnectionStringBuilder(_connectionString);
        string? dir = Path.GetDirectoryName(builder.DataSource);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    /// <summary>
    /// 최초 실행 시 테이블/인덱스를 자동 생성한다(P11-2). "IF NOT EXISTS"라 매 호출마다 실행해도
    /// 저렴하며, DB 파일이 삭제된 뒤 재실행돼도 자동으로 재생성된다.
    /// </summary>
    private static void EnsureSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE IF NOT EXISTS IntegrityCheckHistory (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CheckedAtLocal TEXT NOT NULL,
    ComPort TEXT NOT NULL,
    IsSuccess INTEGER NOT NULL,
    ResponseCode TEXT NULL,
    ModuleId TEXT NULL,
    ReaderAuthId TEXT NULL,
    PosId TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_IntegrityCheckHistory_Today
    ON IntegrityCheckHistory (ComPort, IsSuccess, CheckedAtLocal);
CREATE INDEX IF NOT EXISTS IX_IntegrityCheckHistory_CheckedAt
    ON IntegrityCheckHistory (CheckedAtLocal DESC);";
        command.ExecuteNonQuery();
    }
}
