using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Media.Animation;

namespace KFTCOneCAP.Wpf.Views.Controls;

/// <summary>
/// 결제 알림창(<see cref="PaymentNoticeWindow"/>) VanProcessing 상태에서 리더기 몸통 "표면"에
/// 있어야 하는 효과를 담당한다: 슬롯 내부 빛 흐름 — IC 슬롯 또는 MS 슬롯 채널을 따라 하늘색
/// 하이라이트가 좌상단 -> 우하단으로 부드럽게 흐름. VanProcessing 직전 카드 종류(IC 삽입 vs MS
/// 스와이프)에 따라 둘 중 하나만 재생한다(<see cref="Play(bool)"/> 파라미터).
///
/// "원형 진행광"(바닥 원판 테두리를 도는 링)은 2026-08-24 자산 분리(원판 circle.png / 몸통
/// reader_kftc.png)에 따라 <see cref="PaymentProcessingRing"/>로 옮겼다 — 그 링은 원판 위·몸통
/// 아래 z-order에 고정돼야 하는 반면, 이 컨트롤의 효과는 몸통 표면 위(몸통보다 위 레이어)에
/// 있어야 하므로 같은 컨트롤에 둘 수 없다.
///
/// 2026-08-24 6차 수정: 로고 펄스(LogoPulseGlow/LogoCoreGlow)는 사용자 피드백("이상해 보인다")에
/// 따라 완전히 제거했다.
///
/// 2026-08-24 7차 수정: 사용자 피드백("IC칩쪽에 벗어나 있다")에 따라 reader_kftc.png를 재실측해
/// IC 슬롯 흐름 위치/크기를 정정하고, MS 슬롯용 흐름(SlotFlowMs)을 추가했다 — 둘 다 항상 같이 도는
/// 게 아니라 VanProcessing 진입 직전 카드 종류에 맞는 하나만 재생된다.
/// </summary>
public partial class PaymentProcessingIndicator : UserControl
{
    private const double SlotFlowSeconds = 1.4;

    // IC 슬롯 빛 흐름 이동 거리 — reader_kftc.png 실측(진한 파란 홈 색상 연결영역, 1536x1024 기준
    // (543,207)-(949,447)px, 주축 p0(546.86,213.79)->p1(942.73,443.63))을 340x226.67 캔버스로
    // 스케일 변환(계수 0.221354)한 시작점(121.05,47.32) -> 끝점(208.66,98.19) 차이.
    private const double IcDeltaX = 87.61;
    private const double IcDeltaY = 50.87;

    // MS 슬롯 빛 흐름 이동 거리 — 같은 마스크의 y<330 구간(앞쪽 대각선, 리더기 뒤쪽 모서리로
    // 꺾이기 전) 실측 (585,98)-(1000,329)px, 주축 p0(585.66,108.08)->p1(992.69,341.74)을 같은
    // 스케일로 변환한 시작점(129.63,23.92) -> 끝점(219.72,75.65) 차이.
    private const double MsDeltaX = 90.09;
    private const double MsDeltaY = 51.73;

    private bool _isPlaying;

    public PaymentProcessingIndicator()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 슬롯 내부 빛 흐름을 무한 재생한다. <paramref name="isMs"/>가 true면 MS 슬롯 흐름을,
    /// false면 IC 슬롯 흐름을 재생하고 반대쪽은 끈 상태로 둔다(둘 다 동시에 돌지 않는다).
    /// </summary>
    public void Play(bool isMs)
    {
        if (_isPlaying)
        {
            return;
        }
        _isPlaying = true;

        if (isMs)
        {
            PlaySlot(SlotFlowMs, SlotFlowMsTranslate, MsDeltaX, MsDeltaY);
        }
        else
        {
            PlaySlot(SlotFlow, SlotFlowTranslate, IcDeltaX, IcDeltaY);
        }
    }

    private static void PlaySlot(Ellipse ellipse, TranslateTransform translate, double deltaX, double deltaY)
    {
        var xSlot = new DoubleAnimation(0, deltaX, TimeSpan.FromSeconds(SlotFlowSeconds))
        {
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        var ySlot = new DoubleAnimation(0, deltaY, TimeSpan.FromSeconds(SlotFlowSeconds))
        {
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        translate.BeginAnimation(TranslateTransform.XProperty, xSlot);
        translate.BeginAnimation(TranslateTransform.YProperty, ySlot);

        var slotOpacity = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(SlotFlowSeconds),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        slotOpacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        slotOpacity.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromPercent(0.2)));
        slotOpacity.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromPercent(0.75)));
        slotOpacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1.0)));
        ellipse.BeginAnimation(UIElement.OpacityProperty, slotOpacity);
    }

    /// <summary>애니메이션을 정지하고 리소스를 해제한다(IC/MS 양쪽 모두).</summary>
    public void Stop()
    {
        _isPlaying = false;
        SlotFlowTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        SlotFlowTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        SlotFlow.BeginAnimation(UIElement.OpacityProperty, null);

        SlotFlowMsTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        SlotFlowMsTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        SlotFlowMs.BeginAnimation(UIElement.OpacityProperty, null);
    }
}
