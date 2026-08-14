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
/// 참조한다(리터럴 색상 중복 금지 — CLAUDE.md/PRD 2.1 규칙).
/// </summary>
public sealed class IntegrityCheckRow
{
    public IntegrityCheckRow(string checkTime, string port, string resultCode, string moduleId, string readerId, string posId)
    {
        CheckTime = checkTime;
        Port = port;
        ResultCode = resultCode;
        ModuleId = moduleId;
        ReaderId = readerId;
        PosId = posId;
    }

    public string CheckTime { get; }
    public string Port { get; }

    /// <summary>PRD 4.6: "00" = 정상, 그 외 = 오류.</summary>
    public string ResultCode { get; }

    public string ModuleId { get; }
    public string ReaderId { get; }
    public string PosId { get; }

    public bool IsOk => ResultCode == "00";

    public string ResultText => IsOk ? "정상" : "오류";

    public Brush ResultBackground => (Brush)Application.Current.Resources[IsOk ? "ResultOkBgBrush" : "ResultErrorBgBrush"];

    public Brush ResultForeground => (Brush)Application.Current.Resources[IsOk ? "ResultOkTextBrush" : "ResultErrorTextBrush"];
}
