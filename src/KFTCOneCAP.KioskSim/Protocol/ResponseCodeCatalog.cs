using System;
using System.Collections.Generic;

namespace KFTCOneCAP.KioskSim.Protocol
{
    /// <summary>
    /// 응답 전문 <c>#7 응답 코드</c>(3자리 문자열)를 사람이 읽는 설명으로 풀어주는 표.
    ///
    /// Phase 19 실행계획서(docs/payment_relay/development_plan.md) P19-6: 여기 적힌 문자열은
    /// 본 앱(KFTCOneCAP.Wpf) <c>Services/Payment/PosResultCodeMapper.cs</c>와
    /// <c>docs/reader_dll/API명세서.md</c> §9(리더기 업무 응답코드 00~23 표)를 **참고용으로 읽고
    /// 값만 옮겨 적은 것**이다 — P19-2와 같은 원칙대로 그 파일들을 참조/의존하지 않는다(코드 공유 0).
    /// 두 파일이 나중에 바뀌어도 이 표는 자동으로 따라가지 않으므로, 실제 코드 체계가 바뀌면 이
    /// 표도 사람이 다시 옮겨 적어야 한다.
    /// </summary>
    public static class ResponseCodeCatalog
    {
        /// <summary>정확히 일치하는 코드에 대한 설명(<c>000</c>/<c>E</c>계열/<c>D</c>계열).</summary>
        private static readonly Dictionary<string, string> Exact = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["000"] = "정상",
            ["E01"] = "사용자 취소",
            ["E02"] = "Timeout",
            ["E03"] = "설정 화면 사용 중",
            ["E04"] = "리더기 미설정",
            ["E05"] = "무결성 실패",
            ["E40"] = "길이 불일치",
            ["E41"] = "알 수 없는 거래구분",
            ["E99"] = "내부 오류",
            ["D01"] = "VAN DLL 로드 실패",
            ["D02"] = "VAN 통신 실패",
        };

        /// <summary>
        /// 리더기 SPEC 공통 "업무 응답 코드"(00~23, ASCII 2자리) — 원캡이 이 값을 그대로
        /// "R"+2자리로 옮겨 <c>R0x</c> 계열 코드를 만든다(<c>PosResultCodeMapper.
        /// FormatReaderBusinessFailureCode</c>). 출처: <c>docs/reader_dll/API명세서.md</c> §9 표.
        /// </summary>
        private static readonly Dictionary<string, string> ReaderBusinessCode = new Dictionary<string, string>
        {
            ["00"] = "리더기 상태 정상",
            ["01"] = "리더기 무결성 오류(무결성 체크 오류)",
            ["02"] = "Reader Error(IC카드를 넣어주세요) — 카드 리딩 도중 제거",
            ["03"] = "사용자 취소(단말기/멀티패드 종료 버튼)",
            ["04"] = "거래요청 Timeout",
            ["05"] = "금액 요청 IC",
            ["06"] = "IC 카드 거래 불가(카드매체 불량)",
            ["07"] = "FallBack(MS가능한 거래)",
            ["08"] = "IC 카드 삽입되어있음(카드제거 요청)",
            ["09"] = "상황에 맞지 않는 명령(2차검증 대기 중 부적절 요청 등)",
            ["10"] = "상호인증오류(Key 상호인증 시)",
            ["11"] = "암호화/복호화오류(Key 다운로드 시)",
            ["12"] = "MS거래 불가! IC카드로 진행(IC카드를 MS로 Swipe)",
            ["13"] = "리더기 KEY 다운로드 요망",
            ["14"] = "MS카드를 넣어주세요(MS전용카드 시)",
            ["15"] = "RF카드 리딩 에러",
            ["16"] = "비정상 RF카드 접촉",
            ["17"] = "음성/동영상 파일 번호 없음",
            ["18"] = "현금IC 카드 복수 계좌 거래 불가",
            ["19"] = "사용자 확인(입력 버튼)",
            ["20"] = "2차 검증 데이터 오류(EMV 데이터 오류)",
            ["21"] = "정의되지 않은 전문 코드",
            ["22"] = "지원되지 않는 전문 코드(정의는 되어 있으나 해당 리더기 미지원)",
            ["23"] = "필드값 오류",
        };

        /// <summary>
        /// 리더기 DLL(ReaderSerial.dll) 연동 레벨 실패(<c>R2x</c>). 출처:
        /// <c>Services/Payment/PosResultCodeMapper.cs</c>의 <c>DllCallFailure</c> 분기(값만 옮김).
        /// </summary>
        private static readonly Dictionary<string, string> ReaderDllFailureCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["R20"] = "READER_ERR_PORT_NOT_OPEN(포트가 열려 있지 않은 상태에서 명령을 시도함)",
            ["R21"] = "READER_ERR_SEND_FAIL(명령 송신 자체가 실패함)",
            ["R22"] = "READER_ERR_BUSY(리더기가 이미 다른 명령을 처리 중)",
            ["R23"] = "READER_ERR_PORT_NOT_FOUND(지정한 COM 포트를 찾을 수 없음)",
            ["R24"] = "READER_ERR_PORT_OPEN_FAIL(포트 오픈 실패)",
            ["R25"] = "READER_ERR_COMMAND_NOT_ALLOWED(허용되지 않는 명령)",
            ["R27"] = "CommunicationError(응답 수신 중 통신 오류)",
            ["R28"] = "그 외 DLL 연동 오류 catch-all(PORT_CONFIG_FAIL/PORT_CLOSING/PORT_ALREADY_OPEN/" +
                      "INVALID_LENGTH/BUFFER_OVERFLOW/INTERNAL/INVALID_ARGUMENT/MAX_READER_COUNT/" +
                      "INVALID_READER_ID/PINPAD_NOT_SUPPORTED 등 — 결제 흐름 중 실관찰 없음)",
            ["R29"] = "리더기 응답을 특정할 수 없는 방어적 실패(참여 리더기 전원 송신 실패 / 성공인데 " +
                      "카드데이터가 빈 방어 경로 / 07·12 재시도 상한 초과 중 하나 — 원인 구분은 로그가 담당)",
        };

        /// <summary>
        /// 응답 코드 문자열을 사람이 읽는 설명으로 바꾼다. 모르는 코드는 절대 추측해서 채우지
        /// 않고 "정의되지 않은 코드"라고 정직하게 알린다(development_plan.md P19-6 요구사항).
        /// </summary>
        public static string Describe(string? code)
        {
            if (code == null)
                return "정의되지 않은 코드(값 없음)";

            string trimmed = code.Trim();
            if (trimmed.Length == 0)
                return "정의되지 않은 코드(빈 값)";

            if (Exact.TryGetValue(trimmed, out var exact))
                return exact;

            // R 계열: "R"+2자리. 리더기 업무 응답코드(R0x, 00~23 전체를 그대로 옮김)와
            // 리더기 DLL 연동 실패(R2x, 20~29 중 일부)가 **같은 R20~R23 문자열을 공유한다** —
            // 실제 원인은 CardReadCommandOutcome.Kind로 갈라지지만(PosResultCodeMapper.cs),
            // 전문에 실리는 3자리 코드만 보고는 둘을 구분할 방법이 없다. 그래서 겹치는 구간은
            // 두 가능성을 모두 보여준다(추측으로 하나만 골라 적지 않는다).
            if (trimmed.Length == 3 && (trimmed[0] == 'R' || trimmed[0] == 'r'))
            {
                string digits = trimmed.Substring(1);
                bool hasBusiness = ReaderBusinessCode.TryGetValue(digits, out var businessDesc);
                bool hasDllFailure = ReaderDllFailureCode.TryGetValue("R" + digits, out var dllDesc);

                if (hasBusiness && hasDllFailure)
                {
                    return $"[리더기 업무 응답코드 실패(R0x)일 경우] {businessDesc} / " +
                           $"[리더기 DLL 연동 실패(R2x)일 경우] {dllDesc} " +
                           "— 코드 문자열만으로는 두 체계 중 어느 쪽인지 구분되지 않는다(로그 대조 필요).";
                }
                if (hasBusiness)
                    return $"리더기 업무 응답코드 실패(R0x): {businessDesc}";
                if (hasDllFailure)
                    return $"리더기 DLL 연동 실패(R2x): {dllDesc}";
            }

            return "정의되지 않은 코드";
        }
    }
}
