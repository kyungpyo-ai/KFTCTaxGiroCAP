using System;
using KFTCOneCAP.Wpf.Security;

namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 25 P25-1 완료 조건("Release 빌드에서 실제로 배열이 0으로 채워지는 것을 확인한다") 전용
/// 셀프테스트. <see cref="SecureClear"/>가 버퍼를 기능적으로 실제 값(0)으로 채우는지를 이 클래스
/// 하나로 확인한다 — 전체 파이프라인(카드리딩·PIN·전문 본문)을 훑는 심사 증적용 하네스는
/// P25-9(<c>--memory-clear-test</c>)가 별도로 만든다. 이 클래스는 그 전 단계, "헬퍼 자체가 진짜로
/// 지우는가"만 본다.
///
/// <b>이 테스트가 증명하지 않는 것</b>(CP1 Opus 리뷰 F2, 2026-09-03) — 이 테스트는 <c>Clear</c> 직후
/// 버퍼를 다시 읽어서 검사하는데, 그 읽기 자체가 그 전의 쓰기를 "누군가 나중에 읽는 값"으로 만들어
/// 버린다. 즉 이 테스트가 통과한다는 사실은 <see cref="SecureClear.Clear(byte[])"/>가
/// <c>MethodImplOptions.NoOptimization</c> 없이 짜여 있었어도 통과했을 것이므로, "JIT의 dead store
/// elimination을 막았다"는 증거가 되지 못한다 — 그건 순수 기능 테스트다. JIT 방어의 실제 근거는
/// <see cref="SecureClear"/>의 <c>Clear</c> 메서드들이 <c>NoOptimization</c>으로 애초에 최적화 패스
/// 자체를 타지 않는다는 구조적 사실에 있다(<see cref="SecureClear"/> 클래스 주석 참고). 이 테스트는
/// 그와 별개로 "버퍼가 실제로 0이 된다"는, 그 자체로도 필요한 기능 검증이다.
///
/// Debug 빌드 결과는 근거로 인정하지 않는다(PRD.md §4.4 #4/development_plan.md 위험 #2) — JIT
/// 최적화 동작이 Release와 다르다. 반드시 Release 빌드로 실행한 로그를 완료 조건 증적으로 삼는다.
/// </summary>
internal static class SecureClearSelfTest
{
    public static void RunAll()
    {
        bool byteOk = RunByteArrayCase();
        bool charOk = RunCharArrayCase();
        bool nullOk = RunNullAndEmptyCase();

        bool allPassed = byteOk && charOk && nullOk;
        FileLogger.Info(LogCategory.App,
            $"[SecureClearSelfTest] 완료 — byte[]={(byteOk ? "통과" : "실패")}, char[]={(charOk ? "통과" : "실패")}, " +
            $"null/빈배열={(nullOk ? "통과" : "실패")}, 종합={(allPassed ? "통과" : "실패")}");
    }

    private static bool RunByteArrayCase()
    {
        byte[] buffer = new byte[32];
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] = (byte)(i + 1); // 0이 아닌 값으로 채워, 클리어 전/후를 명확히 구분한다.

        SecureClear.Clear(buffer);

        foreach (byte b in buffer)
        {
            if (b != 0x00)
            {
                FileLogger.Error(LogCategory.App, "[SecureClearSelfTest] byte[] 케이스 실패 — 클리어 후 0이 아닌 바이트가 남아 있음(JIT가 덮어쓰기를 제거했을 가능성)");
                return false;
            }
        }

        return true;
    }

    private static bool RunCharArrayCase()
    {
        char[] buffer = "1234567890".ToCharArray(); // PIN 자리수와 비슷한 길이의 표본.

        SecureClear.Clear(buffer);

        foreach (char c in buffer)
        {
            if (c != '\0')
            {
                FileLogger.Error(LogCategory.App, "[SecureClearSelfTest] char[] 케이스 실패 — 클리어 후 NUL이 아닌 문자가 남아 있음(JIT가 덮어쓰기를 제거했을 가능성)");
                return false;
            }
        }

        return true;
    }

    private static bool RunNullAndEmptyCase()
    {
        try
        {
            SecureClear.Clear((byte[]?)null);
            SecureClear.Clear(Array.Empty<byte>());
            SecureClear.Clear((char[]?)null);
            SecureClear.Clear(Array.Empty<char>());
            return true;
        }
        catch (Exception ex)
        {
            FileLogger.Error(LogCategory.App, $"[SecureClearSelfTest] null/빈 배열 케이스에서 예외 발생: {ex.GetType().Name} - {ex.Message}");
            return false;
        }
    }
}
