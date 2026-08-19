using KFTCOneCAP.Wpf.Protocol.Reader;

namespace KFTCOneCAP.Wpf.Services.Reader
{
    /// <summary>SendCardReadCommandAsync(0x2B->0x3B)의 결과. PRD §4.3~§4.6의 응답코드 3갈래
    /// (00/07/12/그 외)를 여기서 그대로 노출한다 — 재요청/무효화 판단(Phase 15 결제 Flow 몫)은
    /// 이 값을 보고 호출자가 수행한다. CardData는 Success일 때만 채워진다(Protocol/Reader/
    /// CardReadResponseParser 참고 — VAN 매핑은 Phase 17로 보류, 여기서는 구조화해서 보관만 함).</summary>
    internal sealed class CardReadCommandOutcome
    {
        internal ReaderCommandOutcomeKind Kind { get; }
        internal string ResponseCode { get; }
        internal CardReadData? CardData { get; }
        internal int DllResult { get; }
        internal string DllResultName { get; }
        internal string Detail { get; }

        internal ReaderFailureCategory FailureCategory => Kind.ToFailureCategory();

        /// <summary>PRD §4.4 FALLBACK 처리 대상 — 응답코드 "07".</summary>
        internal bool IsFallback => Kind == ReaderCommandOutcomeKind.BusinessFailure && ResponseCode == "07";

        /// <summary>PRD §4.5 응답코드 12 재요청 대상.</summary>
        internal bool IsRetryCode12 => Kind == ReaderCommandOutcomeKind.BusinessFailure && ResponseCode == "12";

        private CardReadCommandOutcome(ReaderCommandOutcomeKind kind, string responseCode, CardReadData? cardData,
            int dllResult, string dllResultName, string detail)
        {
            Kind = kind;
            ResponseCode = responseCode;
            CardData = cardData;
            DllResult = dllResult;
            DllResultName = dllResultName;
            Detail = detail;
        }

        internal static CardReadCommandOutcome Success(string responseCode, CardReadData? cardData) =>
            new CardReadCommandOutcome(ReaderCommandOutcomeKind.Success, responseCode, cardData, 0, string.Empty, string.Empty);

        internal static CardReadCommandOutcome BusinessFailure(string responseCode) =>
            new CardReadCommandOutcome(ReaderCommandOutcomeKind.BusinessFailure, responseCode, null, 0, string.Empty, string.Empty);

        internal static CardReadCommandOutcome DllCallFailure(int dllResult, string dllResultName, string detail) =>
            new CardReadCommandOutcome(ReaderCommandOutcomeKind.DllCallFailure, string.Empty, null, dllResult, dllResultName, detail);

        internal static CardReadCommandOutcome Timeout() =>
            new CardReadCommandOutcome(ReaderCommandOutcomeKind.Timeout, string.Empty, null, 0, string.Empty, "응답 대기 시간 초과");

        internal static CardReadCommandOutcome CommunicationError(string detail) =>
            new CardReadCommandOutcome(ReaderCommandOutcomeKind.CommunicationError, string.Empty, null, 0, string.Empty, detail);

        internal static CardReadCommandOutcome FromParsed(CardReadResponseResult parsed)
        {
            if (parsed.ParseFailed)
                return CommunicationError("0x3B 응답 데이터 길이 부족(2byte 미만)");

            return parsed.IsSuccess ? Success(parsed.ResponseCode, parsed.CardData) : BusinessFailure(parsed.ResponseCode);
        }
    }
}
