namespace KFTCOneCAP.Wpf.Protocol.Reader
{
    /// <summary>
    /// 0x2B(거래정보 요청) Data 필드 값의 모음. Builder(TransactionInfoRequestBuilder)가 이 값들을
    /// SPEC 순서·길이·패딩대로 조립한다. POS 소켓 전문이 아직 미확정(PRD §10)이라 거래
    /// 일시/금액처럼 원래 POS에서 와야 하는 값은 호출자가 임시 값을 채워 넣는다 —
    /// TransactionInfoRequestBuilder의 각 상수 옆에 교체 지점을 주석으로 남겨 둔다.
    /// </summary>
    internal sealed class TransactionInfoRequest
    {
        /// <summary>거래 일시 X(14), "YYYYMMDDHHmmSS" 형식. TODO(Phase 15/POS 전문 확정): POS 요청의
        /// 거래 시각으로 교체.</summary>
        internal string TransactionDateTime { get; }

        /// <summary>거래 금액 X(18), 왼쪽 '0' 패딩. TODO(Phase 15/POS 전문 확정): POS 요청의 결제
        /// 금액으로 교체.</summary>
        internal string Amount { get; }

        /// <summary>AID 인덱스 X(1). PRD/샘플 모두 특정 값 요구가 없어 "0"(미사용)을 기본으로 둔다.</summary>
        internal string AidIndex { get; }

        /// <summary>
        /// 거래구분(길이 2 + 가변). PRD §4.3 IC 정상 요청은 "ARQo", §4.4 FALLBACK 재요청은 "F"
        /// (development_plan.md P10-2). 이 값 자체가 payload이며, 길이 프리픽스는 Builder가 CP949
        /// byte 길이로부터 자동 계산한다.
        /// </summary>
        internal string TransactionTypeCode { get; }

        /// <summary>PIN 블록 입력 여부 X(1), '0'/'1'. PRD에 PIN 입력 정책이 아직 없어 '0'(미입력)을
        /// 기본으로 둔다 — TODO(§10 확정 시 재검토).</summary>
        internal string PinBlockInputRequired { get; }

        /// <summary>리더기 화면 표시 문구 1~4(X(16) 각). PRD가 아직 문구 내용을 정하지 않아
        /// vendor/ReaderSerial/CSharpSample/CommandFieldSpecs.cs의 예시 문구를 임시로 쓴다
        /// (PRD §4.3 "나머지 요청 필드는 리더기 샘플 소스를 참고한다"). 완성형 그대로 넘긴다 —
        /// 조합형 변환은 DLL(JohabConverter)이 담당하므로 여기서 미리 변환하면 이중 변환으로
        /// 깨진다(development_plan.md P10-2).</summary>
        internal string Message1 { get; }
        internal string Message2 { get; }
        internal string Message3 { get; }
        internal string Message4 { get; }

        internal TransactionInfoRequest(
            string transactionDateTime,
            string amount,
            string aidIndex,
            string transactionTypeCode,
            string pinBlockInputRequired,
            string message1,
            string message2,
            string message3,
            string message4)
        {
            TransactionDateTime = transactionDateTime;
            Amount = amount;
            AidIndex = aidIndex;
            TransactionTypeCode = transactionTypeCode;
            PinBlockInputRequired = pinBlockInputRequired;
            Message1 = message1;
            Message2 = message2;
            Message3 = message3;
            Message4 = message4;
        }
    }
}
