// MainForm.cs — ReaderSerial.dll C# P/Invoke 연동 최소 예제 (P10-2)
//
// 리더기 1대 연동 수준으로 만든다(2026-08-03 사용자 확정 — 2대 페일오버
// 시나리오는 이 예제 범위 밖). src/ReaderSerialTestUI(MFC)의 기능(19개 SPEC
// 명령 필드 입력, CALLBACK 로그, POS 연동 권장 패턴)을 C# WinForms로 재현한다.
//
// 디자이너(.resx/Designer.cs) 없이 코드로 컨트롤을 직접 만든다 — SDK 스타일
// csproj에서 굳이 리소스 파일을 곁들이지 않아도 예제로서 충분하고, 유지보수
// 시 diff가 더 명확하다.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ReaderSerialCSharpSample
{
    public sealed class MainForm : Form
    {
        // CALLBACK 델리게이트는 여기서 인스턴스 필드로 참조를 계속 유지한다.
        // .NET P/Invoke의 흔한 함정: 델리게이트를 임시 객체로 DllImport 호출에
        // 바로 넘기면, 그 델리게이트를 참조하는 관리 코드 쪽 루트가 없어 GC가
        // 회수할 수 있다 — 이후 네이티브 코드(리더기 수신 스레드)가 이미 해제된
        // 함수 포인터를 호출하게 되어 크래시로 이어진다. Reader_ClosePort까지
        // 살아있어야 하므로 필드로 계속 붙잡아 둔다.
        private readonly ReaderCallback _nativeCallback;

        // P17-2: PinpadCallback도 ReaderCallback과 동일한 이유로 인스턴스
        // 필드로 계속 참조를 유지한다(GC에 의한 콜백 델리게이트 조기 회수 방지).
        private readonly PinpadCallback _nativePinpadCallback;

        private int _readerId = -1;
        private bool _connected;

        private TextBox _portBox;
        private TextBox _baudBox;
        private Button _btnOpen;
        private Button _btnClose;
        private Label _statusLabel;
        private Button _btnStatus;
        private Button _btnInit;
        private ComboBox _commandCombo;
        private Panel _fieldPanel;
        private Button _btnSend;
        private ComboBox _pinpadCommandCombo;
        private Panel _pinpadFieldPanel;
        private Button _btnPinpadSend;
        private TextBox _logBox;

        private List<byte> _commandCodes;
        private List<FieldSpec> _currentFieldSpecs = new List<FieldSpec>();
        private byte _currentCommandCode;
        private readonly List<Label> _fieldLabels = new List<Label>();
        private readonly List<TextBox> _fieldEdits = new List<TextBox>();

        private List<PinpadCommandCode> _pinpadCommandCodes;
        private List<PinpadFieldSpec> _currentPinpadFieldSpecs = new List<PinpadFieldSpec>();
        private PinpadCommandCode _currentPinpadCommandCode;
        private readonly List<Label> _pinpadFieldLabels = new List<Label>();
        private readonly List<TextBox> _pinpadFieldEdits = new List<TextBox>();

        public MainForm()
        {
            _nativeCallback = OnReaderCallback;
            _nativePinpadCallback = OnPinpadCallback;
            BuildUi();
        }

        private void BuildUi()
        {
            Text = "ReaderSerial C# 연동 예제 (P10-2/P17-2)";
            ClientSize = new Size(760, 860);
            MinimumSize = new Size(640, 600);
            StartPosition = FormStartPosition.CenterScreen;

            // --- 연결 영역 ---
            var connGroup = new GroupBox { Text = "연결", Left = 10, Top = 10, Width = 730, Height = 90, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };

            var portLabel = new Label { Text = "COM 포트:", Left = 10, Top = 25, Width = 60 };
            _portBox = new TextBox { Left = 75, Top = 22, Width = 50, Text = "3" };
            var baudLabel = new Label { Text = "Baud Rate:", Left = 140, Top = 25, Width = 65 };
            _baudBox = new TextBox { Left = 210, Top = 22, Width = 70, Text = "115200" };

            _btnOpen = new Button { Text = "열기", Left = 300, Top = 20, Width = 70 };
            _btnOpen.Click += (s, e) => OpenReader();

            _btnClose = new Button { Text = "닫기", Left = 380, Top = 20, Width = 70 };
            _btnClose.Click += (s, e) => CloseReader();

            _statusLabel = new Label { Text = "미연결", Left = 10, Top = 55, Width = 700, Height = 20 };

            connGroup.Controls.AddRange(new Control[] { portLabel, _portBox, baudLabel, _baudBox, _btnOpen, _btnClose, _statusLabel });

            // --- 빠른 명령 영역 ---
            _btnStatus = new Button { Text = "상태 확인 (Reader_IsPortOpen)", Left = 10, Top = 108, Width = 220 };
            _btnStatus.Click += (s, e) => CheckStatus();

            _btnInit = new Button { Text = "초기화 요청 전송 (0x60)", Left = 240, Top = 108, Width = 200 };
            _btnInit.Click += (s, e) => SendInitRequest();

            // --- 명령 전송 영역 ---
            var sendGroup = new GroupBox { Text = "명령 전송", Left = 10, Top = 145, Width = 730, Height = 320, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };

            var comboLabel = new Label { Text = "명령:", Left = 10, Top = 25, Width = 40 };
            _commandCombo = new ComboBox { Left = 55, Top = 22, Width = 400, DropDownStyle = ComboBoxStyle.DropDownList };
            _commandCombo.SelectedIndexChanged += (s, e) => OnCommandSelectionChanged();

            _btnSend = new Button { Text = "전송", Left = 465, Top = 20, Width = 90 };
            _btnSend.Click += (s, e) => SendSelectedCommand();

            // 필드가 많은 명령(0x2B 거래정보=13개)은 세로로 넘치므로 AutoScroll
            // 패널에 담는다 — MFC 버전은 CScrollBar를 직접 다뤄야 했지만
            // WinForms Panel.AutoScroll이 이를 대신해 줘서 훨씬 단순하다.
            _fieldPanel = new Panel
            {
                Left = 10,
                Top = 55,
                Width = 705,
                Height = 250,
                AutoScroll = true,
                BorderStyle = BorderStyle.FixedSingle,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            };

            sendGroup.Controls.AddRange(new Control[] { comboLabel, _commandCombo, _btnSend, _fieldPanel });

            // --- 핀패드 명령 전송 영역 (P17-2) ---
            var pinpadGroup = new GroupBox { Text = "핀패드 명령 전송", Left = 10, Top = 475, Width = 730, Height = 195, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };

            var pinpadComboLabel = new Label { Text = "명령:", Left = 10, Top = 25, Width = 40 };
            _pinpadCommandCombo = new ComboBox { Left = 55, Top = 22, Width = 400, DropDownStyle = ComboBoxStyle.DropDownList };
            _pinpadCommandCombo.SelectedIndexChanged += (s, e) => OnPinpadCommandSelectionChanged();

            _btnPinpadSend = new Button { Text = "전송", Left = 465, Top = 20, Width = 90 };
            _btnPinpadSend.Click += (s, e) => SendSelectedPinpadCommand();

            _pinpadFieldPanel = new Panel
            {
                Left = 10,
                Top = 55,
                Width = 705,
                Height = 130,
                AutoScroll = true,
                BorderStyle = BorderStyle.FixedSingle,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };

            pinpadGroup.Controls.AddRange(new Control[] { pinpadComboLabel, _pinpadCommandCombo, _btnPinpadSend, _pinpadFieldPanel });

            // --- 로그 영역 ---
            var logLabel = new Label { Text = "로그:", Left = 10, Top = 680, Width = 60, Anchor = AnchorStyles.Top | AnchorStyles.Left };
            _logBox = new TextBox
            {
                Left = 10,
                Top = 700,
                Width = 730,
                Height = 150,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            };

            Controls.AddRange(new Control[] { connGroup, _btnStatus, _btnInit, sendGroup, pinpadGroup, logLabel, _logBox });

            _commandCodes = CommandFieldSpecs.GetAllFieldCommandCodes();
            foreach (byte code in _commandCodes)
            {
                _commandCombo.Items.Add(CommandNames.DisplayName(code));
            }
            if (_commandCombo.Items.Count > 0)
            {
                _commandCombo.SelectedIndex = 0;
            }

            _pinpadCommandCodes = PinpadFieldSpecs.GetAllPinpadCommandCodes();
            foreach (PinpadCommandCode code in _pinpadCommandCodes)
            {
                _pinpadCommandCombo.Items.Add(PinpadCommandNames.DisplayName(code));
            }
            if (_pinpadCommandCombo.Items.Count > 0)
            {
                _pinpadCommandCombo.SelectedIndex = 0;
            }

            FormClosing += (s, e) => ClosePortIfOpen();
        }

        // ================= 로그 =================

        private void AppendLog(string text)
        {
            _logBox.AppendText(text + Environment.NewLine);
        }

        // ================= 연결 관리 =================

        private void OpenReader()
        {
            if (_connected)
            {
                AppendLog("이미 연결되어 있습니다. 먼저 닫기를 눌러주세요");
                return;
            }

            int portNumber = ParseIntOrZero(_portBox.Text);
            int baudRate = ParseIntOrZero(_baudBox.Text);

            int result = ReaderSerialNative.Reader_OpenPort(portNumber, baudRate, _nativeCallback, _nativePinpadCallback, IntPtr.Zero, out int newReaderId);
            if (result == (int)ReaderResult.READER_OK)
            {
                _readerId = newReaderId;
                _connected = true;
                AppendLog($"[열기] COM{portNumber}, {baudRate} bps -> READER_OK, readerId={newReaderId}");
            }
            else
            {
                AppendLog($"[열기] COM{portNumber}, {baudRate} bps -> 실패 (result={ReaderNames.FormatResult(result)})");
            }
            UpdateStatusLabel();
        }

        private void CloseReader()
        {
            if (!_connected)
            {
                AppendLog("열려 있는 리더기가 없습니다");
                return;
            }

            int result = ReaderSerialNative.Reader_ClosePort(_readerId);
            AppendLog($"[닫기] readerId={_readerId} -> result={ReaderNames.FormatResult(result)}");
            if (result == (int)ReaderResult.READER_OK)
            {
                _readerId = -1;
                _connected = false;
            }
            UpdateStatusLabel();
        }

        private void ClosePortIfOpen()
        {
            if (_connected)
            {
                ReaderSerialNative.Reader_ClosePort(_readerId);
                _readerId = -1;
                _connected = false;
            }
        }

        private void CheckStatus()
        {
            if (!_connected)
            {
                AppendLog("[상태 확인] 열려 있는 리더기가 없습니다");
                return;
            }

            int isOpen = ReaderSerialNative.Reader_IsPortOpen(_readerId);
            if (isOpen < 0)
            {
                AppendLog($"[상태 확인] readerId={_readerId} -> Reader_IsPortOpen={ReaderNames.FormatResult(isOpen)}");
            }
            else
            {
                AppendLog($"[상태 확인] readerId={_readerId} -> Reader_IsPortOpen={isOpen}");
            }
        }

        private void UpdateStatusLabel()
        {
            if (!_connected)
            {
                _statusLabel.Text = "미연결";
            }
            else
            {
                _statusLabel.Text = $"readerId={_readerId}, 연결됨";
            }
        }

        private static int ParseIntOrZero(string text)
        {
            return int.TryParse(text, out int value) ? value : 0;
        }

        // ================= P10-1b POS 연동 권장 패턴 =================
        //
        // Reader_SendCommand를 직접 부르지 않고 이 래퍼로만 호출한다.
        // readerId가 없으면 먼저 Open을 시도하고, 이미 연결된 상태에서 보낸
        // 명령이 포트 계열 에러(READER_ERR_PORT_NOT_OPEN)
        // 로 실패하면 Close -> Open -> 재시도를 한 번만 수행한다.
        // READER_ERR_BUSY 등 포트와 무관한 에러는 복구 대상에서 제외한다 —
        // 이미 다른 명령이 진행 중이라는 뜻이므로 여기서 Close하면 그 명령을
        // 강제로 죽이게 된다. Reader_IsPortOpen을 사전 체크로 쓰지 않고 Send를
        // 먼저 시도하는 이유도 동일하다(체크와 Send 사이에도 레이스가 있어
        // 신뢰할 수 없고, Reader_SendCommand가 이미 포트 상태를 원자적으로
        // 검증하므로 중복 호출이 된다). 재오픈 성공 시 새로 발급된 readerId로
        // 반드시 덮어쓴다 — 옛 id를 재사용하면 무조건 실패한다.
        // (DOC/개발문서/실행계획서.md P10-1b, CLAUDE.md "Recommended POS-side recovery pattern")
        private int SendCommandSafe(byte commandCode, byte[] data, int dataLength)
        {
            const string autoPrefix = "[자동복구] ";

            if (_readerId < 0)
            {
                int openResult = TryAutoOpenReader(autoPrefix);
                if (openResult != (int)ReaderResult.READER_OK)
                {
                    return openResult;
                }
            }

            int result = ReaderSerialNative.Reader_SendCommand(_readerId, commandCode, data, dataLength);

            if (result == (int)ReaderResult.READER_ERR_PORT_NOT_OPEN)
            {
                AppendLog($"{autoPrefix}전송 중 포트 계열 에러 감지(result={ReaderNames.FormatResult(result)}) -> Close 후 재연결 시도");

                ReaderSerialNative.Reader_ClosePort(_readerId);
                _readerId = -1;
                _connected = false;

                int reopenResult = TryAutoOpenReader(autoPrefix);
                if (reopenResult != (int)ReaderResult.READER_OK)
                {
                    AppendLog(autoPrefix + "재연결 실패 - readerId를 초기화합니다(다음 명령에서 다시 Open부터 시도)");
                    return reopenResult;
                }

                result = ReaderSerialNative.Reader_SendCommand(_readerId, commandCode, data, dataLength);
                if (result == (int)ReaderResult.READER_OK)
                {
                    AppendLog($"{autoPrefix}재연결 성공(readerId={_readerId}) -> 재전송 성공");
                }
                else
                {
                    AppendLog($"{autoPrefix}재연결 성공(readerId={_readerId}) -> 재전송도 실패(result={ReaderNames.FormatResult(result)})");
                }
            }
            else if (result == (int)ReaderResult.READER_ERR_SEND_FAIL)
            {
                // DLL이 이미 operationState를 즉시 IDLE로 복귀시켰으므로(2026-08-03)
                // 이 0x60 재전송은 필수가 아니다 — 리더기 쪽이 여전히 깨진 프레임을
                // 붙잡고 있을 잔여 가능성에 대비한 방어적 권장 조치일 뿐이다. 결과를
                // 기다리지 않고 로그만 남기며, 원래의 SEND_FAIL은 그대로 호출자에게
                // 반환한다.
                AppendLog($"{autoPrefix}전송 실패(result={ReaderNames.FormatResult(result)}) 감지 -> 프레임 재동기화용 초기화 요청(0x60) 방어적 전송");
                ReaderSerialNative.Reader_SendCommand(_readerId, 0x60, null, 0);
            }

            return result;
        }

        // SendCommandSafe가 "readerId 없음" 또는 포트 계열 에러로 Close한
        // 직후에만 호출된다 — 항상 닫힌/없는 상태에서 불리므로, 수동 "열기"
        // 버튼(OpenReader)과 달리 이미 연결되어 있는지 확인하지 않는다.
        private int TryAutoOpenReader(string logPrefix)
        {
            int portNumber = ParseIntOrZero(_portBox.Text);
            int baudRate = ParseIntOrZero(_baudBox.Text);

            int result = ReaderSerialNative.Reader_OpenPort(portNumber, baudRate, _nativeCallback, _nativePinpadCallback, IntPtr.Zero, out int newReaderId);
            if (result == (int)ReaderResult.READER_OK)
            {
                // 새로 발급된 readerId로 반드시 상태를 덮어쓴다 — 옛 id로
                // 계속 Send하면 무조건 실패한다.
                _readerId = newReaderId;
                _connected = true;
                AppendLog($"{logPrefix}COM{portNumber}, {baudRate} bps -> READER_OK, readerId={newReaderId}");
            }
            else
            {
                AppendLog($"{logPrefix}COM{portNumber}, {baudRate} bps -> 실패 (result={ReaderNames.FormatResult(result)})");
            }
            UpdateStatusLabel();
            return result;
        }

        // 최소 시나리오: 포트 열기 -> 초기화 요청(0x60) 전송 -> CALLBACK에서
        // 응답 로그 출력 -> 포트 닫기(닫기는 별도 버튼/폼 종료 시 수행).
        private void SendInitRequest()
        {
            int result = SendCommandSafe(CommandCodes.INIT_REQUEST, null, 0);
            AppendLog($"[초기화 요청(0x60)] readerId={_readerId} -> result={ReaderNames.FormatResult(result)}");
        }

        // ================= 명령 필드 패널 =================

        private void OnCommandSelectionChanged()
        {
            int sel = _commandCombo.SelectedIndex;
            if (sel < 0 || sel >= _commandCodes.Count)
            {
                return;
            }
            RebuildFieldPanel(_commandCodes[sel]);
        }

        private void RebuildFieldPanel(byte commandCode)
        {
            _fieldPanel.SuspendLayout();
            _fieldPanel.Controls.Clear();
            _fieldLabels.Clear();
            _fieldEdits.Clear();

            _currentCommandCode = commandCode;
            _currentFieldSpecs = CommandFieldSpecs.GetCommandFieldSpecs(commandCode);

            if (_currentFieldSpecs.Count == 0)
            {
                var notice = new Label { Text = "이 명령은 Data 필드가 없습니다.", Left = 8, Top = 8, Width = 600, AutoSize = true };
                _fieldPanel.Controls.Add(notice);
                _fieldPanel.ResumeLayout();
                return;
            }

            const int rowHeight = 26;
            const int labelWidth = 420;
            const int editLeft = 430;
            const int editWidth = 240;

            int top = 8;
            for (int i = 0; i < _currentFieldSpecs.Count; ++i)
            {
                FieldSpec spec = _currentFieldSpecs[i];

                var label = new Label { Text = spec.Label, Left = 8, Top = top + 3, Width = labelWidth, Height = rowHeight - 4 };
                _fieldPanel.Controls.Add(label);
                _fieldLabels.Add(label);

                var edit = new TextBox { Left = editLeft, Top = top, Width = editWidth, Text = spec.DefaultValue };
                _fieldPanel.Controls.Add(edit);
                _fieldEdits.Add(edit);

                top += rowHeight;
            }

            _fieldPanel.ResumeLayout();
        }

        // 필드 패널의 TextBox 값을 SPEC 필드 순서대로 구분자 없이 이어붙여
        // Data를 만들고, 전송 미리보기를 로그에 남긴다.
        private byte[] BuildSendBuffer(out string label)
        {
            label = CommandNames.DisplayName(_currentCommandCode);

            var buffer = new List<byte>();
            for (int i = 0; i < _currentFieldSpecs.Count && i < _fieldEdits.Count; ++i)
            {
                FieldSpec spec = _currentFieldSpecs[i];
                string editText = _fieldEdits[i].Text;

                if (spec.Kind == FieldKind.FIXED)
                {
                    buffer.AddRange(FieldEncoding.PadFixedFieldBytes(editText, spec.Width, spec.Pad));
                }
                else // LENGTH_PREFIXED
                {
                    buffer.AddRange(FieldEncoding.BuildLengthPrefixedFieldBytes(editText, spec.Width, out _));
                }
            }

            byte[] data = buffer.ToArray();
            AppendLog($"[{label} 전송Data] len={data.Length} data={FieldEncoding.BytesToDisplayAscii(data)}");
            return data;
        }

        private void SendSelectedCommand()
        {
            byte[] data = BuildSendBuffer(out string label);
            int result = SendCommandSafe(_currentCommandCode, data.Length > 0 ? data : null, data.Length);
            AppendLog($"[{label}] readerId={_readerId} -> result={ReaderNames.FormatResult(result)}");
        }

        // ================= 핀패드 명령 필드 패널 (P17-2) =================

        private void OnPinpadCommandSelectionChanged()
        {
            int sel = _pinpadCommandCombo.SelectedIndex;
            if (sel < 0 || sel >= _pinpadCommandCodes.Count)
            {
                return;
            }
            RebuildPinpadFieldPanel(_pinpadCommandCodes[sel]);
        }

        private void RebuildPinpadFieldPanel(PinpadCommandCode commandCode)
        {
            _pinpadFieldPanel.SuspendLayout();
            _pinpadFieldPanel.Controls.Clear();
            _pinpadFieldLabels.Clear();
            _pinpadFieldEdits.Clear();

            _currentPinpadCommandCode = commandCode;
            _currentPinpadFieldSpecs = PinpadFieldSpecs.GetPinpadCommandFieldSpecs(commandCode);

            if (_currentPinpadFieldSpecs.Count == 0)
            {
                var notice = new Label { Text = "이 명령은 Data 필드가 없습니다.", Left = 8, Top = 8, Width = 600, AutoSize = true };
                _pinpadFieldPanel.Controls.Add(notice);
                _pinpadFieldPanel.ResumeLayout();
                return;
            }

            const int rowHeight = 26;
            const int labelWidth = 420;
            const int editLeft = 430;
            const int editWidth = 240;

            int top = 8;
            for (int i = 0; i < _currentPinpadFieldSpecs.Count; ++i)
            {
                PinpadFieldSpec spec = _currentPinpadFieldSpecs[i];

                var label = new Label { Text = spec.Label, Left = 8, Top = top + 3, Width = labelWidth, Height = rowHeight - 4 };
                _pinpadFieldPanel.Controls.Add(label);
                _pinpadFieldLabels.Add(label);

                var edit = new TextBox { Left = editLeft, Top = top, Width = editWidth, Text = spec.DefaultValue };
                _pinpadFieldPanel.Controls.Add(edit);
                _pinpadFieldEdits.Add(edit);

                top += rowHeight;
            }

            _pinpadFieldPanel.ResumeLayout();
        }

        // 필드 패널의 TextBox 값을 PinpadFieldSpecs 순서 그대로 구분자 없이
        // 이어붙여 Data를 만든다. HEX_BINARY 필드는 유효하지 않은 hex 문자를
        // 조용히 0x00으로 치환하지 않는다 — 파싱 실패 시 null을 반환하고
        // 전송 자체를 중단한다(MFC HexStringToBytes 수정과 동일한 이유,
        // WorkingKey/ACN/RNUM처럼 암호화 키 관련 필드에서 특히 위험하다).
        private byte[] BuildPinpadSendBuffer(out string label)
        {
            label = PinpadCommandNames.DisplayName(_currentPinpadCommandCode);

            var buffer = new List<byte>();
            for (int i = 0; i < _currentPinpadFieldSpecs.Count && i < _pinpadFieldEdits.Count; ++i)
            {
                PinpadFieldSpec spec = _currentPinpadFieldSpecs[i];
                string editText = _pinpadFieldEdits[i].Text;

                switch (spec.Kind)
                {
                    case PinpadFieldKind.DECIMAL_BYTE:
                        {
                            int value = int.TryParse(editText, out int parsed) ? parsed : 0;
                            value = Math.Max(0, Math.Min(255, value));
                            buffer.Add((byte)value);
                            break;
                        }

                    case PinpadFieldKind.HEX_BYTE:
                        {
                            if (!FieldEncoding.TryParseHexString(editText, 1, out byte[] hexBytes, out string failReason))
                            {
                                AppendLog($"[핀패드][{label} 전송 중단] 필드 \"{spec.Label}\" 값 \"{editText}\" 파싱 실패: {failReason}");
                                return null;
                            }
                            buffer.AddRange(hexBytes);
                            break;
                        }

                    case PinpadFieldKind.HEX_BINARY:
                        {
                            if (!FieldEncoding.TryParseHexString(editText, spec.Width, out byte[] hexBytes, out string failReason))
                            {
                                AppendLog($"[핀패드][{label} 전송 중단] 필드 \"{spec.Label}\" 값 \"{editText}\" 파싱 실패: {failReason}");
                                return null;
                            }
                            buffer.AddRange(hexBytes);
                            break;
                        }
                }
            }

            byte[] data = buffer.ToArray();
            AppendLog($"[핀패드][{label} 전송Data] len={data.Length} data={FieldEncoding.BytesToDisplayAscii(data)}");
            return data;
        }

        // Pinpad_SendCommand는 SendCommandSafe(리더기용)와 달리 자동 재연결을
        // 하지 않는다 — 핀패드 명령 실패는 대개 조합 시퀀스 자체의 실패
        // (NAK/타임아웃/Tamper 등)이지 포트가 끊긴 것이 아니며, 포트가 열려
        // 있지 않으면 즉시 READER_ERR_PORT_NOT_OPEN을 반환할 뿐 자동으로
        // 복구되지 않는다(PRD §19-18, 명시적 재오픈 필요 — MFC SendToPinpad와 동일 근거).
        private void SendSelectedPinpadCommand()
        {
            if (!_connected)
            {
                AppendLog("[핀패드] 열려 있는 포트가 없습니다 - 먼저 \"열기\"로 포트를 여세요");
                return;
            }

            byte[] data = BuildPinpadSendBuffer(out string label);
            if (data == null)
            {
                return;
            }

            int result = ReaderSerialNative.Pinpad_SendCommand(_readerId, (byte)_currentPinpadCommandCode, data.Length > 0 ? data : null, data.Length);
            AppendLog($"[핀패드][{label}] readerId={_readerId} -> result={ReaderNames.FormatResult(result)}");
        }

        // ================= CALLBACK =================

        // 리더기 수신 스레드에서 직접 호출된다. 여기서 UI 컨트롤을 절대
        // 건드리지 않는다 — data를 즉시 배열로 복사한 뒤 Control.BeginInvoke로
        // UI 스레드에 위임한다(MFC 버전이 PostMessage로 위임하는 것과 동일한
        // 원리, CLAUDE.md/PRD SS7.6).
        //
        // data는 이 함수가 실행되는 동안에만 유효하다 — 함수가 반환된 직후
        // DLL이 내부 임시 버퍼를 0으로 지우고 정리하므로, 반드시 이 안에서
        // Marshal.Copy로 즉시 복사해야 한다(CLAUDE.md "CALLBACK data lifetime").
        private void OnReaderCallback(
            int readerId,
            int eventType,
            byte commandCode,
            IntPtr data,
            int dataLength,
            IntPtr userContext)
        {
            byte[] copy = Array.Empty<byte>();
            if (dataLength > 0 && data != IntPtr.Zero)
            {
                copy = new byte[dataLength];
                Marshal.Copy(data, copy, 0, dataLength);
            }

            try
            {
                if (IsHandleCreated && !IsDisposed)
                {
                    BeginInvoke(new Action(() => HandleReaderEventOnUiThread(readerId, eventType, commandCode, copy)));
                }
            }
            catch (ObjectDisposedException)
            {
                // 폼이 닫히는 도중 마지막 CALLBACK이 도착한 경우 — 무시한다.
            }
            catch (InvalidOperationException)
            {
                // 핸들이 아직 파괴 중인 경우도 동일하게 무시한다.
            }
        }

        // 반드시 UI 스레드에서만 실행된다(위 BeginInvoke를 통해서만 호출됨).
        private void HandleReaderEventOnUiThread(int readerId, int eventType, byte commandCode, byte[] data)
        {
            string ascii = FieldEncoding.BytesToDisplayAscii(data);
            string line = $"readerId={readerId} eventType={ReaderNames.FormatEventType(eventType)} " +
                          $"commandCode=0x{commandCode:X2} " +
                          $"dataLength={data.Length} data={ascii}";
            AppendLog(line);
        }

        // 핀패드 수신 스레드에서 직접 호출된다. OnReaderCallback과 동일한
        // 이유로 여기서 UI를 건드리지 않고 즉시 Marshal.Copy로 복사한 뒤
        // BeginInvoke로 UI 스레드에 위임한다(CLAUDE.md CALLBACK 데이터 수명 규칙).
        private void OnPinpadCallback(
            int readerId,
            int eventType,
            byte commandCode,
            IntPtr data,
            int dataLength,
            IntPtr userContext)
        {
            byte[] copy = Array.Empty<byte>();
            if (dataLength > 0 && data != IntPtr.Zero)
            {
                copy = new byte[dataLength];
                Marshal.Copy(data, copy, 0, dataLength);
            }

            try
            {
                if (IsHandleCreated && !IsDisposed)
                {
                    BeginInvoke(new Action(() => HandlePinpadEventOnUiThread(readerId, eventType, commandCode, copy)));
                }
            }
            catch (ObjectDisposedException)
            {
                // 폼이 닫히는 도중 마지막 CALLBACK이 도착한 경우 — 무시한다.
            }
            catch (InvalidOperationException)
            {
                // 핸들이 아직 파괴 중인 경우도 동일하게 무시한다.
            }
        }

        // 반드시 UI 스레드에서만 실행된다. MFC OnPinpadEvent와 동일한 로그
        // 형식. 2026-08-12 PINPAD_CALLBACK 전면 재설계로 failInfo(3byte) payload
        // 개념이 완전히 제거됐다 - eventType 자체가 실패 원인을 표현하므로
        // data[2]를 파싱할 필요가 없다. data는 PINPAD_EVENT_RESPONSE일 때만
        // 실제 응답 데이터고, 그 외 모든 이벤트는 항상 비어 있다(리더기와 동일한
        // 패턴) - 그래서 항상 raw ASCII로 그대로 표시하면 된다(PINPAD_EVENT_RESPONSE가
        // 아니면 자연히 "(none)" 형태가 된다).
        private void HandlePinpadEventOnUiThread(int readerId, int eventType, byte commandCode, byte[] data)
        {
            string dataText = FieldEncoding.BytesToDisplayAscii(data);

            string line = $"[핀패드] readerId={readerId} eventType={ReaderNames.FormatPinpadEventType(eventType)} " +
                          $"commandCode={ReaderNames.FormatPinpadCommandCode(commandCode)} dataLength={data.Length} data={dataText}";
            AppendLog(line);
        }
    }
}
