namespace KFTCOneCAP.Wpf.Protocol.Reader
{
    /// <summary>
    /// 리더기 전문 구분 코드(SPEC 명령/응답 코드, PRD §2.2/§6.1). Phase 9(파일럿)에서는 초기화
    /// 요청/응답 1쌍만 정의했고, Phase 10(P10-1/P10-2)에서 이 프로젝트가 실제로 쓰는 나머지 3종
    /// (상태체크/무결성체크/카드 리딩)을 추가했다. 값은 전부
    /// vendor/ReaderSerial/CSharpSample/CommandCodes.cs와 동일하다(SPEC과 이미 대조 확인된 값).
    /// </summary>
    internal static class ReaderCommandCodes
    {
        internal const byte INIT_REQUEST = 0x60;
        internal const byte INIT_RESPONSE = 0x70;

        internal const byte STATUS_REQUEST = 0x61;
        internal const byte STATUS_RESPONSE = 0x71;

        internal const byte INTEGRITY_CHECK_REQUEST = 0x62;
        internal const byte INTEGRITY_CHECK_RESPONSE = 0x72;

        internal const byte TRANSACTION_INFO_REQUEST = 0x2B;
        internal const byte CARD_READ_RESPONSE = 0x3B;

        // Phase 24(P24-2) — 리더기 키다운로드 3종(PRD §3.4). 값 출처는 development_plan.md
        // "P24-2. 리더기 전문 3종 + ReaderService 명령 3종" 지시(0x63/0x73, 0x64/0x74, 0x65/0x75).
        internal const byte KEY_DOWNLOAD_START_REQUEST = 0x63;
        internal const byte KEY_DOWNLOAD_START_RESPONSE = 0x73;

        internal const byte KEY_DOWNLOAD_AUTH_REQUEST = 0x64;
        internal const byte KEY_DOWNLOAD_AUTH_RESPONSE = 0x74;

        internal const byte KEY_DOWNLOAD_USING_KEY_REQUEST = 0x65;
        internal const byte KEY_DOWNLOAD_USING_KEY_RESPONSE = 0x75;
    }
}
