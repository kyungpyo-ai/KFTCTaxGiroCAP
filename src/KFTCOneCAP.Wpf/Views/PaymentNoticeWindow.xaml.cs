using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KFTCOneCAP.Wpf.Services.Payment;
using KFTCOneCAP.Wpf.ViewModels;

namespace KFTCOneCAP.Wpf.Views;

/// <summary>
/// 결제 알림창 (PRD §5.2). 
/// IC 삽입 / MS 스와이프 / VAN 통신중 3개 상태의 고품질 실사 애니메이션 및 문구 크로스페이드 처리.
/// </summary>
public partial class PaymentNoticeWindow : Window
{
    private const double CrossfadeSeconds = 0.25;
    private const double FadeInSeconds = 0.15;

    // 리더기 이미지 기준 위치 (reader.png: 340x226.67 at Canvas.Left=205, Canvas.Top=260)
    private const double ReaderLeft = 205;
    private const double ReaderTop = 260;

    // 1. IC 카드 위치 (폭 96px, 높이 214.6px - 카드의 하단이 리더기 IC 슬롯에 체결되는 위치)
    private const double CardIcDisplayWidth = 96;
    private const double CardIcRestLeft = 327;
    private const double CardIcRestTop = 145;
    private const double CardIcSlideFromY = -60;

    // 2. MS 카드 위치 — 이동 각도는 화살표(31.4도, 위 참고)와 이미 일치.
    // 2026-08-24 3차 수정: 정지 위치는 리더기 뒤쪽 MS(마그네틱) 슬롯 블록의 앞쪽 모서리(reader.png
    // 실측 — 원본 이미지 x=1080,y=330~640 → 창 좌표 약 x=444,y=333~393)로 이미 맞춰뒀는데도 여전히
    // 리더기를 덮는 문제가 있었다 — 원인은 애니메이션 **구조** 자체였다. IC 카드는 바깥(위)에서
    // 시작해 슬롯 앞(Y=0, 정지 위치)에 "도달하면 멈추고" 그 자리에서 유지하다가 사라지는데(아래
    // PlayIcCardAnimation 키프레임 참고 — 0%에서 시작점, 38%에 정지 위치 도달 후 75~88%까지 유지,
    // 100%에 순간 리셋), MS는 정지 위치를 **지나쳐서 반대쪽 끝까지 왕복**하는 구조였다(SlideFromX
    // ↔ SlideToX, 대칭 왕복). 그래서 정지 위치 자체는 슬롯 앞이었어도 왕복 중 정지 위치를 넘어
    // 리더기 안쪽까지 계속 들어갔다 나왔다 했던 것이다. IC와 완전히 같은 구조(바깥 1점 → 슬롯 앞에서
    // 정지 → 유지 → 순간 리셋)로 PlayMsCardAnimation을 다시 짜고, "바깥" 오프셋 하나만 남겼다
    // (화살표 반대 방향 = 리더기에서 먼 방향, 즉 오른쪽 아래로 화살표 각도만큼).
    private const double CardMsDisplayWidth = 145;
    private const double CardMsRestLeft = 440;
    private const double CardMsRestTop = 290;
    private const double CardMsSlideFromX = 80;
    private const double CardMsSlideFromY = 49;

    // 화살표 위치 및 크기 (IC: 글씨와 겹치지 않도록 카드 왼쪽 아래에 배치)
    private const double ArrowIcDisplayWidth = 52;
    private const double ArrowIcLeft = 255;
    private const double ArrowIcTop = 155;

    private const double ArrowMsDisplayWidth = 85;
    private const double ArrowMsLeft = 485;
    private const double ArrowMsTop = 175;

    private readonly PaymentNoticeViewModel _viewModel;
    private readonly PaymentNoticeEscapeHook _escapeHook;
    private EventHandler? _dispatcherShutdownHandler;
    private bool _isFirstRender = true;
    private bool _isTextAFront = true;

    public PaymentNoticeWindow() : this(new PaymentNoticeViewModel())
    {
        // 기본 실행 시 3초 주기로 3가지 상태(IC -> MS -> PROCESSING) 자동 순환 데모
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        int stateIndex = 0;
        timer.Tick += (_, _) =>
        {
            stateIndex = (stateIndex + 1) % 3;
            _viewModel.State = stateIndex switch
            {
                0 => PaymentNoticeState.IcCardRequest,
                1 => PaymentNoticeState.FallbackCardRequest,
                _ => PaymentNoticeState.VanProcessing,
            };
        };
        // (Opus 검증 리뷰 2026-08-24, H-1) 창을 닫아도 아무도 Stop()하지 않아 타이머가 Dispatcher에
        // 영구히 남아 계속 발화하고, 그 클로저가 창/뷰모델까지 붙들어 누수로 이어지는 결함이 실측으로
        // 확인됐다(닫은 뒤 10초간 3초 주기 그대로 계속 발화). Closed에서 반드시 멈춘다.
        Closed += (_, _) => timer.Stop();
        timer.Start();
    }

    public PaymentNoticeWindow(PaymentNoticeViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;

        SuppressHomeWindowForeground();

        ReaderImage.Source = PaymentNoticeBackgroundSource.ReaderSource;

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        // P13-5: ESC 전역 훅. _tryCancel은 "삼킬지 판정"과 "취소 확정"을 한 번에 동기 처리한다
        // (H-3 수정 — ViewModel.TryMarkCanceled 주석 참고). 통지(RaiseCanceledEvent)만 훅 내부에서
        // Dispatcher.BeginInvoke로 지연된다.
        _escapeHook = new PaymentNoticeEscapeHook(
            tryCancel: () => _viewModel.TryMarkCanceled(),
            notifyCanceled: () => _viewModel.RaiseCanceledEvent(),
            dispatcher: Dispatcher);

        // (Opus 검증 리뷰 2026-08-24, M-1) 훅 설치를 생성자가 아니라 Loaded로 옮겼다 — 생성자에서
        // 설치하면 "창을 만들기만 하고 Show()는 하지 않는" 워밍업류 패턴(HomeWindow의
        // ReaderSetupWindow 워밍업과 같은 최적화가 나중에 이 창에도 적용될 경우)에서 전역 키보드
        // 훅이 화면에 보이지도 않는 창 때문에 걸린 채 남을 수 있다(실측 확인: Show/Close 안 하고
        // 생성만 하면 훅이 영구히 걸림). Loaded는 실제로 화면에 표시될 때만 발생하므로, "보일 때만
        // 설치, 닫히면 해제"가 정확히 대칭을 이룬다.
        Loaded += (_, _) =>
        {
            ApplyState(_viewModel.State, animate: false);
            _escapeHook.Install();
        };
        Closed += PaymentNoticeWindow_Closed;

        // 해제는 PaymentNoticeWindow_Closed에서(3중 보장 중 ①), 아래 Dispatcher.ShutdownStarted
        // 백스톱이 ③(development_plan.md P13-5 "해제 3중 보장").
        _dispatcherShutdownHandler = (_, _) => _escapeHook.Uninstall();
        Dispatcher.ShutdownStarted += _dispatcherShutdownHandler;
    }

    /// <summary>
    /// (docs/payment_relay/development_plan.md P13-4, PRD §5.1) 알림창 표시가 홈 화면을 전면에
    /// 끌어올리지 않는다 — 반대로, 홈 화면이 떠 있는 상태였다면 알림창이 뜨기 전에 홈 화면을
    /// 먼저 트레이로 내린다. 그러지 않으면 알림창을 닫을 때 바로 뒤에 있던 홈 화면이 OS 기본
    /// 활성화 순서상 자동으로 전면에 올라온다(실기 검증 완료 — 2026-08-24).
    /// </summary>
    private static void SuppressHomeWindowForeground()
    {
        if (Application.Current is null)
        {
            return;
        }

        foreach (Window window in Application.Current.Windows)
        {
            if (window is HomeWindow home)
            {
                home.MinimizeToTrayForPaymentNotice();
            }
        }
    }

    private void PlayIcCardAnimation()
    {
        StopCard();

        var yFrames = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(2.4),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        yFrames.KeyFrames.Add(new DiscreteDoubleKeyFrame(CardIcSlideFromY, KeyTime.FromPercent(0.0)));
        yFrames.KeyFrames.Add(new DiscreteDoubleKeyFrame(CardIcSlideFromY, KeyTime.FromPercent(0.08)));
        yFrames.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromPercent(0.38), new KeySpline(0.1, 0.9, 0.2, 1.0)));
        yFrames.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromPercent(0.75)));
        yFrames.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromPercent(0.88)));
        yFrames.KeyFrames.Add(new DiscreteDoubleKeyFrame(CardIcSlideFromY, KeyTime.FromPercent(1.0)));
        CardTranslate.BeginAnimation(TranslateTransform.YProperty, yFrames);

        var opFrames = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(2.4),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        opFrames.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(0.0)));
        opFrames.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(0.10)));
        opFrames.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(0.75)));
        opFrames.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(0.88)));
        opFrames.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(1.0)));
        CardImage.BeginAnimation(UIElement.OpacityProperty, opFrames);
    }

    private void PlayMsCardAnimation()
    {
        StopCard();

        // 2026-08-24: IC 카드(PlayIcCardAnimation)와 완전히 같은 구조로 정정 — 바깥(화살표 반대
        // 방향, 리더기에서 먼 쪽) 1점에서 시작해 슬롯 앞(오프셋 0 = 정지 위치)에 도달하면 "그 자리에서
        // 멈춰" 유지하다가, 순간적으로 바깥으로 리셋 후 다시 반복한다. 정지 위치를 지나쳐 반대쪽까지
        // 왕복하지 않으므로 리더기 안쪽까지 파고드는 문제가 없다.
        var xFrames = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(2.0),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        xFrames.KeyFrames.Add(new DiscreteDoubleKeyFrame(CardMsSlideFromX, KeyTime.FromPercent(0.0)));
        xFrames.KeyFrames.Add(new DiscreteDoubleKeyFrame(CardMsSlideFromX, KeyTime.FromPercent(0.08)));
        xFrames.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromPercent(0.38), new KeySpline(0.2, 0.0, 0.2, 1.0)));
        xFrames.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromPercent(0.75)));
        xFrames.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromPercent(0.88)));
        xFrames.KeyFrames.Add(new DiscreteDoubleKeyFrame(CardMsSlideFromX, KeyTime.FromPercent(1.0)));
        CardTranslate.BeginAnimation(TranslateTransform.XProperty, xFrames);

        var yFrames = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(2.0),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        yFrames.KeyFrames.Add(new DiscreteDoubleKeyFrame(CardMsSlideFromY, KeyTime.FromPercent(0.0)));
        yFrames.KeyFrames.Add(new DiscreteDoubleKeyFrame(CardMsSlideFromY, KeyTime.FromPercent(0.08)));
        yFrames.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromPercent(0.38), new KeySpline(0.2, 0.0, 0.2, 1.0)));
        yFrames.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromPercent(0.75)));
        yFrames.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromPercent(0.88)));
        yFrames.KeyFrames.Add(new DiscreteDoubleKeyFrame(CardMsSlideFromY, KeyTime.FromPercent(1.0)));
        CardTranslate.BeginAnimation(TranslateTransform.YProperty, yFrames);

        var opFrames = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(2.0),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        opFrames.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(0.0)));
        opFrames.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(0.10)));
        opFrames.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(0.75)));
        opFrames.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(0.88)));
        opFrames.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(1.0)));
        CardImage.BeginAnimation(UIElement.OpacityProperty, opFrames);
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PaymentNoticeViewModel.State))
        {
            ApplyState(_viewModel.State, animate: !_isFirstRender);
        }
    }

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

            ConfigureOverlay(state);
            ConfigureCard(state);
            FadeElement(OverlayHost, 1, FadeInSeconds);
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

        // 문구 크로스페이드
        FadeElement(frontText, 0, CrossfadeSeconds);
        FadeElement(backText, 1, CrossfadeSeconds);

        // 오버레이 및 카드 전환: 페이드아웃 -> 새 상태 설정 -> 페이드인
        FadeElement(OverlayHost, 0, CrossfadeSeconds, () =>
        {
            ProcessingIndicator.Stop();
            ConfigureOverlay(state);
            FadeElement(OverlayHost, 1, FadeInSeconds);
        });

        FadeElement(CardImage, 0, CrossfadeSeconds, () =>
        {
            StopCard();
            ConfigureCard(state);
        });
    }

    private void ConfigureCard(PaymentNoticeState state)
    {
        StopCard();

        if (state == PaymentNoticeState.VanProcessing)
        {
            // 거래 중: 카드를 표시하지 않음 (카드 제거)
            CardImage.Source = null;
            CardImage.Opacity = 0;
            return;
        }

        var source = PaymentNoticeBackgroundSource.GetCardSource(state);
        CardImage.Source = source;

        double cardAspect = source is null ? 1.0 : (double)source.PixelHeight / source.PixelWidth;

        if (state == PaymentNoticeState.IcCardRequest)
        {
            CardImage.Width = CardIcDisplayWidth;
            CardImage.Height = CardIcDisplayWidth * cardAspect;

            Canvas.SetLeft(CardImage, CardIcRestLeft);
            Canvas.SetTop(CardImage, CardIcRestTop);

            PlayIcCardAnimation();
        }
        else // FallbackCardRequest (MS)
        {
            CardImage.Width = CardMsDisplayWidth;
            CardImage.Height = CardMsDisplayWidth * cardAspect;

            Canvas.SetLeft(CardImage, CardMsRestLeft);
            Canvas.SetTop(CardImage, CardMsRestTop);

            PlayMsCardAnimation();
        }
    }

    private void StopCard()
    {
        CardImage.BeginAnimation(UIElement.OpacityProperty, null);
        CardTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        CardTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        CardImage.Opacity = 0;
        CardTranslate.X = 0;
        CardTranslate.Y = 0;
    }

    private void ConfigureOverlay(PaymentNoticeState state)
    {
        if (state == PaymentNoticeState.VanProcessing)
        {
            ArrowImage.Visibility = Visibility.Collapsed;
            ProcessingIndicator.Visibility = Visibility.Visible;

            Canvas.SetLeft(OverlayHost, ReaderLeft);
            Canvas.SetTop(OverlayHost, ReaderTop);
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

        bounceDown.Stop(ArrowImage);
        bounceLeft.Stop(ArrowImage);
        ArrowTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        ArrowTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        ArrowTranslate.X = 0;
        ArrowTranslate.Y = 0;

        double aspect = arrowSource is null ? 1 : (double)arrowSource.PixelHeight / arrowSource.PixelWidth;

        if (state == PaymentNoticeState.IcCardRequest)
        {
            double displayWidth = ArrowIcDisplayWidth;
            ArrowImage.Width = displayWidth;
            ArrowImage.Height = displayWidth * aspect;

            Canvas.SetLeft(OverlayHost, ArrowIcLeft);
            Canvas.SetTop(OverlayHost, ArrowIcTop);
            ArrowRotate.Angle = 0;
            bounceDown.Begin(ArrowImage, isControllable: true);
        }
        else // FallbackCardRequest
        {
            double displayWidth = ArrowMsDisplayWidth;
            ArrowImage.Width = displayWidth;
            ArrowImage.Height = displayWidth * aspect;

            Canvas.SetLeft(OverlayHost, ArrowMsLeft);
            Canvas.SetTop(OverlayHost, ArrowMsTop);
            ArrowRotate.Angle = PaymentNoticeBackgroundSource.UseArrowMsAsset
                ? 0
                : PaymentNoticeBackgroundSource.RotatedArrowIcAngleForFallback;
            bounceLeft.Begin(ArrowImage, isControllable: true);
        }
    }

    private static void FadeElement(UIElement element, double to, double durationSeconds, Action? onCompleted = null)
    {
        var anim = new DoubleAnimation(to, TimeSpan.FromSeconds(durationSeconds));
        if (onCompleted != null)
        {
            anim.Completed += (_, _) => onCompleted();
        }
        element.BeginAnimation(UIElement.OpacityProperty, anim);
    }

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

    private void PaymentNoticeWindow_Closed(object? sender, EventArgs e)
    {
        StopCard();
        ProcessingIndicator.Stop();
        ((Storyboard)ArrowImage.Resources["ArrowBounceDownStoryboard"]).Stop(ArrowImage);
        ((Storyboard)ArrowImage.Resources["ArrowBounceLeftStoryboard"]).Stop(ArrowImage);
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;

        // 해제 3중 보장 ①(P13-5). 창이 사라지는 경로(취소/완료/X/Alt+F4)는 전부 Closed로 모인다 —
        // 경로마다 해제 코드를 복붙하지 않는다(P12-6에서 확인된 결함과 같은 종류를 반복하지 않기 위함).
        _escapeHook.Uninstall();
        if (_dispatcherShutdownHandler != null)
        {
            Dispatcher.ShutdownStarted -= _dispatcherShutdownHandler;
            _dispatcherShutdownHandler = null;
        }
    }
}
