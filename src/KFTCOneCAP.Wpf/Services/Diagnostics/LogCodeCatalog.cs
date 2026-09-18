using System;
using System.Collections.Generic;

namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// 2026-09-17 사용자 요청 — 로그 줄의 코드 슬롯(<c>PosResultCodeMapper</c>가 만드는 <c>E0x</c>/<c>R0x</c>/
/// <c>R2x</c>/<c>D0x</c>와 <see cref="InternalFaultCodes"/>의 <c>S</c>계열)만 보고는 어떤 오류인지 코드
/// 체계표를 직접 열어봐야 알 수 있었다 — 로그만 보고 바로 파악되도록 <see cref="FileLogSink.Write"/>가
/// 코드가 있는 레코드마다 이 표를 찾아 <b>다음 줄에 별도로</b> 설명을 찍는다(호출부 151곳을 개별로
/// 고치지 않는다). 처음엔 메시지 끝/코드 슬롯 안에 붙이는 안을 시도했으나 각각 "원문 덤프와 헷갈림",
/// "코드 슬롯 세로 정렬 계약 위반" 문제가 있어 별도 줄로 정착했다(<see cref="FileLogSink.BuildCodeNoteBytes"/>
/// 참고).
///
/// <b>KioskSim의 <c>ResponseCodeCatalog</c>와 코드 공유는 하지 않는다</b>(P19-2 원칙 — 그 표는 POS
/// 시뮬레이터가 "응답으로 받은" 코드를 사람이 읽게 풀어주는 반대편 표이고, 이 표는 원캡 자신의 로그용이다.
/// 두 값 체계는 지금 동일하지만 각자 옮겨 적은 것이라 나중에 한쪽만 바뀌면 사람이 손으로 맞춰야 한다).
/// 모르는 코드는 추측해서 채우지 않고 <see langword="null"/>을 돌려준다(호출부가 원본 메시지만 그대로 둔다).
/// </summary>
internal static class LogCodeCatalog
{
    private static readonly Dictionary<string, string> Descriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        // PosPaymentResultCode 계열(PosResultCodeMapper.ToTelegramCode(PosPaymentResultCode)).
        ["E01"] = "사용자 취소",
        ["E02"] = "Timeout",
        ["E03"] = "설정 화면 사용 중",
        ["E04"] = "리더기 미설정",
        ["E05"] = "무결성 실패",
        ["E06"] = "Kiosk ID 불일치",
        ["E07"] = "거래 상태 조회 — 일치하는 원거래 없음",
        ["E99"] = "내부 오류",

        // 요청 전문 프레이밍 오류(PosRequestTelegram.Parse/PosMessageFramer, fault_alert_catalog.md §2.3).
        ["E40"] = "길이 불일치",
        ["E41"] = "알 수 없는 거래구분",
        ["E42"] = "전문 식별 불가(본문 16바이트 미만이라 #4를 읽지 못함)",
        ["E43"] = "전문 형식 오류(길이 필드 파손으로 프레이밍 자체가 깨짐)",

        // VAN DLL(KFTC_GIRO.dll) 연동 실패(PosResultCodeMapper.ToTelegramCode(VanFailureKind)).
        ["D01"] = "VAN DLL 로드 실패",
        ["D02"] = "VAN 통신 실패",

        // 서버 응답 코드(SPEC p.21) — 원캡은 판단하지 않고 그대로 relay하는 값이라 PosResultCodeMapper가
        // 만들지 않는다(§4.10/§4.11). SPEC 20260915 개정판에서 신설(P29-3, fault_alert_catalog.md §2.1).
        // 로그에서 뜻을 바로 읽기 위한 설명일 뿐, 이 추가가 relay 분기나 FaultAlertJudge 판정 대상 여부를
        // 바꾸지 않는다(둘 다 그대로 둔다는 것이 2026-09-18 확정 사항).
        ["031"] = "전문 전송 일자 오류(서버 판정, 원캡은 relay만 함)",

        // 리더기 DLL(ReaderSerial.dll) 연동 레벨 실패(PosResultCodeMapper — DllCallFailure/CommunicationError).
        ["R24"] = "READER_ERR_PORT_NOT_OPEN(포트가 열려 있지 않은 상태에서 명령 시도)",
        ["R25"] = "READER_ERR_SEND_FAIL(명령 송신 자체가 실패함)",
        ["R26"] = "READER_ERR_BUSY(리더기가 이미 다른 명령을 처리 중)",
        ["R27"] = "READER_ERR_PORT_NOT_FOUND(지정한 COM 포트를 찾을 수 없음)",
        ["R28"] = "READER_ERR_PORT_OPEN_FAIL(포트 오픈 실패)",
        ["R29"] = "READER_ERR_COMMAND_NOT_ALLOWED(허용되지 않는 명령)",
        ["R30"] = "CommunicationError(응답 수신 중 통신 오류)",
        ["R31"] = "그 외 DLL 연동 오류 catch-all(결제 흐름 중 실관찰 없음)",
        ["R32"] = "리더기 응답을 특정할 수 없는 방어적 실패(전원 송신 실패/카드데이터 없는 방어 경로/재시도 상한 초과 중 하나 — 원인은 메시지 본문 참고)",

        // 전문이 나가지 않는 내부 사건(InternalFaultCodes, fault_alert_catalog.md §2.2).
        ["S01"] = "POS 소켓 리스닝 실패",
        ["S02"] = "수락 루프 사망(이후 새 연결 영구 불가)",
        ["S03"] = "무결성 이력 저장·조회 실패",
        ["S04"] = "로그 보관 정리 실패(삭제 실패)",
        ["S05"] = "전역 키보드 훅 설치 실패",
        ["S06"] = "응답 전달 실패(완료 콜백 처리 중 예외 — POS가 결과 코드를 받지 못함)",
        ["S07"] = "응답 송신 실패(완성된 프레임 폐기)",
        ["S08"] = "응답 직렬화 실패(송신 시도조차 못 함)",
        ["S09"] = "동시 연결 상한 초과 거부",
        ["S10"] = "연결 처리 중 예외",
        ["S11"] = "DLL 로드 스모크 실패(기동 시점 사전 점검)",
    };

    /// <summary>
    /// R0x(리더기 업무 응답코드, SPEC 00~23)는 값 공간이 커 위 <see cref="Descriptions"/> 표에 개별로
    /// 두지 않고 별도 사전으로 분리한다 — 리더기 SPEC 문서(<c>docs/reader_dll/API명세서.md</c> §9)를
    /// 옮겨 적은 값(<c>PosResultCodeMapper.FormatReaderBusinessFailureCode</c>가 이 값을 그대로
    /// "R"+2자리로 옮긴다).
    /// </summary>
    private static readonly Dictionary<string, string> ReaderBusinessCodeDescriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["00"] = "리더기 상태 정상",
        ["01"] = "리더기 무결성 오류",
        ["02"] = "Reader Error(카드 리딩 도중 제거)",
        ["03"] = "사용자 취소(단말기/멀티패드 종료 버튼)",
        ["04"] = "거래요청 Timeout",
        ["05"] = "금액 요청 IC",
        ["06"] = "IC 카드 거래 불가(카드매체 불량)",
        ["07"] = "FallBack(MS가능한 거래)",
        ["08"] = "IC 카드 삽입되어있음(카드제거 요청)",
        ["09"] = "상황에 맞지 않는 명령",
        ["10"] = "상호인증오류(Key 상호인증 시)",
        ["11"] = "암호화/복호화오류(Key 다운로드 시)",
        ["12"] = "MS거래 불가! IC카드로 진행",
        ["13"] = "리더기 KEY 다운로드 요망",
        ["14"] = "MS카드를 넣어주세요(MS전용카드 시)",
        ["15"] = "RF카드 리딩 에러",
        ["16"] = "비정상 RF카드 접촉",
        ["17"] = "음성/동영상 파일 번호 없음",
        ["18"] = "현금IC 카드 복수 계좌 거래 불가",
        ["19"] = "사용자 확인(입력 버튼)",
        ["20"] = "2차 검증 데이터 오류(EMV 데이터 오류)",
        ["21"] = "정의되지 않은 전문 코드",
        ["22"] = "지원되지 않는 전문 코드(해당 리더기 미지원)",
        ["23"] = "필드값 오류",
    };

    /// <summary>
    /// <paramref name="code"/>에 대한 설명을 찾는다. 모르는 코드(장래 신설 코드가 아직 이 표에 반영되지
    /// 않은 경우 등)면 <see langword="null"/> — 호출부는 그냥 설명 없이 원본 메시지만 쓴다(추측 금지).
    /// </summary>
    internal static string? Describe(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return null;
        }

        string trimmed = code!.Trim();

        if (Descriptions.TryGetValue(trimmed, out string? exact))
        {
            return exact;
        }

        // R0x: "R"+2자리, 24~32는 위 Descriptions에서 이미 처리됐으므로 여기 도달한다는 것은
        // 00~23(리더기 업무 응답코드) 범위라는 뜻이다.
        if (trimmed.Length == 3 && (trimmed[0] == 'R' || trimmed[0] == 'r')
            && ReaderBusinessCodeDescriptions.TryGetValue(trimmed.Substring(1), out string? businessDesc))
        {
            return businessDesc;
        }

        return null;
    }
}
