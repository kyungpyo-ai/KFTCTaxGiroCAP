namespace KFTCOneCAP.Wpf.Protocol.Reader
{
    /// <summary>
    /// 무결성 체크 응답(0x72) 파싱 결과. "00"이면 성공(PRD §4.2/§6.4), 그 외 SPEC 업무 응답
    /// 코드면 실패. 0x72는 [71]과 달리 SPEC 예외 규정이 없는 일반 응답이므로(§2.1 공통 사항 —
    /// "00" 아니면 응답코드 2byte만 송신), 응답 Data에는 항상 응답코드 2byte만 온다고 본다.
    /// ParseFailed는 데이터가 SPEC 형식(2byte ASCII)에 못 미칠 때만 true다 — 하드웨어 데이터는
    /// 언제든 깨질 수 있으므로 예외 대신 결과 값으로 표현한다(Protocol/Reader/InitResponseParser와
    /// 동일한 관례, Phase 10 P10-1 원칙).
    /// </summary>
    internal readonly struct IntegrityResponseResult
    {
        internal bool ParseFailed { get; }
        internal string ResponseCode { get; }
        internal bool IsSuccess => !ParseFailed && ResponseCode == "00";

        private IntegrityResponseResult(bool parseFailed, string responseCode)
        {
            ParseFailed = parseFailed;
            ResponseCode = responseCode;
        }

        internal static IntegrityResponseResult Failed() => new IntegrityResponseResult(true, string.Empty);

        internal static IntegrityResponseResult Of(string responseCode) => new IntegrityResponseResult(false, responseCode);
    }

    /// <summary>0x72(무결성 체크 응답) 전문 파서. 첫 2byte(ASCII)가 SPEC 업무 응답 코드
    /// (docs/reader_dll/DLL연동가이드.md §3).</summary>
    internal static class IntegrityResponseParser
    {
        internal static IntegrityResponseResult Parse(byte[] data)
        {
            if (data == null || data.Length < 2)
                return IntegrityResponseResult.Failed();

            string code = System.Text.Encoding.ASCII.GetString(data, 0, 2);
            return IntegrityResponseResult.Of(code);
        }
    }
}
