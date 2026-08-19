namespace KFTCOneCAP.Wpf.Protocol.Reader
{
    /// <summary>
    /// 리더기 전문 구분 코드(SPEC 명령/응답 코드, PRD §6.1). Phase 9(파일럿)에서는 초기화
    /// 요청/응답 1쌍만 정의한다 — 나머지 명령(0x61/0x62/0x2B 등)은 Phase 10에서 이 파일에 이어
    /// 추가한다. vendor/ReaderSerial/CSharpSample/CommandCodes.cs의 INIT_REQUEST(0x60)와 동일한 값.
    /// </summary>
    internal static class ReaderCommandCodes
    {
        internal const byte INIT_REQUEST = 0x60;
        internal const byte INIT_RESPONSE = 0x70;
    }
}
