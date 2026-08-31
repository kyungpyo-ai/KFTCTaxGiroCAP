using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using KFTCOneCAP.KioskSim.Net;
using KFTCOneCAP.KioskSim.Preset;
using KFTCOneCAP.KioskSim.Protocol;

namespace KFTCOneCAP.KioskSim.Forms
{
    /// <summary>
    /// 메인 화면. 탭 구조를 쓴다(Phase 19 실행계획서 P19-5/P19-7): "전문 전송" 탭에 3전문 독립 버튼 +
    /// 필드별 입력 그리드 + raw ASCII 미리보기 + 전송을 담고, "오류 주입" 탭에는 8종 오류 상황 버튼과
    /// 각각의 기대 결과를 담는다(확정된 설계 결정 2번 — 정상 경로와 오류 주입을 탭으로 격리, 정상
    /// 경로 코드는 이 탭에서도 재사용하지 않는다 — <see cref="Net.ErrorInjectionClient"/> 참고).
    ///
    /// 이 클래스는 업체에 연동 샘플로도 전달된다(README.md 참고) — 코드를 읽는 사람이 SPEC을
    /// 몰라도 "필드 계약(누가 무엇을 채우는가)"을 알 수 있게 그리드/주석을 짰다.
    /// </summary>
    public class MainForm : Form
    {
        // 그리드 열 인덱스(사람이 읽기 쉬운 이름으로 상수화).
        private const int ColNumber = 0;
        private const int ColName = 1;
        private const int ColRepresentation = 2;
        private const int ColLength = 3;
        private const int ColPosition = 4;
        private const int ColSetLocation = 5;
        private const int ColValue = 6;

        // 응답 필드 분해 그리드(P19-6) 열 인덱스.
        private const int RespColNumber = 0;
        private const int RespColName = 1;
        private const int RespColSetLocation = 2;
        private const int RespColRequestValue = 3;
        private const int RespColResponseValue = 4;

        /// <summary>CP949(본 파일 안에서 독립 정의 — P19-2 원칙, 본 앱/다른 파일과 공유하지 않는다).</summary>
        private static readonly Encoding Cp949 = Encoding.GetEncoding(949);

        private readonly TabControl _tabControl;
        private readonly TabPage _sendTab;
        private readonly TabPage _errorInjectionTab;

        private readonly Button _btnSelect501008;
        private readonly Button _btnSelect800000;
        private readonly Button _btnSelect902614;
        private readonly Label _lblSelectedSchema;

        private readonly DataGridView _grid;
        private readonly TextBox _previewTextBox;
        private readonly Button _btnRefreshPreview;
        private readonly Button _btnSend;
        private readonly Button _btnSavePreset;
        private readonly Label _lblStatus;
        private readonly Label _lblResponseCode;
        private readonly Label _lblField51Warning;
        private readonly DataGridView _responseGrid;
        private readonly TextBox _responseTextBox;

        // ---- 오류 주입 탭(P19-7) ----
        /// <summary>시나리오 번호(1~8) → 결과를 보여줄 라벨. 버튼 클릭 시 이 라벨을 갱신한다.</summary>
        private readonly Dictionary<int, Label> _errorScenarioResultLabels = new Dictionary<int, Label>();

        /// <summary>시나리오 번호(1~8) → 실행 버튼. 실행 중에는 그 버튼만 비활성화한다(다른 시나리오는
        /// 동시에 눌러도 무방하다 — 각자 독립된 소켓을 쓴다).</summary>
        private readonly Dictionary<int, Button> _errorScenarioButtons = new Dictionary<int, Button>();

        /// <summary>현재 그리드에 떠 있는 전문 스키마(선택 전이면 null).</summary>
        private TelegramSchema? _currentSchema;

        /// <summary>
        /// 직전에 실제로 전송한 요청 본문(그리드가 그 뒤 편집되어도 흔들리지 않도록 전송 시점에
        /// 스냅샷해 둔다) + 그 스키마. 응답 필드 분해 화면(P19-6)이 "요청값 vs 응답값"을 나란히
        /// 보여줄 때 이 스냅샷을 쓴다(전송 후 그리드를 사용자가 계속 만질 수 있으므로 현재 그리드
        /// 값을 다시 읽으면 안 된다).
        /// </summary>
        private byte[]? _lastRequestBody;
        private TelegramSchema? _lastRequestSchema;

        /// <summary>
        /// 세 전문 전부의 kiosk 편집 가능 필드 현재 값(전문타입 → 필드번호 → 값).
        /// 시작 시 프리셋/코드 기본값으로 채워지고, 사용자가 그리드를 편집할 때마다 즉시 갱신된다
        /// (탭을 오가도 값이 유지되게 하기 위함 — "직전 입력을 조용히 잃어버리지 않는다").
        /// "프리셋으로 저장" 버튼을 누르면 이 값 전체가 파일에 기록된다.
        /// </summary>
        private readonly Dictionary<string, Dictionary<int, string>> _currentValues = new Dictionary<string, Dictionary<int, string>>();

        /// <summary>그리드 셀 값 변경 이벤트가 "코드가 프로그램적으로 값을 채우는 중"에는 반응하지 않게 막는 플래그.</summary>
        private bool _suppressGridEvents;

        public MainForm()
        {
            Text = "KFTCOneCAP 키오스크 시뮬레이터 — Phase 19";
            Width = 1360;
            Height = 980;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("맑은 고딕", 9F);

            // ---- 시작 시 프리셋 로드(없으면 코드 기본값) ----
            var loaded = PresetStore.Load();
            InitializeCurrentValues(loaded);
            if (!loaded.ParsedOk && loaded.Warning != null)
            {
                // 파싱 실패는 화면이 뜬 뒤 사용자에게 알려도 늦지 않다(폴백은 이미 끝났다).
                this.Load += (s, e) => MessageBox.Show(this, loaded.Warning, "프리셋 파일 경고",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            _tabControl = new TabControl { Dock = DockStyle.Fill };
            _sendTab = new TabPage("전문 전송");
            _errorInjectionTab = new TabPage("오류 주입");
            _tabControl.TabPages.Add(_sendTab);
            _tabControl.TabPages.Add(_errorInjectionTab);
            Controls.Add(_tabControl);

            // ---- 오류 주입 탭(P19-7) ----
            BuildErrorInjectionTab();

            // ---- 전문 전송 탭 ----
            var topPanel = new Panel { Dock = DockStyle.Top, Height = 56, Padding = new Padding(8) };
            _btnSelect501008 = new Button { Text = "501008\n국고 상세 고지내역 조회", Width = 220, Height = 40, Left = 8, Top = 8 };
            _btnSelect800000 = new Button { Text = "800000\n카드 정보 조회", Width = 220, Height = 40, Left = 236, Top = 8 };
            _btnSelect902614 = new Button { Text = "902614\n국고 신용카드 승인요청", Width = 220, Height = 40, Left = 464, Top = 8 };
            _lblSelectedSchema = new Label
            {
                Text = "선택된 전문: 없음",
                Left = 700,
                Top = 18,
                Width = 400,
                Font = new Font("맑은 고딕", 10F, FontStyle.Bold),
            };
            topPanel.Controls.Add(_btnSelect501008);
            topPanel.Controls.Add(_btnSelect800000);
            topPanel.Controls.Add(_btnSelect902614);
            topPanel.Controls.Add(_lblSelectedSchema);

            var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 560, Padding = new Padding(8) };

            var previewLabel = new Label { Text = "보낼 전문 raw ASCII 미리보기(CP949 디코딩):", Dock = DockStyle.Top, Height = 18 };
            _previewTextBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Dock = DockStyle.Top,
                Height = 90,
                Font = new Font("Consolas", 9F),
                BackColor = Color.WhiteSmoke,
            };

            var buttonRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, FlowDirection = FlowDirection.LeftToRight };
            _btnRefreshPreview = new Button { Text = "미리보기 갱신", Width = 110 };
            _btnSend = new Button { Text = "전송", Width = 110, Enabled = false };
            _btnSavePreset = new Button { Text = "현재 값을 프리셋으로 저장", Width = 180 };
            buttonRow.Controls.Add(_btnRefreshPreview);
            buttonRow.Controls.Add(_btnSend);
            buttonRow.Controls.Add(_btnSavePreset);

            _lblStatus = new Label { Text = "대기 중.", Dock = DockStyle.Top, Height = 20, ForeColor = Color.DarkBlue };

            var responseLabel = new Label { Text = "응답 결과:", Dock = DockStyle.Top, Height = 18, Font = new Font("맑은 고딕", 9F, FontStyle.Bold) };
            _lblResponseCode = new Label
            {
                Text = "#7 응답 코드: (아직 응답 없음)",
                Dock = DockStyle.Top,
                Height = 20,
                ForeColor = Color.Black,
            };
            _lblField51Warning = new Label
            {
                // 902614가 아닌 전문일 때는 해당 없음으로 비워 둔다 — ShowFieldDecomposition이 채운다.
                Text = string.Empty,
                Dock = DockStyle.Top,
                Height = 20,
                ForeColor = Color.DarkGreen,
                Font = new Font("맑은 고딕", 9F, FontStyle.Bold),
            };

            var responseGridLabel = new Label
            {
                Text = "응답 필드 분해(값(요청) vs 값(응답), 달라진 셀은 노란색으로 강조):",
                Dock = DockStyle.Top,
                Height = 18,
            };

            // 필드 분해 그리드(위, 남는 공간의 대부분)와 raw ASCII 미리보기(아래, 고정 높이)를
            // 위아래로 나눈다 — SplitContainer 하나가 이 bottomPanel의 유일한 Fill 대상이 된다.
            var responseSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 6,
            };

            _responseGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                EditMode = DataGridViewEditMode.EditProgrammatically,
            };
            _responseGrid.Columns.Add("respColNumber", "#번호");
            _responseGrid.Columns.Add("respColName", "필드명");
            _responseGrid.Columns.Add("respColSetLocation", "SET 장소");
            _responseGrid.Columns.Add("respColRequestValue", "값(요청)");
            _responseGrid.Columns.Add("respColResponseValue", "값(응답)");
            _responseGrid.Columns[RespColNumber].Width = 55;
            _responseGrid.Columns[RespColName].Width = 260;
            _responseGrid.Columns[RespColSetLocation].Width = 150;
            _responseGrid.Columns[RespColRequestValue].Width = 300;
            _responseGrid.Columns[RespColResponseValue].Width = 300;
            _responseGrid.Columns[RespColResponseValue].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            var rawResponseLabel = new Label { Text = "응답 본문 raw ASCII(CP949 디코딩):", Dock = DockStyle.Top, Height = 18 };
            _responseTextBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9F),
                BackColor = Color.WhiteSmoke,
            };

            responseSplit.Panel1.Controls.Add(_responseGrid);
            responseSplit.Panel1.Controls.Add(responseGridLabel);
            responseSplit.Panel2.Controls.Add(_responseTextBox);
            responseSplit.Panel2.Controls.Add(rawResponseLabel);

            // Dock 순서 주의: 나중에 추가한 컨트롤일수록 레이아웃에서 우선 배치된다.
            // responseSplit(Fill)을 가장 먼저 추가해 나머지 Top-docked 컨트롤들이 위쪽부터
            // 자리를 잡고, 그 뒤 responseSplit이 남은 공간을 채우게 한다.
            bottomPanel.Controls.Add(responseSplit);
            bottomPanel.Controls.Add(_lblField51Warning);
            bottomPanel.Controls.Add(_lblResponseCode);
            bottomPanel.Controls.Add(responseLabel);
            bottomPanel.Controls.Add(_lblStatus);
            bottomPanel.Controls.Add(buttonRow);
            bottomPanel.Controls.Add(_previewTextBox);
            bottomPanel.Controls.Add(previewLabel);

            // SplitContainer 초기 SplitterDistance는 핸들 생성(레이아웃 확정) 후에 설정해야
            // "SplitterDistance가 허용 범위를 벗어났다" 예외를 피할 수 있다.
            responseSplit.HandleCreated += (s, e) =>
            {
                if (responseSplit.Height > 220)
                    responseSplit.SplitterDistance = responseSplit.Height - 160;
            };

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
            };
            _grid.Columns.Add("colNumber", "#번호");
            _grid.Columns.Add("colName", "필드명");
            _grid.Columns.Add("colRepresentation", "표현");
            _grid.Columns.Add("colLength", "길이");
            _grid.Columns.Add("colPosition", "POSITION");
            _grid.Columns.Add("colSetLocation", "SET 장소");
            _grid.Columns.Add("colValue", "값");

            _grid.Columns[ColNumber].Width = 55;
            _grid.Columns[ColName].Width = 260;
            _grid.Columns[ColRepresentation].Width = 60;
            _grid.Columns[ColLength].Width = 55;
            _grid.Columns[ColPosition].Width = 70;
            _grid.Columns[ColSetLocation].Width = 130;
            _grid.Columns[ColValue].Width = 280;
            _grid.Columns[ColValue].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            foreach (DataGridViewColumn col in _grid.Columns)
            {
                if (col.Index != ColValue)
                    col.ReadOnly = true; // 값 열 외에는 항상 읽기 전용(SET 장소와 무관하게 표시 전용).
            }

            // 순서: Fill(그리드)을 먼저 추가하고 Top/Bottom을 나중에 추가해야 Top/Bottom이
            // 자기 영역을 먼저 차지하고 그리드가 남는 공간을 채운다(위 bottomPanel과 동일한 이유).
            _sendTab.Controls.Add(_grid);
            _sendTab.Controls.Add(bottomPanel);
            _sendTab.Controls.Add(topPanel);

            // ---- 이벤트 연결 ----
            _btnSelect501008.Click += (s, e) => SelectSchema(TelegramSchemas.Notice501008);
            _btnSelect800000.Click += (s, e) => SelectSchema(TelegramSchemas.CardInfo800000);
            _btnSelect902614.Click += (s, e) => SelectSchema(TelegramSchemas.CardApproval902614);
            _btnRefreshPreview.Click += (s, e) => UpdatePreview();
            _btnSavePreset.Click += (s, e) => SavePreset();
            _btnSend.Click += async (s, e) => await OnSendClickAsync();
            _grid.CellValueChanged += Grid_CellValueChanged;
            _grid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                // 텍스트 셀 편집 중 커밋되지 않은 값도 즉시 CellValueChanged로 반영되게 한다
                // (기본 동작은 포커스를 옮겨야 커밋된다 — 미리보기를 실시간처럼 보이게 하려면 이 처리가 필요).
                if (_grid.IsCurrentCellDirty)
                    _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
        }

        /// <summary>
        /// "오류 주입" 탭 구성(Phase 19 실행계획서 P19-7). 8개 시나리오를 위→아래로 나열한다.
        /// 각 행: [실행 버튼] [기대 결과(정적 텍스트, 실행 전에도 보임)] [실제 결과(실행 후 갱신)].
        /// 업체가 자기 서버를 만들 때 그대로 체크리스트로 쓸 수 있도록 "기대 결과"를 코드에 박아
        /// 항상 화면에 보이게 한다(development_plan.md P19-7 요구사항).
        /// </summary>
        private void BuildErrorInjectionTab()
        {
            var headerLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 54,
                Padding = new Padding(8),
                Font = new Font("맑은 고딕", 9F, FontStyle.Bold),
                Text = "이 탭은 잘못된 프로토콜/네트워크 상황을 일부러 만들어 본 앱(KFTCOneCAP)의 방어 동작을 " +
                       "확인한다. Net/ErrorInjectionClient.cs가 로우레벨 TCP 소켓으로 직접 구현했다(정상 " +
                       "경로 OneCapClient는 재사용하지 않는다 — 완성된 프레임만 다루도록 설계돼 있어 여기 " +
                       "맞지 않는다).",
            };
            _errorInjectionTab.Controls.Add(headerLabel);

            var scenarioPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 8,
                AutoScroll = true,
                Padding = new Padding(8),
            };
            scenarioPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            scenarioPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 480));
            scenarioPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 8; i++)
                scenarioPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));

            AddErrorScenarioRow(scenarioPanel, 0, 1, "1. 선언 길이 ≠ 실제 본문 길이",
                "501008(정상 706바이트)을 길이 헤더 \"0700\"으로 선언하고 딱 그만큼(700바이트)만 보낸다.\n" +
                "기대: #7 응답 코드 = E40(길이 불일치).",
                () => ErrorInjectionClient.Scenario1_DeclaredLengthMismatch());

            AddErrorScenarioRow(scenarioPanel, 1, 2, "2. 알 수 없는 거래 구분 코드(#4)",
                "501008 프레이밍은 정상이되 #4(거래 구분 코드)에 존재하지 않는 \"999999\"를 넣어 보낸다.\n" +
                "기대: #7 응답 코드 = E41(알 수 없는 거래구분).",
                () => ErrorInjectionClient.Scenario2_UnknownTransactionType());

            AddErrorScenarioRow(scenarioPanel, 2, 3, "3. 길이 필드가 숫자가 아님",
                "길이 헤더 4바이트에 \"abcd\"를 넣고 아무 본문이나 뒤에 붙여 보낸다.\n" +
                "기대: 응답 없이 서버가 그 연결을 닫는다(재동기화 불가 설계).",
                () => ErrorInjectionClient.Scenario3_NonNumericLengthHeader());

            AddErrorScenarioRow(scenarioPanel, 3, 4, "4. 본문을 나눠 보내기",
                "정상 501008 프레임(710바이트)을 100바이트씩 조각내 조각 사이에 짧은 지연을 두고 보낸다.\n" +
                "기대: 서버 프레이머가 부분 수신을 누적해 정상 응답한다.",
                () => ErrorInjectionClient.Scenario4_ChunkedSend());

            AddErrorScenarioRow(scenarioPanel, 4, 5, "5. 응답 전 연결 강제 종료",
                "정상 501008 요청을 보낸 직후 응답을 기다리지 않고 소켓을 바로 닫는다. 이어서 자동으로\n" +
                "정상 501008을 하나 더 보낸다. 기대: 서버가 죽지 않고 다음 요청을 정상 처리한다.",
                () => ErrorInjectionClient.Scenario5_AbortBeforeResponse());

            AddErrorScenarioRow(scenarioPanel, 5, 6, "6. 응답을 읽지 않고 붙들기",
                "정상 501008 요청을 보내고 연결은 열어 둔 채 응답을 7초간 절대 Read하지 않다가 닫는다.\n" +
                "이어서 자동으로 정상 501008을 하나 더 보낸다. 기대: 서버 송신 타임아웃(5초, " +
                "PosSocketServer.SendTimeoutMilliseconds)이 지나도 그 뒤 요청은 막히지 않는다.",
                () => ErrorInjectionClient.Scenario6_HoldResponseUnread());

            AddErrorScenarioRow(scenarioPanel, 6, 7, "7. 연속 2건 즉시 전송",
                "501008 두 개를 서로 다른 연결로 거의 동시에 보낸다.\n" +
                "기대: 단일 워커 큐가 직렬화해서 둘 다 정상 응답한다.",
                () => ErrorInjectionClient.Scenario7_ConcurrentRequests());

            AddErrorScenarioRow(scenarioPanel, 7, 8, "8. 버퍼 상한 초과",
                "길이 헤더에 \"9999\"를 선언하고 그 뒤로 완성되지 않는 쓰레기 바이트를 64KB(65536바이트)\n" +
                "넘게 계속 보낸다. 기대: 서버가 버퍼 상한을 넘기면 연결을 닫는다.",
                () => ErrorInjectionClient.Scenario8_BufferOverflowAttempt());

            _errorInjectionTab.Controls.Add(scenarioPanel);
        }

        /// <summary>오류 주입 탭 한 행(버튼 + 기대 결과 + 실제 결과 라벨)을 만들어 테이블에 추가한다.</summary>
        private void AddErrorScenarioRow(TableLayoutPanel panel, int row, int scenarioNumber, string title,
            string expectedText, Func<string> runScenario)
        {
            var button = new Button
            {
                Text = title,
                Dock = DockStyle.Fill,
                Margin = new Padding(4),
            };

            var expectedLabel = new Label
            {
                Text = "기대 결과: " + expectedText,
                Dock = DockStyle.Fill,
                Margin = new Padding(4),
                AutoSize = false,
            };

            var resultLabel = new Label
            {
                Text = "(아직 실행하지 않음)",
                Dock = DockStyle.Fill,
                Margin = new Padding(4),
                AutoSize = false,
                ForeColor = Color.Gray,
            };

            button.Click += async (s, e) => await RunErrorScenarioAsync(scenarioNumber, runScenario);

            panel.Controls.Add(button, 0, row);
            panel.Controls.Add(expectedLabel, 1, row);
            panel.Controls.Add(resultLabel, 2, row);

            _errorScenarioButtons[scenarioNumber] = button;
            _errorScenarioResultLabels[scenarioNumber] = resultLabel;
        }

        /// <summary>
        /// 시나리오 하나를 백그라운드 스레드에서 실행하고(UI 스레드를 막지 않는다 — 6/7번은 수 초
        /// 걸린다) 결과를 그 행의 결과 라벨에 반영한다. 실행 중에는 그 버튼만 비활성화한다.
        /// </summary>
        private async System.Threading.Tasks.Task RunErrorScenarioAsync(int scenarioNumber, Func<string> runScenario)
        {
            var button = _errorScenarioButtons[scenarioNumber];
            var resultLabel = _errorScenarioResultLabels[scenarioNumber];

            button.Enabled = false;
            resultLabel.Text = "실행 중…";
            resultLabel.ForeColor = Color.DarkBlue;

            try
            {
                string result = await System.Threading.Tasks.Task.Run(runScenario);
                resultLabel.Text = result;
                // 모든 시나리오 메서드는 문제 있는 경로(기대와 다름/불일치/오류)에서 예외 없이
                // "확인 필요"라는 문구를 반드시 포함하도록 통일돼 있다(ErrorInjectionClient.cs
                // 전수 확인). 예전에는 "불일치"만 찾아 시나리오 3의 타임아웃 분기("기대와 다름 …
                // 확인 필요"에는 "불일치"가 없다)가 초록(정상)으로 잘못 표시됐다(2026-08-31 검증에서
                // 발견 — 낮음, 검증 도구의 오탐).
                resultLabel.ForeColor = result.Contains("확인 필요") ? Color.DarkRed : Color.DarkGreen;
            }
            catch (Exception ex)
            {
                resultLabel.Text = $"[예외] {ex.GetType().Name}: {ex.Message}";
                resultLabel.ForeColor = Color.DarkRed;
            }
            finally
            {
                button.Enabled = true;
            }
        }

        /// <summary>세 전문 전부의 kiosk 편집 가능 필드 값을 프리셋/코드 기본값으로 초기화한다.</summary>
        private void InitializeCurrentValues(PresetStore.LoadResult loaded)
        {
            _currentValues["501008"] = PresetStore.BuildInitialValues(loaded, TelegramSchemas.Notice501008);
            _currentValues["800000"] = PresetStore.BuildInitialValues(loaded, TelegramSchemas.CardInfo800000);
            _currentValues["902614"] = PresetStore.BuildInitialValues(loaded, TelegramSchemas.CardApproval902614);
        }

        /// <summary>전문 버튼을 눌렀을 때: 스키마 전환 + 그리드 재구성 + 미리보기 갱신.</summary>
        private void SelectSchema(TelegramSchema schema)
        {
            _currentSchema = schema;
            _lblSelectedSchema.Text = $"선택된 전문: {schema.TxType} (총 {schema.TotalLength}바이트, 필드 {schema.Fields.Count}개)";
            _btnSend.Enabled = true;
            LoadGridForSchema(schema);
            UpdatePreview();
            ClearResponseDisplay(); // 전문을 바꾸면 이전 전문의 응답 분해 결과가 화면에 남아 있으면 안 된다.
        }

        /// <summary>응답 관련 표시(필드 분해 그리드/코드 해설/#51 경고/raw ASCII)를 전부 비운다.</summary>
        private void ClearResponseDisplay()
        {
            _responseGrid.Rows.Clear();
            _lblResponseCode.Text = "#7 응답 코드: (아직 응답 없음)";
            _lblResponseCode.ForeColor = Color.Black;
            _lblField51Warning.Text = string.Empty;
            _responseTextBox.Text = string.Empty;
        }

        /// <summary>스키마의 필드 전체를 SPEC 순서(번호 오름차순)로 그리드에 나열한다.</summary>
        private void LoadGridForSchema(TelegramSchema schema)
        {
            _suppressGridEvents = true;
            try
            {
                _grid.Rows.Clear();
                var byNumber = _currentValues[schema.TxType];

                foreach (var field in schema.Fields.OrderBy(f => f.Number))
                {
                    // AlwaysBlank 필드(FILLER/예비 정보 FIELD, #5 상태 코드 등)는 SetLocation이
                    // Kiosk여도 편집을 막는다 — 정의된 유효값이 없어 편집 가능하게 열어두면 업체가
                    // 무엇을 넣어야 하는지 헷갈리고 잘못된 값을 실수로 채워 보낼 위험만 커진다
                    // (2026-08-28 사용자 확정, TelegramField.AlwaysBlank 문서 참고).
                    bool editable = field.SetLocation == TelegramSetLocation.Kiosk && !field.AlwaysBlank;
                    string valueCellText = editable && byNumber.TryGetValue(field.Number, out var v) ? v : string.Empty;

                    int rowIndex = _grid.Rows.Add(
                        field.Number,
                        field.Name,
                        field.Representation.ToString(),
                        field.Length,
                        field.Position,
                        DescribeSetLocation(field),
                        valueCellText);

                    var row = _grid.Rows[rowIndex];
                    row.Tag = field;
                    row.Cells[ColValue].ReadOnly = !editable;
                    if (!editable)
                    {
                        row.Cells[ColValue].Style.BackColor = Color.LightGray;
                        row.Cells[ColValue].Style.ForeColor = Color.DimGray;
                        row.DefaultCellStyle.BackColor = Color.WhiteSmoke;
                    }
                    else
                    {
                        row.Cells[ColValue].Style.BackColor = Color.White;
                    }
                }
            }
            finally
            {
                _suppressGridEvents = false;
            }
        }

        /// <summary>
        /// AlwaysBlank인 Kiosk 필드는 "kiosk (직접 입력)"이 아니라 별도 문구로 보여준다 — 배경색은
        /// 다른 잠긴 필드(OneCap/InternetGiro/Van 담당)와 같은 회색이지만, "왜 잠겼는지"가 달라서
        /// (남이 채우는 게 아니라 애초에 값이 필요 없어서) 문구를 구분해 둔다.
        /// </summary>
        private static string DescribeSetLocation(TelegramField field)
        {
            if (field.SetLocation == TelegramSetLocation.Kiosk && field.AlwaysBlank)
                return "kiosk (공백 고정, 편집 불가)";
            return DescribeSetLocation(field.SetLocation);
        }

        private static string DescribeSetLocation(TelegramSetLocation location)
        {
            switch (location)
            {
                case TelegramSetLocation.Kiosk: return "kiosk (직접 입력)";
                case TelegramSetLocation.OneCap: return "원캡이 채움";
                case TelegramSetLocation.InternetGiro: return "인터넷지로가 채움";
                case TelegramSetLocation.Van: return "VAN이 채움";
                default: return location.ToString();
            }
        }

        /// <summary>값 열이 편집될 때마다 <see cref="_currentValues"/>에 즉시 반영하고 미리보기를 갱신한다.</summary>
        private void Grid_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
        {
            if (_suppressGridEvents || _currentSchema == null)
                return;
            if (e.RowIndex < 0 || e.ColumnIndex != ColValue)
                return;

            var row = _grid.Rows[e.RowIndex];
            var field = (TelegramField)row.Tag!;
            if (field.SetLocation != TelegramSetLocation.Kiosk || field.AlwaysBlank)
                return; // 읽기 전용 셀은 편집될 수 없지만, 방어적으로 한 번 더 확인(AlwaysBlank도 동일).

            string value = Convert.ToString(row.Cells[ColValue].Value) ?? string.Empty;
            _currentValues[_currentSchema.TxType][field.Number] = value;

            UpdatePreview();
        }

        /// <summary>
        /// 현재 그리드 값으로 <see cref="TelegramBuffer"/>를 만든다. 값이 빈 문자열인 필드는
        /// <see cref="TelegramBuffer.Write"/>를 아예 호출하지 않는다 — tools/spec_client.ps1의
        /// Set-Field가 빈 값이면 건너뛰는 것과 동일한 규칙이다(빈 값 = "이 필드는 안 쓴다", 버퍼
        /// 초기화값인 space로 남는다).
        /// </summary>
        private TelegramBuffer BuildBufferFromGrid()
        {
            if (_currentSchema == null)
                throw new InvalidOperationException("전문이 선택되지 않았다.");

            var buffer = new TelegramBuffer(_currentSchema);
            foreach (DataGridViewRow row in _grid.Rows)
            {
                var field = (TelegramField)row.Tag!;
                string value = Convert.ToString(row.Cells[ColValue].Value) ?? string.Empty;
                if (value.Length == 0)
                    continue;
                buffer.Write(field.Number, value);
            }
            return buffer;
        }

        private void UpdatePreview()
        {
            if (_currentSchema == null)
            {
                _previewTextBox.Text = string.Empty;
                return;
            }

            try
            {
                var buffer = BuildBufferFromGrid();
                byte[] body = buffer.ToBytes();
                _previewTextBox.Text = Cp949.GetString(body);
            }
            catch (Exception ex)
            {
                // 길이 초과 등 TelegramBuffer.Write 예외를 미리보기 단계에서 사람이 바로 볼 수 있게.
                _previewTextBox.Text = $"[미리보기 생성 실패] {ex.Message}";
            }
        }

        private void SavePreset()
        {
            try
            {
                PresetStore.Save(_currentValues);
                _lblStatus.Text = $"프리셋 저장 완료 ({PresetStore.PresetFilePath}).";
                _lblStatus.ForeColor = Color.DarkGreen;
            }
            catch (Exception ex)
            {
                _lblStatus.Text = $"프리셋 저장 실패: {ex.Message}";
                _lblStatus.ForeColor = Color.DarkRed;
                MessageBox.Show(this, ex.Message, "프리셋 저장 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async System.Threading.Tasks.Task OnSendClickAsync()
        {
            if (_currentSchema == null)
                return;

            TelegramBuffer buffer;
            try
            {
                buffer = BuildBufferFromGrid();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "전문 생성 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 응답 필드 분해(P19-6)가 "지금 그리드 값"이 아니라 "실제로 보낸 값"과 비교해야 하므로
            // 전송 시점의 요청 본문을 스냅샷해 둔다(전송 후에도 사용자가 그리드를 계속 편집할 수 있음).
            _lastRequestBody = buffer.ToBytes();
            _lastRequestSchema = _currentSchema;

            byte[] frame = TelegramCodec.Encode(buffer.ToBytes());
            UpdatePreview();
            ClearResponseDisplay();

            SetSendingState(true, _currentSchema.TxType);
            try
            {
                // OneCapClient.SendAsync의 onElapsed 콜백은 백그라운드 스레드에서 호출된다
                // (Net/OneCapClient.cs 클래스 주석 참고) — 반드시 Control.Invoke로 UI 스레드로
                // 넘겨야 한다. 폼이 이미 닫히는 중이면 Invoke가 예외를 던질 수 있어 IsDisposed로 방어한다.
                Action<TimeSpan> onElapsed = elapsed =>
                {
                    if (IsDisposed || !IsHandleCreated)
                        return;
                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            if (!IsDisposed)
                                _lblStatus.Text = $"응답 대기 중… ({elapsed.TotalSeconds:F1}초)";
                        }));
                    }
                    catch (ObjectDisposedException)
                    {
                        // 폼 종료 경합 — 무시.
                    }
                };

                OneCapClientResult result = await OneCapClient.SendAsync(frame, onElapsed);
                ShowResult(result);
            }
            finally
            {
                SetSendingState(false, _currentSchema.TxType);
            }
        }

        /// <summary>
        /// 버튼 활성/비활성만 담당한다. <c>sending=false</c>일 때 상태 라벨을 "대기 중."으로
        /// 덮어쓰지 않는다 — 예전에는 여기서 무조건 덮어써서, <see cref="OnSendClickAsync"/>가
        /// 직전에 <see cref="ShowResult"/>로 남긴 실패 원인 메시지(연결 거부/타임아웃 등)가
        /// finally 블록에서 곧바로 지워지는 결함이 있었다(2026-08-31 검증에서 발견 — H-1).
        /// 특히 <see cref="ShowResult"/>가 응답 본문이 없을 때 "위 상태 메시지 참고"라고
        /// 안내하는데 그 메시지 자체가 사라지면 사용자가 실패 원인을 확인할 방법이 없었다.
        /// </summary>
        private void SetSendingState(bool sending, string txType)
        {
            _btnSend.Enabled = !sending;
            _btnSelect501008.Enabled = !sending;
            _btnSelect800000.Enabled = !sending;
            _btnSelect902614.Enabled = !sending;
            if (sending)
            {
                _lblStatus.ForeColor = Color.DarkBlue;
                _lblStatus.Text = $"{txType} 전송 중… 응답 대기 중… (0.0초)";
            }
        }

        private void ShowResult(OneCapClientResult result)
        {
            _lblStatus.Text = $"[결과: {result.Kind}] {result.Message}" +
                (result.Error != null ? $" / 예외: {result.Error.GetType().Name}: {result.Error.Message}" : string.Empty);
            _lblStatus.ForeColor = result.Kind == OneCapClientResultKind.Success ? Color.DarkGreen : Color.DarkRed;

            if (result.Kind != OneCapClientResultKind.Success || result.ResponseBody == null)
            {
                // 전송(프레이밍) 자체가 실패한 경우 — 응답 본문이 없으므로 필드 분해/코드 해설도 없다.
                // 이 실패는 TelegramCodec/OneCapClient 계층 문제이지, SPEC #7 응답 코드 체계와는 다른 층이다.
                ClearResponseDisplay();
                _lblResponseCode.Text = "#7 응답 코드: (응답 본문 없음 — 전송/수신 자체가 실패했다. 위 상태 메시지 참고)";
                _lblResponseCode.ForeColor = Color.DarkRed;
                return;
            }

            _responseTextBox.Text = Cp949.GetString(result.ResponseBody);

            if (_lastRequestSchema == null || _lastRequestBody == null)
            {
                // 방어적 분기 — OnSendClickAsync가 항상 먼저 스냅샷을 남기므로 실제로는 발생하지 않는다.
                _lblResponseCode.Text = "#7 응답 코드: (직전 요청 스냅샷을 찾을 수 없어 분해할 수 없음)";
                _lblResponseCode.ForeColor = Color.DarkRed;
                return;
            }

            ShowFieldDecomposition(_lastRequestSchema, _lastRequestBody, result.ResponseBody);
        }

        /// <summary>
        /// 응답 본문을 요청과 같은 스키마로 분해해 그리드에 나란히 채우고, <c>#7 응답 코드</c>를
        /// 해설하고, 902614 응답의 <c>#51</c>은 값 자체를 절대 화면에 찍지 않고 마스킹 문구로만
        /// 보여준다(Phase 19 실행계획서 P19-6, PRD §8.4).
        /// </summary>
        private void ShowFieldDecomposition(TelegramSchema schema, byte[] requestBody, byte[] responseBody)
        {
            _responseGrid.Rows.Clear();

            if (responseBody.Length != schema.TotalLength)
            {
                // TelegramBuffer(schema, body) 생성자가 곧 예외를 던질 상황이므로, 필드 그리드
                // 분해는 여기서 포기한다(예외를 그대로 밖으로 던지지 않는다). 다만 E41(알 수 없는
                // 거래구분) 응답은 공통부 70바이트만 오는 게 정상이라(PosUnknownTransactionErrorResponse,
                // 본 앱 쪽 문서로만 확인 — 소스 참조 없음) 이 분기를 가장 자주 타는 것이 바로 그
                // 케이스다. #7(POSITION=20, 길이=3)은 3전문 공통부에 공유되는 고정 위치이므로,
                // 본문이 그 위치까지만 있어도 스키마 없이 직접 읽어 보여준다(2026-08-31 검증에서
                // "E41 응답에서 #7이 아예 안 보인다"는 결함으로 발견 — M-1). 70바이트에도 못 미치는
                // 비정상 응답은 그마저도 안 되므로 그 사실을 그대로 알린다.
                const int responseCodePosition = 20;
                const int responseCodeLength = 3;
                if (responseBody.Length >= responseCodePosition + responseCodeLength)
                {
                    string rawCode = Cp949.GetString(responseBody, responseCodePosition, responseCodeLength).TrimEnd(' ');
                    _lblResponseCode.Text =
                        $"#7 응답 코드: \"{rawCode}\" — {ResponseCodeCatalog.Describe(rawCode)} " +
                        $"(응답 본문이 {responseBody.Length}바이트뿐이라 전체 필드 그리드 분해는 불가 — 기대 {schema.TotalLength}바이트)";
                    _lblResponseCode.ForeColor = rawCode == "000" ? Color.DarkGreen : Color.DarkRed;
                }
                else
                {
                    _lblResponseCode.Text =
                        $"#7 응답 코드: (응답 본문이 {responseBody.Length}바이트뿐이라 #7 위치(20~22)조차 읽을 수 없음 — " +
                        $"기대 {schema.TotalLength}바이트)";
                    _lblResponseCode.ForeColor = Color.DarkRed;
                }
                return;
            }

            var reqBuffer = new TelegramBuffer(schema, requestBody);
            var respBuffer = new TelegramBuffer(schema, responseBody);
            bool isCardApproval = schema.TxType == "902614";

            foreach (var field in schema.Fields.OrderBy(f => f.Number))
            {
                string requestValue = reqBuffer.Read(field.Number);
                string responseValueDisplay;
                bool isMaskedPinField = isCardApproval && field.Number == 51;
                bool isWarning = false;

                if (isMaskedPinField)
                {
                    // PRD §8.4 / Phase 18 H-1·H-2: #51(암호화된 비밀번호 정보)은 값 자체를
                    // 화면(과 로그)에 절대 노출하지 않는다. 길이만으로 정상/경고를 판단한다.
                    string rawMasked = respBuffer.Read(field.Number); // 문자 계열이라 TrimEnd(' ')된 실값(또는 빈 문자열).
                    int byteLength = Cp949.GetByteCount(rawMasked);
                    if (byteLength == 0)
                    {
                        responseValueDisplay = "정상(공백)";
                    }
                    else
                    {
                        responseValueDisplay = $"경고: 길이 {byteLength}의 값이 실려 있음(값은 표시하지 않음)";
                        isWarning = true;
                    }
                }
                else
                {
                    responseValueDisplay = respBuffer.Read(field.Number);
                }

                int rowIndex = _responseGrid.Rows.Add(
                    field.Number,
                    field.Name,
                    DescribeSetLocation(field),
                    requestValue,
                    responseValueDisplay);

                var row = _responseGrid.Rows[rowIndex];
                bool differs = !isMaskedPinField && requestValue != responseValueDisplay;

                if (isWarning)
                {
                    // #51에 값이 실려 있는 것은 결함 정황이므로 강한 경고색으로 구분한다.
                    row.Cells[RespColResponseValue].Style.BackColor = Color.MistyRose;
                    row.Cells[RespColResponseValue].Style.ForeColor = Color.DarkRed;
                }
                else if (differs)
                {
                    // 원캡(또는 인터넷지로/VAN)이 요청과 다른 값으로 채운 필드 — 실제로 채워졌음을 강조.
                    row.Cells[RespColResponseValue].Style.BackColor = Color.LightYellow;
                }
            }

            string responseCode = respBuffer.Read(7);
            _lblResponseCode.Text = $"#7 응답 코드: \"{responseCode}\" — {ResponseCodeCatalog.Describe(responseCode)}";
            _lblResponseCode.ForeColor = responseCode.Trim() == "000" ? Color.DarkGreen : Color.DarkRed;

            if (isCardApproval)
            {
                string rawPin = respBuffer.Read(51);
                _lblField51Warning.Text = Cp949.GetByteCount(rawPin) == 0
                    ? "#51(암호화된 비밀번호 정보): 정상(공백)"
                    : $"#51(암호화된 비밀번호 정보): 경고 — 길이 {Cp949.GetByteCount(rawPin)}의 값이 실려 있음(값은 표시하지 않음, PRD §8.4 위반 정황)";
                _lblField51Warning.ForeColor = Cp949.GetByteCount(rawPin) == 0 ? Color.DarkGreen : Color.DarkRed;
            }
            else
            {
                _lblField51Warning.Text = string.Empty;
            }
        }
    }
}
