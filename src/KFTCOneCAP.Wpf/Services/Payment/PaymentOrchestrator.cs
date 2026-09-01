using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using KFTCOneCAP.Wpf.Protocol.Pos;
using KFTCOneCAP.Wpf.Protocol.Pos.Schemas;
using KFTCOneCAP.Wpf.Protocol.Reader;
using KFTCOneCAP.Wpf.Services.Diagnostics;
using KFTCOneCAP.Wpf.Services.Reader;
using KFTCOneCAP.Wpf.Services.Settings;
using KFTCOneCAP.Wpf.Services.Storage;
using KFTCOneCAP.Wpf.Services.Van;

namespace KFTCOneCAP.Wpf.Services.Payment;

/// <summary>
/// Phase 15(docs/payment_relay/development_plan.md P15-6~P15-9)에서 만든 "요청 1건 = 결제 1건" 단일
/// 흐름을, Phase 17(P17-5)에서 **전문 종별 3분기**로 재구성했다 — SPEC 확보 결과 POS가 501008(고지내역
/// 조회, 카드리딩 없음)/800000(카드정보조회)/902614(승인요청) 3종 독립 전문을 보낸다는 것이 확인됐기
/// 때문이다(각 전문은 TCP 연결도 독립이다 — <c>Services/Pos/PosSocketServer</c>가 연결 단위로
/// <see cref="ProcessAsync"/>를 호출한다).
///
/// <code>
/// ProcessAsync(전문)
///  ├ 501008 → [알림창 PROCESSING] → VAN 중계 → 응답
///  ├ 800000 → [알림창 IC] → 무결성 선행 → 카드리딩 라운드 → BIN 채움 → [PROCESSING] → VAN 중계 → 응답
///  └ 902614 → [알림창 IC] → 무결성 선행 → 카드리딩 라운드 → [알림창 PIN](Phase 18) → 7+1개 필드 채움
///             → [PROCESSING] → VAN 중계 → 응답
/// </code>
///
/// <b>공통 부품은 그대로 재사용한다</b>(재구성 최대 리스크 관리 지점, P17-5) — <see
/// cref="RunCardReadingRoundsAsync"/>는 Phase 15/16이 26개 시나리오로 검증한 FALLBACK/12 재요청/단일
/// 유효 응답 게이트/취소·Timeout 경합 로직을 로직 변경 없이 그대로 담고 있다. 이 재구성에서 그 동작이
/// 하나도 바뀌지 않는 것이 목표다.
///
/// <b>응답은 두 경로(P17-3)</b>: VAN까지 도달하면 VAN이 준 바이트를 그대로 relay하고(<see
/// cref="PosResponseTelegram.Relay"/>), OneCAP이 VAN에 도달하기 전 자체 실패(취소/Timeout/설정화면/
/// 리더기 실패/전문 오류)하면 요청을 clone해 실패 코드만 얹는다(<see
/// cref="PosResponseTelegram.Failure(PosRequestTelegram, string)"/>). 이 클래스는 어느 경로도 직접
/// 판단하지 않는다 — <see cref="PosResultCodeMapper"/>가 열거값/outcome을 SPEC 코드 문자열로 바꾸는
/// 유일한 지점이다(Services/Payment 안에 "E01" 같은 리터럴이 이 클래스에 등장하지 않는다).
///
/// <see cref="TransactionQueue"/>가 이 클래스의 <see cref="ProcessAsync"/>를 처리 위임으로 받는다. 큐가
/// 워커 스레드 하나로 거래를 직렬화하므로, **이 클래스는 거래 사이에 어떤 가변 상태도 인스턴스 필드로
/// 들고 있지 않다**(Phase 16 체크포인트 리뷰 H-1 계승) — 거래 1건 동안만 살아 있어야 하는 것은 전부
/// <see cref="TransactionScope"/>에 담아 클로저로 넘긴다.
///
/// **생성자 인자 중 <see cref="_readerEndpoints"/>는 순서가 의미를 가진다** — 인덱스 0은 리더기1,
/// 인덱스 1은 리더기2에 대응한다. **정적 접근(<c>App.XXX</c>)을 이 클래스 안에서 하지 않는다.**
/// </summary>
internal sealed class PaymentOrchestrator
{
    // PRD §4.9 — 카드 입력 대기의 시작 데드라인 기본값. 거래 단위로 딱 하나만 존재하고 라운드마다 새로
    // 주지 않는다.
    private static readonly TimeSpan DefaultInitialCardReadDeadline = TimeSpan.FromSeconds(120);

    // PRD §4.9 — 새 사용자 입력 단계가 시작될 때마다 데드라인을 이만큼 연장한다(일반 규칙 — FALLBACK/12
    // 재요청뿐 아니라 Phase 18의 PIN 입력 단계도 같은 상수를 재사용한다).
    private static readonly TimeSpan UserInputStepExtension = TimeSpan.FromSeconds(30);

    // 리더기 명령 타임아웃 하한 — 실제 만료 판정은 PaymentDeadline이 독립적으로 내린다.
    private static readonly TimeSpan MinimumCommandTimeout = TimeSpan.FromSeconds(1);

    // ReaderSetupViewModel의 명령 타임아웃과 동일한 값을 쓴다(같은 0x61/0x62 시퀀스 공유).
    private static readonly TimeSpan IntegrityCommandTimeout = TimeSpan.FromSeconds(5);

    // 07/12 응답이 반복되면 무한 루프가 된다 — 최대 3라운드(최초 1 + 재요청 2)로 제한.
    private const int MaxCardReadRounds = 3;

    private const string AidIndexDefault = "0";
    private const string PinBlockInputRequiredDefault = "0";

    private const string Message1 = "1-----승인------";
    private const string Message2 = "2 카드를        ";
    private const string Message3 = "3    넣어주세요.";
    private const string Message4 = "4  IC  INSERT   ";

    private readonly IReadOnlyList<IReaderEndpoint> _readerEndpoints;
    private readonly Func<ReaderSettings> _loadSettings;
    private readonly IntegrityCheckStore _integrityStore;
    private readonly ObservedIdentityStore _observedIdentityStore;
    private readonly IPaymentNoticePresenter _presenter;
    private readonly IReaderSetupGate _readerSetupGate;
    private readonly IVanRelayService _vanRelay;
    private readonly TimeSpan _initialCardReadDeadline;

    internal PaymentOrchestrator(
        IReadOnlyList<IReaderEndpoint> readerEndpoints,
        IntegrityCheckStore integrityStore,
        ObservedIdentityStore observedIdentityStore,
        IPaymentNoticePresenter presenter,
        IReaderSetupGate readerSetupGate,
        IVanRelayService vanRelay,
        Func<ReaderSettings>? loadSettings = null,
        TimeSpan? initialCardReadDeadline = null)
    {
        _readerEndpoints = readerEndpoints;
        _loadSettings = loadSettings ?? new ReaderSettingsService().Load;
        _integrityStore = integrityStore;
        _observedIdentityStore = observedIdentityStore;
        _presenter = presenter;
        _readerSetupGate = readerSetupGate;
        _vanRelay = vanRelay;
        _initialCardReadDeadline = initialCardReadDeadline ?? DefaultInitialCardReadDeadline;
    }

    /// <summary><see cref="TransactionQueue"/>의 워커 스레드에서 호출된다. 전문 종별로 분기한다
    /// (P17-5) — 공통 게이트(설정화면)만 여기서 처리하고, 나머지는 각 Handle*Async가 맡는다.</summary>
    internal async Task<PosResponseTelegram> ProcessAsync(PosRequestTelegram request)
    {
        string txId = LogTxId(request);

        // ===== 공통 1단계 — 설정 화면 게이트(모든 전문 공통, 2026-08-25 확정 P15-4/2026-08-26 P17-5) =====
        if (_readerSetupGate.IsReaderSetupOpen)
        {
            // 개선권장 A-1(P22 리뷰) — 이 분기는 아래 switch 이전에 return하므로 133행 근처의 중앙화된
            // "거래 확정" 로그를 우회한다. 여기서 구조화 로그로 직접 남긴다(레거시 Warn(string) 호출은
            // 정보 중복이라 이 구조화 버전으로 대체했다).
            string gateRejectCode = PosResultCodeMapper.ToTelegramCode(PosPaymentResultCode.ReaderSetupInProgress);
            FileLogger.Warn(LogCategory.Payment, "[PaymentOrchestrator] 거래 확정 — 리더기 설정 화면 점유로 거부", gateRejectCode, txId);
            return PosResponseTelegram.Failure(request, gateRejectCode);
        }

        try
        {
            PosResponseTelegram response = request.TransactionTypeCode switch
            {
                NoticeInquirySchema.FixedTransactionType => await HandleNoticeInquiryAsync(request, txId).ConfigureAwait(false),
                CardInfoInquirySchema.FixedTransactionType => await HandleCardInfoInquiryAsync(request, txId).ConfigureAwait(false),
                CardApprovalSchema.FixedTransactionType => await HandleCardApprovalAsync(request, txId).ConfigureAwait(false),
                _ => throw new InvalidOperationException(
                    $"txId={txId} PosSchemaRegistry가 인식하는 전문만 여기 도달해야 함(라우팅은 P17-3 PosRequestTelegram.Parse가 이미 끝냄): '{request.TransactionTypeCode}'"),
            };

            // P22-6(PRD.md §1.5 경계 표 "거래 수명" — 거래 확정). 모든 분기(정상 relay/자체 실패)가
            // PosResponseTelegram 한 개로 수렴하는 이 지점에서 한 번만 남긴다 — 분기마다 흩어 찍지 않는다
            // (§1.5 "분량 감각" 위반 방지). 결과코드는 #7(응답 전문 공통부, 3전문 동일 POSITION)을 그대로
            // 읽는다 — PosResultCodeMapper가 이미 만든 값이라 여기서 새로 매핑하지 않는다.
            FileLogger.Info(LogCategory.Payment, "[PaymentOrchestrator] 거래 확정", response.Read(ResultCodeFieldNumber), txId);
            return response;
        }
        catch (Exception)
        {
            // 개선권장 A-2(P22 리뷰) — 예외 경로는 InternalError로 POS에 응답이 나가지만(TransactionQueue
            // 워커 루프가 처리) 이 지점의 중앙 확정 로그를 우회한다. 응답/워커 루프 동작은 바꾸지 않고
            // 로그 한 줄만 남긴 뒤 그대로 다시 던진다.
            FileLogger.Warn(LogCategory.Payment, "[PaymentOrchestrator] 거래 확정 — 내부 오류(InternalError)", code: null, txId);
            throw;
        }
    }

    /// <summary>SPEC 응답 공통부 <c>#7</c>(처리결과코드) — P22-6 로깅 전용. 필드 위치는 3전문 공통
    /// (<c>PosSocketServer.ResultCodeFieldNumber</c>와 동일한 값).</summary>
    private const int ResultCodeFieldNumber = 7;

    /// <summary>
    /// 501008 — 카드리딩이 없는 순수 중계(P17-5 확정 사항 4). 무결성 선행 판정도, 카드입력 데드라인도
    /// 없다 — 리더기가 하나도 설정되지 않은 상태에서도 정상 동작해야 한다. 알림창은 곧바로 통신중으로
    /// 띄운다.
    /// </summary>
    private async Task<PosResponseTelegram> HandleNoticeInquiryAsync(PosRequestTelegram request, string txId)
    {
        FileLogger.Info($"[PaymentOrchestrator] txId={txId} 501008(고지내역조회) — 카드리딩 없이 즉시 중계");

        _presenter.Show(PaymentNoticeState.VanProcessing);
        try
        {
            // 카드리딩이 없는 전문이라 초기화할 채택 리더기도 없다(H-3의 cardReadWinner=null 경우).
            return await RelayToVanAsync(request, txId, cardReadWinner: null).ConfigureAwait(false);
        }
        finally
        {
            _presenter.Close();
        }
    }

    /// <summary>
    /// 800000 — 카드리딩(BIN 8자리만 사용) 후 중계. 알림창 IC→(카드리딩 로직은 902614와 완전히 동일,
    /// P17-5 확정 사항 3)→PROCESSING.
    /// </summary>
    private async Task<PosResponseTelegram> HandleCardInfoInquiryAsync(PosRequestTelegram request, string txId)
    {
        FileLogger.Info($"[PaymentOrchestrator] txId={txId} 800000(카드정보조회) 시작");

        return await RunCardTransactionAsync(
            request, txId,
            amountFieldNumber: 15, // #15 납부세액
            requiresPin: false, // 800000은 PIN 단계가 없다(Phase 18 확정 사항 1) — #51 필드 자체가 없음
            fillOneCapFields: (winner, cardData, pin) =>
            {
                string cardNumber = cardData.CardNumber;
                if (cardNumber.Length < 8)
                {
                    winner.SendInvalidationInit();
                    FileLogger.Error($"[PaymentOrchestrator] txId={txId} 카드번호가 8자리 미만이라 BIN을 추출할 수 없음: 길이={cardNumber.Length}");
                    return PosResponseTelegram.Failure(request, PosResultCodeMapper.ReaderNoCardDataDefensiveCode);
                }

                string bin = cardNumber.Substring(0, 8);
                request.Telegram.Write(14, bin); // #14 BIN
                FileLogger.Info($"[PaymentOrchestrator] txId={txId} BIN 채움 완료 — VAN 중계로");
                return null; // null = 실패 아님, VAN 중계로 진행
            }).ConfigureAwait(false);
    }

    /// <summary>
    /// 902614 — 카드리딩(원캡 담당 7필드 채움) → PIN 입력(Phase 18, <c>requiresPin: true</c>) → 중계.
    /// <c>#51</c>(암호화된 비밀번호 정보)은 화면 키패드로 수집된 PIN(P18-4가 만든 통로)을
    /// <see cref="PinFieldEncoder.ToTelegramValue"/>로 변환해 채운다(P18-5) — SEED 암호화가 확정되면
    /// 그 메서드 본문만 바뀐다.
    /// </summary>
    private async Task<PosResponseTelegram> HandleCardApprovalAsync(PosRequestTelegram request, string txId)
    {
        FileLogger.Info($"[PaymentOrchestrator] txId={txId} 902614(신용카드 승인요청) 시작");

        return await RunCardTransactionAsync(
            request, txId,
            amountFieldNumber: 29, // #29 총 납부 금액
            requiresPin: true, // 902614만 PIN 단계를 거친다(Phase 18 확정 사항 1)
            fillOneCapFields: (winner, cardData, pin) =>
            {
                if (pin == null)
                {
                    throw new InvalidOperationException(
                        "902614(requiresPin: true) 흐름에서 PIN이 null임 — CollectPinAsync가 성공했을 때만 이 델리게이트에 도달해야 함");
                }

                FillCardApprovalFields(request, cardData, pin);
                FileLogger.Info($"[PaymentOrchestrator] txId={txId} 승인요청 필드 8종 채움 완료(#43~#46,#48,#50,#51,#53) — VAN 중계로");
                return null;
            }).ConfigureAwait(false);
    }

    /// <summary>
    /// #43/#44/#45/#46/#48/#50/#51/#53 8필드를 채운다 — #43~#46/#48/#50/#53 7필드는 카드리딩 응답으로,
    /// #51은 화면 키패드로 수집된 PIN을 <see cref="PinFieldEncoder"/>로 변환해 채운다(Phase 18 P18-5).
    ///
    /// <b>필드 매핑 근거(2026-08-26/27, 두 SPEC 문서 사이에 명시적 대응표가 없어 <c>
    /// reader-pinpad-spec-expert</c> 조사 + 사용자 확정으로 정함)</b>:
    /// <list type="bullet">
    /// <item><c>#44</c> FALLBACK CODE(N2) = <see cref="CardReadData.FallbackCode"/>(X1, '0'~'7') — 명칭·
    ///   값 체계가 리더기 SPEC과 정확히 일치. 좌측 0패딩은 <see cref="PosField.Pad"/>가 자동 처리한다.</item>
    /// <item><c>#45</c> 복호화 정보(AN18) = <see cref="CardReadData.KeyVersion"/>(2) + <see
    /// cref="CardReadData.Tc"/>(6) + <see cref="CardReadData.ModuleId"/>(10) = 18바이트(2026-08-27
    ///   사용자 확정 — 리더기 SPEC의 "리더기 암호화 정보"(X20)는 후보였으나 길이가 20으로 2바이트
    ///   초과해 채택하지 않았다).</item>
    /// <item><c>#46</c> 암호화된 카드정보(AN196) = <see cref="CardReadData.EncryptedData"/>(가변길이) —
    ///   196바이트를 초과하면 <see cref="PosField.Pad"/>가 예외를 던진다(조용히 잘리지 않는다). 실제로
    ///   초과하는지는 실장비 검증(P17-7) 대상.</item>
    /// <item><c>#48</c> 거래 입력 유형(AN1, 2/4/5) = <see cref="CardReadData.Wcc"/> 매핑(2026-08-27
    ///   사용자 확정): <c>I</c>(IC)→"5", <c>;</c>(Swipe)→"2", <c>P</c>(Pay-On)→"4". 그 외 값(RF/QR/
    ///   Key-IN 등, 이 결제 Flow가 다루는 IC/FALLBACK 범위 밖)은 예외.</item>
    /// <item><c>#51</c> 암호화된 비밀번호 정보(ANS100) = 화면 키패드로 수집된 PIN 4자리를
    ///   <see cref="PinFieldEncoder.ToTelegramValue"/>로 변환한 값(Phase 18 P18-5, SPEC SET 장소
    ///   표기 모순은 development_plan.md Phase 18 "착수 전 확인이 필요한 것" #3 참고 — 사용자 확정으로
    ///   원캡 담당). <b>이 필드 값은 어떤 로그에도 남기지 않는다.</b></item>
    /// <item><c>#53</c> EMV DATA(ANS604) = <c>"0600"</c>(4자리 고정 길이 서브필드, 항상 이 값) +
    ///   <see cref="CardReadData.EmvEncodedData"/>(가변길이, "EMV 인코딩 데이터") — 나머지는 space로
    ///   채워 총 604바이트를 맞춘다(2026-08-27 사용자 확정). 이 필드 자체가 "4바이트 길이 서브필드 +
    ///   최대 600바이트 가변 데이터" 내부 구조를 가진 것이라, 앞 4바이트는 실제 데이터 길이가 아니라
    ///   이 서브필드의 최대 용량(600)을 고정으로 적는다.</item>
    /// </list>
    /// </summary>
    private static void FillCardApprovalFields(PosRequestTelegram request, CardReadData cardData, string pin)
    {
        string readerAuthId = cardData.ReaderAuthId;
        if (readerAuthId.Length != 16)
        {
            throw new InvalidOperationException(
                $"#43 조합용 리더기 인증 식별 번호가 16자가 아님(VAN이 조용히 거절하기 전에 여기서 드러나야 함): 길이={readerAuthId.Length}");
        }

        string programId = ProgramIdentifier;
        if (programId.Length != 16)
            throw new InvalidOperationException($"프로그램 식별자 상수가 16자가 아님: '{programId}'(길이={programId.Length})");

        request.Telegram.Write(43, readerAuthId + programId); // 보안단말기 인증번호 = 리더기(16)+프로그램(16)
        request.Telegram.Write(44, cardData.FallbackCode);
        request.Telegram.Write(45, cardData.KeyVersion + cardData.Tc + cardData.ModuleId); // 2+6+10=18
        request.Telegram.Write(46, cardData.EncryptedData);
        request.Telegram.Write(48, MapTransactionInputType(cardData.Wcc));
        request.Telegram.Write(50, "2"); // 신용카드 승인 인증방식 고정값(SPEC p.17)
        request.Telegram.Write(51, PinFieldEncoder.ToTelegramValue(pin)); // 값 자체는 로그에 남기지 않는다
        request.Telegram.Write(53, EmvDataSubfieldLengthPrefix + cardData.EmvEncodedData);
    }

    /// <summary>#53 EMV DATA 내부의 4바이트 길이 서브필드 — 실제 데이터 길이가 아니라 이 서브필드의
    /// 최대 용량(600)을 항상 고정으로 적는다(2026-08-27 사용자 확정, 클래스 주석 참고).</summary>
    private const string EmvDataSubfieldLengthPrefix = "0600";

    private static string MapTransactionInputType(string wcc) => wcc switch
    {
        "I" => "5", // IC
        ";" => "2", // Swipe(MS)
        "P" => "4", // Pay-On
        _ => throw new InvalidOperationException(
            $"거래 입력 유형(#48)을 판단할 수 없는 WCC 값: '{wcc}' — 이 Flow는 IC/Swipe/Pay-On만 다룬다(RF/QR/Key-IN 등은 범위 밖)"),
    };

    /// <summary>
    /// 보안단말기 인증번호(#43)의 프로그램 식별자 절반(16자, 여신협회 등록값). SPEC 확정 전까지
    /// <c>KFTCTAXGIROCAP01</c>을 쓴다(2026-08-26 사용자 확정, development_plan.md P17-6) — 이 상수
    /// 하나만 바꾸면 되도록 이 자리에만 둔다.
    /// </summary>
    internal const string ProgramIdentifier = "KFTCTAXGIROCAP01";

    /// <summary>
    /// 800000/902614가 공유하는 전체 흐름: 무결성 선행 → 알림창 IC → 카드리딩 라운드 →
    /// (<paramref name="requiresPin"/>이면 PIN 입력, Phase 18 P18-4) → 필드 채움
    /// (<paramref name="fillOneCapFields"/>로 전문별 위임) → **VAN 중계까지**(P17-5 확정 사항 3 — 두
    /// 전문의 카드리딩 로직은 완전히 동일하다).
    ///
    /// <b>VAN 중계가 이 메서드 안에 있는 이유</b>(2026-08-27, Phase 17 최종 검증 H-2 수정): 알림창
    /// 수명(<c>Show</c>~<c>Close</c>)과 결과 확정 게이트 봉인이 <b>VAN 구간까지 포함해</b> 하나의
    /// try/finally로 감싸여야 한다. 처음엔 이 메서드가 카드리딩까지만 하고 호출자가 VAN을 이어받게
    /// 나눴는데, 그러면 <c>finally</c>의 <c>_presenter.Close()</c>가 VAN보다 <b>먼저</b> 실행돼
    /// <see cref="RelayToVanAsync"/>의 <c>ChangeState(VanProcessing)</c>이 이미 닫힌 창에 도착했다 —
    /// 실제 <c>PaymentNoticePresenter</c>는 그 호출을 "무시 + Warn 로그"로 처리하므로 **VAN 통신 중
    /// 화면이 사용자에게 전혀 보이지 않는** 결함이 됐다(PRD §4.10 위반). Phase 15/16이 원래 이 구조를
    /// 하나의 try/finally로 유지하고 있었고, Phase 17 재구성에서 분리하며 생긴 회귀다.
    ///
    /// <b>결과 확정(<c>Gate.TryClaim(FlowResult)</c>) 시점(Phase 18 P18-4 재배치)</b>: PIN 단계가 없으면
    /// (800000) 카드리딩 성공 직후 그대로 확정한다(기존과 동일). PIN 단계가 있으면(902614) <see
    /// cref="CollectPinAsync"/>가 취소/Timeout과 PIN 완료를 경합시킨 뒤 **PIN이 이긴 경우에만** 이
    /// 메서드로 돌아와 확정한다 — 카드리딩이 끝난 순간부터 VAN 진입 직전까지 취소·ESC·Timeout이 계속
    /// 동작해야 하기 때문이다(로드맵 확정 사항, PIN 입력 중에도 취소 가능해야 함).
    /// </summary>
    private async Task<PosResponseTelegram> RunCardTransactionAsync(
        PosRequestTelegram request, string txId, int amountFieldNumber, bool requiresPin,
        Func<IReaderEndpoint, CardReadData, string?, PosResponseTelegram?> fillOneCapFields)
    {
        // ===== 참여 후보 결정(§2.2.3) =====
        var settings = _loadSettings();
        string[] configuredPorts = { settings.Port1, settings.Port2 };
        var candidates = new List<IReaderEndpoint>();
        for (int i = 0; i < _readerEndpoints.Count && i < configuredPorts.Length; i++)
        {
            if (ComPortFormat.ToPortNumber(configuredPorts[i]) > 0)
                candidates.Add(_readerEndpoints[i]);
        }

        if (candidates.Count == 0)
        {
            FileLogger.Warn($"[PaymentOrchestrator] txId={txId} 설정된 리더기가 없음(양쪽 미사용) — 카드 리딩 시도 안 함");
            return PosResponseTelegram.Failure(request, PosResultCodeMapper.ToTelegramCode(PosPaymentResultCode.NoReaderConfigured));
        }

        // ===== 무결성 선행 판정(§4.2) =====
        var participants = new List<IReaderEndpoint>();
        foreach (IReaderEndpoint candidate in candidates)
        {
            string comPortDisplay = candidate.ComPortDisplay;

            if (_integrityStore.HasSuccessToday(comPortDisplay))
            {
                FileLogger.Info($"[PaymentOrchestrator] txId={txId} {comPortDisplay} 금일 무결성 성공 이력 있음 — 재검사 생략, 참여");
                participants.Add(candidate);
                continue;
            }

            IntegrityCheckSequenceOutcome outcome = await candidate
                .RunIntegrityCheckAsync(IntegrityCommandTimeout, IntegrityCommandTimeout)
                .ConfigureAwait(false);

            if (outcome.IsSuccess)
            {
                FileLogger.Info($"[PaymentOrchestrator] txId={txId} {comPortDisplay} 무결성 체크 성공 — 참여");
                participants.Add(candidate);
            }
            else
            {
                FileLogger.Warn($"[PaymentOrchestrator] txId={txId} {comPortDisplay} 무결성 체크 실패(Kind={outcome.Kind}) — 카드 리딩에서 제외");
            }
        }

        if (participants.Count == 0)
        {
            FileLogger.Warn($"[PaymentOrchestrator] txId={txId} 참여 가능한 리더기 없음(무결성 전원 실패)");
            return PosResponseTelegram.Failure(request, PosResultCodeMapper.ToTelegramCode(PosPaymentResultCode.IntegrityCheckFailure));
        }

        // ===== 알림창 + 카드 리딩 =====
        var scope = new TransactionScope(txId);
        using var deadline = new PaymentDeadline(_initialCardReadDeadline);
        _ = MonitorDeadlineAsync(deadline, scope);

        // P22-6(PRD.md §1.5 경계 표 "거래 수명" — 거래 시작). 501008은 카드입력 데드라인이 없어(클래스
        // 요약 참고) 이 로그가 없다 — 800000/902614만 여기를 지난다.
        FileLogger.Info(LogCategory.Payment, $"[PaymentOrchestrator] 거래 시작 — 카드입력 데드라인 {_initialCardReadDeadline.TotalSeconds:F0}초", code: null, txId);

        string transactionDateTime = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        string amount = request.Read(amountFieldNumber);

        EventHandler onCanceled = (_, _) => OnCanceled(scope);

        try
        {
            _presenter.Canceled += onCanceled;
            _presenter.Show(PaymentNoticeState.IcCardRequest);

            CardReadRoundResult roundResult = await RunCardReadingRoundsAsync(participants, amount, transactionDateTime, deadline, scope).ConfigureAwait(false);
            if (roundResult.EarlyFailureCode != null)
                return PosResponseTelegram.Failure(request, roundResult.EarlyFailureCode);

            // 902614만 여기서 PIN을 수집한다(Phase 18 P18-4) — 결과 확정은 PIN 대기가 끝난 뒤로
            // 미뤄진다(아래 TryClaim). PIN 대기 중 취소/Timeout이 이기면 CollectPinAsync가 이미
            // InterruptCode로 실패 응답을 만들어 돌려주므로 여기서는 그 값을 그대로 반환한다.
            string? pin = null;
            if (requiresPin)
            {
                PinCollectionResult pinResult = await CollectPinAsync(scope, deadline, txId).ConfigureAwait(false);
                if (pinResult.EarlyFailureCode != null)
                    return PosResponseTelegram.Failure(request, pinResult.EarlyFailureCode);

                pin = pinResult.Pin;
            }

            // VAN 진입 직전에 이 거래를 FlowResult로 확정한다(선착순 규칙이 VAN 경계에서도 지켜지는
            // 지점 — 2025-08-25/2026-08-26 계승). PIN 단계가 있는 902614는 이 확정 시점이 카드리딩
            // 직후가 아니라 **PIN 입력 완료 후**로 옮겨졌다(Phase 18 P18-4) — 그래야 PIN 입력 중에도
            // 취소·ESC·Timeout이 계속 유효하다.
            if (!scope.Gate.TryClaim(TransactionOutcomeReason.FlowResult))
            {
                TransactionOutcomeReason reason = scope.Gate.ClaimedReason!.Value;
                FileLogger.Info($"[PaymentOrchestrator] txId={txId} 카드 리딩(및 PIN 입력) 완료했으나 필드 채움 전 이미 확정됨({reason}) — 미진입");
                roundResult.Winner?.SendInvalidationInit();
                return PosResponseTelegram.Failure(request, InterruptCode(reason));
            }

            // VAN 구간부터는 취소가 결과를 바꾸지 않는다 — 위에서 이미 FlowResult로 확정했으므로
            // onCanceled가 이후에 불려도 TryClaim이 실패해 조용히 무시된다. 여기서는 구독만 끊는다.
            _presenter.Canceled -= onCanceled;

            PosResponseTelegram? fieldFillFailure = fillOneCapFields(roundResult.Winner!, roundResult.CardData!, pin);
            if (fieldFillFailure != null)
                return fieldFillFailure;

            // 알림창이 아직 열려 있는 상태에서 VAN으로 넘어간다(H-2 수정 — 클래스 주석 참고).
            // 채택된 리더기를 함께 넘긴다 — VAN 통신 실패 시 초기화 대상이다(PRD §4.10, H-3 수정).
            return await RelayToVanAsync(request, txId, roundResult.Winner).ConfigureAwait(false);
        }
        finally
        {
            _presenter.Canceled -= onCanceled;
            _presenter.Close();

            if (scope.Gate.TryClaim(TransactionOutcomeReason.FlowResult))
            {
                FileLogger.Warn($"[PaymentOrchestrator] txId={txId} 결과가 확정되지 않은 채 거래가 종료됨(예외 경로로 추정) — 대기 중이던 리더기를 정리한다");
                FireInterruptCleanup(TransactionOutcomeReason.FlowResult, scope);
            }
        }
    }

    /// <summary>
    /// Phase 18(P18-4) — 902614 카드리딩 성공 후 PIN 4자리를 화면 키패드로 수집한다. <see
    /// cref="RunCardReadingRoundsAsync"/>의 <c>Task.WhenAny(broadcastTask, interruptTask)</c>와 정확히
    /// 같은 대기 패턴을 쓴다 — 새 대기 규약을 만들지 않는다.
    ///
    /// <b>구독 순서가 이 메서드의 핵심이다</b>: <see cref="IPaymentNoticePresenter.PinEntered"/> 구독을
    /// <see cref="IPaymentNoticePresenter.ChangeState"/>(PinEntry) 호출 <b>전에</b> 건다. Phase 15 Opus
    /// 리뷰 H-1이 취소에서 정확히 이 실수를 잡았다(Show 뒤에 구독을 걸어 그 사이의 취소가 유실됨) —
    /// 순서가 반대면 <c>FakePaymentNoticePresenter.FirePinEnteredSynchronouslyOnChangeState</c>처럼
    /// <c>ChangeState</c> 안에서 즉시 발화하는 PIN 완료가 구독자 없이 허공에 사라진다.
    ///
    /// PIN 대기 중 취소/Timeout이 이기면 <see cref="InterruptCode"/>로 실패 코드를 만들어 돌려준다.
    /// **리더기 초기화(0x60)는 여기서 별도로 보내지 않는다** — 취소/Timeout 확정은
    /// <c>gate.TryClaim</c>이 성공하는 순간 <c>OnCanceled</c>/<c>MonitorDeadlineAsync</c>가 이미
    /// <see cref="FireInterruptCleanup"/>으로 <c>scope.PendingParticipants</c>(카드리딩 라운드 참여자,
    /// winner 포함) 전원에게 0x60을 예약해 뒀다(2026-08-27 체크포인트 리뷰 M-1 — 처음엔 여기서도
    /// <c>winner.SendInvalidationInit()</c>을 불렀는데, <see cref="RunCardReadingRoundsAsync"/>의
    /// 인터럽트 대기 경로(<c>firstCompleted == interruptTask</c>)가 의도적으로 이 호출을 하지 않는
    /// 것과 같은 이유로 중복이었다 — 정리 책임은 <see cref="FireInterruptCleanup"/> 한 곳에만 둔다).
    /// 구독 해제는 <c>finally</c>에서 항상 수행한다(<see cref="Canceled"/>와 같은 누수 검증 대상).
    /// </summary>
    private async Task<PinCollectionResult> CollectPinAsync(TransactionScope scope, PaymentDeadline deadline, string txId)
    {
        deadline.Extend(UserInputStepExtension);
        FileLogger.Info($"[PaymentOrchestrator] txId={txId} PIN 입력 단계 진입 — 데드라인 {UserInputStepExtension.TotalSeconds:F0}초 연장(남은데드라인={deadline.Remaining.TotalSeconds:F1}s)");

        var pinTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<PinEnteredEventArgs> onPinEntered = (_, e) => pinTcs.TrySetResult(e.Pin);

        try
        {
            _presenter.PinEntered += onPinEntered; // ★ ChangeState보다 반드시 먼저(위 클래스 주석 참고)
            _presenter.ChangeState(PaymentNoticeState.PinEntry);

            Task<string> pinTask = pinTcs.Task;
            Task interruptTask = scope.Gate.Interrupted;
            Task firstCompleted = await Task.WhenAny(pinTask, interruptTask).ConfigureAwait(false);

            if (firstCompleted == interruptTask)
            {
                TransactionOutcomeReason reason = scope.Gate.ClaimedReason!.Value;
                FileLogger.Info($"[PaymentOrchestrator] txId={txId} PIN 입력 대기 중 확정됨({reason}) — 즉시 실패 응답(리더기 초기화는 FireInterruptCleanup이 이미 예약함)");
                return PinCollectionResult.Early(InterruptCode(reason));
            }

            string pin = await pinTask.ConfigureAwait(false);
            FileLogger.Info($"[PaymentOrchestrator] txId={txId} PIN 4자리 입력 완료 — 통신중으로 진행(값은 로그에 남기지 않음)");
            return PinCollectionResult.Success(pin);
        }
        finally
        {
            _presenter.PinEntered -= onPinEntered;
        }
    }

    /// <summary>
    /// PRD §4.3~§4.7 카드 리딩 라운드. Phase 15/16이 검증한 로직은 손대지 않았다 — 시그니처만
    /// <see cref="PosRequestTelegram"/>/<see cref="PosResponseTelegram"/>에 의존하지 않도록 다듬었다
    /// (문자열 코드만 돌려주고 응답 전문 조립은 호출자 몫).
    /// </summary>
    private async Task<CardReadRoundResult> RunCardReadingRoundsAsync(
        IReadOnlyList<IReaderEndpoint> participants, string amount, string transactionDateTime,
        PaymentDeadline deadline, TransactionScope scope)
    {
        string txId = scope.TransactionId;
        TransactionOutcomeGate gate = scope.Gate;
        IReadOnlyList<IReaderEndpoint> roundParticipants = participants;
        string transactionTypeCode = TransactionInfoRequestBuilder.TransactionTypeIc;

        for (int round = 1; round <= MaxCardReadRounds; round++)
        {
            if (gate.ClaimedReason is { } claimedBeforeRound)
            {
                FileLogger.Info($"[PaymentOrchestrator] txId={txId} 카드 리딩 라운드 {round} 시작 전 이미 확정됨({claimedBeforeRound}) — 중단");
                return CardReadRoundResult.Early(InterruptCode(claimedBeforeRound));
            }

            TransactionInfoRequest infoRequest = transactionTypeCode == TransactionInfoRequestBuilder.TransactionTypeIc
                ? TransactionInfoRequestBuilder.CreateIcRequest(transactionDateTime, amount, AidIndexDefault, Message1, Message2, Message3, Message4, PinBlockInputRequiredDefault)
                : TransactionInfoRequestBuilder.CreateFallbackRequest(transactionDateTime, amount, AidIndexDefault, Message1, Message2, Message3, Message4, PinBlockInputRequiredDefault);

            scope.PendingParticipants = roundParticipants;

            TimeSpan roundTimeout = ClampCommandTimeout(deadline.Remaining);
            FileLogger.Info($"[PaymentOrchestrator] txId={txId} 카드 리딩 라운드 {round}/{MaxCardReadRounds} 시작 — 참여 {roundParticipants.Count}대, 거래구분={transactionTypeCode}, 남은데드라인={roundTimeout.TotalSeconds:F1}s");

            Task<CardReadBroadcastResult> broadcastTask = CardReadBroadcaster.SendAsync(roundParticipants, infoRequest, roundTimeout, txId);
            Task interruptTask = gate.Interrupted;
            Task firstCompleted = await Task.WhenAny(broadcastTask, interruptTask).ConfigureAwait(false);

            if (firstCompleted == interruptTask)
            {
                TransactionOutcomeReason reason = gate.ClaimedReason!.Value;
                FileLogger.Info($"[PaymentOrchestrator] txId={txId} 카드 리딩 라운드 {round} 대기 중 확정됨({reason}) — 리더기 응답을 기다리지 않고 즉시 처리");
                return CardReadRoundResult.Early(InterruptCode(reason));
            }

            CardReadBroadcastResult broadcast = await broadcastTask.ConfigureAwait(false);

            if (!broadcast.HasWinner)
            {
                if (!gate.TryClaim(TransactionOutcomeReason.FlowResult))
                {
                    TransactionOutcomeReason reason = gate.ClaimedReason!.Value;
                    FileLogger.Info($"[PaymentOrchestrator] txId={txId} 카드 리딩 라운드 {round} 완료 시점에 이미 확정됨({reason}) — 우선 처리");
                    return CardReadRoundResult.Early(InterruptCode(reason));
                }

                FileLogger.Warn($"[PaymentOrchestrator] txId={txId} 카드 리딩 라운드 {round} — 참여 리더기 전원 송신 실패(또는 참여자 없음)");
                return CardReadRoundResult.Early(PosResultCodeMapper.ReaderBroadcastNoWinnerCode);
            }

            IReaderEndpoint winner = broadcast.Winner!;
            CardReadCommandOutcome outcome = broadcast.WinnerOutcome!;

            switch (outcome.Kind)
            {
                case ReaderCommandOutcomeKind.Success:
                    if (outcome.CardData == null)
                    {
                        if (!gate.TryClaim(TransactionOutcomeReason.FlowResult))
                        {
                            TransactionOutcomeReason reason = gate.ClaimedReason!.Value;
                            winner.SendInvalidationInit();
                            return CardReadRoundResult.Early(InterruptCode(reason));
                        }

                        FileLogger.Error($"[PaymentOrchestrator] txId={txId} 응답코드 00인데 CardData가 없음 — 방어적으로 실패 처리");
                        winner.SendInvalidationInit();
                        return CardReadRoundResult.Early(PosResultCodeMapper.ReaderNoCardDataDefensiveCode);
                    }

                    FileLogger.Info($"[PaymentOrchestrator] txId={txId} 카드 리딩 성공(라운드 {round}) — 필드 채움 단계로");

                    // P22-7(PRD.md §1.6 관측 지점 "카드리딩 응답 — 거래마다, 자동"). 값 자체는 로그에
                    // 남기지 않는다(ObservedIdentityStore 클래스 요약) — DB에만 원문 저장.
                    _observedIdentityStore.Upsert(winner.ComPortDisplay, ObservedIdentityStore.ReaderAuthIdKey, outcome.CardData.ReaderAuthId);

                    return CardReadRoundResult.Success(winner, outcome.CardData);

                case ReaderCommandOutcomeKind.BusinessFailure when outcome.IsFallback:
                    deadline.Extend(UserInputStepExtension);
                    FileLogger.Info($"[PaymentOrchestrator] txId={txId} FALLBACK(07) — MS 재요청(채택된 그 리더기에만, 거래구분 F), 데드라인 {UserInputStepExtension.TotalSeconds:F0}초 연장");
                    _presenter.ChangeState(PaymentNoticeState.FallbackCardRequest);
                    roundParticipants = new[] { winner };
                    transactionTypeCode = TransactionInfoRequestBuilder.TransactionTypeFallback;
                    continue;

                case ReaderCommandOutcomeKind.BusinessFailure when outcome.IsRetryCode12:
                    deadline.Extend(UserInputStepExtension);
                    FileLogger.Info($"[PaymentOrchestrator] txId={txId} 응답코드 12 — 재요청(채택된 그 리더기에만, 거래구분 유지), 데드라인 {UserInputStepExtension.TotalSeconds:F0}초 연장");
                    roundParticipants = new[] { winner };
                    continue;

                case ReaderCommandOutcomeKind.BusinessFailure:
                    if (!gate.TryClaim(TransactionOutcomeReason.FlowResult))
                    {
                        TransactionOutcomeReason reason = gate.ClaimedReason!.Value;
                        winner.SendInvalidationInit();
                        return CardReadRoundResult.Early(InterruptCode(reason));
                    }

                    FileLogger.Warn($"[PaymentOrchestrator] txId={txId} 카드 리딩 실패 — 응답코드={outcome.ResponseCode}");
                    winner.SendInvalidationInit();
                    return CardReadRoundResult.Early(PosResultCodeMapper.ToTelegramCode(outcome));

                case ReaderCommandOutcomeKind.Timeout:
                    // 리더기 로컬 명령 타임아웃 — 거래 전체 Timeout(E02)과 사용자에게 같은 결과여야
                    // 하므로 outcome 기반 매핑(R2x)이 아니라 PosPaymentResultCode.Timeout을 쓴다
                    // (PosResultCodeMapper 클래스 주석, 이 kind로 outcome 오버로드를 호출하면 예외가
                    // 나도록 만들어 뒀다 — 실수로 R코드가 섞여 들어가는 걸 막는 안전장치).
                    if (!gate.TryClaim(TransactionOutcomeReason.FlowResult))
                    {
                        TransactionOutcomeReason reason = gate.ClaimedReason!.Value;
                        winner.SendInvalidationInit();
                        return CardReadRoundResult.Early(InterruptCode(reason));
                    }

                    FileLogger.Warn($"[PaymentOrchestrator] txId={txId} 리더기 명령 타임아웃(라운드 {round})");
                    winner.SendInvalidationInit();
                    return CardReadRoundResult.Early(PosResultCodeMapper.ToTelegramCode(PosPaymentResultCode.Timeout));

                default: // DllCallFailure, CommunicationError
                    if (!gate.TryClaim(TransactionOutcomeReason.FlowResult))
                    {
                        TransactionOutcomeReason reason = gate.ClaimedReason!.Value;
                        winner.SendInvalidationInit();
                        return CardReadRoundResult.Early(InterruptCode(reason));
                    }

                    FileLogger.Warn($"[PaymentOrchestrator] txId={txId} 카드 리딩 DLL 연동 실패(Kind={outcome.Kind}): {outcome.Detail}");
                    winner.SendInvalidationInit();
                    return CardReadRoundResult.Early(PosResultCodeMapper.ToTelegramCode(outcome));
            }
        }

        if (!gate.TryClaim(TransactionOutcomeReason.FlowResult))
        {
            TransactionOutcomeReason reason = gate.ClaimedReason!.Value;
            foreach (IReaderEndpoint p in roundParticipants)
                p.SendInvalidationInit();
            return CardReadRoundResult.Early(InterruptCode(reason));
        }

        FileLogger.Warn($"[PaymentOrchestrator] txId={txId} 카드 리딩 재요청 상한({MaxCardReadRounds}) 초과");
        foreach (IReaderEndpoint p in roundParticipants)
            p.SendInvalidationInit();
        return CardReadRoundResult.Early(PosResultCodeMapper.ReaderRetryLimitExceededCode);
    }

    /// <summary>
    /// VAN 중계(PRD §4.10). 성공하면 VAN 응답 바이트를 그대로 relay하고(P17-3), 통신 실패면 D 코드로
    /// 실패 응답을 합성한다. **VAN 응답 내용(승인/거절)은 해석하지 않는다** — relay 원칙.
    ///
    /// <paramref name="cardReadWinner"/>는 PRD §4.10의 "실패 시 Reader 초기화"를 위해 받는다
    /// (2026-08-27, Phase 17 최종 검증 H-3 수정 — Phase 15의 <c>RunVanApprovalAsync</c>가 VAN 실패
    /// 경로에서 <c>Winner?.SendInvalidationInit()</c>을 호출하고 있었는데 Phase 17 재구성에서 winner
    /// 참조와 함께 통째로 빠졌다). `501008`은 카드리딩이 없어 <c>null</c>이다.
    /// </summary>
    private async Task<PosResponseTelegram> RelayToVanAsync(PosRequestTelegram request, string txId, IReaderEndpoint? cardReadWinner)
    {
        _presenter.ChangeState(PaymentNoticeState.VanProcessing);

        VanRelayOutcome outcome = await _vanRelay.RelayAsync(request).ConfigureAwait(false);

        switch (outcome.Kind)
        {
            case VanRelayOutcomeKind.Success:
                // 승인/거절 어느 쪽이든 리더기를 초기화하지 않는다(2026-08-27 확정).
                // 이유는 relay 원칙(응답코드 미해석) 때문이 아니라 **리더기 상태가 둘이 동일하기
                // 때문**이다 — 리더기는 0x3B로 카드 데이터를 돌려준 시점에 자기 명령을 끝냈고, 그 뒤
                // VAN에서 승인이 났는지 거절됐는지는 알지도 못한다. 따라서 "승인엔 init 안 하고 거절엔
                // 한다"는 구분 자체가 리더기 관점에서 성립하지 않는다(Phase 15의 RunVanApprovalAsync가
                // 거절에만 init하던 비대칭은 근거 없는 코드였다). Phase 16 실장비 검증에서 연속 4건
                // 승인 거래를 중간 초기화 없이 수행해 다음 거래로 상태가 새지 않는 것도 실증됐다.
                FileLogger.Info($"[PaymentOrchestrator] txId={txId} VAN 응답 수신 — relay");
                return PosResponseTelegram.Relay(request.Schema, outcome.ResponseBody!);

            default: // CommunicationFailure
                // 이 경로에서만 초기화한다. 다만 근거는 "리더기가 정리를 필요로 해서"가 아니라
                // (위 Success 주석과 같은 이유로 리더기 상태는 여기서도 동일하다) PRD §4.10의 "실패 시
                // Reader 초기화" 문구를 문자 그대로 지키고, fire-and-forget이라 비용이 없기 때문이다.
                FileLogger.Warn($"[PaymentOrchestrator] txId={txId} VAN DLL 통신 실패: {outcome.Detail}");
                cardReadWinner?.SendInvalidationInit();
                return PosResponseTelegram.Failure(request, PosResultCodeMapper.ToTelegramCode(outcome.FailureKind!.Value));
        }
    }

    /// <summary>취소 알림 — <see cref="IPaymentNoticePresenter.Canceled"/>는 UI 스레드에서 발생한다.
    /// 게이트 확정만 이 자리에서 동기로 하고, 실제 0x60 발사는 <see cref="FireInterruptCleanup"/>이
    /// <c>Task.Run</c>으로 넘긴다.</summary>
    private void OnCanceled(TransactionScope scope)
    {
        if (scope.Gate.TryClaim(TransactionOutcomeReason.UserCanceled))
        {
            FileLogger.Info($"[PaymentOrchestrator] txId={scope.TransactionId} 사용자 취소 통지 수신 — 거래 확정(UserCanceled)");
            FireInterruptCleanup(TransactionOutcomeReason.UserCanceled, scope);
        }
        else
        {
            FileLogger.Info($"[PaymentOrchestrator] txId={scope.TransactionId} 사용자 취소 통지 수신 — 이미 다른 사유({scope.Gate.ClaimedReason})로 확정되어 무시");
        }
    }

    /// <summary>거래 데드라인(PRD §4.9)을 감시한다. 실제로 만료됐을 때만 게이트를 Timeout으로 확정
    /// 시도한다.</summary>
    private async Task MonitorDeadlineAsync(PaymentDeadline deadline, TransactionScope scope)
    {
        bool actuallyExpired = await deadline.WaitForExpiryAsync().ConfigureAwait(false);
        if (!actuallyExpired)
            return;

        if (scope.Gate.TryClaim(TransactionOutcomeReason.Timeout))
        {
            FileLogger.Warn($"[PaymentOrchestrator] txId={scope.TransactionId} 거래 데드라인 만료 — 거래 확정(Timeout)");
            FireInterruptCleanup(TransactionOutcomeReason.Timeout, scope);
        }
    }

    /// <summary>취소/Timeout 정리 경로를 하나로 통일한다 — 대기 중인 참여 리더기 전부에 0x60을
    /// 백그라운드에서 발사한다.</summary>
    private static void FireInterruptCleanup(TransactionOutcomeReason reason, TransactionScope scope)
    {
        IReadOnlyList<IReaderEndpoint> pending = scope.PendingParticipants;
        FileLogger.Info($"[PaymentOrchestrator] txId={scope.TransactionId} {reason} 확정 — 대기 중인 참여 리더기 {pending.Count}대에 초기화(0x60) 전송 예약(백그라운드)");

        Task.Run(() =>
        {
            foreach (IReaderEndpoint endpoint in pending)
            {
                try
                {
                    endpoint.SendInvalidationInit();
                }
                catch (Exception ex)
                {
                    FileLogger.Warn($"[PaymentOrchestrator] {reason} 처리 중 리더기 초기화 실패(무시하고 계속): {ex.Message}");
                }
            }
        });
    }

    /// <summary>취소/Timeout으로 확정된 거래의 SPEC 코드. <see cref="TransactionOutcomeReason.FlowResult"/>
    /// 는 이 메서드가 다루지 않는다 — 정상 흐름은 이미 자기 자신의 코드를 알고 있다.</summary>
    private static string InterruptCode(TransactionOutcomeReason reason) => reason switch
    {
        TransactionOutcomeReason.UserCanceled => PosResultCodeMapper.ToTelegramCode(PosPaymentResultCode.UserCanceled),
        TransactionOutcomeReason.Timeout => PosResultCodeMapper.ToTelegramCode(PosPaymentResultCode.Timeout),
        _ => throw new InvalidOperationException($"FlowResult은 인터럽트 코드를 만들지 않는다: {reason}"),
    };

    private static TimeSpan ClampCommandTimeout(TimeSpan remaining) =>
        remaining < MinimumCommandTimeout ? MinimumCommandTimeout : remaining;

    /// <summary>
    /// 로그 상관용 식별자. **SPEC `#9`(키오스크/요청기관 전문 관리 번호, AN12)를 쓴다** — SPEC이
    /// "발급기별 전송 일자별 유일한 값"으로 정의한 이 프로젝트의 정식 상관관계 키이고, 3전문 공통부에
    /// 항상 kiosk가 채워 보낸다. 이 값으로 로그를 남겨야 POS·VAN 로그와 같은 거래를 맞대어 추적할 수
    /// 있다(2026-08-27, Phase 17 최종 검증 M-1 수정 — 그전엔 <c>GetHashCode()</c> 기반 값을 써서
    /// 프로세스 밖에서는 아무 의미가 없었다).
    ///
    /// 비어 있으면(POS가 안 채웠거나 공백) 전문 종별 + 객체 해시로 대체한다 — 로그에서 거래를
    /// 구분하는 최소 수단은 남겨야 하기 때문이다.
    /// </summary>
    private static string LogTxId(PosRequestTelegram request)
    {
        string managementNumber = request.Read(TelegramManagementNumberFieldNumber);
        return managementNumber.Length > 0
            ? managementNumber
            : $"{request.TransactionTypeCode}-NOID-{request.GetHashCode():X8}";
    }

    /// <summary>SPEC `#9` — 요청기관(501008)/은행·센터(800000·902614) 전문 관리 번호. 이름 표기는
    /// 전문마다 다르지만 오프셋·길이·용도는 같다(<c>PosCommonHeader</c> 주석 참고).</summary>
    private const int TelegramManagementNumberFieldNumber = 9;

    /// <summary>
    /// 거래 1건 동안만 존재하는 상태 묶음(Phase 16 체크포인트 리뷰 H-1 계승). 결과 확정 게이트와
    /// "취소/Timeout 시 초기화할 리더기 목록"을 함께 들고 다닌다 — 인스턴스 필드에 두면 앞 거래의
    /// 뒤늦은 콜백이 다음 거래의 리더기를 초기화할 위험이 있다(자세한 이유는 Phase 16 문서 참고).
    /// </summary>
    private sealed class TransactionScope
    {
        private volatile IReadOnlyList<IReaderEndpoint> _pendingParticipants = Array.Empty<IReaderEndpoint>();

        internal TransactionScope(string transactionId)
        {
            TransactionId = transactionId;
        }

        internal string TransactionId { get; }

        internal TransactionOutcomeGate Gate { get; } = new();

        internal IReadOnlyList<IReaderEndpoint> PendingParticipants
        {
            get => _pendingParticipants;
            set => _pendingParticipants = value;
        }
    }

    /// <summary>카드 리딩 라운드의 결과 — <see cref="EarlyFailureCode"/>가 있으면 즉시 실패 응답을
    /// 합성할 SPEC 코드(실패/타임아웃/취소), 없으면 <see cref="Winner"/>/<see cref="CardData"/>로 필드
    /// 채움 단계를 진행한다.</summary>
    private sealed class CardReadRoundResult
    {
        private CardReadRoundResult(string? earlyFailureCode, IReaderEndpoint? winner, CardReadData? cardData)
        {
            EarlyFailureCode = earlyFailureCode;
            Winner = winner;
            CardData = cardData;
        }

        internal string? EarlyFailureCode { get; }

        internal IReaderEndpoint? Winner { get; }

        internal CardReadData? CardData { get; }

        internal static CardReadRoundResult Early(string code) => new(code, null, null);

        internal static CardReadRoundResult Success(IReaderEndpoint winner, CardReadData cardData) => new(null, winner, cardData);
    }

    /// <summary>PIN 수집(<see cref="CollectPinAsync"/>)의 결과 — <see cref="EarlyFailureCode"/>가 있으면
    /// PIN 대기 중 취소/Timeout이 이겼다는 뜻(즉시 실패 응답), 없으면 <see cref="Pin"/>에 입력된 4자리가
    /// 담긴다. <see cref="CardReadRoundResult"/>와 정확히 같은 모양(Phase 18 P18-4).</summary>
    private sealed class PinCollectionResult
    {
        private PinCollectionResult(string? earlyFailureCode, string? pin)
        {
            EarlyFailureCode = earlyFailureCode;
            Pin = pin;
        }

        internal string? EarlyFailureCode { get; }

        internal string? Pin { get; }

        internal static PinCollectionResult Early(string code) => new(code, null);

        internal static PinCollectionResult Success(string pin) => new(null, pin);
    }
}
