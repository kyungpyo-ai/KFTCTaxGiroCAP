using System;
using KFTCOneCAP.Wpf.Protocol.Pos;
using KFTCOneCAP.Wpf.Services.Reader;

namespace KFTCOneCAP.Wpf.Services.Payment;

/// <summary>
/// <see cref="PosPaymentResultCode"/>(Flow 내부 의도)를 SPEC <c>#7 응답 코드</c>에 실을 3자리 문자열로
///바꾸는 **유일한 지점**(docs/payment_relay/development_plan.md P17-4). Flow(<c>PaymentOrchestrator</c>
/// 등)는 이 클래스를 통해서만 코드 문자열을 얻고, "E01" 같은 리터럴을 직접 쓰지 않는다.
///
/// <b>P17-3 계획 대비 위치 정정</b>: 원래 계획은 이 매핑이 <c>Protocol/Pos/PosPaymentResponse.Create</c>
/// 안에 있었다(구 임시 전문 시절 패턴을 그대로 따름). 하지만 `R2x`(리더기 DLL 연동 실패) 코드를 정하려면
/// <see cref="ReaderCommandOutcomeKind"/>/리더기 DLL 오류 이름을 알아야 하는데, 그건 <c>Protocol/Pos</c>가
/// 몰라야 하는 계층이다(ROADMAP "계층 구조" — `Protocol`은 리더기 DLL 세부사항을 알지 못한다). 그래서
/// 이 매핑은 리더기·VAN 실패를 둘 다 이미 아는 <c>Services/Payment</c>로 옮겼다. `Protocol/Pos`가 맡는
/// 부분은 완성된 3자리 문자열을 <see cref="PosResponseTelegram.Failure(PosRequestTelegram, string)"/>에
/// 실어 응답 전문을 만드는 것뿐이다.
///
/// <b>`Approved`/`VanDeclined`는 이 매핑에 등장하지 않는다</b> — 성공과 VAN 거절은 모두 VAN이 실제로
/// 응답한 경우이므로 relay 경로(P17-3)를 타고, 우리가 코드를 합성하지 않는다. 이 두 값으로 호출하면
/// 예외가 난다(호출자가 relay와 failure 경로를 혼동했다는 뜻이므로 조용히 넘기지 않는다).
/// </summary>
internal static class PosResultCodeMapper
{
    /// <summary>
    /// 세부 원인이 필요 없는 원캡 자체 판단 코드(<c>E</c>). <see cref="PosPaymentResultCode.
    /// ReaderResponseFailure"/>/<see cref="PosPaymentResultCode.ReaderDllFailure"/>는 세부 원인이
    /// 반드시 필요하므로 이 오버로드가 아니라 <see cref="ToTelegramCode(CardReadCommandOutcome)"/>를
    /// 쓴다 — 여기서 호출하면 예외가 난다(원인을 잃어버린 채 넘어가는 걸 막기 위함).
    /// </summary>
    internal static string ToTelegramCode(PosPaymentResultCode resultCode) => resultCode switch
    {
        PosPaymentResultCode.UserCanceled => "E01",
        PosPaymentResultCode.Timeout => "E02",
        PosPaymentResultCode.SetupScreenInProgress => "E03",
        PosPaymentResultCode.NoReaderConfigured => "E04",
        PosPaymentResultCode.IntegrityCheckFailure => "E05",
        PosPaymentResultCode.KioskIdMismatch => "E06",
        PosPaymentResultCode.InternalError => "E99",

        PosPaymentResultCode.Approved =>
            throw new ArgumentException("Approved는 relay 경로 전용이다 — 이 매핑을 거치면 안 된다(P17-3)."),
        PosPaymentResultCode.VanDeclined =>
            throw new ArgumentException("VanDeclined는 VAN이 실제로 응답한 경우라 relay 경로다 — 이 매핑을 거치면 안 된다(P17-3)."),
        PosPaymentResultCode.ReaderResponseFailure =>
            throw new ArgumentException($"{nameof(PosPaymentResultCode.ReaderResponseFailure)}는 리더기 업무 응답코드가 필요하다 — {nameof(ToTelegramCode)}(CardReadCommandOutcome)를 쓸 것."),
        PosPaymentResultCode.ReaderDllFailure =>
            throw new ArgumentException($"{nameof(PosPaymentResultCode.ReaderDllFailure)}는 DLL 오류 세부 원인이 필요하다 — {nameof(ToTelegramCode)}(CardReadCommandOutcome)를 쓸 것."),
        PosPaymentResultCode.VanCommunicationFailure =>
            throw new ArgumentException($"{nameof(PosPaymentResultCode.VanCommunicationFailure)}는 VAN 실패 종류가 필요하다 — {nameof(ToTelegramCode)}(VanFailureKind)를 쓸 것."),

        _ => throw new ArgumentOutOfRangeException(nameof(resultCode), resultCode, "매핑되지 않은 PosPaymentResultCode"),
    };

    /// <summary>
    /// 카드리딩 실패(<c>R</c>) — <see cref="CardReadCommandOutcome.Kind"/>로 업무 응답코드 실패(<c>R0x</c>)와
    /// DLL 연동 실패(<c>R2x</c>)를 한 번에 분기한다. <see cref="CardReadCommandOutcome.FailureCategory"/>가
    /// <see cref="ReaderFailureCategory.None"/>(성공)이면 호출하면 안 된다 — 성공은 카드리딩 완료 후
    /// 다음 단계(VAN)로 진행하는 것이지 실패 응답을 만드는 상황이 아니다.
    /// </summary>
    internal static string ToTelegramCode(CardReadCommandOutcome outcome) => outcome.Kind switch
    {
        // R0x: 리더기가 정상 응답했지만 업무적으로 실패(00/07/12 제외) — 리더기가 준 2자리 코드를
        // 그대로 R+코드로 옮긴다. 별도 채번 없이 리더기 SPEC 코드를 그대로 노출해야 리더기 SPEC
        // 문서(00~23)와 우리 로그를 대조하기 쉽다.
        ReaderCommandOutcomeKind.BusinessFailure => FormatReaderBusinessFailureCode(outcome.ResponseCode),

        // R2x: DLL 연동 레벨 실패. ReaderResult 이름 문자열로 분기한다(Interop.ReaderResult를 이
        // 계층에서 직접 참조하지 않기 위해 — CardReadCommandOutcome.DllResultName이 이미
        // ReaderSerialNative.ReaderResultToString이 만든 이름이다).
        ReaderCommandOutcomeKind.DllCallFailure => outcome.DllResultName switch
        {
            "READER_ERR_PORT_NOT_OPEN" => "R20",
            "READER_ERR_SEND_FAIL" => "R21",
            "READER_ERR_BUSY" => "R22",
            "READER_ERR_PORT_NOT_FOUND" => "R23",
            "READER_ERR_PORT_OPEN_FAIL" => "R24",
            "READER_ERR_COMMAND_NOT_ALLOWED" => "R25",
            _ => "R28", // PORT_CONFIG_FAIL/PORT_CLOSING/PORT_ALREADY_OPEN/INVALID_LENGTH/BUFFER_OVERFLOW/
                        // INTERNAL/INVALID_ARGUMENT/MAX_READER_COUNT/INVALID_READER_ID/PINPAD_NOT_SUPPORTED
                        // 등 — 결제 흐름 중 실제로 관찰된 적 없는 종류의 catch-all(development_plan.md
                        // P17-4 참고, 필요해지면 개별 코드로 쪼갠다).
        },

        // R29는 "예비"가 아니라 아래 ReaderBroadcastNoWinnerCode/ReaderNoCardDataDefensiveCode 두 방어적
        // 상황을 위해 남겨 뒀다(원래 계획엔 "예비"로 적었으나 P17-5 구현 중 실제 쓸 곳이 생겨 정정).

        // CommunicationError도 DLL 연동 레벨 실패로 분류되지만(ReaderCommandOutcomeKindExtensions
        // .ToFailureCategory) DllResultName이 비어 있으므로 별도 분기한다.
        ReaderCommandOutcomeKind.CommunicationError => "R27",

        // Timeout은 **일부러 여기 없다.** 리더기 로컬 명령 타임아웃과 거래 전체 데드라인 Timeout은
        // 사용자에게 같은 결과("카드 입력 시간 초과")로 보여야 한다(PaymentOrchestrator 클래스 주석 —
        // "어느 쪽이 근소하게 먼저 확정되든 결과 코드는 동일해야 사용자에게 차이가 없다"). 이 kind를
        // R2x로 따로 매핑하면 어느 경로가 이겼는지에 따라 POS가 받는 코드가 달라지는 비결정성이
        // 생긴다 — 그래서 리더기 로컬 Timeout도 반드시 <see cref="ToTelegramCode(PosPaymentResultCode)"/>
        // 의 E02(PosPaymentResultCode.Timeout)를 쓰도록 강제한다.
        ReaderCommandOutcomeKind.Timeout =>
            throw new ArgumentException(
                "리더기 로컬 Timeout은 거래 전체 Timeout(E02)과 같은 결과여야 한다 — " +
                $"{nameof(ToTelegramCode)}(PosPaymentResultCode.Timeout)을 쓸 것, R 코드로 분리하지 않는다."),

        ReaderCommandOutcomeKind.Success =>
            throw new ArgumentException("성공(Success) 결과로는 실패 응답 코드를 만들 수 없다 — 호출자가 성공/실패 분기를 잘못했다."),

        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome.Kind, "매핑되지 않은 ReaderCommandOutcomeKind"),
    };

    // R29 — "실패는 확실하지만 실어 보낼 CardReadCommandOutcome이 없는" 세 가지 경우가 공유한다.
    // 셋 다 서로 원인이 다르지만(전원 송신 실패/카드데이터 없는 방어 경로/재시도 상한 초과), 공통점은
    // "세부 outcome을 식별할 수 없다"는 것뿐이라 개별 코드로 쪼갤 근거가 없다 — 로그(FileLogger)의
    // 메시지가 실제 구분을 담당한다.

    /// <summary>참여 리더기 전원이 송신 자체에 실패해 개별 <see cref="CardReadCommandOutcome"/>이 없는
    /// 경우(<c>CardReadBroadcastResult.HasWinner == false</c>).</summary>
    internal static string ReaderBroadcastNoWinnerCode => "R29";

    /// <summary>업무 응답코드가 성공(00)인데 카드 데이터가 비어 있는, 이론상 불가능해야 하는 방어적
    /// 경로(<c>CardReadResponseParser</c> 계약 위반 방지용).</summary>
    internal static string ReaderNoCardDataDefensiveCode => "R29";

    /// <summary>07/12 응답이 반복돼 최대 재요청 횟수(<c>MaxCardReadRounds</c>)를 넘긴 경우 — 마지막
    /// 라운드의 outcome이 루프 지역 변수라 여기까지 살아남지 않는다.</summary>
    internal static string ReaderRetryLimitExceededCode => "R29";

    private static string FormatReaderBusinessFailureCode(string readerResponseCode)
    {
        if (readerResponseCode.Length != 2)
        {
            throw new ArgumentException(
                $"리더기 업무 응답코드는 2자리여야 함(SPEC 00~23): '{readerResponseCode}'", nameof(readerResponseCode));
        }

        return "R" + readerResponseCode;
    }

    /// <summary>VAN DLL(<c>KFTC_GIRO.dll</c>) 연동 실패(<c>D</c>) — Phase 20에서 실제로 채워진다.</summary>
    internal static string ToTelegramCode(VanFailureKind kind) => kind switch
    {
        VanFailureKind.DllLoadFailure => "D01",
        VanFailureKind.CommunicationFailure => "D02",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "매핑되지 않은 VanFailureKind"),
    };
}

/// <summary>
/// VAN DLL(<c>KFTC_GIRO.dll</c>) 연동 실패의 두 갈래(PRD §4.10) — Phase 20에서 실제 호출부가 이 값을
/// 채운다. 지금은 <see cref="PosResultCodeMapper"/>가 미리 매핑을 갖출 수 있도록 타입만 정의해 둔다.
/// </summary>
internal enum VanFailureKind
{
    /// <summary>DLL 자체를 로드하지 못함(Phase 8 로드 스모크가 실패로 이어지는 경우).</summary>
    DllLoadFailure,

    /// <summary><c>FNAISCRDVAN</c> 호출 자체가 실패(<c>nRet == -1</c>) — 서버 거절과 구분됨(PRD §4.10).</summary>
    CommunicationFailure,
}
