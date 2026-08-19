using System.Text;

namespace KFTCOneCAP.Wpf.Protocol.Reader
{
    /// <summary>
    /// 상태체크 응답(0x71) 파싱 결과. 필드 위치/길이/인코딩 출처(추측 금지 원칙에 따라
    /// reader-pinpad-spec-expert에 위임해 확인, 2026-08-19):
    /// `암호화리더기설계서_20250122.pdf` §3.2 "리더기 상태 확인"(footer p.12 / PDF p.17)의
    /// `[71] 리더기 상태 확인 응답` 테이블 —
    ///   응답코드(X(2), ASCII) → 리더기 인증 식별 번호(X(16), H/W모델명12+F/W버전4, ASCII)
    ///   → 모듈 ID(X(10), 모델코드3+IPEK버전1+Y1+M1+"####"4, ASCII).
    ///
    /// **[71] 전용 예외 규정**(같은 문서 §2.1 "공통 사항", footer p.10 / PDF p.15): 대부분의 응답
    /// 전문은 응답코드가 "00"이 아니면 응답코드 2byte만 오지만, [71]은 명시적 예외로 응답코드가
    /// 무엇이든(00/08/그 외) 리더기 인증 식별 번호·모듈 ID 필드가 항상 함께 온다. 따라서 이
    /// 파서는 응답코드 값과 무관하게 항상 28byte(2+16+10)를 요구한다 — 0x72/0x70처럼 "실패 시
    /// 2byte만"이라는 일반 규칙을 여기 적용하면 안 된다.
    /// </summary>
    internal readonly struct StatusResponseResult
    {
        internal bool ParseFailed { get; }
        internal string ResponseCode { get; }
        internal string ReaderAuthId { get; }
        internal string ModuleId { get; }

        /// <summary>
        /// PRD §6.2: 응답코드 "00" 또는 "08"이면 성공으로 처리한다. SPEC 원문(§2.2 응답 코드 표)에서
        /// "08"은 "IC 카드 삽입되어있음"을 뜻할 뿐 SPEC이 "성공"이라 규정한 값은 아니다 — 00/08을
        /// 함께 성공 취급하는 것은 SPEC이 아니라 이 프로젝트(PRD)의 업무 판단이다.
        /// </summary>
        internal bool IsSuccess => !ParseFailed && (ResponseCode == "00" || ResponseCode == "08");

        private StatusResponseResult(bool parseFailed, string responseCode, string readerAuthId, string moduleId)
        {
            ParseFailed = parseFailed;
            ResponseCode = responseCode;
            ReaderAuthId = readerAuthId;
            ModuleId = moduleId;
        }

        internal static StatusResponseResult Failed() => new StatusResponseResult(true, string.Empty, string.Empty, string.Empty);

        internal static StatusResponseResult Of(string responseCode, string readerAuthId, string moduleId) =>
            new StatusResponseResult(false, responseCode, readerAuthId, moduleId);
    }

    /// <summary>0x71(상태체크 응답) 전문 파서. 파싱 실패(길이 부족)는 예외가 아니라
    /// StatusResponseResult.ParseFailed로 표현한다(Phase 10 P10-1 원칙, Protocol/Reader/InitResponseParser
    /// 와 동일한 관례).</summary>
    internal static class StatusResponseParser
    {
        private const int ResponseCodeLength = 2;
        private const int ReaderAuthIdLength = 16;
        private const int ModuleIdLength = 10;
        private const int TotalLength = ResponseCodeLength + ReaderAuthIdLength + ModuleIdLength;

        internal static StatusResponseResult Parse(byte[] data)
        {
            if (data == null || data.Length < TotalLength)
                return StatusResponseResult.Failed();

            string code = Encoding.ASCII.GetString(data, 0, ResponseCodeLength);
            string readerAuthId = Encoding.ASCII.GetString(data, ResponseCodeLength, ReaderAuthIdLength);
            string moduleId = Encoding.ASCII.GetString(data, ResponseCodeLength + ReaderAuthIdLength, ModuleIdLength);
            return StatusResponseResult.Of(code, readerAuthId, moduleId);
        }
    }
}
