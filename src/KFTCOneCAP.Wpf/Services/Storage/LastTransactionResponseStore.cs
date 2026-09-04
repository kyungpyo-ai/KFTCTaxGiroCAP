using System;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using KFTCOneCAP.Wpf.Security;
using KFTCOneCAP.Wpf.Services.Diagnostics;

namespace KFTCOneCAP.Wpf.Services.Storage;

/// <summary>
/// Phase 26(docs/payment_relay/development_plan.md P26-2, PRD.md §7.1) — "직전 거래 상태 조회"
/// 전문(P26-3/P26-4)이 응답을 복구할 수 있도록, <b>가장 최근 902614(추후 다른 전문도 포함 가능) 거래의
/// 응답 원문 전체</b>를 저장해 둔다. <see cref="ObservedIdentityStore"/>를 그대로 본뜬 구조다:
///
/// - <see cref="IntegrityCheckStore"/>와 물리적으로 같은 SQLite 파일(<see
///   cref="IntegrityCheckStore.DefaultDatabasePath"/>)을 쓰지만, 관심사가 다르므로(이력 vs 최신 1건)
///   별도 테이블(<c>last_transaction_response</c>)로 둔다.
/// - **이력을 쌓지 않는다.** 고정 키(<see cref="FixedRowId"/>) 1행만 upsert한다 — 다음 거래가 오면
///   이전 행을 덮어쓴다(PRD §7.1 "직전 거래"는 항상 단수).
/// - <see cref="IntegrityCheckStore"/>/<see cref="ObservedIdentityStore"/>와 동일하게 **공개 메서드는
///   예외를 밖으로 던지지 않는다**(PRD §9) — DB 파일이 잠기거나 손상돼도 결제가 죽으면 안 된다. 실패는
///   반환값(<see cref="Save"/>의 <c>bool</c>, <see cref="TryLoad"/>의 <c>null</c>)과 <see
///   cref="FileLogger"/> 경고로 표현한다.
/// - 저장하는 응답 본문은 <b><see cref="Protocol.Pos.PosResponseTelegram"/>이 카드리딩·PIN 필드(#45/#46/
///   #51/#53)를 이미 지운 뒤의 바이트</b>여야 한다(P26-1) — 이 클래스는 그 전제를 강제하지 않고 호출자가
///   지킨다(계층 규칙상 <c>Services/Storage/</c>는 <c>Protocol/</c>의 스키마 세부를 알 필요가 없다).
/// - <b>메모리 클리어</b>: <paramref name="responseBody"/>는 호출자가 이후에도 그대로 써야 하는 값(POS로
///   보낼 원문)이므로 이 클래스가 직접 지우지 않는다 — 대신 SQLite 파라미터 바인딩용으로 <b>별도 복사본</b>을
///   만들고, 바인딩이 끝난 뒤(<c>ExecuteNonQuery</c> 반환 시점) 그 복사본만 <see cref="SecureClear.Clear(byte[])"/>
///   로 즉시 지운다(운영 PRD §4.3.3 "임시 버퍼는 즉시", 카드정보 원본과 같은 취급).
/// </summary>
public sealed class LastTransactionResponseStore
{
    /// <summary>이력이 아니라 "가장 최근 1건"만 저장하므로 고정 키를 쓴다.</summary>
    private const int FixedRowId = 1;

    private readonly string _connectionString;

    public LastTransactionResponseStore()
        : this(IntegrityCheckStore.DefaultDatabasePath())
    {
    }

    /// <summary>테스트/진단 하네스가 임시 경로를 지정할 수 있도록 내부 생성자를 열어 둔다
    /// (<see cref="IntegrityCheckStore"/>/<see cref="ObservedIdentityStore"/>와 동일한 패턴).</summary>
    internal LastTransactionResponseStore(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
        }.ToString();
    }

    /// <summary>
    /// 직전 거래 응답 1건을 upsert한다(고정 키, 이력 없음). 저장 실패는 예외를 던지지 않고 <c>false</c>를
    /// 반환한다 — 저장은 복구 보조 수단이지 거래 성립 조건이 아니다(P26-2 완료 조건, PRD §9).
    /// </summary>
    /// <param name="managementNumber">SPEC <c>#9</c> 키오스크(요청기관) 전문 관리 번호.</param>
    /// <param name="transactionTypeCode">원거래 거래구분 코드(예: "902614").</param>
    /// <param name="responseBody">응답 전문 원문(P26-1을 이미 통과한 바이트여야 함). 이 메서드는 이
    /// 배열 자체를 지우지 않는다 — 저장용으로 별도 복사한 뒤 그 복사본만 지운다(클래스 요약 참고).</param>
    /// <param name="respondedAt">응답 시각(로컬).</param>
    public bool Save(string managementNumber, string transactionTypeCode, byte[] responseBody, DateTime respondedAt)
    {
        if (responseBody is null)
        {
            FileLogger.Warn(LogCategory.Payment, $"직전 거래 응답 저장 실패(managementNumber={managementNumber}, txType={transactionTypeCode}): responseBody가 null");
            return false;
        }

        byte[] bufferCopy = (byte[])responseBody.Clone();
        try
        {
            using var connection = OpenConnection();
            EnsureSchema(connection);

            using var command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO last_transaction_response
    (id, management_number, transaction_type_code, response_body, responded_at)
VALUES
    ($id, $managementNumber, $transactionTypeCode, $responseBody, $respondedAt)
ON CONFLICT(id) DO UPDATE SET
    management_number = excluded.management_number,
    transaction_type_code = excluded.transaction_type_code,
    response_body = excluded.response_body,
    responded_at = excluded.responded_at;";
            command.Parameters.AddWithValue("$id", FixedRowId);
            command.Parameters.AddWithValue("$managementNumber", managementNumber);
            command.Parameters.AddWithValue("$transactionTypeCode", transactionTypeCode);
            command.Parameters.AddWithValue("$responseBody", bufferCopy);
            command.Parameters.AddWithValue("$respondedAt", respondedAt.ToString(IntegrityCheckStore.TimestampFormat, CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();

            return true;
        }
        catch (Exception ex)
        {
            // 응답 원문 자체는 로그에 남기지 않는다(마스커 대상이긴 하지만, 저장 실패 로그에는 상관
            // 키(#9/거래구분)만 남기는 것으로 충분하다 — ObservedIdentityStore와 동일한 절제 원칙).
            FileLogger.Warn(LogCategory.Payment, $"직전 거래 응답 저장 실패(managementNumber={managementNumber}, txType={transactionTypeCode}): {ex.GetType().Name} - {ex.Message}");
            return false;
        }
        finally
        {
            // 바인딩(및 ExecuteNonQuery)이 끝난 시점에는 SQLite 엔진이 이미 값을 넘겨받았으므로, 저장용
            // 복사본은 더 이상 필요 없다 — 즉시 지운다(운영 PRD §4.3.3, 카드정보 원본과 같은 취급).
            SecureClear.Clear(bufferCopy);
        }
    }

    /// <summary>
    /// 저장된 직전 거래 응답 1건을 조회한다. 기록이 없거나 조회 자체가 실패하면 <c>null</c>을
    /// 반환한다(예외를 던지지 않는다, P26-2 완료 조건).
    /// </summary>
    public LastTransactionResponseRecord? TryLoad()
    {
        try
        {
            using var connection = OpenConnection();
            EnsureSchema(connection);

            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT management_number, transaction_type_code, response_body, responded_at
FROM last_transaction_response
WHERE id = $id;";
            command.Parameters.AddWithValue("$id", FixedRowId);

            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
                return null;

            string managementNumber = reader.GetString(0);
            string transactionTypeCode = reader.GetString(1);
            byte[] responseBody = reader.GetFieldValue<byte[]>(2);
            DateTime respondedAt = DateTime.ParseExact(
                reader.GetString(3), IntegrityCheckStore.TimestampFormat, CultureInfo.InvariantCulture);

            return new LastTransactionResponseRecord(managementNumber, transactionTypeCode, responseBody, respondedAt);
        }
        catch (Exception ex)
        {
            FileLogger.Warn(LogCategory.Payment, $"직전 거래 응답 조회 실패: {ex.GetType().Name} - {ex.Message}");
            return null;
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

    /// <summary>최초 실행 시 테이블을 자동 생성한다("IF NOT EXISTS", <see cref="IntegrityCheckStore"/>/
    /// <see cref="ObservedIdentityStore"/>와 동일한 패턴) — 어느 스토어가 먼저 열든 순서와 무관하게
    /// 안전하다(같은 파일, 서로 다른 테이블).</summary>
    private static void EnsureSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE IF NOT EXISTS last_transaction_response (
    id INTEGER PRIMARY KEY,
    management_number TEXT NOT NULL,
    transaction_type_code TEXT NOT NULL,
    response_body BLOB NOT NULL,
    responded_at TEXT NOT NULL
);";
        command.ExecuteNonQuery();
    }
}

/// <summary>
/// <see cref="LastTransactionResponseStore.TryLoad"/>가 돌려주는 조회 결과. 원본 값을 그대로 담은 DTO다
/// (계층 규칙 — 화면 표시용 서식·변환은 이 클래스의 책임이 아니다, <see
/// cref="Services.Storage.IntegrityCheckHistoryEntry"/>와 동일한 원칙).
/// </summary>
public sealed class LastTransactionResponseRecord
{
    public LastTransactionResponseRecord(
        string managementNumber, string transactionTypeCode, byte[] responseBody, DateTime respondedAt)
    {
        ManagementNumber = managementNumber;
        TransactionTypeCode = transactionTypeCode;
        ResponseBody = responseBody;
        RespondedAt = respondedAt;
    }

    /// <summary>SPEC <c>#9</c> 키오스크(요청기관) 전문 관리 번호.</summary>
    public string ManagementNumber { get; }

    /// <summary>원거래 거래구분 코드(예: "902614").</summary>
    public string TransactionTypeCode { get; }

    /// <summary>응답 전문 원문(BLOB) — P26-1을 이미 통과해 카드리딩·PIN 필드가 지워진 상태여야 한다.</summary>
    public byte[] ResponseBody { get; }

    /// <summary>응답 시각(로컬).</summary>
    public DateTime RespondedAt { get; }
}
