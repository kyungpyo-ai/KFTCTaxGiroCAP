using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace KFTCOneCAP.Wpf.Views.Controls;

/// <summary>
/// 결제 알림창(<see cref="PaymentNoticeWindow"/>) VanProcessing 상태의 애니메이션 오버레이:
/// 1. 원형 진행광: 바닥 타원 둘레를 따라 24% 길이의 네온 진행광 아크와 헤드 라이트가 시계 방향으로 회전.
/// 2. 슬롯 내부 빛 흐름: IC 슬롯을 따라 하늘색 하이라이트가 좌상단 -> 우하단으로 부드럽게 흐름.
/// 3. 은은한 펄스: 로고와 바닥 테두리가 주기적으로 밝아졌다 어두워짐.
/// </summary>
public partial class PaymentProcessingIndicator : UserControl
{
    private const double OrbitCycleSeconds = 1.6;
    private const double SlotFlowSeconds = 1.4;
    private const double PulseSeconds = 1.2;

    // 바닥 타원 파라미터 (reader.png 실측 원판 좌표: center(170, 137), rx=148, ry=40)
    private const double EllipseCenterX = 170;
    private const double EllipseCenterY = 137;
    private const double EllipseRadiusX = 148;
    private const double EllipseRadiusY = 40;

    // 2026-08-24 수정: 타원 뒤쪽 절반(180~360도)은 리더기 몸통에 가려져야 하는데 이 오버레이가
    // 몸통보다 위 레이어라 그대로 비쳐 보이는 문제가 있어, 보이는 앞쪽 반원(0~180도)만 오간다
    // (XAML의 OrbitGlowArc/OrbitCoreArc Data를 반원 경로로 바꾼 것과 같은 이유). 반원 길이(원주의
    // 절반, Ramanujan 근사) ≈ 320.
    private const double OrbitHalfArcLength = 320;

    // 슬롯 빛 흐름 이동 거리 (X: 120 -> 195, Y: 60 -> 96)
    private const double SlotDeltaX = 75;
    private const double SlotDeltaY = 36;

    private bool _isPlaying;

    public PaymentProcessingIndicator()
    {
        InitializeComponent();
    }

    /// <summary>3개 효과(원형 진행광/슬롯 빛 흐름/펄스)를 동시에 무한 재생한다.</summary>
    public void Play()
    {
        if (_isPlaying)
        {
            return;
        }
        _isPlaying = true;

        // 1-1. 바닥 타원 진행광 아크 이동 (StrokeDashOffset) — 경로 자체가 이제 보이는 반원뿐이라
        // (열린 경로) 한 방향으로 계속 스크롤하면 끝에서 툭 끊기므로 왕복(AutoReverse)한다.
        var dashAnim = new DoubleAnimation(0, -OrbitHalfArcLength, TimeSpan.FromSeconds(OrbitCycleSeconds))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        };
        OrbitGlowArc.BeginAnimation(Shape.StrokeDashOffsetProperty, dashAnim);
        OrbitCoreArc.BeginAnimation(Shape.StrokeDashOffsetProperty, dashAnim);

        // 1-2. 진행광 선두 헤드라이트 점(OrbitHeadLight) — 같은 이유로 보이는 반원(0~180도)만 왕복.
        var xFrames = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(OrbitCycleSeconds),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        };
        var yFrames = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(OrbitCycleSeconds),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        };

        const int numKeyFrames = 24;
        for (int i = 0; i <= numKeyFrames; i++)
        {
            double progress = (double)i / numKeyFrames;
            double angleDeg = 180.0 * progress;
            double rad = angleDeg * Math.PI / 180.0;

            double x = EllipseCenterX + EllipseRadiusX * Math.Cos(rad) - (OrbitHeadLight.Width / 2);
            double y = EllipseCenterY + EllipseRadiusY * Math.Sin(rad) - (OrbitHeadLight.Height / 2);

            var keyTime = KeyTime.FromPercent(progress);
            xFrames.KeyFrames.Add(new LinearDoubleKeyFrame(x, keyTime));
            yFrames.KeyFrames.Add(new LinearDoubleKeyFrame(y, keyTime));
        }
        OrbitHeadLight.BeginAnimation(Canvas.LeftProperty, xFrames);
        OrbitHeadLight.BeginAnimation(Canvas.TopProperty, yFrames);

        // 2. 슬롯 내부 빛 흐름
        var xSlot = new DoubleAnimation(0, SlotDeltaX, TimeSpan.FromSeconds(SlotFlowSeconds))
        {
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        var ySlot = new DoubleAnimation(0, SlotDeltaY, TimeSpan.FromSeconds(SlotFlowSeconds))
        {
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        SlotFlowTranslate.BeginAnimation(TranslateTransform.XProperty, xSlot);
        SlotFlowTranslate.BeginAnimation(TranslateTransform.YProperty, ySlot);

        var slotOpacity = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(SlotFlowSeconds),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        slotOpacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        slotOpacity.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromPercent(0.2)));
        slotOpacity.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromPercent(0.75)));
        slotOpacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1.0)));
        SlotFlow.BeginAnimation(UIElement.OpacityProperty, slotOpacity);

        // 3. 로고 & 하단 베이스 은은한 펄스
        var basePulse = new DoubleAnimation(0.1, 0.45, TimeSpan.FromSeconds(PulseSeconds))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        BasePulseGlow.BeginAnimation(UIElement.OpacityProperty, basePulse);

        var logoPulse = new DoubleAnimation(0.15, 0.55, TimeSpan.FromSeconds(PulseSeconds))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        LogoPulseGlow.BeginAnimation(UIElement.OpacityProperty, logoPulse);

        var corePulse = new DoubleAnimation(0.3, 0.9, TimeSpan.FromSeconds(PulseSeconds))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        LogoCoreGlow.BeginAnimation(UIElement.OpacityProperty, corePulse);
    }

    /// <summary>애니메이션을 정지하고 리소스를 해제한다.</summary>
    public void Stop()
    {
        _isPlaying = false;
        OrbitGlowArc.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
        OrbitCoreArc.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
        OrbitHeadLight.BeginAnimation(Canvas.LeftProperty, null);
        OrbitHeadLight.BeginAnimation(Canvas.TopProperty, null);
        SlotFlowTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        SlotFlowTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        SlotFlow.BeginAnimation(UIElement.OpacityProperty, null);
        BasePulseGlow.BeginAnimation(UIElement.OpacityProperty, null);
        LogoPulseGlow.BeginAnimation(UIElement.OpacityProperty, null);
        LogoCoreGlow.BeginAnimation(UIElement.OpacityProperty, null);
    }
}


