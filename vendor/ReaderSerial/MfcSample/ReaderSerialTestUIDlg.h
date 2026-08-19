// ReaderSerialTestUIDlg.h — 메인 다이얼로그 클래스 정의 (P6-2)
//
// CALLBACK(OnReaderCallback)은 리더기 수신 스레드에서 직접 호출되므로, 그 안에서
// UI 컨트롤을 조작하지 않는다. 대신 콜백 안에서 데이터를 복사한 뒤
// PostMessage(WM_APP_READER_EVENT)로 UI 스레드에 전달하고, OnReaderEvent 핸들러
// (UI 스레드에서 실행)에서만 ListBox를 갱신한다. (CLAUDE.md, PRD §7.6)
#pragma once

#include <afxwin.h>
#include <afxdialogex.h>
#include <vector>
#include "resource.h"
#include "ReaderSerial.h"
#include "CommandFieldSpecs.h"
#include "PinpadFieldSpecs.h"

// WM_APP_READER_EVENT의 LPARAM으로 전달되는 힙 할당 데이터. 핸들러가 처리 후
// delete한다. data는 CALLBACK 호출이 끝나면 DLL이 무효화하므로(PRD §8.2), 반드시
// CALLBACK 안에서 이 구조체로 복사해 두어야 한다.
struct ReaderEventData
{
    int readerId;
    int eventType;
    unsigned char commandCode;
    int dataLength;
    unsigned char data[4096];  // ReaderSerial의 MAX_FRAME_LENGTH(FrameBuilder.h)와 동일 — 전체 응답 Data를 담기 위함
};

#define WM_APP_READER_EVENT (WM_APP + 1)

// WM_APP_PINPAD_EVENT의 LPARAM으로 전달되는 힙 할당 데이터. PINPAD_CALLBACK은
// READER_CALLBACK과 이벤트 세분화 방식이 달라(commandCode의 의미가 다르다 —
// PinpadEventData 쪽은 항상 POS가 요청한 PinpadCommandCode 그대로 고정) 별도
// 구조체를 둔다. data 수명 규칙은 ReaderEventData와 동일 — CALLBACK 안에서 즉시 복사한다.
struct PinpadEventData
{
    int readerId;
    int eventType;
    unsigned char commandCode;  // 2026-08-12: result(ReaderResult) 대신 PinpadCommandCode
    int dataLength;
    unsigned char data[64];  // 핀패드 응답 최대 길이(SEED 32byte hex-ASCII)보다 넉넉히 확보
};

#define WM_APP_PINPAD_EVENT (WM_APP + 2)

// 2026-07-16 사용자 요청: POS 프로그램이 실제로 그러하듯, 한 프로세스가 리더기
// 2대(A/B)를 동시에 열어 다룰 수 있어야 한다. m_readerId 단일 필드를 배열로
// 일반화하는 대신, 포트 번호/연결 상태/오류 상태를 한데 묶은 슬롯 구조체로
// 관리한다(DOC/개발문서/실행계획서.md §4 참조).
constexpr int kReaderSlotCount = 2;

struct ReaderSlot
{
    int readerId = -1;
    int comPort = 0;
    bool connected = false;   // Reader_OpenPort 성공 후 Reader_ClosePort 전까지 true
    bool portError = false;   // READER_EVENT_RECEIVE_ERROR 수신 후 true — PRD §19-18(자동 재연결 없음)에
                              // 따라 반드시 Reader_ClosePort 후 재오픈해야 하므로, connected는 true로
                              // 유지한 채(닫기 버튼을 눌러야 함) 이 플래그만 별도로 표시한다.
};

// P10-1: 리더기 A/B는 각자 독립된 "명령 선택 콤보박스 + 필드 입력 패널"을
// 가진다 — 한쪽에서 명령을 바꿔도 다른 쪽 세팅이 보존되어야 하므로, 예전처럼
// 다이얼로그 전체에 하나만 두지 않고 슬롯(kReaderSlotCount)별로 완전히
// 분리해 소유한다. commandCombo는 리소스(.rc)의 실제 콤보 컨트롤에
// DDX_Control로 연결되고, fieldPanelWnd/fieldScrollBar는 OnInitDialog에서
// 슬롯별 위치에 런타임 생성된다(P7-12 고정 뷰포트 방식을 슬롯마다 그대로 적용).
struct CommandPanel
{
    CComboBox commandCombo;
    std::vector<FieldSpec> currentFieldSpecs;
    std::vector<CEdit*> fieldEdits;
    std::vector<CStatic*> fieldLabels;
    unsigned char currentCommandCode = 0;

    CStatic fieldPanelWnd;
    CScrollBar fieldScrollBar;
    CRect fieldPanelRectPx;
    int fieldScrollPos = 0;
    int fieldContentHeightPx = 0;
};

// P17-1: 핀패드 명령 패널. CommandPanel과 동일한 구조를 핀패드 명령 5종
// (PinpadFieldSpecs.h)에 대해 슬롯별로 병렬로 둔다 — 리더기 패널과 완전히
// 독립된 콤보/필드 패널/스크롤바를 가지며, 실제 전송 대상 포트(readerId)만
// m_readers[slot]을 그대로 공유한다(멀티패드 구성에서는 리더기 패널과 같은
// readerId, 별도 핀패드 포트 구성에서는 그 슬롯의 readerId만 사용됨).
struct PinpadCommandPanel
{
    CComboBox commandCombo;
    std::vector<PinpadFieldSpec> currentFieldSpecs;
    std::vector<CEdit*> fieldEdits;
    std::vector<CStatic*> fieldLabels;
    unsigned char currentCommandCode = 0;  // PinpadCommandCode 값

    CStatic fieldPanelWnd;
    CScrollBar fieldScrollBar;
    CRect fieldPanelRectPx;
    int fieldScrollPos = 0;
    int fieldContentHeightPx = 0;
};

// 페일오버 전송(요구사항 3번째 버튼)에 대해 "이번 라운드에 참여한 리더기 중
// 어느 쪽 최종 응답(READER_EVENT_RESPONSE)이 먼저 왔는지"를 UI 스레드
// (OnReaderEvent)에서만 추적한다. CALLBACK은 PostMessage로 이미 직렬화되어
// OnReaderEvent가 항상 UI 스레드에서 순차 실행되므로, 이 상태에는 별도 락이
// 필요 없다(기존 아키텍처 원칙 그대로 재사용).
//
// commandCode는 슬롯별 배열이다(P10-1) — 페일오버 전송은 패널 1의 명령을
// 리더기 A에, 패널 2의 명령을 리더기 B에 동시에 보내며 두 명령이 서로 다를
// 수 있으므로(예: 리더기 A=0x67 IC카드리딩, 리더기 B=0x2B 거래정보 — 리더기
// 기종별 지원 명령이 달라 실제로 발생하는 상황), 무효화 판정도 참가자 각자의
// 배열 항목을 기준으로 개별적으로 이뤄져야 한다(ReaderSerialTestUIDlg.cpp의
// OnReaderEvent 참조).
struct BroadcastRound
{
    bool active = false;
    bool participated[kReaderSlotCount] = { false, false };
    bool responded[kReaderSlotCount] = { false, false };
    int winnerIndex = -1;
    unsigned char commandCode[kReaderSlotCount] = { 0, 0 };
};

class CReaderSerialTestUIDlg : public CDialogEx
{
public:
    explicit CReaderSerialTestUIDlg(CWnd* pParent = nullptr);

    enum { IDD = IDD_READERSERIALTESTUI_DIALOG };

protected:
    virtual void DoDataExchange(CDataExchange* pDX);
    virtual BOOL OnInitDialog();

    afx_msg void OnBnClickedButtonOpenA();
    afx_msg void OnBnClickedButtonOpenB();
    afx_msg void OnBnClickedButtonCloseA();
    afx_msg void OnBnClickedButtonCloseB();
    afx_msg void OnBnClickedButtonStatus();
    afx_msg void OnBnClickedButtonInit();
    afx_msg void OnDestroy();
    afx_msg void OnSize(UINT nType, int cx, int cy);
    afx_msg void OnVScroll(UINT nSBCode, UINT nPos, CScrollBar* pScrollBar);
    afx_msg BOOL OnMouseWheel(UINT nFlags, short zDelta, CPoint pt);
    afx_msg LRESULT OnReaderEvent(WPARAM wParam, LPARAM lParam);
    afx_msg LRESULT OnPinpadEvent(WPARAM wParam, LPARAM lParam);

    // P10-1: 명령 선택 콤보박스는 슬롯(리더기 A/B)별로 독립이다. 콤보 변경 시
    // 그 슬롯의 필드 패널만 재생성한다(다른 슬롯의 세팅은 그대로 보존됨).
    afx_msg void OnCbnSelchangeComboCommandA();
    afx_msg void OnCbnSelchangeComboCommandB();

    // P17-1: 핀패드 명령 콤보박스도 슬롯별로 독립이다.
    afx_msg void OnCbnSelchangeComboPinpadA();
    afx_msg void OnCbnSelchangeComboPinpadB();
    afx_msg void OnBnClickedButtonSendPinpadA();
    afx_msg void OnBnClickedButtonSendPinpadB();

    // 전송 버튼 3종류(요구사항 3):
    //   - 리더기 1/2 전송: 각 슬롯 패널의 현재 세팅으로 해당 리더기 한 대에만
    //     전송한다. 무효화 없음.
    //   - 페일오버 전송: 패널 1의 명령을 리더기 A에, 패널 2의 명령을
    //     리더기 B에 동시에 전송하고, 먼저 응답한 쪽을 채택해 나머지를
    //     초기화(0x60)로 무효화한다.
    afx_msg void OnBnClickedButtonSendReader1();
    afx_msg void OnBnClickedButtonSendReader2();
    afx_msg void OnBnClickedButtonSendFailover();

    DECLARE_MESSAGE_MAP()

private:
    static void __stdcall OnReaderCallback(
        int readerId,
        int eventType,
        unsigned char commandCode,
        const unsigned char* data,
        int dataLength,
        void* userContext);

    // P17-1: PINPAD_CALLBACK — READER_CALLBACK과 3번째 파라미터의 의미가 다르다
    // (PinpadEventData 쪽 commandCode는 항상 POS가 요청한 PinpadCommandCode
    // 그대로 고정). 리더기 CALLBACK과 동일하게 핀패드 수신 스레드(시퀀스 엔진)에서
    // 직접 호출되므로, 여기서도 UI를 건드리지 않고 데이터만 복사해
    // PostMessage(WM_APP_PINPAD_EVENT)로 넘긴다.
    static void __stdcall OnPinpadCallback(
        int readerId,
        int eventType,
        unsigned char commandCode,
        const unsigned char* data,
        int dataLength,
        void* userContext);

    void AppendLog(const CString& text);
    void ClosePortIfOpen();

    // index(0=A, 1=B)에 해당하는 리더기를 열고/닫고, 상태 라벨을 갱신한다.
    void OpenReader(int index);
    void CloseReader(int index);
    void UpdateStatusLabel(int index);
    static CString ReaderTag(int index);
    int FindReaderIndexById(int readerId) const;

    // P10-1b POS 연동 권장 패턴: Reader_SendCommand를 직접 부르는 대신 이
    // 래퍼를 통해서만 호출한다. readerId가 없으면(최초 상태거나 이전
    // 복구까지 실패해 무효화된 상태) 먼저 Open을 시도하고, 이미 열려 있는
    // 상태에서 보낸 SendCommand가 포트 계열 에러(READER_ERR_PORT_NOT_OPEN)로
    // 실패하면 Close 후 재오픈해 한 번 더
    // 재시도한다. READER_ERR_BUSY 등 포트와 무관한 에러는 이미 진행 중인
    // 다른 명령이 있다는 뜻이므로 여기서 Close를 걸면 그 명령을 강제로
    // 죽이게 되어, 복구 대상에서 절대 제외한다. Reader_IsPortOpen을 사전
    // 체크로 쓰지 않고 Send를 우선 시도한 뒤 반환값으로만 분기한다
    // (DOC/개발문서/실행계획서.md P10-1b, 2026-07-31 설계 확정).
    //
    // 반환값은 최종적으로 시도된 Reader_SendCommand의 결과다. 다만 readerId가
    // 없는 상태에서 자동 Open 자체가 실패하면 Reader_SendCommand는 한 번도
    // 호출되지 않으므로, 그 경우엔 Reader_OpenPort의 실패 result를 그대로
    // 반환한다(호출자가 result만 보고 로그에 표시해도 실패 사유가 드러나게
    // 하기 위함).
    int SendCommandSafe(int slot, unsigned char commandCode, const unsigned char* data, int dataLength);

    // slot(0=A, 1=B)을 UI에 입력된 COM포트/보드레이트로 무조건 연다. 이미
    // 연결되어 있는지 확인하는 OpenReader(수동 "열기" 버튼용)와 달리, 이
    // 함수는 SendCommandSafe가 "readerId 없음" 또는 포트 계열 에러로 Close한
    // 직후에만 호출되므로 항상 닫힌/없는 상태에서 불린다. logPrefix는 로그
    // 줄 접두어로, 사용자가 수동 조작과 자동 복구를 로그만 보고 구분할 수
    // 있게 한다(예: "[자동복구] ").
    int TryAutoOpenReader(int slot, const CString& logPrefix);

    // 슬롯별로 서로 다를 수 있는 commandCode/data/dataLength를 각 슬롯에 맞는
    // 리더기(0=A, 1=B)에 동시에 전송하고 새 BroadcastRound를 시작한다.
    // 연결된(portError가 아닌) 리더기가 하나도 없으면 안내 로그만 남기고
    // 아무 것도 보내지 않는다. 특정 슬롯이 미연결/portError면 그 슬롯만
    // 건너뛴다(부분 참여). 직전 라운드가 아직 active(응답 대기 중)였다면 그
    // 추적은 폐기하고 로그로만 남긴다(테스트 도구이므로 과도한 방어 로직은
    // 두지 않는다 — DOC/개발문서/실행계획서.md §4 참조).
    // validSlot == nullptr이면 두 슬롯 모두 유효하다고 간주한다(기존 호출부
    // 호환용 - 예: OnBnClickedButtonInit는 hex 필드가 없는 고정 0x60 명령이라
    // 실패할 여지가 없다). validSlot[i] == false인 슬롯은 SendCommandSafe를
    // 호출하지 않고 건너뛴다(2026-08-07, BuildAndLogSendBuffer의 HEX_BINARY
    // 파싱 실패 전파용).
    void BroadcastFailover(
        const unsigned char commandCode[kReaderSlotCount],
        const unsigned char* const data[kReaderSlotCount],
        const int dataLength[kReaderSlotCount],
        const CString label[kReaderSlotCount],
        const bool validSlot[kReaderSlotCount] = nullptr);

    // slot(0=A, 1=B) 필드 패널의 EditText 값을 SPEC 필드 순서대로 concat해
    // buffer/dataLength를 만들고, 전송 미리보기를 로그에 남긴다. label에는
    // 그 슬롯에서 현재 선택된 명령의 표시 이름을 담아 돌려준다. 리더기 1/2
    // 개별 전송과 페일오버 전송이 모두 이 buffer 구성 로직을 슬롯별로
    // 독립 호출해 공유한다 — 실제 전송 대상만 달라진다.
    //
    // 반환값 false는 HEX_BINARY 필드 중 하나라도 파싱에 실패했음을 뜻한다
    // (2026-08-07 - 잘못된 hex 문자를 조용히 0으로 채워 전송하던 버그 수정).
    // 이 경우 실패 사유는 이미 AppendLog로 남겼고 dataLength=0이므로, 호출자는
    // 그 슬롯의 실제 전송(Reader_SendCommand 호출)을 하지 않아야 한다.
    bool BuildAndLogSendBuffer(int slot, unsigned char* buffer, size_t bufferCapacity, int& dataLength, CString& label);

    // index(0=A, 1=B) 리더기 한 대에만 commandCode/data를 전송한다. 해당
    // 슬롯이 미연결/portError 상태면 안내 로그만 남기고 아무 것도 보내지
    // 않는다. 페일오버가 아니므로 m_broadcastRound는 건드리지 않는다.
    void SendToReader(int index, unsigned char commandCode, const unsigned char* data, int dataLength, const CString& label);

    // slot(0=A, 1=B) 콤보박스에서 선택된 명령의 CommandFieldSpecs를 조회해
    // 필드별 라벨(CStatic)+EditText(CEdit)를 그 슬롯의 필드 패널
    // (m_commandPanels[slot].fieldPanelWnd)에 동적으로 생성/배치한다. 이전에
    // 만든 컨트롤은 먼저 전부 파괴한다. 필드가 없는 명령(0x62/0x63)은 안내
    // 문구 하나만 표시한다. 필드 개수가 뷰포트를 넘치면 그 슬롯의 스크롤바
    // (m_commandPanels[slot].fieldScrollBar)로만 접근된다(IDC_LIST_LOG는
    // 항상 고정 위치 유지).
    void RebuildFieldPanel(int slot, unsigned char commandCode);
    void DestroyFieldPanelControls(int slot);

    // m_commandPanels[slot]의 필드 패널 스크롤 위치를 newPos(px)로 이동시키고,
    // 그 안의 모든 자식(라벨/EditText)을 ScrollWindowEx(SW_SCROLLCHILDREN)로
    // 함께 옮긴다.
    void ScrollFieldPanel(int slot, int newPos);

    // P17-1: 핀패드 필드 패널도 리더기 필드 패널(RebuildFieldPanel/
    // DestroyFieldPanelControls/ScrollFieldPanel)과 동일한 패턴을 따르되,
    // PinpadFieldSpecs.h의 필드 스펙과 별도 ID 대역/뷰포트를 쓴다.
    void RebuildPinpadFieldPanel(int slot, PinpadCommandCode commandCode);
    void DestroyPinpadFieldPanelControls(int slot);
    void ScrollPinpadFieldPanel(int slot, int newPos);

    // slot(0=A, 1=B) 핀패드 필드 패널의 EditText 값을 PinpadFieldSpecs 순서
    // 그대로 구분자 없이 concat해 Data를 만들고 전송 미리보기를 로그에
    // 남긴다. 반환값 의미는 BuildAndLogSendBuffer(리더기용)와 동일
    // (HEX_BYTE/HEX_BINARY 파싱 실패 시 false, 호출자는 전송하지 않아야 한다).
    bool BuildAndLogPinpadSendBuffer(int slot, unsigned char* buffer, size_t bufferCapacity, int& dataLength, CString& label);

    // index(0=A, 1=B)에 대응하는 포트로 핀패드 명령을 전송한다. 그 슬롯의
    // 포트가 열려 있지 않으면(멀티패드든 별도 핀패드 포트든, Reader_OpenPort로
    // 먼저 열려 있어야 함) 안내 로그만 남기고 아무 것도 보내지 않는다 —
    // 핀패드 명령은 리더기 명령과 달리 SendCommandSafe의 자동 재연결
    // 대상이 아니다(핀패드 실패는 포트 상태와 무관한 조합 시퀀스 실패이므로).
    void SendToPinpad(int index, PinpadCommandCode commandCode, const unsigned char* data, int dataLength, const CString& label);

    ReaderSlot m_readers[kReaderSlotCount];
    CStatic m_statusLabels[kReaderSlotCount];
    BroadcastRound m_broadcastRound;

    CEdit m_logList;
    CRect m_logListInitRect;
    CSize m_dlgInitSize;

    // 콤보박스에 나열할 명령 코드 목록은 슬롯과 무관하게 동일하므로
    // (GetAllFieldCommandCodes) 한 벌만 공유한다. 슬롯별로 달라지는 것은
    // "그중 무엇이 현재 선택되어 있는가"(CommandPanel::currentCommandCode)뿐이다.
    std::vector<unsigned char> m_commandCodesByComboIndex;
    CommandPanel m_commandPanels[kReaderSlotCount];

    // P17-1: 핀패드 명령 콤보박스 목록(5종, 슬롯 무관 공유)과 슬롯별 패널.
    std::vector<PinpadCommandCode> m_pinpadCommandCodesByComboIndex;
    PinpadCommandPanel m_pinpadPanels[kReaderSlotCount];
};
