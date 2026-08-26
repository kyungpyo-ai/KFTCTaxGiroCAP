using System.Windows;
using System.Windows.Media;

namespace KFTCOneCAP.Wpf.Models;

/// <summary>
/// 무결성 체크 리스트(PRD 4.6)의 한 행. Phase 5(ROADMAP.md)는 실제 조회 로직이 없어(원본도
/// 미구현 스텁) 조회 버튼 클릭 시 더미 데이터로 채운다 — Views/ReaderSetupWindow.xaml.cs
/// QueryButton_Click 참고.
///
/// 결과 칩 색상(ResultBackground/ResultForeground)은 Themes/Colors.xaml에 이미 정의된
/// ResultOkBgBrush/ResultOkTextBrush/ResultErrorBgBrush/ResultErrorTextBrush 리소스를 그대로
/// 참조한다(리터럴 색상 중복 금지 — CLAUDE.md/PRD 2.1 규칙). "통신실패"는 별도 색상을 새로 만들지
/// 않고 "오류"와 같은 빨강 칩을 재사용한다(2026-08-26, 아래 <see cref="ResponseCode"/> 문서 참고).
/// </summary>
public sealed class IntegrityCheckRow
{
    public IntegrityCheckRow(string checkTime, string port, string? responseCode, string moduleId, string readerId, string posId)
    {
        CheckTime = checkTime;
        Port = port;
        ResponseCode = responseCode;
        ModuleId = moduleId;
        ReaderId = readerId;
        PosId = posId;
    }

    public string CheckTime { get; }
    public string Port { get; }

    /// <summary>
    /// 0x72(또는 실패한 0x71) 응답의 업무 응답코드. **null이면 리더기가 응답 자체를 주지 못한
    /// 것**(DLL 연동 실패/타임아웃/통신 오류)이다 — "00"이 아닌 실제 응답코드를 받은 경우와는 원인이
    /// 다르므로 <see cref="ResultText"/>에서 구분해서 보여준다(2026-08-26, PRD_WPF.md 4.6 갱신 —
    /// 실기 확인 중 사용자가 "통신 자체가 실패했을 때는 오류로 남기면 안 된다"고 지적해 반영).
    /// </summary>
    public string? ResponseCode { get; }

    public string ModuleId { get; }
    public string ReaderId { get; }
    public string PosId { get; }

    public bool IsOk => ResponseCode == "00";

    /// <summary>리더기가 응답을 준 적이 있는가 — false면 통신/DLL 레벨에서 이미 실패해 업무
    /// 응답코드 자체가 없다는 뜻이다.</summary>
    public bool HasResponse => ResponseCode != null;

    public string ResultText => ResponseCode switch
    {
        "00" => "정상",
        null => "통신실패",
        _ => "오류",
    };

    public Brush ResultBackground => (Brush)Application.Current.Resources[IsOk ? "ResultOkBgBrush" : "ResultErrorBgBrush"];

    public Brush ResultForeground => (Brush)Application.Current.Resources[IsOk ? "ResultOkTextBrush" : "ResultErrorTextBrush"];
}
