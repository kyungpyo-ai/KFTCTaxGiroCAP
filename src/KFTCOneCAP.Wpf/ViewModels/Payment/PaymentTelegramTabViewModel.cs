using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KFTCOneCAP.Wpf.Protocol.Pos;
using KFTCOneCAP.Wpf.Services.Pos;

namespace KFTCOneCAP.Wpf.ViewModels.Payment;

/// <summary>
/// Phase 29 P29-6(docs/payment_relay/development_plan.md "P29-6", PRD.md §12.5) — 결제 화면 탭 1개
/// (501008/800000/902614 중 하나)의 ViewModel. 요청 필드 목록(편집 가능) + 응답 필드 목록(읽기전용) +
/// 임의값 재생성/전송 커맨드를 갖는다.
///
/// <b>2026-09-18 사용자 지적으로 UI 재작업</b> — 처음엔 DataGrid 표 + hex/CP949 원문 패널까지 보여주는
/// "엔지니어 진단 화면"으로 만들었으나, 참고 이미지(<c>docs/home_reader_setup/screenshots/pay_screen.png</c>)
/// 처럼 "레이블+값" 카드/행 목록만 보여주는 쪽으로 단순화했다 — 원문 hex/텍스트 패널은 화면에서 완전히
/// 뺐다(development_plan.md P29-6 원문 지시보다 이 사용자 확정이 우선). 그 대신 실제 소켓에 나가는
/// 바이트는 여전히 <see cref="_requestTelegram"/>이 정확히 들고 있다 — 화면에 보여주지 않을 뿐 전송
/// 로직 자체는 바뀌지 않았다.
///
/// <b>전송 경로는 소켓(localhost:8002)뿐이다</b>(PRD §12.4) — <c>PaymentOrchestrator</c>를 직접 호출하지
/// 않는다. <see cref="PosClient"/> 인스턴스는 전송 1회(연결→전송→응답 수신)마다 새로 만들고 끝나면 버린다
/// (그 클래스 계약 — 실패 후 인스턴스는 재사용하지 않는다).
///
/// 이 클래스는 WPF 타입을 전혀 참조하지 않는다(ViewModels → Services → Protocol 단방향 계층 규칙,
/// docs/payment_relay/ROADMAP.md "계층 구조"). 로그는 이 화면이 직접 남기지 않는다 — <c>PosSocketServer</c>/
/// <c>PaymentOrchestrator</c>가 이미 자기 마스킹 규칙대로 로그를 남기므로, 여기서 또 남기면 화면이 일부러
/// 마스킹하지 않은 원문(PRD §12.5)이 로그로 새는 경로를 새로 만들게 된다.
/// </summary>
public sealed partial class PaymentTelegramTabViewModel : ObservableObject
{
    /// <summary>SPEC #4 거래 구분 코드 필드 번호. <see cref="PosRequestTelegram.Parse"/>가 이 필드만
    /// 보고 스키마를 라우팅하므로(<c>PosSchemaRegistry.TryResolve</c>), 임의값 생성기가 채운 무작위 값을
    /// 그대로 보내면 서버가 전문 자체를 식별하지 못해(E41) 이 화면의 왕복 검증 목적이 성립하지 않는다.
    /// <see cref="PosRandomValueGenerator"/>는 필드 이름/업무 의미를 보지 않는다는 원칙(PRD §12.3)을
    /// 지키려고 이 필드를 특별 취급하지 않으므로, 여기서 생성 직후 이 필드만 스키마의 고정값으로
    /// 되돌린다 — 이건 "값의 업무 의미"가 아니라 "전문을 어느 스키마로 파싱할지"를 결정하는 프로토콜
    /// 라우팅 키라 §12.3이 피하려는 것과 다른 층위다.
    /// </summary>
    private const int TransactionTypeFieldNumber = 4;

    /// <summary>Regenerate()의 902614 #27/#28 축소 난수 상한 — Services/Van/StubVanRelayService.cs의
    /// 같은 이름 상수와 스케일을 맞췄다(체크포인트 1 M-2 수정과 같은 성격, 2026-09-23 사용자 승인).
    /// 이 클래스는 Services 계층을 참조하지 않으므로(계층 규칙) 상수를 직접 여기 복제한다.</summary>
    private const int RealisticAmountMaxExclusive8Digits = 100_000_000; // 0~99,999,999

    private const int RealisticFeeMaxExclusive6Digits = 1_000_000; // 0~999,999

    private static readonly Random RandomAmountSource = new();

    private readonly PosTelegramSchema _schema;
    private readonly HashSet<int> _ownedByOneCap;

    /// <summary>2026-09-18 사용자 확정 — 요청 패널은 SET 장소에 kiosk가 포함된 필드만 보여준다(kiosk가
    /// 실제로 채워 보내는 자리이므로). kiosk가 전혀 없는 필드(원캡 단독/인터넷지로/VAN/디지털예산)는
    /// 요청에서 아예 빠지고 응답 쪽에서만 보인다 — "카드리딩 필드는 요청에 회색으로 보인다"던 이전 설계는
    /// 이 확정으로 대체됐다.</summary>
    private readonly HashSet<int> _kioskFieldNumbers;

    /// <summary>
    /// 응답 패널에 보여줄 필드 번호 — <b>kiosk 외의 주체가 하나라도 SET 장소에 있으면</b> 포함한다(2026-09-18
    /// 2차 재확정). 요청·응답은 배타적 분리가 아니다 — 같은 필드라도 "전문을 송신하는 기관에서 SET"하는
    /// 방향성 필드(예: `#6`/`#8`)는 kiosk가 요청에 값을 실어 보내고, 그 응답에서는 다른 주체(인터넷지로 등)가
    /// 다른 값으로 채워 돌려준다(SPEC p.6 각주) — 그래서 kiosk+다른 주체 조합 필드는 요청·응답 양쪽에 다
    /// 나타난다. 원캡 단독 담당 필드만 예외적으로 제외한다(원캡이 채운 카드번호/PIN 등은 화면에 노출할
    /// 이유가 없다) — <b>단 800000의 `#14`(BIN)</b>는 원캡 담당이어도 보여준다(카드 정보 조회 전문의
    /// 유일한 목적이 그 조회 결과이기 때문). 902614의 원캡 담당 8개(카드번호/PIN 등 민감정보)는 이 예외에
    /// 해당하지 않아 응답에도 나타나지 않는다.
    /// </summary>
    private readonly HashSet<int> _responseFieldNumbers;

    private readonly Func<TimeSpan> _responseTimeoutProvider;

    private PosTelegram _requestTelegram;
    private bool _suppressRowChangeHandling;

    /// <summary>Phase 30 P30-4(PRD §13.6) — 전송 성공 시 저장하는 마지막 응답 전문. 이 화면 창(정확히는
    /// 이 탭 VM 인스턴스) 생존 기간 동안만 메모리에 유지한다 — SQLite 등 영속 저장소에 쓰지 않는다.
    /// <see cref="Regenerate"/>가 호출되면 즉시 비운다: <see cref="HasResponse"/>가 false인데 이전 응답이
    /// 캐시에 남아 있으면 다른 탭이 그 값을 계속 읽어가는 모순이 생기기 때문이다.</summary>
    private PosTelegram? _lastResponseTelegram;

    public PaymentTelegramTabViewModel(string tabTitle, PosTelegramSchema schema, Func<TimeSpan> responseTimeoutProvider)
    {
        TabTitle = tabTitle;
        _schema = schema;
        _ownedByOneCap = new HashSet<int>(schema.FieldsOwnedByOneCap().Select(f => f.Number));
        _kioskFieldNumbers = new HashSet<int>(
            schema.Fields.Where(f => f.Owners.HasFlag(PosFieldOwner.Kiosk)).Select(f => f.Number));

        bool isCardInfoInquiry = schema.TransactionTypeCode == "800000";
        _responseFieldNumbers = new HashSet<int>(schema.Fields
            .Where(f => HasNonKioskOwner(f)
                && (!_ownedByOneCap.Contains(f.Number) || (isCardInfoInquiry && f.Number == 14)))
            .Select(f => f.Number));

        _responseTimeoutProvider = responseTimeoutProvider;

        RegenerateCommand = new RelayCommand(Regenerate);
        SendCommand = new AsyncRelayCommand(SendAsync);

        _requestTelegram = null!; // Regenerate()가 즉시 채운다(아래 호출).
        Regenerate();
        RefreshStatusBadge();
    }

    /// <summary>SET 장소에 kiosk 외의 주체(원캡/인터넷지로/VAN/디지털예산)가 하나라도 있는지 —
    /// kiosk 단독 필드만 이 조건을 만족하지 않는다.</summary>
    private static bool HasNonKioskOwner(PosField field) =>
        (field.Owners & ~PosFieldOwner.Kiosk) != PosFieldOwner.None;

    public string TabTitle { get; }

    public string TransactionTypeCode => _schema.TransactionTypeCode;

    public ObservableCollection<PosFieldRowViewModel> RequestRows { get; } = new();

    public ObservableCollection<PosFieldRowViewModel> ResponseRows { get; } = new();

    [ObservableProperty]
    private bool hasResponse;

    [ObservableProperty]
    private bool isSending;

    /// <summary>체크포인트 2 M-2 수정(2026-09-21) — 다른 탭이 전송 중일 때 true. 이 탭 자신의
    /// <see cref="IsSending"/>과는 별개다(자기 자신이 전송 중일 땐 이 값은 false로 유지된다 —
    /// <see cref="PaymentScreenViewModel"/>이 "나를 제외한 나머지 중 하나라도 전송 중"으로 계산해서
    /// 대입한다). <see cref="IsSendBlocked"/>가 두 값을 합쳐 전송/재생성 버튼을 막는다.</summary>
    [ObservableProperty]
    private bool isBlockedByOtherTab;

    /// <summary>전송/재생성 버튼을 막아야 하는지 — 자기 자신이 전송 중이거나 다른 탭이 전송 중일 때.
    /// XAML이 이 값 하나만 보고 두 버튼의 IsEnabled를 결정한다(Views/PaymentScreenWindow.xaml).</summary>
    public bool IsSendBlocked => IsSending || IsBlockedByOtherTab;

    partial void OnIsBlockedByOtherTabChanged(bool value) => OnPropertyChanged(nameof(IsSendBlocked));

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private bool isStatusError;

    /// <summary>응답 카드 헤더의 상태 배지 문구(참고 이미지의 "정상 완료" 알약 — PRD §12.5 UI 재작업).
    /// <see cref="RefreshStatusBadge"/>가 <see cref="HasResponse"/>/<see cref="IsStatusError"/>/
    /// <see cref="IsSending"/> 변화마다 다시 계산한다.</summary>
    [ObservableProperty]
    private string statusBadgeText = "미전송";

    /// <summary>배지 색조 — true면 녹색(성공), false면 중립/오류(회색 또는 빨강, <see cref="StatusBadgeIsError"/>로 구분).</summary>
    [ObservableProperty]
    private bool statusBadgeIsPositive;

    /// <summary>배지 색조 — true면 빨강(오류). <see cref="StatusBadgeIsPositive"/>와 동시에 true가 될 수 없다.</summary>
    [ObservableProperty]
    private bool statusBadgeIsError;

    public IRelayCommand RegenerateCommand { get; }

    public IAsyncRelayCommand SendCommand { get; }

    partial void OnHasResponseChanged(bool value) => RefreshStatusBadge();

    partial void OnIsStatusErrorChanged(bool value) => RefreshStatusBadge();

    partial void OnIsSendingChanged(bool value)
    {
        RefreshStatusBadge();
        OnPropertyChanged(nameof(IsSendBlocked));
    }

    private void RefreshStatusBadge()
    {
        // 2026-09-21 체크포인트 2 L-4 수정 — 원래는 !HasResponse를 IsStatusError보다 먼저 봐서, 전송
        // 계층 실패(연결 실패/타임아웃 등 SendAsync의 catch(Exception) 경로, HasResponse=false·
        // IsStatusError=true)와 필드 길이 초과(OnRequestRowValueChanged) 둘 다 배지가 "미전송"으로
        // 뜨고 좌측의 빨간 "전송 실패: ..." StatusMessage와 모순됐다. IsStatusError를 먼저 봐서 두
        // 경로 다 "오류" 배지가 뜨게 한다 — "미전송"은 이제 IsStatusError 없이 그냥 한 번도 안 보낸
        // 초기 상태(Regenerate 직후)에만 해당한다.
        if (IsSending)
        {
            StatusBadgeText = "전송 중";
            StatusBadgeIsPositive = false;
            StatusBadgeIsError = false;
        }
        else if (IsStatusError)
        {
            StatusBadgeText = "오류";
            StatusBadgeIsPositive = false;
            StatusBadgeIsError = true;
        }
        else if (!HasResponse)
        {
            StatusBadgeText = "미전송";
            StatusBadgeIsPositive = false;
            StatusBadgeIsError = false;
        }
        else
        {
            StatusBadgeText = "응답 수신";
            StatusBadgeIsPositive = true;
            StatusBadgeIsError = false;
        }
    }

    private void Regenerate()
    {
        if (IsSending)
            return;

        _requestTelegram = PosRandomValueGenerator.GenerateRandomRequest(_schema);

        // TransactionTypeFieldNumber 주석 참고 — 라우팅이 성립하도록 고정값으로 되돌린다.
        _requestTelegram.Write(TransactionTypeFieldNumber, _schema.TransactionTypeCode);

        // 체크포인트 1 M-2와 같은 성격의 사전 조치(2026-09-23 사용자 승인) — 902614 #27(납부 세액)과
        // #28(수수료)은 #29=#27+#28 자기참조 합산(RecomputeSelfReferencingChainTargets 아래)의 소스다.
        // PosRandomValueGenerator가 채우는 전 자리 N15 난수를 그대로 두면, 501008만 먼저 보내 #27이
        // 연쇄로 현실적인 값으로 바뀌는 순간(#28은 아직 연쇄 전이라 여전히 거대 난수) #29 재계산이
        // 자리수 초과로 거의 항상 실패한다. StubVanRelayService의 금액 필드 특별 취급과 같은 스케일로
        // 맞춰 여기서도 작은 범위로 다시 채운다.
        if (_schema.TransactionTypeCode == TelegramFieldChainMap.CardApproval902614)
        {
            _requestTelegram.Write(27,
                RandomAmountSource.Next(0, RealisticAmountMaxExclusive8Digits).ToString(CultureInfo.InvariantCulture));
            _requestTelegram.Write(28,
                RandomAmountSource.Next(0, RealisticFeeMaxExclusive6Digits).ToString(CultureInfo.InvariantCulture));
        }

        RebuildRequestRows();
        ApplyFixedChainValues();

        // _lastResponseTelegram을 비운다 — HasResponse=false인데 이전 응답이 캐시에 남아 있으면 다른
        // 탭이 그 값을 계속 읽어가는 모순이 생긴다(PRD §13.6).
        _lastResponseTelegram = null;

        ResponseRows.Clear();
        HasResponse = false;
        StatusMessage = string.Empty;
        IsStatusError = false;
    }

    /// <summary>Phase 30 P30-4(PRD §13.3 "902614 #34 = 고정 '00'") — 출처 전문이 없는 고정값 연쇄를
    /// 적용한다. 어느 전문을 보냈는지와 무관하게 <see cref="Regenerate"/> 직후 항상 적용된다.</summary>
    private void ApplyFixedChainValues()
    {
        foreach (TelegramFieldChainMap.ChainEntry entry in TelegramFieldChainMap.Entries)
        {
            if (entry.Conversion != FieldChainConversion.Fixed || entry.TargetTelegram != _schema.TransactionTypeCode)
                continue;

            string computed = TelegramFieldChainConverter.Convert(
                entry.Conversion, Array.Empty<string>(), _schema[entry.TargetFieldNumber].Length, entry.FixedValue);
            ApplyChainedValue(entry.TargetFieldNumber, computed);
        }
    }

    /// <summary>Phase 30 P30-4(PRD §13.3) — <see cref="PaymentScreenViewModel"/>(다른 탭 응답 연쇄) 및
    /// <see cref="RecomputeSelfReferencingChainTargets"/>(같은 탭 내부 자기참조 합산)가 공유하는 값 적용
    /// 경로. 기존 <see cref="OnRequestRowValueChanged"/> 파이프라인(Value setter → _requestTelegram.Write)을
    /// 그대로 재사용한다 — 새 파이프라인을 만들지 않는다.</summary>
    internal void ApplyChainedValue(int fieldNumber, string value)
    {
        PosFieldRowViewModel? row = RequestRows.FirstOrDefault(r => r.Number == fieldNumber);
        if (row is null)
            return; // 현재 연쇄 대상은 전부 kiosk 소유 필드라 RequestRows에 항상 있어야 정상 — 방어적 처리.

        row.IsChainedField = true;
        row.Value = value; // Value setter가 OnRequestRowValueChanged를 통해 _requestTelegram.Write까지 반영.
    }

    /// <summary>Phase 30 P30-4(PRD §13.6) — 다른 탭(<see cref="PaymentScreenViewModel"/>)이 이 응답을
    /// 읽어 연쇄를 적용할 때 쓴다. 선행 응답이 없으면 null을 돌려주고, 호출자는 그 필드를 건드리지 않는다
    /// (PRD §13.4 — 순서를 강제하지 않는다).</summary>
    internal string? TryReadResponseField(int fieldNumber) => _lastResponseTelegram?.Read(fieldNumber);

    /// <summary>Phase 30 P30-4 — 같은 전문 내부 자기참조 합산(현재 902614 #29 = #27+#28 1건)에 쓴다.</summary>
    internal string ReadRequestField(int fieldNumber) => _requestTelegram.Read(fieldNumber);

    /// <summary>Phase 30 P30-4 — 연쇄 값 변환(<see cref="TelegramFieldChainConverter.Convert"/>) 호출 시
    /// 대상 필드의 CP949 바이트 길이 한도를 넘긴다(절삭 변환에 필요).</summary>
    internal int GetFieldLength(int fieldNumber) => _schema[fieldNumber].Length;

    /// <summary>요청 패널 = kiosk 담당 필드만(클래스 필드 <see cref="_kioskFieldNumbers"/> 주석 참고).
    /// 전부 kiosk가 실제로 채워 보내는 값이므로 항상 편집 가능하다 — "카드리딩 필드를 회색으로 섞어
    /// 보여주는" 이전 설계는 더 이상 쓰지 않는다(그런 필드는 애초에 이 목록에 없다).</summary>
    private void RebuildRequestRows()
    {
        _suppressRowChangeHandling = true;
        try
        {
            RequestRows.Clear();
            foreach (PosField field in _schema.Fields)
            {
                if (!_kioskFieldNumbers.Contains(field.Number))
                    continue;

                RequestRows.Add(new PosFieldRowViewModel(
                    field,
                    _requestTelegram.Read(field.Number),
                    isReadOnly: false,
                    isCardReadingField: false,
                    onValueChanged: OnRequestRowValueChanged));
            }
        }
        finally
        {
            _suppressRowChangeHandling = false;
        }
    }

    /// <summary>요청 카드 편집(PRD §12.5 "값은 편집 가능")이 있을 때마다 전송용 <see cref="_requestTelegram"/>
    /// 에 즉시 반영한다 — 화면에는 원문을 더 보여주지 않지만, 실제로 나갈 바이트는 이 시점에 확정된다.</summary>
    private void OnRequestRowValueChanged(PosFieldRowViewModel row)
    {
        if (_suppressRowChangeHandling)
            return;

        try
        {
            _requestTelegram.Write(row.Number, row.Value ?? string.Empty);

            if (IsStatusError)
            {
                StatusMessage = string.Empty;
                IsStatusError = false;
            }

            // Phase 30 P30-4(PRD §13.7) — 같은 전문 안의 자기참조 합산(902614 #29 = #27+#28)을 재계산한다.
            // ApplyChainedValue가 대상 행의 Value를 세팅하면 이 메서드를 통해 재귀적으로 다시 호출되지만
            // #29는 다른 항목의 소스가 아니므로(TelegramFieldChainMap에 그런 항목 없음) 1단계에서 멈춘다.
            // TelegramFieldChainConverter.Convert가 던질 수 있는 PosProtocolException(합산 자리수 초과/
            // 비숫자 입력)은 아래 catch가 그대로 잡는다 — 새 catch를 따로 두지 않는다.
            RecomputeSelfReferencingChainTargets(row.Number);
        }
        catch (PosProtocolException ex)
        {
            // 필드 길이 초과 — Write가 부분 실패해도 _requestTelegram의 다른 필드는 이미 반영된 상태
            // 그대로 유지된다(PosField.Pad가 예외를 던지는 시점엔 아직 바이트를 옮겨 적지 않는다).
            StatusMessage = $"필드 #{row.Number}({row.Name}) 값이 길이({row.Length}바이트)를 초과합니다: {ex.Message}";
            IsStatusError = true;
        }
    }

    /// <summary>같은 전문(현재는 902614뿐) 안에서 방금 바뀐 필드를 출처로 삼는 연쇄를 재계산한다
    /// (PRD §13.7 — 현재 #29 = #27+#28 1건). 다른 전문에서 오는 연쇄는 <see cref="PaymentScreenViewModel"/>
    /// 이 처리하므로 여기서는 <c>SourceTelegram == TargetTelegram</c>인 항목만 본다.</summary>
    private void RecomputeSelfReferencingChainTargets(int changedFieldNumber)
    {
        foreach (TelegramFieldChainMap.ChainEntry entry in TelegramFieldChainMap.Entries)
        {
            if (entry.SourceTelegram != _schema.TransactionTypeCode || entry.TargetTelegram != _schema.TransactionTypeCode)
                continue; // 자기참조 항목만(다른 전문에서 오는 건 PaymentScreenViewModel이 처리).
            if (!entry.SourceFieldNumbers.Contains(changedFieldNumber))
                continue;

            var sourceValues = entry.SourceFieldNumbers.Select(ReadRequestField).ToList();
            string computed = TelegramFieldChainConverter.Convert(
                entry.Conversion, sourceValues, GetFieldLength(entry.TargetFieldNumber), entry.FixedValue);
            ApplyChainedValue(entry.TargetFieldNumber, computed);
        }
    }

    private async Task SendAsync()
    {
        if (IsSending)
            return;

        IsSending = true;
        StatusMessage = "전송 중...";
        IsStatusError = false;

        try
        {
            byte[] requestBody = _requestTelegram.ToBody();
            TimeSpan timeout = _responseTimeoutProvider();

            byte[] responseBody;
            using (var client = new PosClient())
            {
                await client.ConnectAsync().ConfigureAwait(true);
                responseBody = await client.SendAsync(requestBody, timeout).ConfigureAwait(true);
            }

            // Phase 30 P30-4(PRD §13.6) — _lastResponseTelegram을 HasResponse=true보다 먼저 채운다.
            // HasResponse setter가 PropertyChanged를 동기적으로 발생시키고, PaymentScreenViewModel이
            // 그 이벤트를 받아 곧장 TryReadResponseField로 캐시를 읽어 연쇄를 적용하므로(OnTabPropertyChanged
            // → ApplyChainMappingsFrom), 순서가 바뀌면 캐시가 아직 비어 있는 시점에 읽혀 연쇄가 조용히
            // 스킵된다(실제로 이 순서 오류로 501008→902614 #14 연쇄가 적용되지 않는 것을 화면 검증 중
            // 발견해 이 순서로 고쳤다).
            try
            {
                PosTelegram responseTelegram = PosTelegram.FromBytes(_schema, responseBody);
                _lastResponseTelegram = responseTelegram; // PRD §13.6 — 창 생존 기간 동안만 메모리 캐시.
                RebuildResponseRows(responseTelegram);
                StatusMessage = "전송 완료 — 응답 수신됨";
                IsStatusError = false;
            }
            catch (PosProtocolException ex)
            {
                // 이번 단계(P29-6)는 스텁 VAN이 요청을 clone하는 경로만 쓰므로 실제로는 항상 스키마
                // 총 길이와 일치해야 정상이다 — 그래도 예외를 화면 밖으로 흘려 창을 죽이지 않는다.
                // _lastResponseTelegram은 비어 있는 채로 남는다 — 파싱에 실패한 응답으로 연쇄를 적용하면
                // 안 되므로 의도된 동작이다.
                ResponseRows.Clear();
                StatusMessage = $"응답 필드 파싱 실패(길이 불일치): {ex.Message}";
                IsStatusError = true;
            }

            HasResponse = true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"전송 실패: {ex.Message}";
            IsStatusError = true;
        }
        finally
        {
            IsSending = false;
        }
    }

    /// <summary>응답 패널 = kiosk 외 주체가 하나라도 있는 필드 중 원캡 담당은 제외(800000 #14 예외
    /// 포함) — <see cref="_responseFieldNumbers"/> 주석 참고. kiosk와 다른 주체가 같이 체크된 필드(예:
    /// #3/#6/#8)는 요청 패널에도 있지만 여기 응답 패널에도 나타난다 — 그 자리에 다른 주체가 채워 돌려준
    /// 값이 kiosk가 보낸 값과 다르기 때문이다(클래스 필드 주석 참고). VAN 스텁이 실제로 덮어쓰는 4개
    /// 필드(#3/#6/#7/#8)를 시각적으로 구분하던 것은 2026-09-18 사용자 지시로 없앴다 — 실 VAN이 붙으면
    /// 의미 없어질 표시라 지금부터 강조하지 않는다.</summary>
    private void RebuildResponseRows(PosTelegram responseTelegram)
    {
        ResponseRows.Clear();
        foreach (PosField field in _schema.Fields)
        {
            if (!_responseFieldNumbers.Contains(field.Number))
                continue;

            ResponseRows.Add(new PosFieldRowViewModel(
                field,
                responseTelegram.Read(field.Number),
                isReadOnly: true,
                isCardReadingField: _ownedByOneCap.Contains(field.Number)));
        }
    }
}
