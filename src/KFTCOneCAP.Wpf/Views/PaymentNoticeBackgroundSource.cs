using System;
using System.Windows.Media.Imaging;
using KFTCOneCAP.Wpf.Services.Payment;

namespace KFTCOneCAP.Wpf.Views;

/// <summary>
/// 결제 알림창 배경 이미지 자산 매핑(2026-08-21 자산 구조 재구성).
///
/// <b>배경 소스 단일 지점 규칙</b>(development_plan.md P13-1 "★ 배경 소스 단일 지점")을 그대로
/// 유지한다 — 리더기 이미지 매핑은 <see cref="ReaderSource"/> 한 곳, 화살표 이미지 매핑은
/// <see cref="GetArrowSource"/> 한 곳에만 있다. 다른 코드/XAML은 <c>Assets/Images/PaymentNotice/*</c>
/// 경로 문자열을 새로 만들지 않는다.
///
/// 이전(BG_IMG_IC/MS/PROCESSING.png, 카드까지 합성된 이미지)과 달리, 이번 자산은 리더기 몸통 1장
/// (IC/FALLBACK/PROCESSING 3개 상태 공용, 교체 없음)과 상태별 화살표 2장으로 분리되어 있다 — 자세한
/// 배경은 <c>development_plan.md</c> P13-1 "수정(2026-08-21, ...)" 단락 참고.
///
/// 3장은 정적 생성자에서 한 번만 디코드해 <see cref="System.Windows.Freezable.Freeze"/>한 뒤 캐시한다
/// (표시 지연 방지). 워밍업 창 방식은 쓰지 않는다(P12-6 부작용 확인됨) — <see cref="WarmUp"/>을 앱 기동
/// 시 한 번 호출해 정적 생성자를 앞당겨 실행한다.
/// </summary>
internal static class PaymentNoticeBackgroundSource
{
    /// <summary>
    /// FALLBACK(MS) 상태 화살표 자산 선택 스위치 — 사용자가 스크린샷을 보고 최종 채택할 때까지
    /// 손쉽게 바꿔볼 수 있도록 상수 하나로 스위치한다(development_plan.md P13-1-B "판단 조건").
    /// <c>true</c>(기본값, 1순위): <c>arrow_ms.png</c>(네온 글로우 스타일, reader.png의 글로시 톤과
    /// 함께 검토된 자산)를 그대로 쓴다. <c>false</c>: 이미 스타일이 검증된 <c>arrow_ic.png</c>(아래
    /// 방향)를 90도 회전시켜 재사용한다(장점: 스타일 100% 일치·추가 생성 불필요, 단점: 아이소메트릭
    /// 그림자 방향이 회전 후 어색해질 수 있음 — 실제 화면 스크린샷으로 비교 후 결정).
    /// </summary>
    public const bool UseArrowMsAsset = true;

    /// <summary>
    /// <c>arrow_ic.png</c>를 FALLBACK 화살표로 재사용할 때(<see cref="UseArrowMsAsset"/> = false)
    /// 적용할 회전 각도. 아래(0,1) 방향 벡터를 WPF <c>RotateTransform</c>(양수 = 시계 방향, 화면 좌표계
    /// y-down 기준) +90도 회전하면 왼쪽(-1,0) 방향이 된다 — MS 리딩 방향(오른쪽→왼쪽 긁기)과 일치.
    /// </summary>
    public const double RotatedArrowIcAngleForFallback = 90;

    // 2026-08-24 자산 교체: 리더기 몸통(reader_kftc.png)과 바닥 원판(circle.png)을 분리된 레이어로
    // 받았다 — 이전 reader.png(몸통+원판+그림자 한 장 합성)는 진행광 오버레이를 몸통보다 항상 위
    // z-order에 그릴 수밖에 없어 "몸통 뒤로 지나가는 진행광"을 표현할 수 없었다(반원 클리핑 편법으로
    // 버텼음). 분리 후에는 Canvas 순서를 원판 → 진행광 링 → 몸통으로 쌓아 몸통 뒤쪽 절반이 자동으로
    // 가려지도록 한다(PaymentNoticeWindow.xaml 참고). 두 이미지 모두 1536x1024 동일 캔버스에 그려져
    // 있어(사용자 확인 완료) 같은 Canvas.Left/Top/Width/Height 박스에 그대로 겹쳐 쓸 수 있다.
    private static readonly BitmapImage ReaderBitmap = Load("Assets/Images/PaymentNotice/reader_kftc.png");
    private static readonly BitmapImage PlateBitmapValue = Load("Assets/Images/PaymentNotice/circle.png");
    private static readonly BitmapImage ArrowIcBitmap = Load("Assets/Images/PaymentNotice/arrow_ic.png");
    private static readonly BitmapImage ArrowMsBitmap = Load("Assets/Images/PaymentNotice/arrow_ms.png");

    /// <summary>
    /// 카드 이미지(2026-08-21 추가) — 사용자가 직접 만든 아이소메트릭 카드 사진. 벡터로 각도를
    /// 재현하려던 시도(<c>PaymentCardShape</c>)가 두 차례(리더기 슬롯 각도 그대로 큰 회전 → 다이아몬드,
    /// 세워진 카드+두께 옆면 정정 → 그래도 원본과 미세하게 다름) 실패해 실제 이미지로 교체했다.
    /// </summary>
    private static readonly BitmapImage IcCardBitmap = Load("Assets/Images/PaymentNotice/ic_card.png");
    private static readonly BitmapImage MsCardBitmap = Load("Assets/Images/PaymentNotice/ms_card.png");

    /// <summary>
    /// PIN 입력 화면(Phase 18, P18-2) 상단 아이콘 — 카드+자물쇠, 배경 투명 PNG.
    /// <see cref="PaymentNoticeState.PinEntry"/> 전용, 다른 상태는 참조하지 않는다.
    /// </summary>
    private static readonly BitmapImage PinIconBitmap = Load("Assets/Images/PaymentNotice/비밀번호 입력 아이콘.png");

    /// <summary>
    /// 정지 리더기 이미지 — IC/FALLBACK/PROCESSING 3개 상태 모두 동일(교체 없음, 크로스페이드 대상
    /// 아님). 배경은 이미 투명 PNG로 확인됐다(alpha 검증 완료, 코너 픽셀 A=0).
    /// </summary>
    public static BitmapImage ReaderSource => ReaderBitmap;

    /// <summary>
    /// 바닥 원판 이미지(2026-08-24 추가) — 리더기 몸통과 분리된 레이어. IC/FALLBACK/PROCESSING 3개
    /// 상태 모두 동일(교체 없음). <see cref="ReaderSource"/>와 같은 Canvas 박스에 그려지며, 두 이미지
    /// 사이에 진행광 링을 끼워 넣어야 하므로(z-order: 원판 → 링 → 몸통) 별도로 노출한다.
    /// </summary>
    public static BitmapImage PlateSource => PlateBitmapValue;

    /// <summary>PIN 입력 화면 상단 아이콘(카드+자물쇠).</summary>
    public static BitmapImage PinIconSource => PinIconBitmap;

    /// <summary>앱 기동 시 호출해 정적 생성자(이미지 디코드)를 미리 끝내 둔다. 부작용 없음(idempotent).</summary>
    public static void WarmUp()
    {
        _ = ReaderBitmap;
        _ = PlateBitmapValue;
        _ = ArrowIcBitmap;
        _ = ArrowMsBitmap;
        _ = IcCardBitmap;
        _ = MsCardBitmap;
        _ = PinIconBitmap;
    }

    /// <summary>
    /// 상태별 카드 이미지. <see cref="PaymentNoticeState.VanProcessing"/>과
    /// <see cref="PaymentNoticeState.PinEntry"/>는 카드가 없으므로 <c>null</c>을 반환한다(PinEntry는
    /// PIN 키패드 화면(P18-2의 <c>PinPanel</c>)이 그 자리를 대신하므로 카드 레이어를 숨긴다).
    /// </summary>
    public static BitmapImage? GetCardSource(PaymentNoticeState state) => state switch
    {
        PaymentNoticeState.IcCardRequest => IcCardBitmap,
        PaymentNoticeState.FallbackCardRequest => MsCardBitmap,
        PaymentNoticeState.VanProcessing => null,
        PaymentNoticeState.PinEntry => null,
        _ => null,
    };

    /// <summary>
    /// 상태별 화살표 오버레이 이미지. <see cref="PaymentNoticeState.VanProcessing"/>은 화살표가
    /// 없으므로(로딩 인디케이터로 대체) <c>null</c>을 반환하고, <see cref="PaymentNoticeState.PinEntry"/>도
    /// 리더기 안내 애니메이션이 필요 없으므로(PIN 키패드 화면) <c>null</c>을 반환한다.
    /// </summary>
    public static BitmapImage? GetArrowSource(PaymentNoticeState state) => state switch
    {
        PaymentNoticeState.IcCardRequest => ArrowIcBitmap,
        PaymentNoticeState.FallbackCardRequest => UseArrowMsAsset ? ArrowMsBitmap : ArrowIcBitmap,
        PaymentNoticeState.VanProcessing => null,
        PaymentNoticeState.PinEntry => null,
        _ => null,
    };

    private static BitmapImage Load(string packRelativePath)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri($"pack://application:,,,/KFTCOneCAP.Wpf;component/{packRelativePath}", UriKind.Absolute);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
