using System;
using System.Globalization;

namespace KFTCOneCAP.Wpf.Protocol.Pos;

/// <summary>
/// 앱→POS 결제 응답 전문의 임시 빌더(docs/payment_relay/development_plan.md P14-1). 실제 결과코드
/// 체계는 미확정이므로(PRD §10) 지금은 자유 문자열이다 — Phase 15~17에서 리더기/VAN 실패 사유가
/// 확정되면 이 클래스의 <see cref="ResultCode"/> 값 집합만 정리하면 된다.
///
/// Phase 15(P15-3)부터 결제 Flow는 이 생성자를 직접 쓰지 않고 <see cref="Create"/>만 쓴다 —
/// <see cref="PosPaymentResultCode"/>→전문 코드 매핑이 이 클래스 안에서만 이뤄져야, 실제 SPEC 확정
/// 시 매핑표 하나만 교체하면 되기 때문이다(Flow에는 "00"/"10" 같은 리터럴이 등장하지 않는다).
/// </summary>
internal sealed class PosPaymentResponse
{
    private const string Tag = "PAYRES";

    internal PosPaymentResponse(string resultCode, string transactionId, string message)
    {
        ResultCode = resultCode;
        TransactionId = transactionId;
        Message = message;
    }

    /// <summary>
    /// Phase 15(P15-3) — <see cref="PosPaymentResultCode"/>를 실제 전문 코드 문자열로 바꾸는 **유일한
    /// 지점**. <paramref name="reason"/>은 로그가 아니라 <b>전문 본문에 그대로 실린다</b> — ASCII만
    /// 허용되고 필드 구분자(<c>|</c>)도 쓸 수 없다(<see cref="ValidateBodyField"/>). 한글 사유는
    /// 호출자가 별도로 <c>FileLogger</c>에만 남겨야 한다.
    ///
    /// (2026-08-25, Opus 검증 리뷰 H-1 수정) 이 검증을 <see cref="ToFrame"/>(전송 직전)이 아니라
    /// **여기서 즉시** 한다 — 예전에는 여기를 통과한 뒤 <c>PosSocketServer.SendResponse</c>가
    /// <c>ToFrame()</c> 실패를 "응답 폐기 + 로그"로만 처리해서, 호출자가 <c>CardReadCommandOutcome.
    /// Detail</c> 같은 한글 사유 문자열을 무심코 넘기면 POS가 응답을 **아예 받지 못하고** 10초 유휴
    /// 종료까지 매달리는 사고가 될 수 있었다(원인이 로그에만 남아 추적이 오래 걸림). 지금은 이 자리
    /// (결제 Flow 코드 안, 예외를 던지는 그 시점)에서 즉시 실패하므로 개발 중에 바로 드러난다 —
    /// 설령 호출자가 실수해도 <c>TransactionQueue</c> 워커의 최상위 try/catch가 이 예외를 잡아
    /// <see cref="PosPaymentResultCode.InternalError"/> 응답(ASCII 고정 문자열)으로 대체해 POS에는
    /// 어떤 응답이든 도착한다 — "정보가 부정확한 응답"이 "응답 없음"보다 훨씬 낫다.
    /// </summary>
    internal static PosPaymentResponse Create(PosPaymentResultCode resultCode, string transactionId, string reason)
    {
        string code = resultCode switch
        {
            PosPaymentResultCode.Approved => "00",
            PosPaymentResultCode.ReaderResponseFailure => "10",
            PosPaymentResultCode.ReaderDllFailure => "11",
            PosPaymentResultCode.IntegrityCheckFailure => "12",
            PosPaymentResultCode.NoReaderConfigured => "13",
            PosPaymentResultCode.ReaderSetupInProgress => "14",
            PosPaymentResultCode.UserCanceled => "20",
            PosPaymentResultCode.Timeout => "21",
            PosPaymentResultCode.VanDeclined => "30",
            PosPaymentResultCode.VanCommunicationFailure => "31",
            PosPaymentResultCode.InternalError => "99",
            _ => throw new ArgumentOutOfRangeException(nameof(resultCode), resultCode, "매핑되지 않은 PosPaymentResultCode"),
        };

        ValidateBodyField(reason, nameof(reason));

        return new PosPaymentResponse(code, transactionId, reason);
    }

    /// <summary>
    /// 전문 본문에 들어갈 필드 하나가 안전한지 검증한다: (1) ASCII 범위(0x00~0x7F)만 허용 —
    /// <c>PosMessageEncoding</c>이 ASCII인 동안 비ASCII 문자는 <c>Encoding.ASCII.GetBytes</c>가
    /// 예외 없이 '?'로 치환해 조용히 깨진다(2026-08-24 실측 발견). (2) 필드 구분자(<c>|</c>) 금지 —
    /// 이 값이 <c>Tag|ResultCode|TransactionId|Message</c> 형태로 다른 필드와 이어붙는데, 값 안에
    /// <c>|</c>가 섞이면 POS 쪽 파서(<see cref="Pos.PosPaymentRequest.Parse"/>와 대칭되는 구조)가
    /// 필드 경계를 잘못 나눈다(2026-08-25, Opus 검증 리뷰 H-1 수정 — 원래 ASCII 가드만 있고 이
    /// 구분자 가드가 없었다).
    /// </summary>
    private static void ValidateBodyField(string value, string fieldName)
    {
        foreach (char c in value)
        {
            if (c > 0x7F)
                throw new PosProtocolException($"{fieldName}에 ASCII 범위를 벗어난 문자가 있음('{c}') — PosMessageEncoding이 ASCII인 동안은 영문/숫자만 허용");

            if (c == '|')
                throw new PosProtocolException($"{fieldName}에 필드 구분자('|')가 포함될 수 없음 — POS 파서가 필드 경계를 오인식함: '{value}'");
        }
    }

    internal string ResultCode { get; }

    internal string TransactionId { get; }

    internal string Message { get; }

    /// <summary>
    /// <c>[길이 4자리][PAYRES|결과코드|거래고유번호|메시지]</c> 프레임 바이트를 만든다. 길이 필드
    /// 자릿수(4)·형식은 <see cref="PosMessageFramer"/>가 기대하는 것과 반드시 일치해야 한다(P14-1
    /// 프레이밍 규칙) — 이 메서드가 프레이머와 별개로 프레이밍 규칙을 아는 유일한 지점이므로, 실제
    /// SPEC 확정 시 프레이머와 함께 여기도 같이 바뀐다.
    /// </summary>
    internal byte[] ToFrame()
    {
        // 필드별로 개별 검증한다(전체를 이어붙인 뒤 훑지 않는다) — <c>|</c> 구분자 검사는 필드 단위
        // 에서만 의미가 있다(이어붙인 body에는 설계상 항상 '|'가 들어 있다). <see cref="Create"/>가
        // reason(=Message)을 이미 검증하지만, 이 생성자를 직접 쓰는 경로(App.xaml.cs의 Phase 14
        // 스텁 등, Create를 거치지 않음)와 지금까지 검증한 적 없던 TransactionId까지 이 지점에서
        // 한 번 더 막는다(2026-08-25, Opus 검증 리뷰 H-1 수정 — 방어 계층 하나를 더 둠).
        ValidateBodyField(ResultCode, nameof(ResultCode));
        ValidateBodyField(TransactionId, nameof(TransactionId));
        ValidateBodyField(Message, nameof(Message));

        string body = $"{Tag}|{ResultCode}|{TransactionId}|{Message}";
        byte[] bodyBytes = PosMessageEncoding.Value.GetBytes(body);

        if (bodyBytes.Length > 9999)
            throw new PosProtocolException($"응답 본문이 길이 필드(4자리) 범위를 초과함: {bodyBytes.Length}바이트");

        byte[] lengthBytes = PosMessageEncoding.Value.GetBytes(bodyBytes.Length.ToString("D4", CultureInfo.InvariantCulture));

        byte[] frame = new byte[lengthBytes.Length + bodyBytes.Length];
        Buffer.BlockCopy(lengthBytes, 0, frame, 0, lengthBytes.Length);
        Buffer.BlockCopy(bodyBytes, 0, frame, lengthBytes.Length, bodyBytes.Length);
        return frame;
    }
}
