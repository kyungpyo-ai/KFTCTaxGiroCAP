using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace KFTCOneCAP.Wpf.Converters;

/// <summary>
/// Phase 29 P29-6(docs/payment_relay/PRD.md §12.5) — 결제 화면 응답 카드의 "아직 전송하지 않았습니다"
/// 안내문(<c>HasResponse=false</c>일 때만 보임)에 쓴다. <see cref="InverseBooleanConverter"/>와 같은 이유로
/// 신설한 반전 버전 — <c>BooleanToVisibilityConverter</c>는 <c>ConverterParameter</c>로 반전을 지원하지
/// 않는다.
/// </summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && !b ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
