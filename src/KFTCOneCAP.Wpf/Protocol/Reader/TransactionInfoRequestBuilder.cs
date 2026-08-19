using System;
using System.Collections.Generic;

namespace KFTCOneCAP.Wpf.Protocol.Reader
{
    /// <summary>
    /// 0x2B(거래정보 요청) Data 필드 빌더. 필드 순서·길이·패딩·기본값 출처:
    /// vendor/ReaderSerial/CSharpSample/CommandFieldSpecs.cs의 TRANSACTION_INFO_REQUEST(SPEC
    /// §3.39, p.86~88 — 이미 reader-spec-expert가 SPEC 원문과 대조 확인한 값, development_plan.md
    /// P10-2가 재확인 지시한 대로 2026-08-19 reader-pinpad-spec-expert에게 다시 위임해 13개 필드
    /// 순서·길이·패딩이 SPEC §3.39와 정확히 일치함을 재확인했다). 13개 필드를 그 순서대로
    /// 이어붙인다(구분자 없음).
    /// </summary>
    internal static class TransactionInfoRequestBuilder
    {
        // ===================== 거래구분 상수 (PRD §4.3/§4.4) =====================
        internal const string TransactionTypeIc = "ARQo";
        internal const string TransactionTypeFallback = "F";

        // RF 거래 순서: 거래구분에 'R'이 없으면 "00"(SPEC §3.39). ARQo/F 모두 이 프로젝트에서는
        // 고정값으로 둔다 — CommandFieldSpecs.cs의 TRANSACTION_INFO_REQUEST 기본값과 동일하게
        // "00"을 LENGTH_PREFIXED payload로 그대로 사용한다(참조 구현을 따름, 새로 설계하지 않음).
        private const string RfTransactionSequenceDefault = "00";

        // RF 리딩 방식: vendor 샘플의 TRANSACTION_INFO_REQUEST 기본값("3")을 그대로 쓴다(PRD §4.3
        // "나머지 요청 필드는 리더기 샘플 소스를 참고한다" 지시).
        private const string RfReadingMethodDefault = "3";

        internal static byte[] Build(TransactionInfoRequest request)
        {
            var buffer = new List<byte>(128);

            // 1. 거래 일시 X(14)
            buffer.AddRange(ReaderFieldEncoding.PadFixedFieldBytes(request.TransactionDateTime, 14, ReaderFieldPad.RightSpace));

            // 2. 거래 금액 X(18), 왼쪽 '0' 패딩
            buffer.AddRange(ReaderFieldEncoding.PadFixedFieldBytes(request.Amount, 18, ReaderFieldPad.LeftZero));

            // 3. AID 인덱스 X(1)
            buffer.AddRange(ReaderFieldEncoding.PadFixedFieldBytes(request.AidIndex, 1, ReaderFieldPad.RightSpace));

            // 4. 거래구분 (길이 2 + 가변) — "ARQo"(4자)/"F"(1자) 모두 이 한 경로로 처리된다(P10-2
            // 완료 조건: 양쪽에서 길이 접두가 올바를 것).
            buffer.AddRange(ReaderFieldEncoding.BuildLengthPrefixedFieldBytes(request.TransactionTypeCode, 2));

            // 5. RF 리딩 방식 X(1)
            buffer.AddRange(ReaderFieldEncoding.PadFixedFieldBytes(RfReadingMethodDefault, 1, ReaderFieldPad.RightSpace));

            // 6. RF 거래 순서 (길이 2 + 가변)
            buffer.AddRange(ReaderFieldEncoding.BuildLengthPrefixedFieldBytes(RfTransactionSequenceDefault, 2));

            // 7. PIN 블록 입력 여부 X(1)
            buffer.AddRange(ReaderFieldEncoding.PadFixedFieldBytes(request.PinBlockInputRequired, 1, ReaderFieldPad.RightSpace));

            // 8. FILLER X(16) — 예비 필드, Space 고정
            buffer.AddRange(ReaderFieldEncoding.PadFixedFieldBytes(string.Empty, 16, ReaderFieldPad.RightSpace));

            // 9~12. 메시지 1~4 X(16) 각 — 완성형 그대로(이중 변환 금지, 클래스 주석 참고)
            buffer.AddRange(ReaderFieldEncoding.PadFixedFieldBytes(request.Message1, 16, ReaderFieldPad.RightSpace));
            buffer.AddRange(ReaderFieldEncoding.PadFixedFieldBytes(request.Message2, 16, ReaderFieldPad.RightSpace));
            buffer.AddRange(ReaderFieldEncoding.PadFixedFieldBytes(request.Message3, 16, ReaderFieldPad.RightSpace));
            buffer.AddRange(ReaderFieldEncoding.PadFixedFieldBytes(request.Message4, 16, ReaderFieldPad.RightSpace));

            // 13. payOn Key정보 X(32) — RF카드종류='C'일 때만 값, 그 외 Space. 이 프로젝트는 payOn
            // 카드 종류를 아직 다루지 않으므로(PRD 범위 밖) 항상 Space로 채운다.
            buffer.AddRange(ReaderFieldEncoding.PadFixedFieldBytes(string.Empty, 32, ReaderFieldPad.RightSpace));

            return buffer.ToArray();
        }

        /// <summary>PRD §4.3 IC 정상 요청 — 거래구분 "ARQo". 거래 일시/금액은 POS 전문 미확정
        /// (PRD §10)이라 호출자가 임시 값을 넘긴다 — TransactionInfoRequest 각 필드 주석의 TODO
        /// 참고.</summary>
        internal static TransactionInfoRequest CreateIcRequest(
            string transactionDateTime, string amount, string aidIndex,
            string message1, string message2, string message3, string message4,
            string pinBlockInputRequired = "0") =>
            new TransactionInfoRequest(transactionDateTime, amount, aidIndex, TransactionTypeIc,
                pinBlockInputRequired, message1, message2, message3, message4);

        /// <summary>PRD §4.4 FALLBACK 재요청 — 거래구분 "F". 채택된 그 리더기에만 재요청한다
        /// (Services 계층 책임, 이 빌더는 Data만 만든다).</summary>
        internal static TransactionInfoRequest CreateFallbackRequest(
            string transactionDateTime, string amount, string aidIndex,
            string message1, string message2, string message3, string message4,
            string pinBlockInputRequired = "0") =>
            new TransactionInfoRequest(transactionDateTime, amount, aidIndex, TransactionTypeFallback,
                pinBlockInputRequired, message1, message2, message3, message4);
    }
}
