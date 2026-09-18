using System;
using CommunityToolkit.Mvvm.ComponentModel;
using KFTCOneCAP.Wpf.Protocol.Pos;

namespace KFTCOneCAP.Wpf.ViewModels.Payment;

/// <summary>
/// Phase 29 P29-6(docs/payment_relay/development_plan.md "P29-6", PRD.md §12.5) — 결제 화면 필드 표
/// (요청/응답 공용) 한 행. 요청 표에서는 <see cref="Value"/>가 편집 가능하고(카드리딩 필드는 제외), 응답
/// 표에서는 항상 읽기전용이다. 두 표가 같은 행 타입을 공유하되 <see cref="IsReadOnly"/>/
/// <see cref="IsCardReadingField"/>/<see cref="IsStubOverwritten"/> 세 플래그만으로 표시를 구분한다
/// (실제 시각 표현은 View의 DataGridCell <c>CellStyle</c> 트리거가 담당 — 이 클래스는 WPF 타입을 모른다).
/// </summary>
public sealed partial class PosFieldRowViewModel : ObservableObject
{
    private readonly Action<PosFieldRowViewModel>? _onValueChanged;

    public PosFieldRowViewModel(
        PosField field,
        string initialValue,
        bool isReadOnly,
        bool isCardReadingField,
        bool isStubOverwritten = false,
        Action<PosFieldRowViewModel>? onValueChanged = null)
    {
        Number = field.Number;
        Name = field.Name;
        TypeText = field.Type.ToString();
        Length = field.Length;
        IsReadOnly = isReadOnly;
        IsCardReadingField = isCardReadingField;
        IsStubOverwritten = isStubOverwritten;
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

    /// <summary>응답 표 전용 — VAN 스텁(<c>StubVanRelayService.BuildFakeSuccess</c>)이나 자체 실패 응답
    /// (<c>PosResponseTelegram.Failure</c>)이 실제로 덮어쓰는 4개 필드(#3/#6/#7/#8)인지(PRD §12.5). 이
    /// 4개를 뺀 나머지 응답 값은 우리가 보낸 요청 임의값의 에코일 뿐이다.</summary>
    public bool IsStubOverwritten { get; }

    [ObservableProperty]
    private string value = string.Empty;

    partial void OnValueChanged(string value) => _onValueChanged?.Invoke(this);
}
