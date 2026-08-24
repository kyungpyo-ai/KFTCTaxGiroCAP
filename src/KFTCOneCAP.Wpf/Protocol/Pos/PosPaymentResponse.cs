using System;
using System.Globalization;

namespace KFTCOneCAP.Wpf.Protocol.Pos;

/// <summary>
/// 앱→POS 결제 응답 전문의 임시 빌더(docs/payment_relay/development_plan.md P14-1). 실제 결과코드
/// 체계는 미확정이므로(PRD §10) 지금은 자유 문자열이다 — Phase 15~17에서 리더기/VAN 실패 사유가
/// 확정되면 이 클래스의 <see cref="ResultCode"/> 값 집합만 정리하면 된다.
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
        string body = $"{Tag}|{ResultCode}|{TransactionId}|{Message}";

        // PosMessageEncoding이 ASCII인 동안은 비-ASCII 문자(한글 등)를 넣으면 .NET Encoding.ASCII가
        // 예외 없이 '?'로 치환해 응답이 조용히 깨진다(2026-08-24 --pos-client-test로 실측 발견 —
        // 내부 오류 메시지에 한글을 썼다가 PAYRES 본문이 깨진 채로 나갔다). 그래서 여기서 미리
        // 검증해 호출자 실수를 그 자리에서 예외로 드러낸다.
        foreach (char c in body)
        {
            if (c > 0x7F)
                throw new PosProtocolException($"응답 본문에 ASCII 범위를 벗어난 문자가 있음('{c}') — PosMessageEncoding이 ASCII인 동안은 영문/숫자만 허용");
        }

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
