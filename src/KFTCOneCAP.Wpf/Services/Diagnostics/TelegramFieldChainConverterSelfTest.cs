using System;
using KFTCOneCAP.Wpf.Protocol.Pos;

namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 30 P30-3 완료 조건 전용 셀프테스트(docs/payment_relay/development_plan.md) —
/// <see cref="TelegramFieldChainConverter"/>의 절삭(반글자 방지)·합산 경계 케이스를 확인한다. 소켓·리더기·UI가
/// 전혀 필요 없는 순수 로직 테스트라 <see cref="PosRandomValueGeneratorSelfTest"/>와 같은 성격이다.
///
/// <c>App.xaml.cs</c>가 <c>--field-chain-converter-test</c> 인자로 실행될 때만 <see cref="RunAll"/>을 호출한다.
/// </summary>
internal static class TelegramFieldChainConverterSelfTest
{
    public static void RunAll()
    {
        try
        {
            FileLogger.Info("[field-chain-converter-test] 시작");

            bool truncateOk = RunTruncateBoundaryCases();
            bool sumOk = RunSumCases();
            bool sumOverflowOk = RunSumOverflowThrowsOnPad();

            bool allPassed = truncateOk && sumOk && sumOverflowOk;
            FileLogger.Info(
                $"[field-chain-converter-test] 완료 — 절삭 경계={(truncateOk ? "통과" : "실패")}, " +
                $"합산={(sumOk ? "통과" : "실패")}, 합산 오버플로={(sumOverflowOk ? "통과" : "실패")}, " +
                $"종합={(allPassed ? "통과" : "실패")}");
        }
        catch (Exception ex)
        {
            FileLogger.Error($"[field-chain-converter-test] 예외로 중단: {ex}");
        }
    }

    /// <summary>
    /// 절삭(Truncate) 경계 케이스 전부(P30-3 완료 조건 그대로): 한글만 초과 / 한글+ASCII 혼합에서 경계가
    /// 한글 중간 / 정확히 한도에 맞는 경우(절삭 없음) / 한도를 1바이트만 넘는 경우 / 빈 값 / 한도보다 짧은
    /// 값(절삭 없음). 매 케이스마다 결과를 CP949로 인코딩했을 때 한도 이하인지, 디코딩했을 때 깨진 글자
    /// (대체 문자 U+FFFD)가 없는지 확인한다.
    /// </summary>
    private static bool RunTruncateBoundaryCases()
    {
        bool allOk = true;

        // 1) 한글만으로 구성된 값이 한도를 초과 — "가나다라마"(각 2바이트=10바이트)를 한도 7바이트로.
        //    7바이트에 담을 수 있는 건 "가나다"(6바이트)까지 — 4번째 글자(가나다"라") 2바이트가 남은
        //    1바이트 예산을 넘으므로 통째로 버려진다.
        allOk &= CheckTruncateCase("한글만 초과", "가나다라마", targetLengthBytes: 7, expected: "가나다");

        // 2) 한글+ASCII 혼합, 경계가 한글 문자 중간에 걸침 — "AB가나다"(A=1+B=1+가=2+나=2+다=2=8바이트)를
        //    한도 5바이트로. "A"(1)+"B"(1)+"가"(2)=4바이트, 다음 "나"(2바이트)를 더하면 6바이트로 5바이트를
        //    넘으므로 "나"를 통째로 버려 "AB가"(4바이트)만 남는다.
        allOk &= CheckTruncateCase("한글+ASCII 혼합, 경계가 한글 중간", "AB가나다", targetLengthBytes: 5, expected: "AB가");

        // 3) 값의 바이트 길이가 한도에 정확히 맞는 경우 — 절삭 없이 원본 그대로 반환.
        allOk &= CheckTruncateCase("정확히 한도에 맞음(절삭 없음)", "가나다", targetLengthBytes: 6, expected: "가나다");

        // 4) 한도를 딱 1바이트만 넘는 경우 — "가나다"(6바이트)를 한도 5바이트로. "가나"(4바이트) +
        //    "다"(2바이트)=6바이트로 5바이트를 넘으므로 "다"를 버려 "가나"(4바이트)만 남는다.
        allOk &= CheckTruncateCase("한도를 1바이트만 초과", "가나다", targetLengthBytes: 5, expected: "가나");

        // 5) 빈 값 — 절삭 없이 빈 문자열 그대로.
        allOk &= CheckTruncateCase("빈 값", string.Empty, targetLengthBytes: 10, expected: string.Empty);

        // 6) 값이 한도보다 짧은 경우 — 절삭 없음, 원본 그대로.
        allOk &= CheckTruncateCase("한도보다 짧음(절삭 없음)", "AB가", targetLengthBytes: 20, expected: "AB가");

        return allOk;
    }

    private static bool CheckTruncateCase(string caseName, string input, int targetLengthBytes, string expected)
    {
        string result = TelegramFieldChainConverter.Convert(
            FieldChainConversion.Truncate,
            new[] { input },
            targetLengthBytes);

        bool ok = true;

        if (result != expected)
        {
            FileLogger.Error(LogCategory.App,
                $"[field-chain-converter-test] ★ [{caseName}] 결과 불일치 — 기대=\"{expected}\", 실제=\"{result}\"");
            ok = false;
        }

        byte[] encoded = PosMessageEncoding.Value.GetBytes(result);
        if (encoded.Length > targetLengthBytes)
        {
            FileLogger.Error(LogCategory.App,
                $"[field-chain-converter-test] ★ [{caseName}] 인코딩 결과({encoded.Length}바이트)가 " +
                $"한도({targetLengthBytes}바이트)를 초과함");
            ok = false;
        }

        string decoded = PosMessageEncoding.Value.GetString(encoded);
        if (decoded != result || decoded.IndexOf('�') >= 0)
        {
            FileLogger.Error(LogCategory.App,
                $"[field-chain-converter-test] ★ [{caseName}] 디코딩 시 글자가 깨짐 — 원본=\"{result}\", " +
                $"디코딩=\"{decoded}\"");
            ok = false;
        }

        if (ok)
        {
            FileLogger.Info(
                $"[field-chain-converter-test] [{caseName}] 통과 — 입력=\"{input}\"({PosMessageEncoding.Value.GetByteCount(input)}바이트), " +
                $"한도={targetLengthBytes}바이트, 결과=\"{result}\"({encoded.Length}바이트)");
        }

        return ok;
    }

    /// <summary>합산(Sum) — 정상 합산과 빈 문자열 소스가 0으로 처리되는지 확인한다.</summary>
    private static bool RunSumCases()
    {
        bool allOk = true;

        string sum1 = TelegramFieldChainConverter.Convert(
            FieldChainConversion.Sum, new[] { "100", "200", "300" }, targetLengthBytes: 15);
        if (sum1 != "600")
        {
            FileLogger.Error(LogCategory.App,
                $"[field-chain-converter-test] ★ [합산 정상] 기대=\"600\", 실제=\"{sum1}\"");
            allOk = false;
        }

        // 빈 문자열(공백만 있던 필드)은 0으로 취급 — 연쇄 전 임의값 단계 등에서 아직 채워지지 않은
        // 소스 필드가 있어도 예외로 죽지 않아야 한다.
        string sum2 = TelegramFieldChainConverter.Convert(
            FieldChainConversion.Sum, new[] { "100", string.Empty, "50" }, targetLengthBytes: 15);
        if (sum2 != "150")
        {
            FileLogger.Error(LogCategory.App,
                $"[field-chain-converter-test] ★ [합산, 빈 소스 포함] 기대=\"150\", 실제=\"{sum2}\"");
            allOk = false;
        }

        string sum3 = TelegramFieldChainConverter.Convert(
            FieldChainConversion.Sum, new[] { string.Empty, string.Empty }, targetLengthBytes: 15);
        if (sum3 != "0")
        {
            FileLogger.Error(LogCategory.App,
                $"[field-chain-converter-test] ★ [합산, 전부 빈 소스] 기대=\"0\", 실제=\"{sum3}\"");
            allOk = false;
        }

        if (allOk)
            FileLogger.Info("[field-chain-converter-test] 합산 케이스(정상/빈 소스 혼합/전부 빈 소스) 전부 통과");

        return allOk;
    }

    /// <summary>
    /// 합산 결과가 실제로 <see cref="PosField.Pad"/>를 통과하는지, 자리수를 넘으면 예외가 나는지 확인한다
    /// (PRD §13.7 — 조용히 잘리지 않고 드러나는 게 의도적 설계). N15 필드 하나짜리 임시 스키마를 만들어
    /// 오버플로 합산값을 <see cref="PosTelegram.Write"/> 경로에 태운다.
    /// </summary>
    private static bool RunSumOverflowThrowsOnPad()
    {
        var field = new PosField(1, "합성 테스트 필드(N15)", PosFieldType.N, length: 15, position: 0, PosFieldOwner.Kiosk);
        var schema = new PosTelegramSchema("TEST-SUM", new[] { field }, totalLength: 15);

        // 정상 범위(15자리 이내) — Pad가 예외 없이 통과해야 한다.
        string normalSum = TelegramFieldChainConverter.Convert(
            FieldChainConversion.Sum, new[] { "123456789012340", "0" }, targetLengthBytes: 15);
        bool normalOk;
        try
        {
            PosTelegram telegram = PosTelegram.CreateEmpty(schema);
            telegram.Write(1, normalSum);
            normalOk = telegram.Read(1) == normalSum;
            if (!normalOk)
            {
                FileLogger.Error(LogCategory.App,
                    $"[field-chain-converter-test] ★ [합산 정상 범위 Pad] 기대=\"{normalSum}\", " +
                    $"실제=\"{telegram.Read(1)}\"");
            }
        }
        catch (Exception ex)
        {
            FileLogger.Error(LogCategory.App,
                $"[field-chain-converter-test] ★ [합산 정상 범위 Pad] 예외 없이 통과해야 하는데 예외 발생: {ex.Message}");
            normalOk = false;
        }

        // 오버플로(16자리) — Sum() 자체는 자리수를 검사하지 않고 그대로 반환하지만, Pad()가 예외를
        // 던져야 한다(조용히 잘리지 않음).
        string overflowSum = TelegramFieldChainConverter.Convert(
            FieldChainConversion.Sum, new[] { "999999999999999", "1" }, targetLengthBytes: 15);
        bool overflowThrows;
        try
        {
            PosTelegram telegram = PosTelegram.CreateEmpty(schema);
            telegram.Write(1, overflowSum);
            FileLogger.Error(LogCategory.App,
                $"[field-chain-converter-test] ★ [합산 오버플로 Pad] 예외가 나야 하는데 나지 않음 — " +
                $"합산값=\"{overflowSum}\"({overflowSum.Length}자리)");
            overflowThrows = false;
        }
        catch (PosProtocolException)
        {
            overflowThrows = true;
        }
        catch (Exception ex)
        {
            FileLogger.Error(LogCategory.App,
                $"[field-chain-converter-test] ★ [합산 오버플로 Pad] 예상과 다른 예외 타입 발생: {ex.GetType().Name}: {ex.Message}");
            overflowThrows = false;
        }

        bool ok = normalOk && overflowThrows;
        if (ok)
        {
            FileLogger.Info(
                $"[field-chain-converter-test] 합산→Pad 경로 — 정상 범위(\"{normalSum}\") 통과, " +
                $"오버플로(\"{overflowSum}\", {overflowSum.Length}자리)는 PosProtocolException 발생 확인");
        }

        return ok;
    }
}
