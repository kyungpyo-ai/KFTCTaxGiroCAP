namespace KFTCOneCAP.Wpf.Protocol.Reader
{
    /// <summary>
    /// 초기화 응답(0x70) 파싱 결과. "00"이면 성공, 그 외 SPEC 업무 응답 코드면 실패.
    /// ParseFailed는 데이터 자체가 SPEC 형식(첫 2byte ASCII)에 못 미칠 때만 true다 — 하드웨어에서
    /// 오는 데이터는 언제든 깨질 수 있으므로 예외 대신 결과 값으로 표현한다(ROADMAP Phase 10
    /// P10-1 원칙을 Phase 9 파일럿 범위에서도 동일하게 따른다).
    /// </summary>
    internal readonly struct InitResponseResult
    {
        internal bool ParseFailed { get; }
        internal string ResponseCode { get; }
        internal bool IsSuccess => !ParseFailed && ResponseCode == "00";

        private InitResponseResult(bool parseFailed, string responseCode)
        {
            ParseFailed = parseFailed;
            ResponseCode = responseCode;
        }

        internal static InitResponseResult Failed() => new InitResponseResult(true, string.Empty);

        internal static InitResponseResult Of(string responseCode) => new InitResponseResult(false, responseCode);
    }

    /// <summary>
    /// 0x70(초기화 응답) 전문 파서. SPEC 업무 응답 코드(00~23)는 모든 응답의 첫 2byte(ASCII)에
    /// 실려 온다(docs/reader_dll/DLL연동가이드.md §3) — DLL 오류코드(ReaderResult, 음수 int)와는
    /// 완전히 다른 체계이므로 이 계층에서만 다루고 Services로 그대로 넘기지 않는다.
    /// </summary>
    internal static class InitResponseParser
    {
        internal static InitResponseResult Parse(byte[] data)
        {
            if (data == null || data.Length < 2)
                return InitResponseResult.Failed();

            // SPEC 업무 응답 코드는 ASCII 2문자("00"~"23") — System.Text.Encoding.ASCII로 해석한다.
            string code = System.Text.Encoding.ASCII.GetString(data, 0, 2);
            return InitResponseResult.Of(code);
        }
    }
}
