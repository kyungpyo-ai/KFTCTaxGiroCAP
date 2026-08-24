using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using KFTCOneCAP.Wpf.Services.Payment;
using KFTCOneCAP.Wpf.ViewModels;

namespace KFTCOneCAP.Wpf.Views;

/// <summary>
/// 결제 알림창(PRD §5.2, development_plan.md P13-3). IC / FALLBACK / VAN 통신중 3개 상태를
/// 창 재생성 없이 <see cref="PaymentNoticeViewModel.State"/> 변경만으로 전환한다.
///
/// ===== 자산 구조(2026-08-21 재구성) =====
/// 정지 리더기(<c>ReaderImage</c>, 3개 상태 공용, 교체 없음) + 화살표/로딩 인디케이터 오버레이
/// (<c>OverlayHost</c>, 상태 전환 시 순차 페이드) + 카드(<c>CardImage</c>, 실제 카드 이미지, 순차 페이드
/// + 반복 슬라이드).
/// 문구(<c>TextPanelA/B</c>)만 기존처럼 겹쳐서 동시에 크로스페이드한다. 자세한 배경은
/// <c>docs/payment_relay/development_plan.md</c> P13-1 "수정(2026-08-21, ...)" 단락 참고.
///
/// 이 Phase(시각 구현 범위)에서는 취소 버튼 클릭이 아무 동작도 하지 않는다(P13-2/P13-5의 취소 1회
/// 제한·ESC 전역 훅·<c>IPaymentNoticePresenter</c> 제어 진입점은 이번 범위 밖 — 사용자 지시에 따라
/// 시각/전환 데모만 구현). 이미지 매핑은 <see cref="PaymentNoticeBackgroundSource"/> 한 곳에서만
/// 이루어진다(리더기: <c>ReaderSource</c>, 화살표: <c>GetArrowSource</c>).
/// </summary>
public partial class PaymentNoticeWindow : Window
{
    private const double CrossfadeSeconds = 0.25;
    private const double FadeInSeconds = 0.15;

    // 리더기 이미지 배치(reader.png, Canvas.Left=205, Top=245, 340x226.67) — 화살표/카드/로딩
    // 인디케이터 위치는 전부 이 사각형을 기준으로 계산한다.
    private const double ReaderLeft = 205;
    private const double ReaderTop = 285;
    private const double ReaderWidth = 340;
    private const double ReaderHeight = 226.67;
    private const double ReaderCenterX = ReaderLeft + ReaderWidth / 2; // 375

    // 카드 표시 크기/정지 위치(2026-08-21: 벡터 대신 실제 카드 이미지 ic_card.png(391x874)/
    // ms_card.png(624x770) — ArrowImage와 동일하게 Stretch=Uniform 기준 Width만 정하고 Height는
    // 소스 비율로 계산한다. 정밀한 슬롯 좌표 매칭이 아니라 스크린샷으로 육안 조정한 값이다.
    private const double CardIcDisplayWidth = 92;
    private const double CardIcRestTop = 325;
    private const double CardFallbackDisplayWidth = 150;

    // 카드 반복 슬라이드 오프셋/시간(기존 PaymentCardShape 벡터 카드와 동일한 왕복 방식 — 화살표(0.6~
    // 0.65초)보다 느린 1.1초로 템포를 분리한다).
    private const double CardIcSlideFromY = -160;
    private const double CardFallbackSlideFromX = 220;
    private const double CardSlideSeconds = 1.1;

    // 화살표 표시 너비(Uniform 스트레치 기준, 높이는 소스 이미지 비율로 계산) — IC는 리더기 바로
    // 위에서 아래를 가리키고, FALLBACK은 리더기 오른쪽에서 왼쪽을 가리킨다. 1차 시도(160x160 고정
    // 박스) 스크린샷에서 화살표가 문구 줄과 겹치는 문제가 확인되어, 화살표 실제 표시 크기 + 리더기
    // 위치를 아래로 40px 내려 문구와 화살표 사이 여백을 넉넉히 확보했다(2차 스크린샷으로 재확인).
    private const double ArrowIcDisplayWidth = 64;
    private const double ArrowIcOverlapIntoReader = 14; // 화살표 아래쪽 끝이 리더기 위쪽 여백에 살짝 겹치게
    private const double ArrowFallbackDisplayWidth = 110;
    // reader.png는 투명 여백이 넓어(모델 실제 폭이 박스 폭의 ~76%) 여백만큼 더 겹쳐야 화살표가
    // 리더기 모델 가장자리에 실제로 맞닿는다(스크린샷으로 확인해 조정한 값).
    private const double ArrowFallbackOverlapIntoReader = 95;

    // ReaderImage의 실제 XAML Canvas 위치(PaymentNoticeWindow.xaml 참고) — ReaderTop(위) 상수와 다르다.
    // ReaderTop은 화살표/카드 위치를 육안으로 맞추며 누적된 참조값(현재 XAML의 245와 40px 차이가
    // 있지만 그 값을 기준으로 이미 화면 검증까지 끝난 상태라 건드리지 않는다). ProcessingIndicator는
    // reader.png 자체를 픽셀 스캔해 좌표를 잡았으므로 반드시 ReaderImage의 실제 위치와 겹쳐야 한다.
    private const double ReaderImageLeft = 205;
    private const double ReaderImageTop = 245;

    private readonly PaymentNoticeViewModel _viewModel;
    private readonly Storyboard _cardSlideDownStoryboard;
    private readonly Storyboard _cardSlideLeftStoryboard;
    private Storyboard? _activeCardStoryboard;
    private bool _isFirstRender = true;
    // Storyboard.Begin()이 완료된 뒤에도 애니메이션이 속성값을 계속 보유(HoldEnd)하므로 Opacity를
    // 직접 읽어 앞/뒤 패널을 판정하지 않는다(원본 P13-3 설계와 동일한 이유) — 명시적 플래그로 추적한다.
    private bool _isTextAFront = true;

    public PaymentNoticeWindow(PaymentNoticeViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;

        // 정지 리더기 — 배경 소스 단일 지점(PaymentNoticeBackgroundSource) 규칙에 따라 여기 한 곳에서만
        // Source를 설정한다. 3개 상태 모두 동일하므로 이후 다시 건드리지 않는다.
        ReaderImage.Source = PaymentNoticeBackgroundSource.ReaderSource;

        // 카드 반복 슬라이드 Storyboard(IC=위→아래, FALLBACK=오른쪽→왼쪽) — ArrowImage의 두 Storyboard와
        // 같은 이유로 미리 만들어 두고 상태 전환 시 재생/정지만 한다(같은 CardTranslate를 공유하므로
        // 전환 시 반드시 먼저 Stop 해야 두 축이 동시에 남는 결함을 막는다, ConfigureCard 참고).
        _cardSlideDownStoryboard = new Storyboard { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true };
        AddTranslateAnimation(_cardSlideDownStoryboard, CardTranslate, "Y", CardIcSlideFromY, 0, CardSlideSeconds);

        _cardSlideLeftStoryboard = new Storyboard { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true };
        AddTranslateAnimation(_cardSlideLeftStoryboard, CardTranslate, "X", CardFallbackSlideFromX, 0, CardSlideSeconds);

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        Loaded += (_, _) => ApplyState(_viewModel.State, animate: false);
        Closed += PaymentNoticeWindow_Closed;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PaymentNoticeViewModel.State))
        {
            ApplyState(_viewModel.State, animate: !_isFirstRender);
        }
    }

    /// <summary>
    /// 상태를 반영한다. 문구는 즉시(최초 표시) 또는 크로스페이드(겹쳐서 동시 반대 방향)로,
    /// 화살표/로딩 인디케이터·카드는 이전 것 페이드아웃 → 내용 교체 → 새 것 페이드인(순차)으로
    /// 전환한다(development_plan.md P13-1 "수정(2026-08-21, ...)" 크로스페이드 규칙).
    /// </summary>
    private void ApplyState(PaymentNoticeState state, bool animate)
    {
        _isFirstRender = false;

        if (!animate)
        {
            ApplyText(TextAKr1, TextAKr2, TextAEn1, TextAEn2, state);
            TextPanelA.Opacity = 1;
            TextPanelB.Opacity = 0;
            _isTextAFront = true;

            OverlayHost.Opacity = 0;
            CardImage.Opacity = 0;
            StopCard();
            ProcessingIndicator.Stop();
            ArrowImage.Visibility = Visibility.Collapsed;

            ConfigureAndFadeIn(state);
            return;
        }

        var frontText = _isTextAFront ? TextPanelA : TextPanelB;
        var backText = _isTextAFront ? TextPanelB : TextPanelA;
        _isTextAFront = !_isTextAFront;

        if (ReferenceEquals(backText, TextPanelA))
        {
            ApplyText(TextAKr1, TextAKr2, TextAEn1, TextAEn2, state);
        }
        else
        {
            ApplyText(TextBKr1, TextBKr2, TextBEn1, TextBEn2, state);
        }

        var storyboard = new Storyboard();
        AddOpacityAnimation(storyboard, frontText, 1, 0, CrossfadeSeconds);
        AddOpacityAnimation(storyboard, backText, 0, 1, CrossfadeSeconds);

        // 화살표/로딩/카드는 문구와 같은 타이밍에 페이드아웃(있었다면) — 완료 후 내용을 교체하고 페이드인.
        AddOpacityAnimation(storyboard, OverlayHost, OverlayHost.Opacity, 0, CrossfadeSeconds);
        AddOpacityAnimation(storyboard, CardImage, CardImage.Opacity, 0, CrossfadeSeconds);

        storyboard.Completed += (_, _) =>
        {
            StopCard();
            ProcessingIndicator.Stop();
            ConfigureAndFadeIn(state);
        };
        storyboard.Begin(this);
    }

    /// <summary>
    /// 페이드아웃(또는 최초 표시)이 끝난 뒤: 오버레이(화살표/로딩)와 카드를 새 상태에 맞게 재배치·
    /// 재구성하고 함께 페이드인한 뒤 반복 애니메이션을 시작한다.
    /// </summary>
    private void ConfigureAndFadeIn(PaymentNoticeState state)
    {
        ConfigureOverlay(state);
        ConfigureCard(state);
        // VanProcessing: 카드 없음(ConfigureCard가 이미지를 비우고 Stop 상태로 둔다).

        var fadeIn = new Storyboard();
        AddOpacityAnimation(fadeIn, OverlayHost, 0, 1, FadeInSeconds);

        if (state != PaymentNoticeState.VanProcessing)
        {
            AddOpacityAnimation(fadeIn, CardImage, 0, 1, FadeInSeconds);
        }

        fadeIn.Begin(this);
    }

    /// <summary>
    /// CardImage(카드) 위치·내용을 상태에 맞게 구성하고 반복 슬라이드를 시작한다. 리더기/화살표와
    /// 마찬가지로 이 메서드가 카드 이미지를 화면에 실제로 반영하는 유일한 지점이다(매핑 자체는
    /// PaymentNoticeBackgroundSource.GetCardSource 한 곳).
    /// </summary>
    private void ConfigureCard(PaymentNoticeState state)
    {
        if (state == PaymentNoticeState.VanProcessing)
        {
            CardImage.Source = null;
            return;
        }

        var cardSource = PaymentNoticeBackgroundSource.GetCardSource(state);
        CardImage.Source = cardSource;

        // 두 상태가 같은 CardTranslate(X/Y)를 공유하므로, 전환 시 이전 상태의 Storyboard를 반드시
        // 먼저 멈춘다 — 그러지 않으면 IC(Y축)와 FALLBACK(X축) 애니메이션이 동시에 남아 카드가
        // 대각선으로 움직이는 결함이 생긴다(ConfigureOverlay의 화살표와 동일한 이유).
        _cardSlideDownStoryboard.Stop(CardImage);
        _cardSlideLeftStoryboard.Stop(CardImage);
        CardTranslate.X = 0;
        CardTranslate.Y = 0;

        double displayWidth;
        double aspect = cardSource is null ? 1 : (double)cardSource.PixelHeight / cardSource.PixelWidth;

        if (state == PaymentNoticeState.IcCardRequest)
        {
            displayWidth = CardIcDisplayWidth;
            var displayHeight = displayWidth * aspect;
            CardImage.Width = displayWidth;
            CardImage.Height = displayHeight;

            Canvas.SetLeft(CardImage, ReaderCenterX - displayWidth / 2);
            Canvas.SetTop(CardImage, CardIcRestTop);

            _activeCardStoryboard = _cardSlideDownStoryboard;
            _cardSlideDownStoryboard.Begin(CardImage, isControllable: true);
        }
        else
        {
            displayWidth = CardFallbackDisplayWidth;
            var displayHeight = displayWidth * aspect;
            CardImage.Width = displayWidth;
            CardImage.Height = displayHeight;

            Canvas.SetLeft(CardImage, ReaderLeft + ReaderWidth - displayWidth * 0.35);
            Canvas.SetTop(CardImage, ReaderTop + (ReaderHeight - displayHeight) / 2);

            _activeCardStoryboard = _cardSlideLeftStoryboard;
            _cardSlideLeftStoryboard.Begin(CardImage, isControllable: true);
        }
    }

    private void StopCard()
    {
        _activeCardStoryboard?.Stop(CardImage);
        _activeCardStoryboard = null;
    }

    /// <summary>
    /// OverlayHost(화살표/로딩 인디케이터) 위치·내용을 상태에 맞게 구성하고 반복 애니메이션을 시작한다.
    /// 이 메서드가 리더기/화살표 이미지를 화면에 실제로 반영하는 유일한 지점이다(매핑 자체는
    /// PaymentNoticeBackgroundSource 한 곳).
    /// </summary>
    private void ConfigureOverlay(PaymentNoticeState state)
    {
        if (state == PaymentNoticeState.VanProcessing)
        {
            ArrowImage.Visibility = Visibility.Collapsed;
            ProcessingIndicator.Visibility = Visibility.Visible;

            // ProcessingIndicator(340x226.67)는 ReaderImage와 정확히 같은 위치/크기에 겹쳐 놓는 것을
            // 전제로 내부 좌표를 잡았다(PaymentProcessingIndicator.xaml 상단 주석 참고) — 여기서는
            // ReaderImage와 같은 Canvas 위치(고정 상수 ReaderImageLeft/Top)만 맞춘다.
            Canvas.SetLeft(OverlayHost, ReaderImageLeft);
            Canvas.SetTop(OverlayHost, ReaderImageTop);
            ProcessingIndicator.Play();
            return;
        }

        ProcessingIndicator.Visibility = Visibility.Collapsed;
        ProcessingIndicator.Stop();
        ArrowImage.Visibility = Visibility.Visible;

        var arrowSource = PaymentNoticeBackgroundSource.GetArrowSource(state);
        ArrowImage.Source = arrowSource;

        var arrowResources = ArrowImage.Resources;
        var bounceDown = (Storyboard)arrowResources["ArrowBounceDownStoryboard"];
        var bounceLeft = (Storyboard)arrowResources["ArrowBounceLeftStoryboard"];

        // 두 화살표 반복 애니메이션은 같은 TranslateTransform(X/Y)을 공유하므로, 전환 시 이전 상태의
        // Storyboard를 반드시 먼저 멈춘다 — 그러지 않으면 IC(Y축)와 FALLBACK(X축) 애니메이션이 동시에
        // 남아 화살표가 대각선으로 움직이는 결함이 생긴다.
        bounceDown.Stop(ArrowImage);
        bounceLeft.Stop(ArrowImage);
        ArrowTranslate.X = 0;
        ArrowTranslate.Y = 0;

        // Stretch=Uniform이 정확한 크기로 스케일되도록 Width/Height를 소스 픽셀 비율로 명시한다
        // (Grid가 Auto 크기이므로, 여기서 정한 크기가 곧 오버레이의 표시 크기·박스 크기가 된다).
        double displayWidth;
        double aspect = arrowSource is null ? 1 : (double)arrowSource.PixelHeight / arrowSource.PixelWidth;

        if (state == PaymentNoticeState.IcCardRequest)
        {
            displayWidth = ArrowIcDisplayWidth;
            var displayHeight = displayWidth * aspect;
            ArrowImage.Width = displayWidth;
            ArrowImage.Height = displayHeight;

            // 화살표 아래쪽 끝이 리더기 위쪽 여백에 살짝 겹치도록(슬롯을 가리키는 느낌), 문구 줄과는
            // 겹치지 않도록(스크린샷 확인 후 조정한 값).
            Canvas.SetLeft(OverlayHost, ReaderCenterX - displayWidth / 2);
            Canvas.SetTop(OverlayHost, ReaderTop - displayHeight + ArrowIcOverlapIntoReader);
            ArrowRotate.Angle = 0;
            bounceDown.Begin(ArrowImage, isControllable: true);
        }
        else
        {
            displayWidth = ArrowFallbackDisplayWidth;
            var displayHeight = displayWidth * aspect;
            ArrowImage.Width = displayWidth;
            ArrowImage.Height = displayHeight;

            // 2026-08-21 수정: 원래는 리더기 세로 중앙에 맞춰 배치했는데, 카드(CardImage)의 정지
            // 위치도 같은 세로 범위(리더기 오른쪽)에 있어서 화살표가 카드에 거의 다 가려지는 문제가
            // 실제 화면에서 확인됐다(사용자 지적). 원본 참고 이미지(BG_IMG_MS_illustration.png)에서
            // 화살표가 카드 위쪽에서 아래로 향하는 배치였던 것과 같은 구도로, 화살표를 카드보다 위로
            // 올려 겹치지 않게 했다.
            Canvas.SetLeft(OverlayHost, ReaderLeft + ReaderWidth - ArrowFallbackOverlapIntoReader);
            Canvas.SetTop(OverlayHost, ReaderTop - displayHeight * 0.65);
            ArrowRotate.Angle = PaymentNoticeBackgroundSource.UseArrowMsAsset
                ? 0
                : PaymentNoticeBackgroundSource.RotatedArrowIcAngleForFallback;
            bounceLeft.Begin(ArrowImage, isControllable: true);
        }
    }

    private static void AddOpacityAnimation(Storyboard storyboard, UIElement target, double from, double to, double seconds)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds));
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.OpacityProperty));
        storyboard.Children.Add(animation);
    }

    private static void AddTranslateAnimation(Storyboard storyboard, System.Windows.Media.TranslateTransform target, string axisProperty, double from, double to, double seconds)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(axisProperty));
        storyboard.Children.Add(animation);
    }

    // 문구 원문(원본 BG_IMG_*.bmp에서 그대로 옮김, docs/payment_relay/images 대조 완료). 자산 구조가
    // 바뀌어도 문구 내용은 바뀌지 않는다(이번 작업 지시 원문).
    private static void ApplyText(TextBlock kr1, TextBlock kr2, TextBlock en1, TextBlock en2, PaymentNoticeState state)
    {
        var (kr1Text, kr2Text, en1Text, en2Text) = state switch
        {
            PaymentNoticeState.IcCardRequest => (
                "그림과 같이 카드를 넣어주세요.",
                "결제가 완료될 때 까지 카드를 빼지 마십시오.",
                "Please Insert the card to the IC card reader.",
                "Do not remove the card until the payment is completed"),
            PaymentNoticeState.FallbackCardRequest => (
                "그림과 같이 카드를 긁어주세요.",
                (string?)null,
                "Please swipe the card through the reader",
                (string?)null),
            PaymentNoticeState.VanProcessing => (
                "거래중입니다.",
                (string?)null,
                "Payment is processing",
                (string?)null),
            _ => (string.Empty, (string?)null, string.Empty, (string?)null),
        };

        kr1.Text = kr1Text;
        SetOptionalLine(kr2, kr2Text);
        en1.Text = en1Text;
        SetOptionalLine(en2, en2Text);
    }

    private static void SetOptionalLine(TextBlock block, string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            block.Text = string.Empty;
            block.Visibility = Visibility.Collapsed;
        }
        else
        {
            block.Text = text;
            block.Visibility = Visibility.Visible;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        // TODO(Phase 13 후속): 취소 1회 제한(P13-2)·VanProcessing 중 비활성화·ESC 훅(P13-5)·
        // IPaymentNoticePresenter.Canceled(P13-6)는 이번 시각 구현 범위 밖이라 아직 배선하지 않았다.
    }

    private void PaymentNoticeWindow_Closed(object? sender, EventArgs e)
    {
        // 창이 어떤 경로로 닫히든(X/Alt+F4 등) 카드·화살표·로딩 인디케이터 반복 애니메이션을 반드시
        // 멈춘다(PRD §9 리소스 정리). 화살표는 별도 컨트롤이 아니라 Storyboard를 직접 Begin/Stop하므로
        // 여기서 대상(ArrowImage)에 대해 Stop을 호출한다.
        StopCard();
        ProcessingIndicator.Stop();
        ((Storyboard)ArrowImage.Resources["ArrowBounceDownStoryboard"]).Stop(ArrowImage);
        ((Storyboard)ArrowImage.Resources["ArrowBounceLeftStoryboard"]).Stop(ArrowImage);
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
    }
}
