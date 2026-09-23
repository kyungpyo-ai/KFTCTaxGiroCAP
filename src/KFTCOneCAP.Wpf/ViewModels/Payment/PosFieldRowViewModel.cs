using System;
using CommunityToolkit.Mvvm.ComponentModel;
using KFTCOneCAP.Wpf.Protocol.Pos;

namespace KFTCOneCAP.Wpf.ViewModels.Payment;

/// <summary>
/// Phase 29 P29-6(docs/payment_relay/development_plan.md "P29-6", PRD.md §12.5) — 결제 화면 필드 표
/// (요청/응답 공용) 한 행. 요청 표에서는 <see cref="Value"/>가 편집 가능하고(카드리딩 필드는 제외), 응답
/// 표에서는 항상 읽기전용이다. 두 표가 같은 행 타입을 공유하되 <see cref="IsReadOnly"/>/
/// <see cref="IsCardReadingField"/> 두 플래그만으로 표시를 구분한다(실제 시각 표현은 View의 DataTrigger가
/// 담당 — 이 클래스는 WPF 타입을 모른다).
/// </summary>
public sealed partial class PosFieldRowViewModel : ObservableObject
{
    private readonly Action<PosFieldRowViewModel>? _onValueChanged;

    public PosFieldRowViewModel(
        PosField field,
        string initialValue,
        bool isReadOnly,
        bool isCardReadingField,
        Action<PosFieldRowViewModel>? onValueChanged = null)
    {
        Number = field.Number;
        Name = field.Name;
        TypeText = field.Type.ToString();
        Length = field.Length;
        IsReadOnly = isReadOnly;
        IsCardReadingField = isCardReadingField;
        _onValueChanged = onValueChanged;
        value = initialValue;
    }

    public int Number { get; }

    public string Name { get; }

    public string TypeText { get; }

    public int Length { get; }

    /// <summary>화면 카드/행의 레이블 표시용(Phase 29 P29-6 UI 재작업, 2026-09-18 사용자 지적 —
    /// 참고 이미지 <c>docs/home_reader_setup/screenshots/pay_screen.png</c> 스타일로 "레이블 + 값"
    /// 카드/행 위주로 바꾸면서 추가). 같은 필드 이름이 여러 번 나오므로(예: "예비 정보 FIELD") 번호를
    /// 함께 표시해 구분한다.</summary>
    public string NumberAndName => $"#{Number} {Name}";

    /// <summary>표현/길이는 참고 이미지에는 없는 정보라 카드 본문에는 넣지 않고 ToolTip으로만 노출한다
    /// (완료 조건의 "표현·길이" 표시 요구를 만족시키되 시각적으로는 레이블/값 위주로 단순하게 유지).</summary>
    public string TypeAndLengthTooltip => $"{TypeText} · {Length}바이트";

    /// <summary>요청 표: 2026-09-18 확정 이후 요청 표는 kiosk 담당 필드만 담으므로 항상 false(편집
    /// 가능). 응답 표: 항상 true(응답은 편집 대상이 아니다).</summary>
    public bool IsReadOnly { get; }

    /// <summary>이 필드가 <c>PosTelegramSchema.FieldsOwnedByOneCap()</c>에 속하는지 — 응답 표에서
    /// "원캡이 카드리딩으로 채운 값"이라는 강조 표시에 쓴다. 2026-09-18 확정 이후 요청 표에는 원캡 담당
    /// 필드가 아예 나타나지 않으므로(kiosk 담당이 아니라서 응답 표로 이동) 요청 표에서는 이 플래그가
    /// 항상 false로 전달된다.</summary>
    public bool IsCardReadingField { get; }

    [ObservableProperty]
    private string value = string.Empty;

    /// <summary>Phase 30 P30-4(PRD §13.3) — 앞 전문 응답값으로 자동 채워진 필드인지. 생성자로만 정해지는
    /// <see cref="IsCardReadingField"/>와 달리 이 값은 전송 이후 런타임에 바뀔 수 있어 관찰 가능한
    /// 프로퍼티다. 값 자체는 여전히 편집 가능하다(PRD §13.3 — 읽기전용으로 잠그지 않는다). P30-5(재생성
    /// 제외)와 P30-6(색 구분)이 이 값을 쓴다 — 이 Task 범위는 값을 정확히 세팅하는 것까지다.</summary>
    [ObservableProperty]
    private bool isChainedField;

    partial void OnValueChanged(string value) => _onValueChanged?.Invoke(this);
}
