using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace KFTCOneCAP.Wpf.Views.Controls;

/// <summary>
/// 결제 알림창(<see cref="PaymentNoticeWindow"/>) VanProcessing 상태의 애니메이션 오버레이 — 사용자가
/// 제시한 "거래중 애니메이션" 기획(원형 진행광 회전 + 슬롯 내부 빛 흐름 + 은은한 펄스)을 그대로
/// 구현한다. 이전의 점 3개 로딩 인디케이터를 대체한다. 카드/화살표와
/// 같은 Play()/Stop() 생명주기 패턴 — 창이 닫히거나 상태가 바뀌면 반드시 <see cref="Stop"/>을
/// 호출해야 한다(PRD §9 리소스 정리).
/// </summary>
public partial class PaymentProcessingIndicator : UserControl
{
    private const double RingChaseSeconds = 1.5;
    private const double SlotFlowSeconds = 1.6;
    private const double PulseSeconds = 1.4;

    // 바닥 타원 매개변수(확대 스크린샷 실측, PaymentProcessingIndicator.xaml 주석 참고).
    // 각도 0=오른쪽(3시), 90=아래(6시, 화면 y-down 기준), 180=왼쪽(9시) — 타원의 "아래쪽 절반"
    // (0~180)이 리더기 몸통에 가려지지 않고 보이는 크레센트다. 위쪽 절반(180~360)은 몸통 뒤에 있어야
    // 하는데 이 오버레이가 몸통보다 위 레이어라 그대로 비쳐 보이는 결함이 실측으로 확인돼, 점 6개를
    // 보이는 절반(RingVisibleAngleFrom~To) 안에만 고정 배치하고 Opacity 체이스로 회전감을 낸다.
    private const double RingCenterX = 170;
    private const double RingCenterY = 135;
    private const double RingRadiusX = 135;
    private const double RingRadiusY = 45;
    private const double RingVisibleAngleFrom = 25;
    private const double RingVisibleAngleTo = 140;
    private const double RingDotSize = 12;
    private const double RingChaseStaggerSeconds = 0.12;

    // 슬롯 빛 흐름 이동 경로(로컬 좌표, 확대 스크린샷 재실측 — Canvas.Left=105,Top=54에서 (215,102.5)까지).
    private const double SlotFlowFromX = 0;
    private const double SlotFlowToX = 87;
    private const double SlotFlowFromY = 0;
    private const double SlotFlowToY = 39.5;

    private Storyboard? _activeStoryboard;
    private bool _dotsPositioned;

    public PaymentProcessingIndicator()
    {
        InitializeComponent();
    }

    /// <summary>3개 효과(원형 진행광/슬롯 빛 흐름/펄스)를 동시에 반복 재생한다.</summary>
    public void Play()
    {
        if (_activeStoryboard is not null)
        {
            return;
        }

        PositionRingDotsOnce();

        var storyboard = new Storyboard();

        // 1. 원형 진행광 — 점 6개(RingDot1~6, 타원의 보이는 크레센트에 고정 배치)를 순서대로
        // 밝아졌다 어두워지게 해 "빛이 훑고 지나가는" 체이스 효과로 회전감을 낸다. Path/ArcSegment로
        // 실제 호를 그리려던 두 차례 시도(회전 변환·좌표 역산 모두)가 실제 화면에서 기대와 다르게
        // 나와, 좌표 계산만 신뢰하고(EllipsePoint) 그리기는 단순 Opacity 애니메이션으로 대체했다
        // (XAML 상단 주석 참고).
        var ringDots = new[] { RingDot1, RingDot2, RingDot3, RingDot4, RingDot5, RingDot6 };
        for (int i = 0; i < ringDots.Length; i++)
        {
            AddChasePulse(storyboard, ringDots[i], i * RingChaseStaggerSeconds);
        }

        // 2. 슬롯 내부 빛 흐름 — 좌→우 이동(X/Y 동시) + 양 끝에서 페이드 인/아웃(오팔로 왕복 시 자연스러움).
        AddSlotFlowAxis(storyboard, "X", SlotFlowFromX, SlotFlowToX);
        AddSlotFlowAxis(storyboard, "Y", SlotFlowFromY, SlotFlowToY);

        var slotOpacity = new DoubleAnimationUsingKeyFrames();
        slotOpacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        slotOpacity.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromPercent(0.25)));
        slotOpacity.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromPercent(0.75)));
        slotOpacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        slotOpacity.Duration = TimeSpan.FromSeconds(SlotFlowSeconds);
        slotOpacity.RepeatBehavior = RepeatBehavior.Forever;
        Storyboard.SetTarget(slotOpacity, SlotFlow);
        Storyboard.SetTargetProperty(slotOpacity, new System.Windows.PropertyPath(System.Windows.UIElement.OpacityProperty));
        storyboard.Children.Add(slotOpacity);

        // 3. 은은한 펄스 — 로고 영역 밝기 왕복.
        var pulseAnimation = new DoubleAnimation(0.18, 0.42, TimeSpan.FromSeconds(PulseSeconds))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Storyboard.SetTarget(pulseAnimation, PulseGlow);
        Storyboard.SetTargetProperty(pulseAnimation, new System.Windows.PropertyPath(System.Windows.UIElement.OpacityProperty));
        storyboard.Children.Add(pulseAnimation);

        _activeStoryboard = storyboard;
        storyboard.Begin(this, isControllable: true);
    }

    /// <summary>
    /// 애니메이션을 멈춘다. PROCESSING 상태를 벗어나거나 창이 닫힐 때 반드시 호출한다 —
    /// 그러지 않으면 백그라운드에서 계속 도는 Storyboard가 리소스를 낭비한다(PRD §9).
    /// </summary>
    public void Stop()
    {
        _activeStoryboard?.Stop(this);
        _activeStoryboard = null;
    }

    /// <summary>타원 매개변수식(각도 → 좌표). 0도=오른쪽(3시 방향), 시계 방향(y-down 화면 좌표계 기준).</summary>
    private static Point EllipsePoint(double angleDegrees)
    {
        double radians = angleDegrees * Math.PI / 180.0;
        return new Point(
            RingCenterX + RingRadiusX * Math.Cos(radians),
            RingCenterY + RingRadiusY * Math.Sin(radians));
    }

    /// <summary>
    /// 점 6개를 타원의 보이는 크레센트(RingVisibleAngleFrom~To) 위에 균등 간격으로 1회만 배치한다.
    /// 위치는 고정이고(움직이지 않음), Play()가 이 점들의 Opacity만 순서대로 애니메이션한다.
    /// </summary>
    private void PositionRingDotsOnce()
    {
        if (_dotsPositioned)
        {
            return;
        }

        _dotsPositioned = true;
        var ringDots = new[] { RingDot1, RingDot2, RingDot3, RingDot4, RingDot5, RingDot6 };
        for (int i = 0; i < ringDots.Length; i++)
        {
            double fraction = (double)i / (ringDots.Length - 1);
            double angle = RingVisibleAngleFrom + (RingVisibleAngleTo - RingVisibleAngleFrom) * fraction;
            var point = EllipsePoint(angle);
            Canvas.SetLeft(ringDots[i], point.X - RingDotSize / 2);
            Canvas.SetTop(ringDots[i], point.Y - RingDotSize / 2);
        }
    }

    private static void AddChasePulse(Storyboard storyboard, UIElement dot, double beginSeconds)
    {
        var animation = new DoubleAnimation(0.2, 1.0, TimeSpan.FromSeconds(RingChaseSeconds / 3))
        {
            BeginTime = TimeSpan.FromSeconds(beginSeconds),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Storyboard.SetTarget(animation, dot);
        Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.OpacityProperty));
        storyboard.Children.Add(animation);
    }

    private void AddSlotFlowAxis(Storyboard storyboard, string axis, double from, double to)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromSeconds(SlotFlowSeconds))
        {
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Storyboard.SetTarget(animation, SlotFlowTranslate);
        Storyboard.SetTargetProperty(animation, new System.Windows.PropertyPath(axis));
        storyboard.Children.Add(animation);
    }
}
