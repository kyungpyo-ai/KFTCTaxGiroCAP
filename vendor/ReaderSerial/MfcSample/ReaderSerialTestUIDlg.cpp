// ReaderSerialTestUIDlg.cpp — 메인 다이얼로그 구현 (P6-2)
#include <afxwin.h>
#include <afxdialogex.h>
#include <cstring>

#include "resource.h"
#include "ReaderSerialTestUIDlg.h"
#include "ReaderErrors.h"
#include "CommandCodes.h"
#include "CommandFieldSpecs.h"
#include "PinpadFieldSpecs.h"
#include "TimeoutPolicy.h"
#include "PinpadTypes.h"

namespace
{
    // 동적 생성 필드 컨트롤 ID 대역. 필드 최대 개수(0x2B 거래정보 = 13개)보다
    // 넉넉한 32개까지 예약한다. 라벨/EditText는 서로 다른 대역을 쓴다.
    //
    // (P10-1 버그수정, 2026-07-31) 두 슬롯(리더기 A/B)의 필드 패널은 서로 다른
    // 부모 윈도우(fieldPanelWnd)의 자식이므로 Win32 메시지 라우팅 자체는 같은
    // ID 대역을 슬롯마다 재사용해도 충돌하지 않는다 — 하지만 UI 자동화 도구
    // (FlaUI 등)는 대화상자 전체를 훑어 컨트롤을 "Win32 컨트롤 ID = AutomationId"
    // 기준으로 찾는 경우가 많고, 그 탐색은 부모 윈도우 경계를 고려하지 않는다.
    // 슬롯마다 kFieldLabelIdBase/kFieldEditIdBase를 그대로 재사용하면(0=A,
    // 1=B 둘 다 라벨 ID 2000, 2001... 에디트 ID 2100, 2101...을 씀), 대화상자
    // 전체를 대상으로 한 자동화 탐색에서 리더기A/B의 동일 행 필드 컨트롤이
    // 서로 다른 두 윈도우인데도 완전히 같은 AutomationId로 보인다(UI
    // Automation 스냅샷으로 실제 확인 — AutomationId="2000"이 리더기1/2 필드
    // 패널에 각각 하나씩, 총 두 번 나타남). 이 모호성 때문에 자동화 스크립트가
    // "리더기1의 필드 컨트롤"을 찾으려다 리더기2의 동일 ID 컨트롤을 대신 잡는
    // 오조작이 가능하다 — 슬롯별로 별도 ID 대역을 둬서 전체 대화상자 범위에서도
    // 유일하게 만든다.
    constexpr UINT kFieldLabelIdBase = 2000;
    constexpr UINT kFieldEditIdBase = 2100;
    constexpr int kMaxFieldRows = 20;
    // 슬롯 하나가 쓰는 ID 대역 폭. kMaxFieldRows(20)보다 넉넉히 커서 슬롯0의
    // 라벨/에디트 대역과 슬롯1의 대역이 절대 겹치지 않는다.
    constexpr UINT kFieldIdSlotStride = 1000;

    // 필드 패널 레이아웃(rc의 예약 영역과 맞춘 값, 다이얼로그 좌표계 기준 px
    // 아님 — CreateWindowEx는 px 좌표를 쓰므로 다이얼로그 열린 뒤 CDialogEx::
    // MapDialogRect로 dlu->px 변환해서 쓴다).
    //
    // P7-12: 필드 패널은 다이얼로그의 직접 자식이 아니라 고정 크기 뷰포트
    // 컨테이너(CommandPanel::fieldPanelWnd)의 자식으로 배치된다. 아래 Top/
    // Height는 그 뷰포트 자체의 다이얼로그 좌표(px 변환 전 dlu)이고, Label/
    // Edit의 X는 뷰포트 기준 상대 좌표다. RebuildFieldPanel은 이 상대
    // 오프셋을 (0,0)을 원점으로 하는 별도 dlu->px 변환으로 직접 구하며,
    // 뷰포트 자체의 절대 다이얼로그 좌표(fieldPanelRectPx)는 관여하지
    // 않는다 — 슬롯마다 뷰포트의 절대 위치가 다르기 때문이다.
    //
    // P10-1: 리더기 A/B 패널을 좌우 두 컬럼으로 나란히 배치한다(Left/Right는
    // 슬롯별 배열, Top/Height는 두 컬럼이 같은 높이이므로 공유). 컬럼 내부
    // 레이아웃(Label/Edit X/Width)도 폭이 좁아진 만큼 축소했지만 두 슬롯이
    // 동일한 폭을 쓰므로 공유값 그대로 둔다.
    constexpr int kFieldPanelLeftDlu[kReaderSlotCount] = { 7, 333 };
    constexpr int kFieldPanelRightDlu[kReaderSlotCount] = { 307, 633 };
    constexpr int kFieldScrollBarLeftDlu[kReaderSlotCount] = { 310, 636 };
    // (P10-1 버그수정) 안내 문구(IDC_STATIC_NOTICE) 줄바꿈을 위해 위쪽
    // 컨트롤 블록을 20dlu 내리면서, 뷰포트 상단도 146->166dlu로 20dlu 밀었다.
    // (2026-07-31, 작은 모니터 대응) 다이얼로그 총 높이(520->440dlu) 축소를
    // 위해 뷰포트 높이를 180->140dlu로 줄였다(하단 306dlu) — 필드는 이미
    // 스크롤바로 접근하는 구조라 기능적으로 문제없다. IDC_LIST_LOG(y=316)와의
    // 여백은 축소 전과 동일(10dlu)하게 유지했다(ReaderSerialTestUI.rc 참고).
    // (P17-1) 핀패드 명령 선택 행(166dlu)이 새로 끼어들면서 리더기 필드 패널
    // 뷰포트 상단을 166→190dlu로 24dlu 더 밀었다(ReaderSerialTestUI.rc 참고).
    constexpr int kFieldPanelTopDlu = 190;
    constexpr int kFieldPanelHeightDlu = 140;
    constexpr int kFieldScrollBarWidthDlu = 13;
    constexpr int kFieldRowGapDlu = 6;
    constexpr int kFieldLabelXDlu = 4;
    constexpr int kFieldLabelWidthDlu = 105;
    constexpr int kFieldEditXDlu = 113;
    constexpr int kFieldEditWidthDlu = 180;
    constexpr int kFieldControlHeightDlu = 14;

    // P17-1: 핀패드 필드 패널 뷰포트. 좌우 컬럼 위치(kFieldPanelLeftDlu/
    // RightDlu/kFieldScrollBarLeftDlu)는 리더기 필드 패널과 동일한 컬럼을
    // 그대로 재사용한다 — 다른 것은 세로 위치/높이뿐이다. 핀패드 명령의
    // 최대 필드 수는 6개(PIN_DES)로 리더기(최대 13개)보다 적어 더 낮은
    // 뷰포트로도 대체로 스크롤 없이 들어간다.
    constexpr int kPinpadFieldPanelTopDlu = 340;
    constexpr int kPinpadFieldPanelHeightDlu = 80;

    // 핀패드 동적 필드 컨트롤 ID 대역 — 리더기용(kFieldLabelIdBase=2000/
    // kFieldEditIdBase=2100)과 절대 겹치지 않도록 완전히 다른 대역을 쓴다
    // (위 kFieldLabelIdBase 주석의 "전체 대화상자 범위에서 유일해야 하는" 이유와 동일).
    constexpr UINT kPinpadFieldLabelIdBase = 4000;
    constexpr UINT kPinpadFieldEditIdBase = 4100;

    // ReaderResult(ReaderErrors.h) 값을 사람이 읽을 수 있는 이름으로 변환한다.
    // 테스트 UI 로그 표시 전용이며, DLL 공개/내부 헤더에는 영향을 주지 않는다.
    LPCTSTR ReaderResultToString(int result)
    {
        switch (result)
        {
        case READER_OK:                        return _T("READER_OK");
        case READER_ERR_INVALID_ARGUMENT:      return _T("READER_ERR_INVALID_ARGUMENT");
        case READER_ERR_MAX_READER_COUNT:      return _T("READER_ERR_MAX_READER_COUNT");
        case READER_ERR_INVALID_READER_ID:     return _T("READER_ERR_INVALID_READER_ID");
        case READER_ERR_BUSY:                  return _T("READER_ERR_BUSY");
        case READER_ERR_COMMAND_NOT_ALLOWED:   return _T("READER_ERR_COMMAND_NOT_ALLOWED");
        case READER_ERR_PORT_NOT_FOUND:        return _T("READER_ERR_PORT_NOT_FOUND");
        case READER_ERR_PORT_OPEN_FAIL:        return _T("READER_ERR_PORT_OPEN_FAIL");
        case READER_ERR_PORT_ALREADY_OPEN:     return _T("READER_ERR_PORT_ALREADY_OPEN");
        case READER_ERR_PORT_NOT_OPEN:         return _T("READER_ERR_PORT_NOT_OPEN");
        case READER_ERR_PORT_CONFIG_FAIL:      return _T("READER_ERR_PORT_CONFIG_FAIL");
        case READER_ERR_PORT_CLOSING:          return _T("READER_ERR_PORT_CLOSING");
        case READER_ERR_SEND_FAIL:             return _T("READER_ERR_SEND_FAIL");
        case READER_ERR_INVALID_LENGTH:        return _T("READER_ERR_INVALID_LENGTH");
        case READER_ERR_BUFFER_OVERFLOW:       return _T("READER_ERR_BUFFER_OVERFLOW");
        case READER_ERR_INTERNAL:              return _T("READER_ERR_INTERNAL");
        // P17-1: 핀패드 오류 코드(ReaderErrors.h) — Pinpad_SendCommand의 반환값이
        // 이 값을 쓴다. 2026-08-12부터 PINPAD_CALLBACK에는 더 이상 이 값이 실리지
        // 않는다(3번째 파라미터가 result에서 commandCode로 바뀜, ReaderSerial.h 참조).
        case READER_ERR_PINPAD_NOT_SUPPORTED:  return _T("READER_ERR_PINPAD_NOT_SUPPORTED");
        // 2026-08-12: READER_ERR_PINPAD_STEP_FAILED(-1401)/READER_ERR_PINPAD_TIMEOUT
        // (-1402)는 PINPAD_CALLBACK 재설계로 반환 경로가 사라져 ReaderErrors.h에서
        // 제거됐다 - 이 case들도 함께 삭제.
        // 2026-08-13: READER_ERR_RECEIVE_FAIL/READER_ERR_TIMEOUT/READER_ERR_FRAME_STALL/
        // READER_ERR_LRC_MISMATCH도 같은 이유(resultCode 제거 이후 반환 경로 없음)로
        // ReaderErrors.h에서 제거되어 이 case들도 함께 삭제.
        default:                                return _T("UNKNOWN");
        }
    }

    // PinpadEventType(ReaderSerial.h) 값을 사람이 읽을 수 있는 이름으로 변환한다.
    // 2026-08-12 PINPAD_CALLBACK 전면 재설계: 실패 원인이 PINPAD_EVENT_ERROR
    // 하나로 뭉쳐 failInfo(3byte) payload로 실리던 것을, 리더기 ReaderEventType과
    // 동일하게 원인마다 최상위 eventType으로 승격했다 - PinpadFailReason/failInfo
    // 개념은 완전히 제거되어 별도 디코딩 함수도 더 이상 필요 없다.
    LPCTSTR PinpadEventTypeToString(int eventType)
    {
        switch (eventType)
        {
        case PINPAD_EVENT_RESPONSE:        return _T("PINPAD_EVENT_RESPONSE");
        case PINPAD_EVENT_TIMEOUT:         return _T("PINPAD_EVENT_TIMEOUT");
        case PINPAD_EVENT_NAK:             return _T("PINPAD_EVENT_NAK");
        case PINPAD_EVENT_LRC_ERROR:       return _T("PINPAD_EVENT_LRC_ERROR");
        case PINPAD_EVENT_TAMPER:          return _T("PINPAD_EVENT_TAMPER");
        case PINPAD_EVENT_SEND_FAIL:       return _T("PINPAD_EVENT_SEND_FAIL");
        case PINPAD_EVENT_RECEIVE_ERROR:   return _T("PINPAD_EVENT_RECEIVE_ERROR");
        case PINPAD_EVENT_FRAME_STALL:     return _T("PINPAD_EVENT_FRAME_STALL");
        default:                           return _T("UNKNOWN");
        }
    }

    CString FormatPinpadEventType(int eventType)
    {
        CString text;
        text.Format(_T("%d (%s)"), eventType, PinpadEventTypeToString(eventType));
        return text;
    }

    // PinpadCommandCode(ReaderSerial.h) 값을 사람이 읽을 수 있는 이름으로 변환한다.
    // 2026-08-12: PINPAD_CALLBACK의 3번째 파라미터가 이 값으로 바뀌었다 - POS가
    // Pinpad_SendCommand에 넘긴 원래 명령 코드가 그대로 돌아온다.
    LPCTSTR PinpadCommandCodeToString(unsigned char commandCode)
    {
        switch (commandCode)
        {
        case PINPAD_CMD_INIT:          return _T("PINPAD_CMD_INIT");
        case PINPAD_CMD_PIN_PASSWORD:  return _T("PINPAD_CMD_PIN_PASSWORD");
        case PINPAD_CMD_PIN_NUMBER:    return _T("PINPAD_CMD_PIN_NUMBER");
        case PINPAD_CMD_PIN_DES:       return _T("PINPAD_CMD_PIN_DES");
        case PINPAD_CMD_PIN_SEED:      return _T("PINPAD_CMD_PIN_SEED");
        default:                       return _T("UNKNOWN");
        }
    }

    CString FormatPinpadCommandCode(unsigned char commandCode)
    {
        CString text;
        text.Format(_T("0x%02X (%s)"), commandCode, PinpadCommandCodeToString(commandCode));
        return text;
    }

    // ReaderEventType(ReaderSerial.h) 값을 사람이 읽을 수 있는 이름으로 변환한다.
    LPCTSTR ReaderEventTypeToString(int eventType)
    {
        switch (eventType)
        {
        case READER_EVENT_RESPONSE:        return _T("READER_EVENT_RESPONSE");
        case READER_EVENT_TIMEOUT:         return _T("READER_EVENT_TIMEOUT");
        case READER_EVENT_LRC_ERROR:       return _T("READER_EVENT_LRC_ERROR");
        case READER_EVENT_RECEIVE_ERROR:   return _T("READER_EVENT_RECEIVE_ERROR");
        case READER_EVENT_UNSOLICITED:     return _T("READER_EVENT_UNSOLICITED");
        case READER_EVENT_FRAME_STALL:     return _T("READER_EVENT_FRAME_STALL");
        default:                           return _T("UNKNOWN");
        }
    }

    CString FormatResult(int result)
    {
        CString text;
        text.Format(_T("%d (%s)"), result, ReaderResultToString(result));
        return text;
    }

    CString FormatEventType(int eventType)
    {
        CString text;
        text.Format(_T("%d (%s)"), eventType, ReaderEventTypeToString(eventType));
        return text;
    }

    // CString(유니코드)을 완성형(CP949) byte로 인코딩한다. DLL(ReaderSerial)이
    // msg 필드 조합형 변환 시 CP949 바이트를 입력으로 가정하므로(JohabConverter.h
    // 참조), 여기서 wchar_t 코드유닛을 그대로 1byte로 캐스팅하면(과거 버그) 한글이
    // 깨진 채로 전송된다 — 반드시 WideCharToMultiByte(949)로 인코딩해야 한다.
    int EncodeCp949(const CString& text, unsigned char* outBuf, int capacity)
    {
        if (text.IsEmpty() || capacity <= 0)
        {
            return 0;
        }
        const int written = WideCharToMultiByte(
            949, 0, text.GetString(), text.GetLength(),
            reinterpret_cast<LPSTR>(outBuf), capacity, nullptr, nullptr);
        return (written > 0) ? written : 0;
    }

    // payload를 CP949 byte로 인코딩한 뒤, 그 byte 길이를 prefixWidth자리
    // 숫자('0' 왼쪽 패딩)로 만들어 앞에 붙인다(길이 프리픽스는 SPEC상 byte
    // 길이를 뜻하므로 문자 개수가 아니라 인코딩된 byte 수를 써야 한다).
    // 표현 가능한 최댓값을 넘으면 payload byte를 자른다. 반환값은 실제
    // 기록한 총 byte 수(prefix+payload).
    int BuildLengthPrefixedFieldBytes(const CString& payload, int prefixWidth, unsigned char* outBytes, int capacity, int& outUsedPayloadBytes)
    {
        unsigned char payloadBytes[512];
        int payloadLen = EncodeCp949(payload, payloadBytes, sizeof(payloadBytes));

        int maxLen = 1;
        for (int i = 0; i < prefixWidth; ++i)
        {
            maxLen *= 10;
        }
        maxLen -= 1;
        if (payloadLen > maxLen)
        {
            payloadLen = maxLen;
        }
        outUsedPayloadBytes = payloadLen;

        CString prefix;
        prefix.Format(_T("%0*d"), prefixWidth, payloadLen);

        int total = 0;
        for (int i = 0; i < prefix.GetLength() && total < capacity; ++i)
        {
            outBytes[total++] = static_cast<unsigned char>(prefix.GetAt(i));
        }
        for (int i = 0; i < payloadLen && total < capacity; ++i)
        {
            outBytes[total++] = payloadBytes[i];
        }
        return total;
    }

    // FIXED/LENGTH_PREFIXED 필드는 SPEC상 폭이 "byte" 단위(예: 메시지 X(16)
    // = 16byte)이므로, 완성형 한글처럼 1글자=2byte인 입력을 다룰 때는
    // PadFixedField(문자 단위 패딩)를 그대로 쓰면 폭이 어긋난다. CP949로
    // 인코딩한 뒤 byte 단위로 패딩/절단한다.
    int PadFixedFieldBytes(const CString& text, int widthBytes, FieldPad pad, unsigned char* outBytes)
    {
        unsigned char encoded[512];
        const int encodedLen = EncodeCp949(text, encoded, sizeof(encoded));

        if (encodedLen >= widthBytes)
        {
            const int srcOffset = (pad == FieldPad::LEFT_ZERO) ? (encodedLen - widthBytes) : 0;
            memcpy(outBytes, encoded + srcOffset, static_cast<size_t>(widthBytes));
            return widthBytes;
        }

        const int padCount = widthBytes - encodedLen;
        const unsigned char padByte = (pad == FieldPad::LEFT_ZERO) ? '0' : ' ';
        if (pad == FieldPad::LEFT_ZERO)
        {
            memset(outBytes, padByte, static_cast<size_t>(padCount));
            memcpy(outBytes + padCount, encoded, static_cast<size_t>(encodedLen));
        }
        else
        {
            memcpy(outBytes, encoded, static_cast<size_t>(encodedLen));
            memset(outBytes + encodedLen, padByte, static_cast<size_t>(padCount));
        }
        return widthBytes;
    }

    // hex 문자열(2문자=1byte)을 byteWidth bytes로 변환한다. 2026-08-07 실장비
    // 테스트 중 발견: 예전 구현은 잘못된 hex 문자를 조용히 0으로 치환해 전송했다
    // (예: WorkingKey에 "abcd1234efgh5678" 입력 시 g/h가 0x00으로 치환된 채
    // 실장비로 전송됨) - DES/SEED처럼 암호화 키/PINBLOCK 관련 필드에서는 이것이
    // 위험하므로, 길이가 byteWidth*2와 정확히 일치하지 않거나 유효하지 않은 hex
    // 문자가 하나라도 있으면 false를 반환하고 outBytes 내용은 불특정으로 둔다.
    // 호출자는 실패 시 그 필드의 전송 자체를 중단해야 한다.
    bool HexStringToBytes(const CString& hexText, int byteWidth, unsigned char* outBytes, CString& outFailReason)
    {
        if (hexText.GetLength() != byteWidth * 2)
        {
            outFailReason.Format(_T("길이가 맞지 않음(입력 %d자, 필요 %d자)"), hexText.GetLength(), byteWidth * 2);
            return false;
        }

        auto hexValue = [](TCHAR ch, bool& ok) -> int
        {
            ok = true;
            if (ch >= _T('0') && ch <= _T('9')) return ch - _T('0');
            if (ch >= _T('a') && ch <= _T('f')) return ch - _T('a') + 10;
            if (ch >= _T('A') && ch <= _T('F')) return ch - _T('A') + 10;
            ok = false;
            return 0;
        };

        for (int i = 0; i < byteWidth; ++i)
        {
            bool hiOk = false;
            bool loOk = false;
            const int hi = hexValue(hexText.GetAt(i * 2), hiOk);
            const int lo = hexValue(hexText.GetAt(i * 2 + 1), loOk);
            if (!hiOk || !loOk)
            {
                outFailReason = _T("잘못된 hex 문자");
                return false;
            }
            outBytes[i] = static_cast<unsigned char>((hi << 4) | lo);
        }
        return true;
    }
}

BEGIN_MESSAGE_MAP(CReaderSerialTestUIDlg, CDialogEx)
    ON_BN_CLICKED(IDC_BUTTON_OPEN_A, &CReaderSerialTestUIDlg::OnBnClickedButtonOpenA)
    ON_BN_CLICKED(IDC_BUTTON_OPEN_B, &CReaderSerialTestUIDlg::OnBnClickedButtonOpenB)
    ON_BN_CLICKED(IDC_BUTTON_CLOSE_A, &CReaderSerialTestUIDlg::OnBnClickedButtonCloseA)
    ON_BN_CLICKED(IDC_BUTTON_CLOSE_B, &CReaderSerialTestUIDlg::OnBnClickedButtonCloseB)
    ON_BN_CLICKED(IDC_BUTTON_STATUS, &CReaderSerialTestUIDlg::OnBnClickedButtonStatus)
    ON_BN_CLICKED(IDC_BUTTON_INIT, &CReaderSerialTestUIDlg::OnBnClickedButtonInit)
    ON_CBN_SELCHANGE(IDC_COMBO_COMMAND_A, &CReaderSerialTestUIDlg::OnCbnSelchangeComboCommandA)
    ON_CBN_SELCHANGE(IDC_COMBO_COMMAND_B, &CReaderSerialTestUIDlg::OnCbnSelchangeComboCommandB)
    ON_BN_CLICKED(IDC_BUTTON_SEND_READER1, &CReaderSerialTestUIDlg::OnBnClickedButtonSendReader1)
    ON_BN_CLICKED(IDC_BUTTON_SEND_READER2, &CReaderSerialTestUIDlg::OnBnClickedButtonSendReader2)
    ON_BN_CLICKED(IDC_BUTTON_SEND_FAILOVER, &CReaderSerialTestUIDlg::OnBnClickedButtonSendFailover)
    ON_CBN_SELCHANGE(IDC_COMBO_PINPAD_A, &CReaderSerialTestUIDlg::OnCbnSelchangeComboPinpadA)
    ON_CBN_SELCHANGE(IDC_COMBO_PINPAD_B, &CReaderSerialTestUIDlg::OnCbnSelchangeComboPinpadB)
    ON_BN_CLICKED(IDC_BUTTON_SEND_PINPAD_A, &CReaderSerialTestUIDlg::OnBnClickedButtonSendPinpadA)
    ON_BN_CLICKED(IDC_BUTTON_SEND_PINPAD_B, &CReaderSerialTestUIDlg::OnBnClickedButtonSendPinpadB)
    ON_WM_DESTROY()
    ON_WM_SIZE()
    ON_WM_VSCROLL()
    ON_WM_MOUSEWHEEL()
    ON_MESSAGE(WM_APP_READER_EVENT, &CReaderSerialTestUIDlg::OnReaderEvent)
    ON_MESSAGE(WM_APP_PINPAD_EVENT, &CReaderSerialTestUIDlg::OnPinpadEvent)
END_MESSAGE_MAP()

CReaderSerialTestUIDlg::CReaderSerialTestUIDlg(CWnd* pParent)
    : CDialogEx(IDD_READERSERIALTESTUI_DIALOG, pParent)
{
}

void CReaderSerialTestUIDlg::DoDataExchange(CDataExchange* pDX)
{
    CDialogEx::DoDataExchange(pDX);
    DDX_Control(pDX, IDC_LIST_LOG, m_logList);
    DDX_Control(pDX, IDC_COMBO_COMMAND_A, m_commandPanels[0].commandCombo);
    DDX_Control(pDX, IDC_COMBO_COMMAND_B, m_commandPanels[1].commandCombo);
    DDX_Control(pDX, IDC_STATIC_STATUS_A, m_statusLabels[0]);
    DDX_Control(pDX, IDC_STATIC_STATUS_B, m_statusLabels[1]);
    DDX_Control(pDX, IDC_COMBO_PINPAD_A, m_pinpadPanels[0].commandCombo);
    DDX_Control(pDX, IDC_COMBO_PINPAD_B, m_pinpadPanels[1].commandCombo);
}

BOOL CReaderSerialTestUIDlg::OnInitDialog()
{
    CDialogEx::OnInitDialog();

    // 2026-07-29: 사용자가 실장비를 COM3(A)+COM6(B)에 연결해 사용 중(DOC/개발문서/실행계획서.md §4).
    SetDlgItemText(IDC_EDIT_PORT_A, _T("3"));
    SetDlgItemText(IDC_EDIT_PORT_B, _T("6"));
    SetDlgItemText(IDC_EDIT_BAUD, _T("115200"));

    UpdateStatusLabel(0);
    UpdateStatusLabel(1);

    // P7-11/P10-1: 명령 콤보박스를 21개 SPEC 명령으로 채운다. 두 슬롯
    // (리더기 A/B)이 동일한 목록을 공유하지만 선택 상태는 슬롯별로 독립이다.
    // 콤보 선택 변경 시 OnCbnSelchangeComboCommandA/B가 그 슬롯의 필드
    // 패널만 다시 만든다(다른 슬롯 세팅은 보존됨).
    m_commandCodesByComboIndex = GetAllFieldCommandCodes();

    const UINT fieldPanelWndIds[kReaderSlotCount] = { IDC_STATIC_FIELD_PANEL_A, IDC_STATIC_FIELD_PANEL_B };
    const UINT fieldScrollBarIds[kReaderSlotCount] = { IDC_SCROLLBAR_FIELD_PANEL_A, IDC_SCROLLBAR_FIELD_PANEL_B };

    for (int slot = 0; slot < kReaderSlotCount; ++slot)
    {
        for (unsigned char code : m_commandCodesByComboIndex)
        {
            m_commandPanels[slot].commandCombo.AddString(GetCommandDisplayName(code));
        }
        if (m_commandPanels[slot].commandCombo.GetCount() > 0)
        {
            m_commandPanels[slot].commandCombo.SetCurSel(0);
        }
    }

    CRect clientRect;
    GetClientRect(&clientRect);
    m_dlgInitSize = clientRect.Size();

    m_logList.GetWindowRect(&m_logListInitRect);
    ScreenToClient(&m_logListInitRect);

    // P7-12/P10-1: 필드 라벨/EditText를 담는 고정 크기 뷰포트 컨테이너와,
    // 넘치는 필드에 접근하기 위한 스크롤바를 슬롯마다 한 번씩 만든다(명령
    // 재선택 시에는 RebuildFieldPanel이 그 슬롯 컨테이너의 자식만 다시 만든다).
    for (int slot = 0; slot < kReaderSlotCount; ++slot)
    {
        CommandPanel& panel = m_commandPanels[slot];

        CRect panelDlu(kFieldPanelLeftDlu[slot], kFieldPanelTopDlu, kFieldPanelRightDlu[slot], kFieldPanelTopDlu + kFieldPanelHeightDlu);
        MapDialogRect(&panelDlu);
        panel.fieldPanelRectPx = panelDlu;
        panel.fieldPanelWnd.Create(_T(""), WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | SS_LEFT,
            panel.fieldPanelRectPx, this, fieldPanelWndIds[slot]);

        CRect scrollDlu(kFieldScrollBarLeftDlu[slot], kFieldPanelTopDlu, kFieldScrollBarLeftDlu[slot] + kFieldScrollBarWidthDlu, kFieldPanelTopDlu + kFieldPanelHeightDlu);
        MapDialogRect(&scrollDlu);
        panel.fieldScrollBar.Create(WS_CHILD | WS_VISIBLE | SBS_VERT, scrollDlu, this, fieldScrollBarIds[slot]);
        panel.fieldScrollBar.EnableScrollBar(ESB_DISABLE_BOTH);

        if (!m_commandCodesByComboIndex.empty())
        {
            RebuildFieldPanel(slot, m_commandCodesByComboIndex[0]);
        }
    }

    // P17-1: 핀패드 명령 콤보박스를 5종으로 채우고, 리더기 필드 패널과
    // 동일한 방식으로 슬롯별 뷰포트를 만든다.
    m_pinpadCommandCodesByComboIndex = GetAllPinpadCommandCodes();

    const UINT pinpadFieldPanelWndIds[kReaderSlotCount] = { IDC_STATIC_PINPAD_FIELD_PANEL_A, IDC_STATIC_PINPAD_FIELD_PANEL_B };
    const UINT pinpadFieldScrollBarIds[kReaderSlotCount] = { IDC_SCROLLBAR_PINPAD_FIELD_PANEL_A, IDC_SCROLLBAR_PINPAD_FIELD_PANEL_B };

    for (int slot = 0; slot < kReaderSlotCount; ++slot)
    {
        for (PinpadCommandCode code : m_pinpadCommandCodesByComboIndex)
        {
            m_pinpadPanels[slot].commandCombo.AddString(GetPinpadCommandDisplayName(code));
        }
        if (m_pinpadPanels[slot].commandCombo.GetCount() > 0)
        {
            m_pinpadPanels[slot].commandCombo.SetCurSel(0);
        }

        PinpadCommandPanel& panel = m_pinpadPanels[slot];

        CRect panelDlu(kFieldPanelLeftDlu[slot], kPinpadFieldPanelTopDlu, kFieldPanelRightDlu[slot], kPinpadFieldPanelTopDlu + kPinpadFieldPanelHeightDlu);
        MapDialogRect(&panelDlu);
        panel.fieldPanelRectPx = panelDlu;
        panel.fieldPanelWnd.Create(_T(""), WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | SS_LEFT,
            panel.fieldPanelRectPx, this, pinpadFieldPanelWndIds[slot]);

        CRect scrollDlu(kFieldScrollBarLeftDlu[slot], kPinpadFieldPanelTopDlu, kFieldScrollBarLeftDlu[slot] + kFieldScrollBarWidthDlu, kPinpadFieldPanelTopDlu + kPinpadFieldPanelHeightDlu);
        MapDialogRect(&scrollDlu);
        panel.fieldScrollBar.Create(WS_CHILD | WS_VISIBLE | SBS_VERT, scrollDlu, this, pinpadFieldScrollBarIds[slot]);
        panel.fieldScrollBar.EnableScrollBar(ESB_DISABLE_BOTH);

        if (!m_pinpadCommandCodesByComboIndex.empty())
        {
            RebuildPinpadFieldPanel(slot, m_pinpadCommandCodesByComboIndex[0]);
        }
    }

    return TRUE;
}

void CReaderSerialTestUIDlg::OnDestroy()
{
    DestroyFieldPanelControls(0);
    DestroyFieldPanelControls(1);
    DestroyPinpadFieldPanelControls(0);
    DestroyPinpadFieldPanelControls(1);
    ClosePortIfOpen();
    CDialogEx::OnDestroy();
}

// 다이얼로그가 WS_THICKFRAME으로 리사이즈 가능해졌으므로, 로그 리스트박스만
// 우측/하단으로 늘어나도록 앵커링한다. 다른 컨트롤은 위치를 그대로 둔다.
void CReaderSerialTestUIDlg::OnSize(UINT nType, int cx, int cy)
{
    CDialogEx::OnSize(nType, cx, cy);

    if (!::IsWindow(m_logList.GetSafeHwnd()) || m_dlgInitSize.cx == 0)
    {
        return;
    }

    const int dx = cx - m_dlgInitSize.cx;
    const int dy = cy - m_dlgInitSize.cy;

    CRect newRect(m_logListInitRect);
    newRect.right += dx;
    newRect.bottom += dy;

    m_logList.SetWindowPos(nullptr, newRect.left, newRect.top,
        newRect.Width(), newRect.Height(), SWP_NOZORDER);
}

// P7-12/P10-1: 두 슬롯의 fieldScrollBar 중 이벤트를 보낸 쪽만 찾아 처리한다.
// 다른 스크롤바(로그 리스트박스는 자체 WS_VSCROLL을 쓰므로 여기로 오지
// 않는다)는 기본 처리로 넘긴다.
void CReaderSerialTestUIDlg::OnVScroll(UINT nSBCode, UINT nPos, CScrollBar* pScrollBar)
{
    for (int slot = 0; slot < kReaderSlotCount; ++slot)
    {
        CommandPanel& panel = m_commandPanels[slot];
        if (pScrollBar == nullptr || pScrollBar->GetSafeHwnd() != panel.fieldScrollBar.GetSafeHwnd())
        {
            continue;
        }

        const int lineStep = 20;
        const int pageStep = max(20, panel.fieldPanelRectPx.Height());
        int pos = panel.fieldScrollPos;

        switch (nSBCode)
        {
        case SB_LINEUP:        pos -= lineStep; break;
        case SB_LINEDOWN:      pos += lineStep; break;
        case SB_PAGEUP:        pos -= pageStep; break;
        case SB_PAGEDOWN:      pos += pageStep; break;
        case SB_THUMBTRACK:
        case SB_THUMBPOSITION: pos = static_cast<int>(nPos); break;
        default: return;
        }

        ScrollFieldPanel(slot, pos);
        return;
    }

    // P17-1: 핀패드 필드 패널의 스크롤바도 동일하게 처리한다.
    for (int slot = 0; slot < kReaderSlotCount; ++slot)
    {
        PinpadCommandPanel& panel = m_pinpadPanels[slot];
        if (pScrollBar == nullptr || pScrollBar->GetSafeHwnd() != panel.fieldScrollBar.GetSafeHwnd())
        {
            continue;
        }

        const int lineStep = 20;
        const int pageStep = max(20, panel.fieldPanelRectPx.Height());
        int pos = panel.fieldScrollPos;

        switch (nSBCode)
        {
        case SB_LINEUP:        pos -= lineStep; break;
        case SB_LINEDOWN:      pos += lineStep; break;
        case SB_PAGEUP:        pos -= pageStep; break;
        case SB_PAGEDOWN:      pos += pageStep; break;
        case SB_THUMBTRACK:
        case SB_THUMBPOSITION: pos = static_cast<int>(nPos); break;
        default: return;
        }

        ScrollPinpadFieldPanel(slot, pos);
        return;
    }

    CDialogEx::OnVScroll(nSBCode, nPos, pScrollBar);
}

// 마우스 휠로도 필드 패널을 스크롤할 수 있게 한다. 두 슬롯이 화면에 나란히
// 있으므로(P10-1), 커서(pt, 스크린 좌표)가 클라이언트 좌표로 볼 때 어느
// 슬롯의 패널 영역 위에 있는지로 대상을 가른다 — 슬롯이 하나뿐이던 이전
// 버전처럼 무조건 스크롤하면 엉뚱한 슬롯이 움직인다.
BOOL CReaderSerialTestUIDlg::OnMouseWheel(UINT nFlags, short zDelta, CPoint pt)
{
    CPoint clientPt(pt);
    ScreenToClient(&clientPt);

    for (int slot = 0; slot < kReaderSlotCount; ++slot)
    {
        CommandPanel& panel = m_commandPanels[slot];
        if (panel.fieldScrollBar.GetSafeHwnd() != nullptr && panel.fieldScrollBar.IsWindowEnabled()
            && panel.fieldPanelRectPx.PtInRect(clientPt))
        {
            const int wheelStep = 60;
            ScrollFieldPanel(slot, panel.fieldScrollPos - (zDelta / WHEEL_DELTA) * wheelStep);
            return TRUE;
        }
    }

    // P17-1: 핀패드 필드 패널 위에서도 마우스 휠 스크롤을 지원한다.
    for (int slot = 0; slot < kReaderSlotCount; ++slot)
    {
        PinpadCommandPanel& panel = m_pinpadPanels[slot];
        if (panel.fieldScrollBar.GetSafeHwnd() != nullptr && panel.fieldScrollBar.IsWindowEnabled()
            && panel.fieldPanelRectPx.PtInRect(clientPt))
        {
            const int wheelStep = 60;
            ScrollPinpadFieldPanel(slot, panel.fieldScrollPos - (zDelta / WHEEL_DELTA) * wheelStep);
            return TRUE;
        }
    }

    return CDialogEx::OnMouseWheel(nFlags, zDelta, pt);
}

void CReaderSerialTestUIDlg::ClosePortIfOpen()
{
    for (int i = 0; i < kReaderSlotCount; ++i)
    {
        if (m_readers[i].connected)
        {
            Reader_ClosePort(m_readers[i].readerId);
            m_readers[i] = ReaderSlot();
        }
    }
}

CString CReaderSerialTestUIDlg::ReaderTag(int index)
{
    return (index == 0) ? _T("리더기A") : _T("리더기B");
}

int CReaderSerialTestUIDlg::FindReaderIndexById(int readerId) const
{
    for (int i = 0; i < kReaderSlotCount; ++i)
    {
        if (m_readers[i].readerId == readerId)
        {
            return i;
        }
    }
    return -1;
}

void CReaderSerialTestUIDlg::UpdateStatusLabel(int index)
{
    const ReaderSlot& slot = m_readers[index];
    CString text;
    if (!slot.connected)
    {
        text = _T("미연결");
    }
    else if (slot.portError)
    {
        text.Format(_T("COM%d, readerId=%d, 오류(닫기 후 재연결 필요)"), slot.comPort, slot.readerId);
    }
    else
    {
        text.Format(_T("COM%d, readerId=%d, 연결됨"), slot.comPort, slot.readerId);
    }
    m_statusLabels[index].SetWindowText(text);
}

void CReaderSerialTestUIDlg::OpenReader(int index)
{
    if (m_readers[index].connected)
    {
        AppendLog(ReaderTag(index) + _T(": 이미 연결되어 있습니다. 먼저 닫기를 눌러주세요"));
        return;
    }

    const int portEditId = (index == 0) ? IDC_EDIT_PORT_A : IDC_EDIT_PORT_B;
    CString portStr;
    CString baudStr;
    GetDlgItemText(portEditId, portStr);
    GetDlgItemText(IDC_EDIT_BAUD, baudStr);

    const int portNumber = _ttoi(portStr);
    const int baudRate = _ttoi(baudStr);

    int newReaderId = -1;
    // P17-1: readerCallback과 pinpadCallback을 둘 다 등록한다 — 이 슬롯이
    // 멀티패드(리더기와 같은 포트에 핀패드 내장)로 쓰이면 두 콜백이 같은
    // readerId로 함께 동작하고, 별도 핀패드 전용 포트로 쓰이면 리더기 패널
    // 쪽은 그냥 사용하지 않으면 된다(리더기 콜백을 NULL로 만들 필요 없음).
    const int result = Reader_OpenPort(
        portNumber, baudRate, &CReaderSerialTestUIDlg::OnReaderCallback, &CReaderSerialTestUIDlg::OnPinpadCallback,
        static_cast<void*>(GetSafeHwnd()), &newReaderId);

    CString msg;
    if (result == READER_OK)
    {
        m_readers[index].readerId = newReaderId;
        m_readers[index].comPort = portNumber;
        m_readers[index].connected = true;
        m_readers[index].portError = false;
        msg.Format(_T("[열기] %s COM%d, %d bps -> READER_OK, readerId=%d"),
            ReaderTag(index).GetString(), portNumber, baudRate, newReaderId);
    }
    else
    {
        msg.Format(_T("[열기] %s COM%d, %d bps -> 실패 (result=%s)"),
            ReaderTag(index).GetString(), portNumber, baudRate, FormatResult(result).GetString());
    }
    AppendLog(msg);
    UpdateStatusLabel(index);
}

// P10-1b: SendCommandSafe가 "readerId 없음"이거나 포트 계열 에러로 Close한
// 직후에만 호출한다. 항상 UI에 입력된 COM포트/보드레이트로 무조건
// Reader_OpenPort를 시도한다 — 수동 "열기" 버튼(OpenReader)과 달리 "이미
// 연결되어 있는가"를 확인하지 않는다(호출 시점에 이미 닫힌/없는 상태임이
// 보장되므로).
int CReaderSerialTestUIDlg::TryAutoOpenReader(int slot, const CString& logPrefix)
{
    const int portEditId = (slot == 0) ? IDC_EDIT_PORT_A : IDC_EDIT_PORT_B;
    CString portStr;
    CString baudStr;
    GetDlgItemText(portEditId, portStr);
    GetDlgItemText(IDC_EDIT_BAUD, baudStr);

    const int portNumber = _ttoi(portStr);
    const int baudRate = _ttoi(baudStr);

    int newReaderId = -1;
    // P17-1: OpenReader와 동일하게 readerCallback/pinpadCallback을 둘 다
    // 등록한다(멀티패드/별도 핀패드 두 구성 모두 이 한 번의 Open으로 지원됨).
    const int result = Reader_OpenPort(
        portNumber, baudRate, &CReaderSerialTestUIDlg::OnReaderCallback, &CReaderSerialTestUIDlg::OnPinpadCallback,
        static_cast<void*>(GetSafeHwnd()), &newReaderId);

    CString msg;
    if (result == READER_OK)
    {
        // 새로 발급된 readerId로 반드시 슬롯 상태를 덮어쓴다 — 옛 id로 계속
        // Send하면 무조건 실패한다(DOC/개발문서/실행계획서.md P10-1b).
        m_readers[slot].readerId = newReaderId;
        m_readers[slot].comPort = portNumber;
        m_readers[slot].connected = true;
        m_readers[slot].portError = false;
        msg.Format(_T("%s%s COM%d, %d bps -> READER_OK, readerId=%d"),
            logPrefix.GetString(), ReaderTag(slot).GetString(), portNumber, baudRate, newReaderId);
    }
    else
    {
        msg.Format(_T("%s%s COM%d, %d bps -> 실패 (result=%s)"),
            logPrefix.GetString(), ReaderTag(slot).GetString(), portNumber, baudRate, FormatResult(result).GetString());
    }
    AppendLog(msg);
    UpdateStatusLabel(slot);
    return result;
}

// P10-1b POS 연동 권장 패턴의 실제 구현. Reader_SendCommand를 직접 부르는
// 모든 지점(리더기 1/2 개별 전송, 페일오버 전송, 무효화 재전송)이 이
// 래퍼를 거친다.
int CReaderSerialTestUIDlg::SendCommandSafe(int slot, unsigned char commandCode, const unsigned char* data, int dataLength)
{
    static const CString kAutoRecoverPrefix(_T("[자동복구] "));

    if (m_readers[slot].readerId < 0)
    {
        const int openResult = TryAutoOpenReader(slot, kAutoRecoverPrefix);
        if (openResult != READER_OK)
        {
            // readerId는 계속 "없음"(-1) 상태로 유지된다 — 다음 명령이 다시
            // Open부터 시작한다.
            return openResult;
        }
    }

    int result = Reader_SendCommand(m_readers[slot].readerId, commandCode, data, dataLength);

    // 포트 계열 에러만 복구 대상이다. READER_ERR_BUSY 등 그 외 에러는 이미
    // 정상 진행 중인 다른 명령이 있다는 뜻이므로, 여기서 Close를 걸면 그
    // 명령을 강제로 죽이게 된다 — 절대 여기로 흘러들지 않는다.
    if (result == READER_ERR_PORT_NOT_OPEN)
    {
        CString detectMsg;
        detectMsg.Format(_T("%s%s: 전송 중 포트 계열 에러 감지(result=%s) -> Close 후 재연결 시도"),
            kAutoRecoverPrefix.GetString(), ReaderTag(slot).GetString(), FormatResult(result).GetString());
        AppendLog(detectMsg);

        Reader_ClosePort(m_readers[slot].readerId);
        m_readers[slot] = ReaderSlot();

        const int reopenResult = TryAutoOpenReader(slot, kAutoRecoverPrefix);
        if (reopenResult != READER_OK)
        {
            // 재오픈까지 실패 -> readerId를 "없음"으로 초기화한 채로 둔다
            // (TryAutoOpenReader가 실패 시 m_readers[slot]을 건드리지 않으므로
            // 위에서 대입한 ReaderSlot() 기본값, 즉 readerId=-1이 그대로 유지됨).
            AppendLog(kAutoRecoverPrefix + ReaderTag(slot) + _T(": 재연결 실패 - readerId를 초기화합니다(다음 명령에서 다시 Open부터 시도)"));
            return reopenResult;
        }

        result = Reader_SendCommand(m_readers[slot].readerId, commandCode, data, dataLength);
        if (result == READER_OK)
        {
            CString okMsg;
            okMsg.Format(_T("%s%s: 재연결 성공(readerId=%d) -> 재전송 성공"),
                kAutoRecoverPrefix.GetString(), ReaderTag(slot).GetString(), m_readers[slot].readerId);
            AppendLog(okMsg);
        }
        else
        {
            CString failMsg;
            failMsg.Format(_T("%s%s: 재연결 성공(readerId=%d) -> 재전송도 실패(result=%s)"),
                kAutoRecoverPrefix.GetString(), ReaderTag(slot).GetString(), m_readers[slot].readerId, FormatResult(result).GetString());
            AppendLog(failMsg);
        }
    }
    else if (result == READER_ERR_SEND_FAIL)
    {
        // DLL이 이미 operationState를 즉시 IDLE로 복귀시켰으므로(2026-08-03)
        // 이 0x60 재전송은 필수가 아니다 — 리더기 쪽이 여전히 깨진 프레임을
        // 붙잡고 있을 잔여 가능성에 대비한 방어적 권장 조치일 뿐이다. 결과를
        // 기다리지 않고 로그만 남기며, 원래의 SEND_FAIL은 그대로 호출자에게
        // 반환한다.
        CString resyncMsg;
        resyncMsg.Format(_T("%s%s: 전송 실패(result=%s) 감지 -> 프레임 재동기화용 초기화 요청(0x60) 방어적 전송"),
            kAutoRecoverPrefix.GetString(), ReaderTag(slot).GetString(), FormatResult(result).GetString());
        AppendLog(resyncMsg);
        Reader_SendCommand(m_readers[slot].readerId, 0x60, nullptr, 0);
    }

    return result;
}

void CReaderSerialTestUIDlg::CloseReader(int index)
{
    if (!m_readers[index].connected)
    {
        AppendLog(ReaderTag(index) + _T(": 열려 있는 리더기가 없습니다"));
        return;
    }

    const int readerId = m_readers[index].readerId;
    const int result = Reader_ClosePort(readerId);
    CString msg;
    msg.Format(_T("[닫기] %s readerId=%d -> result=%s"), ReaderTag(index).GetString(), readerId, FormatResult(result).GetString());
    AppendLog(msg);

    if (result == READER_OK)
    {
        m_readers[index] = ReaderSlot();
    }
    UpdateStatusLabel(index);
}

// CEdit(ES_MULTILINE)은 AddString이 없으므로 끝에 텍스트+개행을 이어붙이고,
// 캐럿을 맨 끝으로 옮긴 뒤 EM_SCROLLCARET으로 스크롤해 새 로그가 항상 보이게
// 한다(리스트박스의 SetTopIndex(count-1)와 동등한 동작).
void CReaderSerialTestUIDlg::AppendLog(const CString& text)
{
    const int endPos = m_logList.GetWindowTextLength();
    m_logList.SetSel(endPos, endPos);
    m_logList.ReplaceSel(text + _T("\r\n"), FALSE);
    m_logList.SendMessage(EM_SCROLLCARET);
}

// 리더기 수신 스레드에서 직접 호출된다. UI 컨트롤을 여기서 절대 건드리지 않고,
// data를 즉시 복사한 뒤 PostMessage로 UI 스레드에 위임한다 (CLAUDE.md, PRD §7.6, §8.2).
void __stdcall CReaderSerialTestUIDlg::OnReaderCallback(
    int readerId,
    int eventType,
    unsigned char commandCode,
    const unsigned char* data,
    int dataLength,
    void* userContext)
{
    HWND hWnd = static_cast<HWND>(userContext);
    if (hWnd == nullptr || !::IsWindow(hWnd))
    {
        return;
    }

    ReaderEventData* eventData = new ReaderEventData();
    eventData->readerId = readerId;
    eventData->eventType = eventType;
    eventData->commandCode = commandCode;

    const int copyLength = (dataLength > 0)
        ? min(dataLength, static_cast<int>(sizeof(eventData->data)))
        : 0;
    eventData->dataLength = dataLength;
    if (copyLength > 0 && data != nullptr)
    {
        memcpy(eventData->data, data, copyLength);
    }

    ::PostMessage(hWnd, WM_APP_READER_EVENT, static_cast<WPARAM>(readerId), reinterpret_cast<LPARAM>(eventData));
}

// 핀패드 조합 시퀀스 엔진에서 직접 호출된다(리더기 수신 스레드와 마찬가지로
// UI 스레드가 아니다). OnReaderCallback과 동일한 이유로 UI를 여기서 절대
// 건드리지 않고 data를 즉시 복사한 뒤 PostMessage로 UI 스레드에 위임한다
// (CLAUDE.md, PRD_핀패드.md §8.3).
void __stdcall CReaderSerialTestUIDlg::OnPinpadCallback(
    int readerId,
    int eventType,
    unsigned char commandCode,
    const unsigned char* data,
    int dataLength,
    void* userContext)
{
    HWND hWnd = static_cast<HWND>(userContext);
    if (hWnd == nullptr || !::IsWindow(hWnd))
    {
        return;
    }

    PinpadEventData* eventData = new PinpadEventData();
    eventData->readerId = readerId;
    eventData->eventType = eventType;
    eventData->commandCode = commandCode;

    const int copyLength = (dataLength > 0)
        ? min(dataLength, static_cast<int>(sizeof(eventData->data)))
        : 0;
    eventData->dataLength = dataLength;
    if (copyLength > 0 && data != nullptr)
    {
        memcpy(eventData->data, data, copyLength);
    }

    ::PostMessage(hWnd, WM_APP_PINPAD_EVENT, static_cast<WPARAM>(readerId), reinterpret_cast<LPARAM>(eventData));
}

LRESULT CReaderSerialTestUIDlg::OnReaderEvent(WPARAM wParam, LPARAM lParam)
{
    ReaderEventData* eventData = reinterpret_cast<ReaderEventData*>(lParam);
    if (eventData == nullptr)
    {
        return 0;
    }

    // data 바이트에 0x00(NUL)이 섞여 있으면, CString 자체는 embedded NUL을
    // 담을 수 있어도 그 문자열이 결국 전달되는 Win32 Edit 컨트롤(EM_REPLACESEL)과
    // Format의 "%s" 치환은 둘 다 null-종단 문자열 규약을 따르기 때문에 그
    // 지점에서 화면 표시가 끊긴다. raw ASCII 표시 취지(hex dump 아님)는 유지하되,
    // NUL만 화면에 보이는 대체 기호(␀)로 바꿔서 이후 바이트가 잘리지 않게 한다.
    CString ascii;
    const int asciiLength = min(eventData->dataLength, static_cast<int>(sizeof(eventData->data)));
    for (int i = 0; i < asciiLength; ++i)
    {
        const unsigned char b = eventData->data[i];
        ascii += (b == 0) ? _T('␀') : static_cast<TCHAR>(b);
    }

    // 2026-07-16: 리더기 2대 동시 연동 시 로그만 보고 어느 리더기(A/B)의
    // CALLBACK인지 바로 구분할 수 있어야 한다(DOC/개발문서/실행계획서.md §4).
    const int index = FindReaderIndexById(eventData->readerId);
    const CString tag = (index >= 0) ? ReaderTag(index) : CString(_T("리더기?"));

    // 접두부는 Format으로 만들고 데이터는 += 로 이어붙인다.
    CString line;
    line.Format(
        _T("[%d][%s] readerId=%d eventType=%s commandCode=0x%02X dataLength=%d data="),
        static_cast<int>(wParam),
        tag.GetString(),
        eventData->readerId,
        FormatEventType(eventData->eventType).GetString(),
        eventData->commandCode,
        eventData->dataLength);
    line += ascii.IsEmpty() ? CString(_T("(none)")) : ascii;

    // 이번 브로드캐스트 라운드에서 이 리더기가 최초로 채택되는 응답인지
    // 표시한다(요구사항 4). 채택 여부 판정 자체는 아래 브로드캐스트 라운드
    // 처리에서 하고, 여기서는 그 결과를 로그 줄 뒤에 덧붙이기만 한다.
    const bool isWinnerResponse =
        (index >= 0) && m_broadcastRound.active && m_broadcastRound.participated[index]
        && !m_broadcastRound.responded[index] && eventData->eventType == READER_EVENT_RESPONSE
        && m_broadcastRound.winnerIndex < 0;
    if (isWinnerResponse)
    {
        line += _T("  ★ 채택 (이번 브로드캐스트 최초 응답)");
    }

    AppendLog(line);

    // 물리 포트 연결이 끊긴 경우 상태 라벨을 즉시 갱신한다. PRD §19-18에
    // 따라 자동 재연결은 없으므로, connected는 그대로 두고 portError만
    // 세워 "닫기 후 재연결 필요" 상태를 화면에 보여준다(요구사항 2).
    //
    // P10-1b에서 발견된 버그 수정(2026-07-31): 실제 케이블 분리 시 발생하는
    // 이벤트는 READER_EVENT_RECEIVE_ERROR뿐이다(SerialWorker.cpp의
    // ReportReceiveError 경로). (2026-08-05 재번호 시 READER_EVENT_CONNECTED/
    // DISCONNECTED/SEND_ERROR는 실제로 발생시키는 코드가 없는 죽은 값으로
    // 확인되어 enum에서 제거됨 — 이 감지 조건이 READER_EVENT_RECEIVE_ERROR만
    // 보는 것은 그대로 유효하다.)
    // portError가 이렇게 세워지면 다음 SendCommandSafe 호출에서 readerId는
    // 여전히 유효하므로 자동 재연결이 곧바로 걸리지는 않는다 — Reader_SendCommand를
    // 실제로 시도했을 때 포트 계열 에러(READER_ERR_PORT_NOT_OPEN)로
    // 돌아와야 그때 SendCommandSafe가 Close→Open을 트리거한다("Send 우선 시도"
    // 원칙, Reader_IsPortOpen 사전 체크를 쓰지 않음).
    if (index >= 0 && eventData->eventType == READER_EVENT_RECEIVE_ERROR)
    {
        m_readers[index].portError = true;
        UpdateStatusLabel(index);
    }

    // 페일오버 전송(요구사항 3): 참여 중인 리더기 중 어느 하나의 최종 응답
    // (READER_EVENT_RESPONSE)이 먼저 도착하면, 아직 응답이 안 온 나머지
    // 참여 리더기에만 초기화 요청(0x60)을 보내 무효화한다. 이 무효화로
    // 인해 발생하는 그 리더기의 실제 0x70 응답이 나중에 도착했을 때는
    // winnerIndex가 이미 채워져 있으므로 재-무효화하지 않고 단순히 라운드
    // 종료(responded=true) 처리만 한다.
    //
    // 2026-07-29 사용자 피드백 3건 반영, 2026-07-31(P10-1) 재확인:
    // (1)/(3) 무효화가 실제로 의미 있는 건 거래 계열(TimeoutPolicy::TRADE_TIMEOUT_MS=200초)
    //     명령뿐이다 - 그런 명령은 카드 삽입 대기 등으로 응답까지 최대 200초가 걸릴
    //     수 있으므로, 한쪽이 먼저 끝났으면 나머지를 200초씩 기다리게 두는 대신
    //     무효화하는 실익이 있다. 반면 상태확인(0x61) 등 3초짜리 일반 명령이나
    //     이미 초기화(0x60) 자체를 기다리던 리더기는 어차피 곧 자연스럽게 끝나거나
    //     이미 같은 브로드캐스트로 0x60을 처리 중이므로 무효화가 불필요한 개입이다.
    //     판정 기준은 "무효화를 당할 수도 있는, 아직 응답을 기다리는 리더기(other)
    //     자신이 원래 대기 중이던 명령"이다 — 먼저 응답한 리더기(index)의 명령이
    //     아니다. 두 패널이 서로 다른 명령을 걸어둘 수 있으므로(P10-1 요구사항 3 —
    //     예: 리더기A=0x67 IC카드리딩, 리더기B=0x2B 거래정보) 이 판정은 반드시
    //     m_broadcastRound.commandCode[other]를 기준으로 리더기별로 개별 수행해야
    //     한다. TimeoutPolicy::ResponseTimeoutMsFor(commandCode[other])로 거래
    //     계열인지 확인하면 0x60도 자연히 걸러진다(0x60은 이 표에 없어 항상
    //     DEFAULT_RESPONSE_TIMEOUT_MS로 판정되므로).
    // (2) 거래(200초) 타임아웃이 참여 리더기 중 하나에서 먼저 발생한 경우,
    //     그 리더기는 DLL 내부적으로 이미 IDLE로 복귀했으므로(§13 타임아웃
    //     정책) 더 이상 무효화할 대상이 아니다. 아래 TIMEOUT 분기에서
    //     responded=true로 표시해 "정산 완료"로 취급하고, 이후 다른 리더기의
    //     정상 응답이 이 리더기를 무효화 대상으로 다시 잡지 않도록 한다.
    if (index >= 0 && m_broadcastRound.active && m_broadcastRound.participated[index]
        && !m_broadcastRound.responded[index] && eventData->eventType == READER_EVENT_TIMEOUT)
    {
        m_broadcastRound.responded[index] = true;

        bool allResponded = true;
        for (int i = 0; i < kReaderSlotCount; ++i)
        {
            if (m_broadcastRound.participated[i] && !m_broadcastRound.responded[i])
            {
                allResponded = false;
                break;
            }
        }
        if (allResponded)
        {
            m_broadcastRound.active = false;
        }
    }
    else if (index >= 0 && m_broadcastRound.active && m_broadcastRound.participated[index]
        && !m_broadcastRound.responded[index] && eventData->eventType == READER_EVENT_RESPONSE)
    {
        m_broadcastRound.responded[index] = true;

        if (m_broadcastRound.winnerIndex < 0)
        {
            m_broadcastRound.winnerIndex = index;

            for (int other = 0; other < kReaderSlotCount; ++other)
            {
                if (other == index)
                {
                    continue;
                }
                if (!m_broadcastRound.participated[other] || m_broadcastRound.responded[other])
                {
                    continue;
                }

                // 무효화 필요 여부는 무효화당할 당사자(other)가 원래 대기 중이던
                // 명령 기준으로 판정한다 — 먼저 응답한 index의 명령이 아니다
                // (두 패널이 서로 다른 명령을 걸어둘 수 있으므로, P10-1 요구사항 3).
                const bool otherIsTradeClassCommand =
                    TimeoutPolicy::ResponseTimeoutMsFor(m_broadcastRound.commandCode[other]) == TimeoutPolicy::TRADE_TIMEOUT_MS;
                if (!otherIsTradeClassCommand)
                {
                    continue;
                }

                // PilotCommands::INIT_REQUEST(0x60)는 CommandStateManager
                // 규칙상 WAITING_RESPONSE 중에도 유일하게 허용되어 진행
                // 중이던 명령을 무효화한다(CLAUDE.md "Command admission rule").
                // P10-1b: 이 무효화 전송도 다른 Reader_SendCommand 호출
                // 지점과 동일하게 SendCommandSafe를 거친다 — 무효화 대상
                // 리더기(other)의 케이블이 그 사이 끊겼다면 여기서도 동일한
                // Close→Open 자동 복구가 적용된다.
                const int invalidateResult = SendCommandSafe(other, 0x60, nullptr, 0);
                CString invalidateMsg;
                invalidateMsg.Format(
                    _T("        %s: 다른 리더기 응답 채택으로 인해 초기화 요청(0x60) 전송해 무효화 -> result=%s"),
                    ReaderTag(other).GetString(), FormatResult(invalidateResult).GetString());
                AppendLog(invalidateMsg);
            }
        }

        bool allResponded = true;
        for (int i = 0; i < kReaderSlotCount; ++i)
        {
            if (m_broadcastRound.participated[i] && !m_broadcastRound.responded[i])
            {
                allResponded = false;
                break;
            }
        }
        if (allResponded)
        {
            m_broadcastRound.active = false;
        }
    }

    delete eventData;
    return 0;
}

// P17-1, 2026-08-12 재작성: PINPAD_CALLBACK 로그. 리더기 CALLBACK 로그
// (OnReaderEvent)와 같은 관례(원시 ASCII 표시, 코드에 심볼릭 이름 병기)를
// 따르되, 핀패드는 포트 상태(portError)나 페일오버 라운드와 무관하므로 그
// 처리는 하지 않는다. PINPAD_CALLBACK 전면 재설계로 failInfo(3byte) payload
// 개념이 완전히 제거됐다 - eventType 자체가 실패 원인을 표현하므로, POS는
// data[2]를 파싱할 필요 없이 eventType으로 바로 분기하면 된다. data는
// PINPAD_EVENT_RESPONSE일 때만 실제 응답 데이터고, 그 외 모든 이벤트는
// 항상 nullptr/0이다(리더기와 동일한 패턴).
LRESULT CReaderSerialTestUIDlg::OnPinpadEvent(WPARAM wParam, LPARAM lParam)
{
    PinpadEventData* eventData = reinterpret_cast<PinpadEventData*>(lParam);
    if (eventData == nullptr)
    {
        return 0;
    }

    const int index = FindReaderIndexById(eventData->readerId);
    const CString tag = (index >= 0) ? ReaderTag(index) : CString(_T("리더기?"));

    // 2026-08-12: resultCode 자리가 commandCode로 바뀌었다 - POS가
    // Pinpad_SendCommand에 넘긴 원래 PinpadCommandCode가 그대로 돌아온다.
    CString line;
    line.Format(
        _T("[%d][%s][핀패드] readerId=%d eventType=%s commandCode=%s dataLength=%d data="),
        static_cast<int>(wParam),
        tag.GetString(),
        eventData->readerId,
        FormatPinpadEventType(eventData->eventType).GetString(),
        FormatPinpadCommandCode(eventData->commandCode).GetString(),
        eventData->dataLength);

    // 응답 데이터(예: PINBLOCK)는 리더기 CALLBACK 로그와 동일하게 raw
    // ASCII로 표시한다(hex dump 아님, 사용자 요청 관례). NUL은 화면
    // 표시용 대체 기호(␀)로 바꿔 이후 바이트가 잘리지 않게 한다.
    // PINPAD_EVENT_RESPONSE가 아닌 이벤트는 dataLength가 항상 0이므로
    // 자연히 "(none)"으로 표시된다 - 별도 분기가 필요 없다.
    CString ascii;
    const int asciiLength = min(eventData->dataLength, static_cast<int>(sizeof(eventData->data)));
    for (int i = 0; i < asciiLength; ++i)
    {
        const unsigned char b = eventData->data[i];
        ascii += (b == 0) ? _T('␀') : static_cast<TCHAR>(b);
    }
    line += ascii.IsEmpty() ? CString(_T("(none)")) : ascii;

    AppendLog(line);

    delete eventData;
    return 0;
}

void CReaderSerialTestUIDlg::OnBnClickedButtonOpenA()
{
    OpenReader(0);
}

void CReaderSerialTestUIDlg::OnBnClickedButtonOpenB()
{
    OpenReader(1);
}

void CReaderSerialTestUIDlg::OnBnClickedButtonCloseA()
{
    CloseReader(0);
}

void CReaderSerialTestUIDlg::OnBnClickedButtonCloseB()
{
    CloseReader(1);
}

void CReaderSerialTestUIDlg::OnBnClickedButtonStatus()
{
    for (int i = 0; i < kReaderSlotCount; ++i)
    {
        if (!m_readers[i].connected)
        {
            AppendLog(_T("[상태 확인] ") + ReaderTag(i) + _T(": 열려 있는 리더기가 없습니다"));
            continue;
        }

        const int isOpen = Reader_IsPortOpen(m_readers[i].readerId);
        CString msg;
        if (isOpen < 0)
        {
            msg.Format(_T("[상태 확인] %s readerId=%d -> Reader_IsPortOpen=%s"),
                ReaderTag(i).GetString(), m_readers[i].readerId, FormatResult(isOpen).GetString());
        }
        else
        {
            msg.Format(_T("[상태 확인] %s readerId=%d -> Reader_IsPortOpen=%d"),
                ReaderTag(i).GetString(), m_readers[i].readerId, isOpen);
        }
        AppendLog(msg);
    }
}

void CReaderSerialTestUIDlg::OnBnClickedButtonInit()
{
    const unsigned char commandCodes[kReaderSlotCount] = { 0x60, 0x60 };
    const unsigned char* const dataPtrs[kReaderSlotCount] = { nullptr, nullptr };
    const int dataLengths[kReaderSlotCount] = { 0, 0 };
    const CString labels[kReaderSlotCount] = { _T("초기화 요청(0x60)"), _T("초기화 요청(0x60)") };
    BroadcastFailover(commandCodes, dataPtrs, dataLengths, labels);
}

// P10-1 요구사항 3/5: 슬롯별로 다를 수 있는 commandCode/data를 그 슬롯에
// 대응하는 리더기(0=A, 1=B)에 동시에 전송하고, 이번 라운드의 참여자 집합을
// BroadcastRound에 기록한다. 실제 "먼저 응답한 리더기 채택 + 나머지 무효화"
// 처리는 OnReaderEvent에서 READER_EVENT_RESPONSE를 받을 때 수행한다
// (전송 시점에는 아직 어느 응답도 오지 않았으므로 할 일이 없다).
//
// P10-1b: 미연결/portError 슬롯을 미리 걸러 건너뛰던 기존 로직을 제거했다 —
// SendCommandSafe가 readerId 없음/포트 계열 에러를 각 슬롯 단위로 자동
// 복구하므로, 여기서 미리 걸러내면 오히려 페일오버 쪽에서는 자동 복구가
// 전혀 걸리지 않는 결과가 된다(요구사항: 모든 명령에 동일하게 적용).
// participated[i]는 SendCommandSafe의 결과가 READER_OK일 때만 true로
// 세운다 — READER_OK가 아니면 실제로 프레임이 전송되지 않아 이 슬롯에
// 대한 READER_EVENT_RESPONSE/TIMEOUT이 앞으로도 오지 않으므로, 참여자로
// 남겨두면 라운드가 영원히 active 상태로 남는다.
void CReaderSerialTestUIDlg::BroadcastFailover(
    const unsigned char commandCode[kReaderSlotCount],
    const unsigned char* const data[kReaderSlotCount],
    const int dataLength[kReaderSlotCount],
    const CString label[kReaderSlotCount],
    const bool validSlot[kReaderSlotCount])
{
    if (m_broadcastRound.active)
    {
        AppendLog(CString(_T("[페일오버 전송] 이전 라운드가 아직 응답 대기 중이었지만 새 라운드로 덮어씁니다"))
            + _T("(테스트 도구이므로 이전 라운드 추적은 폐기 — DOC/개발문서/실행계획서.md §4 참조)"));
    }

    m_broadcastRound = BroadcastRound();

    for (int i = 0; i < kReaderSlotCount; ++i)
    {
        m_broadcastRound.commandCode[i] = commandCode[i];

        if (validSlot != nullptr && !validSlot[i])
        {
            AppendLog(CString(_T("[페일오버 전송] ")) + ReaderTag(i).GetString() + _T(": 필드 파싱 실패로 이 슬롯은 전송하지 않습니다"));
            continue;
        }

        const int result = SendCommandSafe(i, commandCode[i], data[i], dataLength[i]);
        CString msg;
        msg.Format(_T("[페일오버 전송] %s(readerId=%d, COM%d) %s -> result=%s"),
            ReaderTag(i).GetString(), m_readers[i].readerId, m_readers[i].comPort,
            label[i].GetString(), FormatResult(result).GetString());
        AppendLog(msg);

        if (result == READER_OK)
        {
            m_broadcastRound.participated[i] = true;
            m_broadcastRound.active = true;
        }
    }

    if (!m_broadcastRound.active)
    {
        AppendLog(_T("[페일오버 전송] 두 리더기 모두 전송에 실패해 이번 라운드는 응답 대기 없이 종료됩니다"));
    }
}

void CReaderSerialTestUIDlg::DestroyFieldPanelControls(int slot)
{
    CommandPanel& panel = m_commandPanels[slot];

    for (CEdit* edit : panel.fieldEdits)
    {
        if (edit != nullptr)
        {
            edit->DestroyWindow();
            delete edit;
        }
    }
    panel.fieldEdits.clear();

    for (CStatic* label : panel.fieldLabels)
    {
        if (label != nullptr)
        {
            label->DestroyWindow();
            delete label;
        }
    }
    panel.fieldLabels.clear();
}

// slot(0=A, 1=B)에서 선택된 commandCode의 필드 스펙(CommandFieldSpecs.h)에
// 맞춰 그 슬롯의 라벨+EditText를 동적으로 만든다. 다이얼로그 리소스에는
// 명령별 컨트롤을 두지 않는다 — SPEC 명령이 추가되더라도 CommandFieldSpecs.cpp
// 테이블만 늘리면 되도록 하기 위함(사용자 지시).
//
// P7-12/P10-1: 모든 라벨/EditText는 다이얼로그가 아니라 그 슬롯의
// fieldPanelWnd(고정 크기 뷰포트)의 자식으로 생성된다 — 좌표는 뷰포트
// 좌상단(fieldPanelRectPx) 기준 상대 좌표다. 필드가 뷰포트 높이를 넘치면
// 다이얼로그를 키우는 대신 그 슬롯의 fieldScrollBar로 스크롤한다. 두 슬롯의
// 뷰포트는 서로 다른 위치에 고정되어 있으므로, 한 슬롯의 필드 개수가 많아져도
// 다른 슬롯이나 IDC_LIST_LOG 위치에는 영향을 주지 않는다.
void CReaderSerialTestUIDlg::RebuildFieldPanel(int slot, unsigned char commandCode)
{
    DestroyFieldPanelControls(slot);

    CommandPanel& panel = m_commandPanels[slot];
    panel.currentCommandCode = commandCode;
    panel.currentFieldSpecs = GetCommandFieldSpecs(commandCode);

    // 가로 좌표/폭은 행마다 동일하므로 한 번만 dlu->px 변환한다. 세로는
    // 라벨 줄바꿈 여부에 따라 행마다 실제 필요 높이가 달라지므로, 아래에서
    // 누적 픽셀 오프셋(currentTopPx)으로 직접 관리한다.
    //
    // (P10-1 버그수정) kFieldLabelXDlu/kFieldEditXDlu는 뷰포트 좌상단을
    // 기준으로 한 "상대" 여백이다 — 뷰포트 자체의 다이얼로그 절대 좌표
    // (panel.fieldPanelRectPx.left, 슬롯마다 다름: 슬롯0≈x=7dlu,
    // 슬롯1≈x=333dlu)를 빼서 상대화하면 안 된다. 두 dlu 값 모두 (0,0)을
    // 원점으로 매핑해야 슬롯에 무관하게 같은 상대 오프셋이 나온다 — 이전
    // 코드처럼 절대 좌표 기준으로 매핑한 뒤 뷰포트의 절대 left를 빼면,
    // 뷰포트 자체가 이미 절대 좌표만큼 이동해 있으므로 슬롯1에서는 큰 음수
    // 오프셋이 되어 자식 컨트롤이 뷰포트 밖(왼쪽 화면 밖)으로 밀려나
    // "안 보이는" 버그가 났다.
    CRect labelLeftDlu(0, 0, kFieldLabelXDlu, 0);
    MapDialogRect(&labelLeftDlu);
    const int labelLeftPx = labelLeftDlu.right;

    CRect labelWidthDlu(0, 0, kFieldLabelWidthDlu, 0);
    MapDialogRect(&labelWidthDlu);
    const int labelWidthPx = labelWidthDlu.right;

    CRect editLeftDlu(0, 0, kFieldEditXDlu, 0);
    MapDialogRect(&editLeftDlu);
    const int editLeftPx = editLeftDlu.right;

    CRect editWidthDlu(0, 0, kFieldEditWidthDlu, 0);
    MapDialogRect(&editWidthDlu);
    const int editWidthPx = editWidthDlu.right;

    // 뷰포트 내부 상대 좌표이므로 첫 행은 0에서 시작한다(다이얼로그 기준
    // kFieldPanelTopDlu 오프셋은 fieldPanelWnd 자체의 위치가 이미 담당).
    int currentTopPx = 0;

    CRect rowHeightDlu(0, 0, 0, kFieldControlHeightDlu);
    MapDialogRect(&rowHeightDlu);
    const int singleLineHeightPx = rowHeightDlu.bottom;

    CRect gapDlu(0, 0, 0, kFieldRowGapDlu);
    MapDialogRect(&gapDlu);
    const int rowGapPx = gapDlu.bottom;

    // 슬롯별 ID 대역 시작점(위 kFieldIdSlotStride 주석 참조) — 라벨/에디트 모두
    // 이 오프셋만큼 밀어서 슬롯0/슬롯1의 동적 컨트롤이 전체 대화상자 범위에서도
    // 서로 다른 Win32 컨트롤 ID(=AutomationId)를 갖게 한다.
    const UINT slotIdOffset = static_cast<UINT>(slot) * kFieldIdSlotStride;

    if (panel.currentFieldSpecs.empty())
    {
        CRect labelPx(labelLeftPx, currentTopPx, labelLeftPx + labelWidthPx, currentTopPx + singleLineHeightPx);

        CStatic* label = new CStatic();
        label->Create(_T("이 명령은 Data 필드가 없습니다."),
            WS_CHILD | WS_VISIBLE, labelPx, &panel.fieldPanelWnd, kFieldLabelIdBase + slotIdOffset);
        label->SetFont(GetFont());
        panel.fieldLabels.push_back(label);

        panel.fieldContentHeightPx = singleLineHeightPx;
        panel.fieldScrollBar.EnableScrollBar(ESB_DISABLE_BOTH);
        panel.fieldScrollPos = 0;
        return;
    }

    const int rowCount = min(static_cast<int>(panel.currentFieldSpecs.size()), kMaxFieldRows);
    for (int i = 0; i < rowCount; ++i)
    {
        const FieldSpec& spec = panel.currentFieldSpecs[i];

        CString labelText = spec.label;

        // 라벨이 폭 안에서 몇 줄로 줄바꿈될지 DT_CALCRECT로 먼저 측정해,
        // 두 줄 이상이 되어도 아래쪽이 잘리지 않도록 라벨 높이를 그 결과에
        // 맞춘다. 다음 행(EditText, 다음 라벨)은 이 실제 높이만큼 밀려난다.
        CRect calcRect(0, 0, labelWidthPx, 0);
        {
            CClientDC dc(this);
            CFont* oldFont = dc.SelectObject(GetFont());
            dc.DrawText(labelText, &calcRect, DT_CALCRECT | DT_WORDBREAK | DT_LEFT);
            dc.SelectObject(oldFont);
        }
        const int labelHeightPx = max(calcRect.Height(), singleLineHeightPx);
        const int rowHeightPx = max(labelHeightPx, singleLineHeightPx);

        CRect labelPx(labelLeftPx, currentTopPx, labelLeftPx + labelWidthPx, currentTopPx + labelHeightPx);

        CStatic* label = new CStatic();
        label->Create(labelText, WS_CHILD | WS_VISIBLE, labelPx, &panel.fieldPanelWnd, kFieldLabelIdBase + slotIdOffset + i);
        label->SetFont(GetFont());
        panel.fieldLabels.push_back(label);

        CRect editPx(editLeftPx, currentTopPx, editLeftPx + editWidthPx, currentTopPx + singleLineHeightPx);

        CEdit* edit = new CEdit();
        edit->Create(WS_CHILD | WS_VISIBLE | WS_BORDER | ES_AUTOHSCROLL, editPx, &panel.fieldPanelWnd, kFieldEditIdBase + slotIdOffset + i);
        edit->SetFont(GetFont());
        edit->SetWindowText(spec.defaultValue);
        panel.fieldEdits.push_back(edit);

        currentTopPx += rowHeightPx + rowGapPx;
    }

    panel.fieldContentHeightPx = currentTopPx;
    panel.fieldScrollPos = 0;

    const int panelHeightPx = panel.fieldPanelRectPx.Height();
    const int maxScrollPx = max(0, panel.fieldContentHeightPx - panelHeightPx);
    if (maxScrollPx > 0)
    {
        panel.fieldScrollBar.EnableScrollBar(ESB_ENABLE_BOTH);

        // SetScrollRange만 쓰면 썸(thumb) 크기가 항상 최소 크기로 고정된다.
        // SIF_PAGE를 채워 SetScrollInfo를 써야 뷰포트/콘텐츠 비율에 맞는
        // 썸 크기가 그려진다 — range 끝은 콘텐츠 전체 높이(-1)로 주고
        // nPage로 뷰포트 높이를 알려주면 Windows가 비율을 계산한다.
        SCROLLINFO si = {};
        si.cbSize = sizeof(si);
        si.fMask = SIF_RANGE | SIF_PAGE | SIF_POS;
        si.nMin = 0;
        si.nMax = panel.fieldContentHeightPx - 1;
        si.nPage = panelHeightPx;
        si.nPos = 0;
        panel.fieldScrollBar.SetScrollInfo(&si, TRUE);
    }
    else
    {
        panel.fieldScrollBar.EnableScrollBar(ESB_DISABLE_BOTH);
        panel.fieldScrollBar.SetScrollRange(0, 0, FALSE);
    }
}

// slot의 fieldScrollBar가 보낸 새 스크롤 위치(px)로 그 슬롯 뷰포트 내용을
// 이동시킨다. ScrollWindowEx(SW_SCROLLCHILDREN)를 써서 fieldPanelWnd의 모든
// 자식(라벨/EditText)을 한 번에 옮긴다 — MoveWindow를 필드마다 호출할 필요가 없다.
void CReaderSerialTestUIDlg::ScrollFieldPanel(int slot, int newPos)
{
    CommandPanel& panel = m_commandPanels[slot];

    // GetScrollRange()는 SetScrollInfo(SIF_PAGE)로 설정한 nMax(콘텐츠 높이-1)를
    // 그대로 반환하므로 그걸 상한으로 쓰면 뷰포트 아래로 빈 공간만큼 더
    // 스크롤되어 버린다. 실제 상한은 "콘텐츠 높이 - 뷰포트 높이"다.
    const int minPos = 0;
    const int maxPos = max(0, panel.fieldContentHeightPx - panel.fieldPanelRectPx.Height());
    newPos = max(minPos, min(maxPos, newPos));

    const int dy = panel.fieldScrollPos - newPos;
    if (dy != 0)
    {
        ::ScrollWindowEx(panel.fieldPanelWnd.GetSafeHwnd(), 0, dy, nullptr, nullptr, nullptr, nullptr,
            SW_SCROLLCHILDREN | SW_INVALIDATE);
    }

    panel.fieldScrollPos = newPos;
    panel.fieldScrollBar.SetScrollPos(newPos, TRUE);
}

// P17-1: 핀패드 필드 패널 세 함수(DestroyPinpadFieldPanelControls/
// RebuildPinpadFieldPanel/ScrollPinpadFieldPanel)는 리더기용
// (DestroyFieldPanelControls/RebuildFieldPanel/ScrollFieldPanel)과 완전히
// 같은 레이아웃 로직을 쓴다 — 차이는 필드 스펙 타입(PinpadFieldSpec)과
// ID 대역(kPinpadFieldLabelIdBase/kPinpadFieldEditIdBase)뿐이다.
void CReaderSerialTestUIDlg::DestroyPinpadFieldPanelControls(int slot)
{
    PinpadCommandPanel& panel = m_pinpadPanels[slot];

    for (CEdit* edit : panel.fieldEdits)
    {
        if (edit != nullptr)
        {
            edit->DestroyWindow();
            delete edit;
        }
    }
    panel.fieldEdits.clear();

    for (CStatic* label : panel.fieldLabels)
    {
        if (label != nullptr)
        {
            label->DestroyWindow();
            delete label;
        }
    }
    panel.fieldLabels.clear();
}

void CReaderSerialTestUIDlg::RebuildPinpadFieldPanel(int slot, PinpadCommandCode commandCode)
{
    DestroyPinpadFieldPanelControls(slot);

    PinpadCommandPanel& panel = m_pinpadPanels[slot];
    panel.currentCommandCode = static_cast<unsigned char>(commandCode);
    panel.currentFieldSpecs = GetPinpadCommandFieldSpecs(commandCode);

    CRect labelLeftDlu(0, 0, kFieldLabelXDlu, 0);
    MapDialogRect(&labelLeftDlu);
    const int labelLeftPx = labelLeftDlu.right;

    CRect labelWidthDlu(0, 0, kFieldLabelWidthDlu, 0);
    MapDialogRect(&labelWidthDlu);
    const int labelWidthPx = labelWidthDlu.right;

    CRect editLeftDlu(0, 0, kFieldEditXDlu, 0);
    MapDialogRect(&editLeftDlu);
    const int editLeftPx = editLeftDlu.right;

    CRect editWidthDlu(0, 0, kFieldEditWidthDlu, 0);
    MapDialogRect(&editWidthDlu);
    const int editWidthPx = editWidthDlu.right;

    int currentTopPx = 0;

    CRect rowHeightDlu(0, 0, 0, kFieldControlHeightDlu);
    MapDialogRect(&rowHeightDlu);
    const int singleLineHeightPx = rowHeightDlu.bottom;

    CRect gapDlu(0, 0, 0, kFieldRowGapDlu);
    MapDialogRect(&gapDlu);
    const int rowGapPx = gapDlu.bottom;

    const UINT slotIdOffset = static_cast<UINT>(slot) * kFieldIdSlotStride;

    if (panel.currentFieldSpecs.empty())
    {
        CRect labelPx(labelLeftPx, currentTopPx, labelLeftPx + labelWidthPx, currentTopPx + singleLineHeightPx);

        CStatic* label = new CStatic();
        label->Create(_T("이 명령은 Data 필드가 없습니다."),
            WS_CHILD | WS_VISIBLE, labelPx, &panel.fieldPanelWnd, kPinpadFieldLabelIdBase + slotIdOffset);
        label->SetFont(GetFont());
        panel.fieldLabels.push_back(label);

        panel.fieldContentHeightPx = singleLineHeightPx;
        panel.fieldScrollBar.EnableScrollBar(ESB_DISABLE_BOTH);
        panel.fieldScrollPos = 0;
        return;
    }

    const int rowCount = min(static_cast<int>(panel.currentFieldSpecs.size()), kMaxFieldRows);
    for (int i = 0; i < rowCount; ++i)
    {
        const PinpadFieldSpec& spec = panel.currentFieldSpecs[i];

        CString labelText = spec.label;

        CRect calcRect(0, 0, labelWidthPx, 0);
        {
            CClientDC dc(this);
            CFont* oldFont = dc.SelectObject(GetFont());
            dc.DrawText(labelText, &calcRect, DT_CALCRECT | DT_WORDBREAK | DT_LEFT);
            dc.SelectObject(oldFont);
        }
        const int labelHeightPx = max(calcRect.Height(), singleLineHeightPx);
        const int rowHeightPx = max(labelHeightPx, singleLineHeightPx);

        CRect labelPx(labelLeftPx, currentTopPx, labelLeftPx + labelWidthPx, currentTopPx + labelHeightPx);

        CStatic* label = new CStatic();
        label->Create(labelText, WS_CHILD | WS_VISIBLE, labelPx, &panel.fieldPanelWnd, kPinpadFieldLabelIdBase + slotIdOffset + i);
        label->SetFont(GetFont());
        panel.fieldLabels.push_back(label);

        CRect editPx(editLeftPx, currentTopPx, editLeftPx + editWidthPx, currentTopPx + singleLineHeightPx);

        CEdit* edit = new CEdit();
        edit->Create(WS_CHILD | WS_VISIBLE | WS_BORDER | ES_AUTOHSCROLL, editPx, &panel.fieldPanelWnd, kPinpadFieldEditIdBase + slotIdOffset + i);
        edit->SetFont(GetFont());
        edit->SetWindowText(spec.defaultValue);
        panel.fieldEdits.push_back(edit);

        currentTopPx += rowHeightPx + rowGapPx;
    }

    panel.fieldContentHeightPx = currentTopPx;
    panel.fieldScrollPos = 0;

    const int panelHeightPx = panel.fieldPanelRectPx.Height();
    const int maxScrollPx = max(0, panel.fieldContentHeightPx - panelHeightPx);
    if (maxScrollPx > 0)
    {
        panel.fieldScrollBar.EnableScrollBar(ESB_ENABLE_BOTH);

        SCROLLINFO si = {};
        si.cbSize = sizeof(si);
        si.fMask = SIF_RANGE | SIF_PAGE | SIF_POS;
        si.nMin = 0;
        si.nMax = panel.fieldContentHeightPx - 1;
        si.nPage = panelHeightPx;
        si.nPos = 0;
        panel.fieldScrollBar.SetScrollInfo(&si, TRUE);
    }
    else
    {
        panel.fieldScrollBar.EnableScrollBar(ESB_DISABLE_BOTH);
        panel.fieldScrollBar.SetScrollRange(0, 0, FALSE);
    }
}

void CReaderSerialTestUIDlg::ScrollPinpadFieldPanel(int slot, int newPos)
{
    PinpadCommandPanel& panel = m_pinpadPanels[slot];

    const int minPos = 0;
    const int maxPos = max(0, panel.fieldContentHeightPx - panel.fieldPanelRectPx.Height());
    newPos = max(minPos, min(maxPos, newPos));

    const int dy = panel.fieldScrollPos - newPos;
    if (dy != 0)
    {
        ::ScrollWindowEx(panel.fieldPanelWnd.GetSafeHwnd(), 0, dy, nullptr, nullptr, nullptr, nullptr,
            SW_SCROLLCHILDREN | SW_INVALIDATE);
    }

    panel.fieldScrollPos = newPos;
    panel.fieldScrollBar.SetScrollPos(newPos, TRUE);
}

void CReaderSerialTestUIDlg::OnCbnSelchangeComboCommandA()
{
    const int sel = m_commandPanels[0].commandCombo.GetCurSel();
    if (sel < 0 || sel >= static_cast<int>(m_commandCodesByComboIndex.size()))
    {
        return;
    }
    RebuildFieldPanel(0, m_commandCodesByComboIndex[sel]);
}

void CReaderSerialTestUIDlg::OnCbnSelchangeComboCommandB()
{
    const int sel = m_commandPanels[1].commandCombo.GetCurSel();
    if (sel < 0 || sel >= static_cast<int>(m_commandCodesByComboIndex.size()))
    {
        return;
    }
    RebuildFieldPanel(1, m_commandCodesByComboIndex[sel]);
}

// P17-1: 핀패드 명령 콤보박스도 리더기 콤보(OnCbnSelchangeComboCommandA/B)와
// 동일한 패턴 — 콤보 변경 시 그 슬롯의 핀패드 필드 패널만 재생성한다.
void CReaderSerialTestUIDlg::OnCbnSelchangeComboPinpadA()
{
    const int sel = m_pinpadPanels[0].commandCombo.GetCurSel();
    if (sel < 0 || sel >= static_cast<int>(m_pinpadCommandCodesByComboIndex.size()))
    {
        return;
    }
    RebuildPinpadFieldPanel(0, m_pinpadCommandCodesByComboIndex[sel]);
}

void CReaderSerialTestUIDlg::OnCbnSelchangeComboPinpadB()
{
    const int sel = m_pinpadPanels[1].commandCombo.GetCurSel();
    if (sel < 0 || sel >= static_cast<int>(m_pinpadCommandCodesByComboIndex.size()))
    {
        return;
    }
    RebuildPinpadFieldPanel(1, m_pinpadCommandCodesByComboIndex[sel]);
}

// slot(0=A, 1=B) 필드 패널의 EditText 값을 SPEC 필드 순서/폭 그대로 구분자
// 없이 concat해 Data를 만들고 전송 미리보기를 로그에 남긴다. '/'로 구분해
// 하나의 Edit에 넣던 이전 방식(P7-9/P7-10)은 잘못된 전제였으므로 제거했다 —
// SPEC 전문 자체에는 필드 구분자가 없다(reader-spec-expert 재확인).
// 리더기 1/2 개별 전송 버튼과 페일오버 전송 버튼이 이 buffer 구성 로직을
// 슬롯별로 독립 호출해 공유하고, 실제 전송 대상(SendToReader vs
// BroadcastFailover)만 갈라진다.
bool CReaderSerialTestUIDlg::BuildAndLogSendBuffer(int slot, unsigned char* buffer, size_t bufferCapacity, int& dataLength, CString& label)
{
    CommandPanel& panel = m_commandPanels[slot];
    label = GetCommandDisplayName(panel.currentCommandCode);

    dataLength = 0;
    CString fieldLog;

    for (size_t i = 0; i < panel.currentFieldSpecs.size() && i < panel.fieldEdits.size(); ++i)
    {
        const FieldSpec& spec = panel.currentFieldSpecs[i];
        CString editText;
        panel.fieldEdits[i]->GetWindowText(editText);

        if (spec.kind == FieldKind::FIXED)
        {
            // width는 byte 단위(SPEC X(n)) — CString의 wchar_t 코드유닛을 그대로
            // 1byte로 캐스팅하면 한글이 깨지므로 CP949로 인코딩한 뒤 byte 단위로
            // 패딩/절단한다(과거 버그 수정).
            unsigned char fieldBytes[512];
            const int width = min(spec.width, static_cast<int>(sizeof(fieldBytes)));
            PadFixedFieldBytes(editText, width, spec.pad, fieldBytes);
            for (int b = 0; b < width && dataLength < static_cast<int>(bufferCapacity); ++b)
            {
                buffer[dataLength++] = fieldBytes[b];
            }
            fieldLog += spec.label + _T("=\"") + editText + _T("\" ");
        }
        else if (spec.kind == FieldKind::LENGTH_PREFIXED)
        {
            // 길이 프리픽스도 byte 길이를 뜻하므로 payload를 CP949로 인코딩한
            // 뒤의 byte 수를 기준으로 계산한다.
            unsigned char fieldBytes[512];
            int usedPayloadBytes = 0;
            const int written = BuildLengthPrefixedFieldBytes(editText, spec.width, fieldBytes, sizeof(fieldBytes), usedPayloadBytes);
            for (int b = 0; b < written && dataLength < static_cast<int>(bufferCapacity); ++b)
            {
                buffer[dataLength++] = fieldBytes[b];
            }
            CString payloadByteNote;
            payloadByteNote.Format(_T("=\"%s\"(%dbyte) "), editText.GetString(), usedPayloadBytes);
            fieldLog += spec.label + payloadByteNote;
        }
        else // HEX_BINARY
        {
            unsigned char bytes[256];
            const int byteWidth = min(spec.width, static_cast<int>(sizeof(bytes)));
            CString hexFailReason;
            if (!HexStringToBytes(editText, byteWidth, bytes, hexFailReason))
            {
                CString errMsg;
                errMsg.Format(_T("[%s 전송 중단] 필드 \"%s\" 값 \"%s\" 파싱 실패: %s"),
                    label.GetString(), spec.label.GetString(), editText.GetString(), hexFailReason.GetString());
                AppendLog(errMsg);
                dataLength = 0;
                return false;
            }
            for (int b = 0; b < byteWidth && dataLength < static_cast<int>(bufferCapacity); ++b)
            {
                buffer[dataLength++] = bytes[b];
            }
            fieldLog += spec.label + _T("=[hex ") + editText + _T("] ");
        }
    }

    // 응답 CALLBACK 로그(OnReaderEvent)와 동일하게, byte를 그대로 1문자로
    // 이어붙인 raw ASCII로 표시한다. 조합형 변환(DLL 내부, MessageFieldTransform)
    // 이후 바이트라 CP949로 정상 디코딩되지 않아 글자가 깨져 보일 수 있지만,
    // hex dump가 아닌 raw 문자 표시를 사용자가 요청했다.
    // OnReaderEvent와 같은 이유로 NUL은 화면 표시용 대체 기호(␀)로 바꾼다 —
    // 그대로 두면 Edit 컨트롤/Format의 null-종단 규약 때문에 그 지점에서
    // 이후 바이트가 전부 잘려 보인다.
    CString sendPreview;
    for (int i = 0; i < dataLength; ++i)
    {
        sendPreview += (buffer[i] == 0) ? _T('␀') : static_cast<TCHAR>(buffer[i]);
    }
    // 응답 로그(OnReaderEvent)와 같은 이유로, embedded NUL이 있는 sendPreview를
    // Format의 "%s"에 넣으면 잘려 보이므로 접두부만 Format하고 += 로 이어붙인다.
    CString previewLog;
    previewLog.Format(_T("[%s 전송Data] len=%d data="), label.GetString(), dataLength);
    previewLog += sendPreview.IsEmpty() ? CString(_T("(none)")) : sendPreview;
    AppendLog(previewLog);
    return true;
}

// index(0=A, 1=B) 리더기 한 대에만 전송한다. BroadcastCommand와 달리
// 브로드캐스트 라운드가 아니므로 m_broadcastRound는 건드리지 않는다 —
// OnReaderEvent는 participated[index]가 false인 이상 이 응답을 브로드캐스트
// 판정 로직에 넣지 않는다.
//
// P10-1b: 미연결/portError를 이유로 여기서 조기 반환하지 않는다 —
// Reader_IsPortOpen 사전 체크 없이 SendCommandSafe로 Send를 우선 시도하고,
// readerId가 없거나 포트 계열 에러면 SendCommandSafe 내부에서 자동으로
// Open(또는 Close 후 재오픈)한 뒤 재시도한다(DOC/개발문서/실행계획서.md P10-1b).
void CReaderSerialTestUIDlg::SendToReader(int index, unsigned char commandCode, const unsigned char* data, int dataLength, const CString& label)
{
    const int result = SendCommandSafe(index, commandCode, data, dataLength);
    CString msg;
    msg.Format(_T("[%s] %s(readerId=%d, COM%d) -> result=%s"),
        label.GetString(), ReaderTag(index).GetString(), m_readers[index].readerId, m_readers[index].comPort,
        FormatResult(result).GetString());
    AppendLog(msg);
}

// 리더기 1(A)에만, 패널 1의 세팅으로 개별 전송한다. 무효화 없음(요구사항 3).
void CReaderSerialTestUIDlg::OnBnClickedButtonSendReader1()
{
    unsigned char buffer[1024];
    int dataLength = 0;
    CString label;
    if (!BuildAndLogSendBuffer(0, buffer, sizeof(buffer), dataLength, label))
    {
        return;
    }
    SendToReader(0, m_commandPanels[0].currentCommandCode, dataLength > 0 ? buffer : nullptr, dataLength, label);
}

// 리더기 2(B)에만, 패널 2의 세팅으로 개별 전송한다. 무효화 없음(요구사항 3).
void CReaderSerialTestUIDlg::OnBnClickedButtonSendReader2()
{
    unsigned char buffer[1024];
    int dataLength = 0;
    CString label;
    if (!BuildAndLogSendBuffer(1, buffer, sizeof(buffer), dataLength, label))
    {
        return;
    }
    SendToReader(1, m_commandPanels[1].currentCommandCode, dataLength > 0 ? buffer : nullptr, dataLength, label);
}

// 패널 1의 명령을 리더기 A에, 패널 2의 명령을 리더기 B에 동시에 전송한다
// (요구사항 3). 두 패널의 명령은 같아도 다를 수도 있다 — 리더기 이중화
// 대기 시나리오에서 리더기 기종별 지원 명령이 달라 서로 다른 명령을
// 걸어둬야 하는 경우가 실제로 있다(예: 리더기A=0x67 IC카드리딩,
// 리더기B=0x2B 거래정보). 연결 안 된 슬롯은 그 슬롯만 건너뛰고, 둘 다
// 미연결이면 BroadcastFailover 내부에서 안내 로그만 남기고 아무 것도
// 보내지 않는다.
void CReaderSerialTestUIDlg::OnBnClickedButtonSendFailover()
{
    unsigned char buffers[kReaderSlotCount][1024];
    int dataLengths[kReaderSlotCount] = { 0, 0 };
    CString labels[kReaderSlotCount];
    unsigned char commandCodes[kReaderSlotCount] = { 0, 0 };
    const unsigned char* dataPtrs[kReaderSlotCount] = { nullptr, nullptr };
    // 2026-08-07: HEX_BINARY 필드 파싱에 실패한 슬롯은 이 라운드에서 전송하지
    // 않는다(잘못된 hex 문자가 조용히 0으로 치환되어 실장비로 나가는 것을 막기
    // 위함) - 다른 슬롯이 유효하면 그 슬롯은 그대로 전송한다.
    bool validSlot[kReaderSlotCount] = { false, false };

    for (int slot = 0; slot < kReaderSlotCount; ++slot)
    {
        validSlot[slot] = BuildAndLogSendBuffer(slot, buffers[slot], sizeof(buffers[slot]), dataLengths[slot], labels[slot]);
        commandCodes[slot] = m_commandPanels[slot].currentCommandCode;
        dataPtrs[slot] = (validSlot[slot] && dataLengths[slot] > 0) ? buffers[slot] : nullptr;
    }

    BroadcastFailover(commandCodes, dataPtrs, dataLengths, labels, validSlot);
}

// P17-1: 핀패드 필드 패널의 EditText 값을 PinpadFieldSpecs 순서 그대로
// 구분자 없이 concat해 Data를 만든다.
// 2026-08-10: Line1/Line2(TEXT_LINE) 입력 필드가 제거되어 DECIMAL_BYTE/
// HEX_BINARY만 남았다 — DLL이 항상 명령별 기본 문구만 쓰도록 바뀌었다
// (PinpadPinCommands.cpp/PinpadMessageText.h 참조).
bool CReaderSerialTestUIDlg::BuildAndLogPinpadSendBuffer(int slot, unsigned char* buffer, size_t bufferCapacity, int& dataLength, CString& label)
{
    PinpadCommandPanel& panel = m_pinpadPanels[slot];
    label = GetPinpadCommandDisplayName(static_cast<PinpadCommandCode>(panel.currentCommandCode));

    dataLength = 0;
    CString fieldLog;

    for (size_t i = 0; i < panel.currentFieldSpecs.size() && i < panel.fieldEdits.size(); ++i)
    {
        const PinpadFieldSpec& spec = panel.currentFieldSpecs[i];
        CString editText;
        panel.fieldEdits[i]->GetWindowText(editText);

        if (spec.kind == PinpadFieldKind::DECIMAL_BYTE)
        {
            int value = _ttoi(editText);
            value = max(0, min(255, value));
            if (dataLength < static_cast<int>(bufferCapacity))
            {
                buffer[dataLength++] = static_cast<unsigned char>(value);
            }
            fieldLog += spec.label + _T("=") + editText + _T(" ");
        }
        else if (spec.kind == PinpadFieldKind::HEX_BYTE)
        {
            unsigned char b[1];
            CString hexFailReason;
            if (!HexStringToBytes(editText, 1, b, hexFailReason))
            {
                CString errMsg;
                errMsg.Format(_T("[핀패드][%s 전송 중단] 필드 \"%s\" 값 \"%s\" 파싱 실패: %s"),
                    label.GetString(), spec.label.GetString(), editText.GetString(), hexFailReason.GetString());
                AppendLog(errMsg);
                dataLength = 0;
                return false;
            }
            if (dataLength < static_cast<int>(bufferCapacity))
            {
                buffer[dataLength++] = b[0];
            }
            fieldLog += spec.label + _T("=[hex ") + editText + _T("] ");
        }
        else if (spec.kind == PinpadFieldKind::HEX_BINARY)
        {
            unsigned char bytes[64];
            const int byteWidth = min(spec.width, static_cast<int>(sizeof(bytes)));
            CString hexFailReason;
            if (!HexStringToBytes(editText, byteWidth, bytes, hexFailReason))
            {
                CString errMsg;
                errMsg.Format(_T("[핀패드][%s 전송 중단] 필드 \"%s\" 값 \"%s\" 파싱 실패: %s"),
                    label.GetString(), spec.label.GetString(), editText.GetString(), hexFailReason.GetString());
                AppendLog(errMsg);
                dataLength = 0;
                return false;
            }
            for (int b = 0; b < byteWidth && dataLength < static_cast<int>(bufferCapacity); ++b)
            {
                buffer[dataLength++] = bytes[b];
            }
            fieldLog += spec.label + _T("=[hex ") + editText + _T("] ");
        }
    }

    CString sendPreview;
    for (int i = 0; i < dataLength; ++i)
    {
        sendPreview += (buffer[i] == 0) ? _T('␀') : static_cast<TCHAR>(buffer[i]);
    }
    CString previewLog;
    previewLog.Format(_T("[핀패드][%s 전송Data] len=%d data="), label.GetString(), dataLength);
    previewLog += sendPreview.IsEmpty() ? CString(_T("(none)")) : sendPreview;
    AppendLog(previewLog);
    return true;
}

// index(0=A, 1=B)에 대응하는 포트로 핀패드 명령을 전송한다. SendCommandSafe와
// 달리 자동 재연결을 하지 않는다 — 핀패드 명령 실패는 대개 조합 시퀀스
// 자체의 실패(NAK/타임아웃/Tamper 등)이지 포트가 끊긴 것이 아니며,
// Pinpad_SendCommand는 포트가 열려 있지 않으면 즉시 READER_ERR_PORT_NOT_OPEN을
// 반환할 뿐 자동으로 복구되지 않는다(리더기의 자동 재연결 정책과 마찬가지로
// PRD §19-18, 명시적 재오픈 필요).
void CReaderSerialTestUIDlg::SendToPinpad(int index, PinpadCommandCode commandCode, const unsigned char* data, int dataLength, const CString& label)
{
    if (!m_readers[index].connected)
    {
        AppendLog(_T("[핀패드] ") + ReaderTag(index)
            + _T(": 열려 있는 포트가 없습니다 - 먼저 그 슬롯의 \"열기\"로 포트를 여세요")
            + _T("(멀티패드는 리더기와 같은 포트, 별도 핀패드는 그 포트를 그대로 열면 됩니다)"));
        return;
    }

    const int result = Pinpad_SendCommand(m_readers[index].readerId, commandCode, data, dataLength);
    CString msg;
    msg.Format(_T("[핀패드][%s] %s(readerId=%d, COM%d) -> result=%s"),
        ReaderTag(index).GetString(), label.GetString(), m_readers[index].readerId, m_readers[index].comPort,
        FormatResult(result).GetString());
    AppendLog(msg);
}

// 리더기 1(A) 포트로 핀패드 패널1의 세팅을 전송한다.
void CReaderSerialTestUIDlg::OnBnClickedButtonSendPinpadA()
{
    unsigned char buffer[256];
    int dataLength = 0;
    CString label;
    if (!BuildAndLogPinpadSendBuffer(0, buffer, sizeof(buffer), dataLength, label))
    {
        return;
    }
    SendToPinpad(0, static_cast<PinpadCommandCode>(m_pinpadPanels[0].currentCommandCode),
        dataLength > 0 ? buffer : nullptr, dataLength, label);
}

// 리더기 2(B) 포트로 핀패드 패널2의 세팅을 전송한다.
void CReaderSerialTestUIDlg::OnBnClickedButtonSendPinpadB()
{
    unsigned char buffer[256];
    int dataLength = 0;
    CString label;
    if (!BuildAndLogPinpadSendBuffer(1, buffer, sizeof(buffer), dataLength, label))
    {
        return;
    }
    SendToPinpad(1, static_cast<PinpadCommandCode>(m_pinpadPanels[1].currentCommandCode),
        dataLength > 0 ? buffer : nullptr, dataLength, label);
}
