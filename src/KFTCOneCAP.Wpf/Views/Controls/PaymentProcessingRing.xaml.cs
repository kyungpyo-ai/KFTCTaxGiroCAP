using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace KFTCOneCAP.Wpf.Views.Controls;

/// <summary>
/// 결제 알림창(<see cref="PaymentNoticeWindow"/>) VanProcessing 상태에서 원판(circle.png) 위,
/// 리더기 몸통(reader_kftc.png) 아래에 배치되는 원형 진행광 링. 2026-08-24 자산 분리(원판/몸통
/// 별도 이미지)에 따라 <see cref="PaymentProcessingIndicator"/>에서 "바닥 링" 관련 요소만 떼어내
/// 별도 컨트롤로 만들었다 — z-order가 원판/몸통 사이로 고정되어 있어야 하므로 몸통 "표면" 효과
/// (슬롯 빛 흐름, 로고 펄스)와는 다른 레이어에 둔다(PaymentNoticeWindow.xaml 참고).
///
/// 이전(반원 클리핑 + AutoReverse 왕복)과 달리, 이제 링이 몸통보다 아래 레이어라 뒤쪽 절반은 몸통의
/// 불투명 픽셀에 자동으로 가려지므로 온전한 360도 경로를 한 방향(시계방향)으로 계속 회전시킨다.
/// </summary>
public partial class PaymentProcessingRing : UserControl
{
    private const double OrbitCycleSeconds = 2.2;

    // 원판(circle.png) 실측 타원 파라미터.
    // 2026-08-24 5차 수정(사용자 재실측 피드백): 이전(4차) 실측은 좌/우/전면 극점 3개만 잡아 반경을
    // 추정하는 성긴 방식이라 정밀도가 부족했다. 이번엔 alpha>128 픽셀을 열(x)마다/행(y)마다 촘촘히
    // (1px 단위) 스캔해 전체 외곽 경계점(수백~수천 개)을 모으고, 최소자승법(algebraic ellipse fit)으로
    // 축정렬 타원(x-cx)^2/rx^2+(y-cy)^2/ry^2=1을 직접 피팅했다.
    // - 하단 경계(각 열의 최하단 y, col_bot)는 x=105~1431 전 구간에서 노이즈 없이 매끈해 전량 사용.
    // - 상단 경계(각 열의 최상단 y, col_top)는 x≈319~1168 구간에서 불연속 점프가 발생하는데, 이는
    //   원판 위 사각 플랫폼이 아니라 circle.png 자산 자체가 "뒤쪽(위) 안쪽 넓은 V자 영역"을 처음부터
    //   완전 투명(alpha=0)으로 비워둔 것이었다(어차피 리더기 몸통에 항상 가려지는 부분이라 자산
    //   제작 시 생략됨) — 이 구간은 피팅에서 제외하고, 좌/우 측면 플랭크(x=105~318, x=1169~1431)만
    //   사용했다. 총 1804개 경계점, 피팅 잔차(정규화 값이 1.0에서 벗어난 정도) 표준편차 0.0049로
    //   매우 타이트하게 수렴: 중심(768.83,610.35), 반경(666.17,346.96) — 1536x1024 기준.
    // - 340x226.67 캔버스로 스케일(340/1536≈0.221354) 변환: 중심(170.18,135.10), 반경(147.46,76.80).
    // - 검증: Python(Pillow)으로 위 타원을 circle.png 위에 빨간 선으로 그려 별도 PNG로 저장,
    //   원판의 흰/파란 테두리 라인과 픽셀 단위로 겹치는 것을 육안 확인했다(좌/우/하단 전 구간 및
    //   상단 좌우 플랭크 모두 일치).
    private const double EllipseCenterX = 170.18;
    private const double EllipseCenterY = 135.10;
    private const double EllipseRadiusX = 147.46;
    private const double EllipseRadiusY = 76.80;

    // Ramanujan 근사로 다시 계산한 이 타원(147.46,76.80)의 둘레(≈722.13) — StrokeDashOffset을 정확히
    // 한 바퀴(=둘레)만큼 이동시키면 임의의 StrokeDashArray 패턴과 무관하게 seamless하게 반복된다.
    private const double EllipseCircumference = 722.13;

    private bool _isPlaying;

    public PaymentProcessingRing()
    {
        InitializeComponent();
    }

    /// <summary>진행광 링 + 베이스 펄스를 동시에 무한 재생한다.</summary>
    public void Play()
    {
        if (_isPlaying)
        {
            return;
        }
        _isPlaying = true;

        // 1. 링 아크 — 온전한 폐곡선이므로 한 방향으로 계속 스크롤해도 끊김이 없다(정확히 둘레만큼
        // 이동하면 다음 사이클 시작점이 이전과 완전히 동일해 seamless).
        var dashAnim = new DoubleAnimation(0, -EllipseCircumference, TimeSpan.FromSeconds(OrbitCycleSeconds))
        {
            RepeatBehavior = RepeatBehavior.Forever,
        };
        OrbitGlowArc.BeginAnimation(Shape.StrokeDashOffsetProperty, dashAnim);
        OrbitCoreArc.BeginAnimation(Shape.StrokeDashOffsetProperty, dashAnim);

        // 2. 선두 헤드라이트 점 — 같은 타원을 매개변수화(위=0도, 시계방향)해 0~360도를 계속 순환.
        var xFrames = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(OrbitCycleSeconds),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        var yFrames = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(OrbitCycleSeconds),
            RepeatBehavior = RepeatBehavior.Forever,
        };

        const int numKeyFrames = 48;
        for (int i = 0; i <= numKeyFrames; i++)
        {
            double progress = (double)i / numKeyFrames;
            double angleDeg = 360.0 * progress;
            double rad = angleDeg * Math.PI / 180.0;

            // 위(0도, 12시 방향)에서 시작해 시계방향(오른쪽 먼저)으로 회전 — XAML Path의 A(sweep=1)
            // 방향과 일치시킨다.
            double x = EllipseCenterX + EllipseRadiusX * Math.Sin(rad) - (OrbitHeadLight.Width / 2);
            double y = EllipseCenterY - EllipseRadiusY * Math.Cos(rad) - (OrbitHeadLight.Height / 2);

            var keyTime = KeyTime.FromPercent(progress);
            xFrames.KeyFrames.Add(new LinearDoubleKeyFrame(x, keyTime));
            yFrames.KeyFrames.Add(new LinearDoubleKeyFrame(y, keyTime));
        }
        OrbitHeadLight.BeginAnimation(Canvas.LeftProperty, xFrames);
        OrbitHeadLight.BeginAnimation(Canvas.TopProperty, yFrames);
    }

    /// <summary>애니메이션을 정지하고 리소스를 해제한다.</summary>
    public void Stop()
    {
        _isPlaying = false;
        OrbitGlowArc.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
        OrbitCoreArc.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
        OrbitHeadLight.BeginAnimation(Canvas.LeftProperty, null);
        OrbitHeadLight.BeginAnimation(Canvas.TopProperty, null);
    }
}
