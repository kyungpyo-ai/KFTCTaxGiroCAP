using System;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using KFTCOneCAP.Wpf.Services.Diagnostics;

namespace KFTCOneCAP.Wpf.Services.Storage;

/// <summary>
/// Phase 22(docs/operations/development_plan.md P22-7, PRD.md §1.6) — "장애 보고 봉투"에 실을 진단
/// 컨텍스트 중 관측 시점이 한정된 값(<c>리더기 인증 식별 번호</c>, <c>H/W모델명(12) + F/W버전(4)</c>)을
/// 저장한다. <see cref="IntegrityCheckStore"/>와 물리적으로 같은 SQLite 파일(<see
/// cref="IntegrityCheckStore.DefaultDatabasePath"/>)을 쓰지만, 관심사가 다르므로(이력 vs 최신 관측값)
/// 별도 클래스·별도 테이블로 둔다.
///
/// - **키-값 스키마**(<c>scope</c>+<c>key</c> 복합키) — 장래 관측 항목이 늘어도 테이블 구조를 바꾸지
///   않는다(PRD §1.6).
/// - **upsert만 한다.** 이력을 쌓지 않는다 — 필요한 건 "가장 최근에 본 값"뿐이다.
/// - <c>scope</c>는 포트 표시 문자열이다. 이 프로젝트에서 이미 통일된 형식(<see
///   cref="Reader.ComPortFormat.ToDisplay"/>, 예: <c>"COM 05"</c>)을 그대로 쓴다 — <see
///   cref="IntegrityCheckStore"/>의 <c>ComPort</c> 컬럼과 같은 규칙이라 값이 흩어지지 않는다(PRD 원문의
///   <c>'COM3'</c> 표기는 예시일 뿐, 이 프로젝트의 기존 표시 형식을 새로 만들지 않는다).
/// - <see cref="IntegrityCheckStore"/>와 동일하게 **공개 메서드는 예외를 밖으로 던지지 않는다**
///   (P11-4) — 진단용 부가 정보를 저장하다가 결제가 실패하면 본말전도다.
/// - 리더기 인증 식별 번호는 DB엔 원문 저장하지만, **호출부는 이 값을 로그 메시지에 넣지 않는다**
///   (PRD §1.4/§1.6, 16자리 hex라 마스커가 카드/PIN 패턴으로 오탐할 위험을 만들지 않기 위해) — 이
///   클래스도 값 자체를 로그로 남기지 않는다(저장 실패 로그에도 scope/key만 남긴다).
/// </summary>
public sealed class ObservedIdentityStore
{
    /// <summary>PRD §1.6 — 이번 범위에서 저장하는 유일한 키. 모듈ID·리더기 이름·리더기 버전·키
    /// 버전은 저장하지 않는다(2026-08-31 확정).</summary>
    public const string ReaderAuthIdKey = "reader_auth_id";

    private readonly string _connectionString;

    public ObservedIdentityStore()
        : this(IntegrityCheckStore.DefaultDatabasePath())
    {
    }

    /// <summary>테스트/진단 하네스가 임시 경로를 지정할 수 있도록 내부 생성자를 열어 둔다
    /// (<see cref="IntegrityCheckStore"/>와 동일한 패턴).</summary>
    internal ObservedIdentityStore(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
        }.ToString();
    }

    /// <summary>
    /// <paramref name="scope"/>+<paramref name="key"/>로 upsert한다(P22-7 완료 조건 "같은 포트로 두 번
    /// 관측하면 행이 늘지 않고 덮어써진다"). 저장 실패는 예외를 던지지 않고 조용히 로그만 남긴다
    /// (P11-4와 동일한 정책 — 관측값 저장 실패가 결제/상태체크 흐름을 막으면 안 된다). <paramref
    /// name="value"/>는 로그에 남기지 않는다(클래스 요약 참고).
    /// </summary>
    public void Upsert(string scope, string key, string value)
    {
        try
        {
            using var connection = OpenConnection();
            EnsureSchema(connection);

            using var command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO observed_identity (scope, key, value, observed_at)
VALUES ($scope, $key, $value, $observedAt)
ON CONFLICT(scope, key) DO UPDATE SET
    value = excluded.value,
    observed_at = excluded.observed_at;";
            command.Parameters.AddWithValue("$scope", scope);
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value);
            command.Parameters.AddWithValue("$observedAt", DateTime.Now.ToString(IntegrityCheckStore.TimestampFormat, CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            // value는 의도적으로 로그에 넣지 않는다(클래스 요약 — 16자리 hex 마스커 오탐 방지 규율).
            // 사소 4(P22 리뷰) — 카테고리 없는 호출을 App으로 구조화한다.
            FileLogger.Error(LogCategory.App, $"observed_identity 저장 실패(scope={scope}, key={key}): {ex.GetType().Name} - {ex.Message}");
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

    /// <summary>최초 실행 시 테이블을 자동 생성한다("IF NOT EXISTS", <see cref="IntegrityCheckStore"/>와
    /// 동일한 패턴) — <see cref="IntegrityCheckStore"/>가 먼저 열든 이 클래스가 먼저 열든 순서와
    /// 무관하게 안전하다(같은 파일, 서로 다른 테이블).</summary>
    private static void EnsureSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE IF NOT EXISTS observed_identity (
    scope TEXT NOT NULL,
    key TEXT NOT NULL,
    value TEXT NOT NULL,
    observed_at TEXT NOT NULL,
    PRIMARY KEY (scope, key)
);";
        command.ExecuteNonQuery();
    }
}
