using System;
using System.Globalization;
using System.Windows.Data;

namespace KFTCOneCAP.Wpf.Converters;

/// <summary>
/// Phase 29 P29-6(docs/payment_relay/PRD.md §12.5) — 결제 화면의 "전송 중에는 버튼 비활성화" 바인딩
/// (<c>IsEnabled="{Binding IsSending, Converter=...}"</c>)에 쓴다. 이 저장소에 지금까지 값 컨버터가
/// 전혀 없었다(1차/2차 범위는 코드비하인드/ViewModel의 파생 bool 프로퍼티로 처리해 옴) — 이 화면만을
/// 위해 ViewModel에 <c>IsNotSending</c> 같은 파생 프로퍼티를 추가하는 대신, 일반적인 bool 반전은 재사용
/// 가능한 컨버터로 두는 편이 통상적인 WPF 관례에 더 가깝다고 판단했다.
/// </summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b ? !b : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b ? !b : value;
}
