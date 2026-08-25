using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace KFTCOneCAP.Wpf.Views.Controls;

/// <summary>
/// 결제 알림창(<see cref="PaymentNoticeWindow"/>) VanProcessing 상태에서 리더기(ReaderImage) 위쪽
/// 공중에 표시되는 "신호 웨이브" 인디케이터. 좌우 대칭 안테나 점 + 동심 반원 호 3겹(신호가 퍼져나가는
/// 모양, 안쪽 호가 먼저 나타났다 먼저 사라지고 바깥쪽 호일수록 늦게 나타나 늦게 사라지는 순차 페이드) +
/// 중앙 파형(진폭이 은은하게 두근거리듯 변하는 정도의 절제된 스케일 애니메이션)으로 구성한다.
///
/// 2026-08-24 신설(펄스 효과 제거 후 대체 "거래중" 표현, 사용자 채택안 4번 "신호 웨이브").
/// </summary>
public partial class PaymentSignalWaveIndicator : UserControl
{
    private const double ArcCycleSeconds = 1.8;
    private const double ArcStaggerSeconds = 0.18;
    private const double WaveformBreatheSeconds = 1.6;

    private bool _isPlaying;

    public PaymentSignalWaveIndicator()
    {
        InitializeComponent();
    }

    /// <summary>동심 호 순차 페이드 + 중앙 파형 은은한 진폭 변화를 동시에 무한 재생한다.</summary>
    public void Play()
    {
        if (_isPlaying)
        {
            return;
        }
        _isPlaying = true;

        // 동심 호 3겹 — 안쪽(반지름 작은) 호부터 먼저 나타났다 먼저 사라지고, 바깥쪽 호일수록
        // BeginTime을 늦춰 "신호가 퍼져나가는" 느낌을 준다. 좌/우는 대칭이므로 같은 타이밍으로 동기화.
        AnimateArc(LeftArc1, RightArc1, 0.9, 0);
        AnimateArc(LeftArc2, RightArc2, 0.55, ArcStaggerSeconds);
        AnimateArc(LeftArc3, RightArc3, 0.3, ArcStaggerSeconds * 2);

        // 중앙 파형 — 진폭이 미세하게 두근거리듯 변하는 정도로 절제된 세로 스케일 애니메이션
        // (펄스처럼 과하지 않게, 0.85~1.15 범위).
        var breathe = new DoubleAnimation(0.85, 1.15, TimeSpan.FromSeconds(WaveformBreatheSeconds))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        WaveformScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, breathe);
    }

    private static void AnimateArc(Path left, Path right, double peakOpacity, double beginTimeSeconds)
    {
        var anim = new DoubleAnimation(0, peakOpacity, TimeSpan.FromSeconds(ArcCycleSeconds))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            BeginTime = TimeSpan.FromSeconds(beginTimeSeconds),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        left.BeginAnimation(UIElement.OpacityProperty, anim);

        // 좌/우가 동일 인스턴스를 공유하면 안 되므로 별도 애니메이션 객체를 사용한다(Freeze되지 않은
        // 애니메이션은 여러 대상에 동시에 붙일 수 없음).
        var animRight = anim.Clone();
        right.BeginAnimation(UIElement.OpacityProperty, animRight);
    }

    /// <summary>애니메이션을 정지하고 리소스를 해제한다.</summary>
    public void Stop()
    {
        _isPlaying = false;
        LeftArc1.BeginAnimation(UIElement.OpacityProperty, null);
        LeftArc2.BeginAnimation(UIElement.OpacityProperty, null);
        LeftArc3.BeginAnimation(UIElement.OpacityProperty, null);
        RightArc1.BeginAnimation(UIElement.OpacityProperty, null);
        RightArc2.BeginAnimation(UIElement.OpacityProperty, null);
        RightArc3.BeginAnimation(UIElement.OpacityProperty, null);
        WaveformScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);

        LeftArc1.Opacity = 0;
        LeftArc2.Opacity = 0;
        LeftArc3.Opacity = 0;
        RightArc1.Opacity = 0;
        RightArc2.Opacity = 0;
        RightArc3.Opacity = 0;
    }
}
