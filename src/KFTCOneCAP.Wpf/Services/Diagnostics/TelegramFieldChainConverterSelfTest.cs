using System;
using KFTCOneCAP.Wpf.Protocol.Pos;
using KFTCOneCAP.Wpf.Protocol.Pos.Schemas;
using KFTCOneCAP.Wpf.Services.Van;

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
            bool sumNonNumericOk = RunSumNonNumericThrowsPosProtocolException();
            bool stubAmountRangeOk = RunStubAmountFieldRangeRegression();

            bool allPassed = truncateOk && sumOk && sumOverflowOk && sumNonNumericOk && stubAmountRangeOk;
            FileLogger.Info(
                $"[field-chain-converter-test] 완료 — 절삭 경계={(truncateOk ? "통과" : "실패")}, " +
                $"합산={(sumOk ? "통과" : "실패")}, 합산 오버플로={(sumOverflowOk ? "통과" : "실패")}, " +
                $"합산 비숫자={(sumNonNumericOk ? "통과" : "실패")}, " +
                $"스텁 금액 범위 회귀={(stubAmountRangeOk ? "통과" : "실패")}, " +
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

        // 빈 문자열(공백만 있던 필드)은 0으로 취급 — 실 VAN이 해당 업무부를 공백으로 돌려줄 수 있어도
        // 예외로 죽지 않아야 한다(P30 체크포인트 지적 L-5, 2026-09-23 정정 — TelegramFieldChainConverter
        // XML 문서 주석 참고).
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

    /// <summary>
    /// M-3(2026-09-23 P30 체크포인트) — 합산 소스 중 하나가 숫자로 파싱되지 않으면 <see
    /// cref="FormatException"/>이 그대로 새지 않고 <see cref="PosProtocolException"/>으로 감싸져 나오는지
    /// 확인한다(PRD §13.3 — 연쇄로 채워진 필드도 사용자가 계속 편집 가능해야 하고, 그 편집값이 숫자가
    /// 아닐 수 있다).
    /// </summary>
    private static bool RunSumNonNumericThrowsPosProtocolException()
    {
        bool ok;
        try
        {
            TelegramFieldChainConverter.Convert(
                FieldChainConversion.Sum, new[] { "100", "사용자가입력한문자" }, targetLengthBytes: 15);
            FileLogger.Error(LogCategory.App,
                "[field-chain-converter-test] ★ [합산 비숫자 소스] 예외가 나야 하는데 나지 않음");
            ok = false;
        }
        catch (PosProtocolException ex) when (ex.InnerException is FormatException)
        {
            ok = true;
        }
        catch (Exception ex)
        {
            FileLogger.Error(LogCategory.App,
                $"[field-chain-converter-test] ★ [합산 비숫자 소스] 예상과 다른 예외 발생: {ex.GetType().Name}: {ex.Message}");
            ok = false;
        }

        if (ok)
            FileLogger.Info("[field-chain-converter-test] 합산 비숫자 소스 — PosProtocolException(inner=FormatException) 발생 확인");

        return ok;
    }

    /// <summary>
    /// M-2(2026-09-23 P30 체크포인트) — <c>StubVanRelayService</c>가 501008/800000 응답의 금액 필드
    /// (<c>#30/#31/#32</c>, <c>#24</c>)를 반복 생성할 때, 그 값들을 합산한 결과가 N15/N12 한도를 넘지
    /// 않고 <see cref="PosField.Pad"/>를 예외 없이 통과하는지 반복 검증한다(스텁이 전 자리 난수를 쓰던
    /// 예전 버전은 약 83% 확률로 자리수 초과 예외가 났었다). 501008/800000 요청을 임의값으로 채워
    /// <see cref="StubVanRelayService"/>에 실제로 태우고, 그 응답을 다시 파싱해 <see
    /// cref="TelegramFieldChainMap"/>의 합산 연쇄(902614 #27, #29, 800000 #15)를 재현한다.
    /// </summary>
    private static bool RunStubAmountFieldRangeRegression()
    {
        // StubVanRelayService.RelayAsync가 매 호출 1초 고정 지연을 두므로(FixedDelay), 반복마다 501008+
        // 800000 두 번 호출해 약 2초씩 걸린다 — 30회면 약 1분. 예전 버그는 단일 시도당 약 83% 확률로
        // 실패했으므로 30회 반복이면 놓칠 확률이 사실상 0(0.17^30)이다.
        const int iterations = 30;

        var random = new Random();
        var stub = new StubVanRelayService();
        PosTelegramSchema noticeSchema = NoticeInquirySchema.Create();
        PosTelegramSchema cardInfoSchema = CardInfoInquirySchema.Create();

        // 합산 결과를 실제로 담을 대상 필드와 같은 크기의 합성 스키마(Pad 예외 여부만 확인하면 되므로
        // 실제 902614/800000 스키마 전체를 만들 필요는 없다).
        var sum902614Field = new PosField(1, "합성 902614 #27/#29(N15)", PosFieldType.N, length: 15, position: 0, PosFieldOwner.Kiosk);
        var sum902614Schema = new PosTelegramSchema("TEST-902614-SUM", new[] { sum902614Field }, totalLength: 15);
        var sum800000Field = new PosField(1, "합성 800000 #15(N15)", PosFieldType.N, length: 15, position: 0, PosFieldOwner.Kiosk);
        var sum800000Schema = new PosTelegramSchema("TEST-800000-SUM", new[] { sum800000Field }, totalLength: 15);

        for (int i = 0; i < iterations; i++)
        {
            try
            {
                PosTelegram noticeRequestTelegram = PosRandomValueGenerator.GenerateRandomRequest(noticeSchema, random);
                // GenerateRandomRequest는 #4(거래 구분 코드)도 kiosk 소유라 임의 숫자로 채운다 —
                // Parse는 #4로 스키마를 역추적하므로, 실제 거래 구분 코드로 덮어써야 501008 스키마로
                // 정상 식별된다(PosRequestTelegram.TransactionTypeFieldNumber/Position/Length 참고).
                noticeRequestTelegram.Write(PosRequestTelegram.TransactionTypeFieldNumber, NoticeInquirySchema.FixedTransactionType);
                PosRequestParseOutcome noticeOutcome = PosRequestTelegram.Parse(noticeRequestTelegram.ToBody());
                if (!noticeOutcome.IsSuccess || noticeOutcome.Telegram is null)
                {
                    FileLogger.Error(LogCategory.App,
                        $"[field-chain-converter-test] ★ [스텁 금액 범위 회귀 #{i}] 501008 요청 파싱 실패");
                    return false;
                }

                VanRelayOutcome noticeVanOutcome = stub.RelayAsync(noticeOutcome.Telegram).GetAwaiter().GetResult();
                PosTelegram noticeResponse = PosTelegram.FromBytes(noticeSchema, noticeVanOutcome.ResponseBody!);
                string field30 = noticeResponse.Read(30);
                string field31 = noticeResponse.Read(31);
                string field32 = noticeResponse.Read(32);

                PosTelegram cardInfoRequestTelegram = PosRandomValueGenerator.GenerateRandomRequest(cardInfoSchema, random);
                cardInfoRequestTelegram.Write(PosRequestTelegram.TransactionTypeFieldNumber, CardInfoInquirySchema.FixedTransactionType);
                PosRequestParseOutcome cardInfoOutcome = PosRequestTelegram.Parse(cardInfoRequestTelegram.ToBody());
                if (!cardInfoOutcome.IsSuccess || cardInfoOutcome.Telegram is null)
                {
                    FileLogger.Error(LogCategory.App,
                        $"[field-chain-converter-test] ★ [스텁 금액 범위 회귀 #{i}] 800000 요청 파싱 실패");
                    return false;
                }

                VanRelayOutcome cardInfoVanOutcome = stub.RelayAsync(cardInfoOutcome.Telegram).GetAwaiter().GetResult();
                PosTelegram cardInfoResponse = PosTelegram.FromBytes(cardInfoSchema, cardInfoVanOutcome.ResponseBody!);
                string field24 = cardInfoResponse.Read(24);

                // 902614 #27 = 501008 #30+#31+#32, 902614 #29 = #27 + (800000 #24를 N15로 widen한 값).
                string sum27 = TelegramFieldChainConverter.Convert(
                    FieldChainConversion.Sum, new[] { field30, field31, field32 }, targetLengthBytes: 15);
                string sum29 = TelegramFieldChainConverter.Convert(
                    FieldChainConversion.Sum, new[] { sum27, field24 }, targetLengthBytes: 15);

                // 800000 #15 = 501008 #30+#31+#32(902614 #27과 동일한 합산 규칙).
                string sum15 = TelegramFieldChainConverter.Convert(
                    FieldChainConversion.Sum, new[] { field30, field31, field32 }, targetLengthBytes: 15);

                PosTelegram probe902614 = PosTelegram.CreateEmpty(sum902614Schema);
                probe902614.Write(1, sum27);
                probe902614.Write(1, sum29);

                PosTelegram probe800000 = PosTelegram.CreateEmpty(sum800000Schema);
                probe800000.Write(1, sum15);
            }
            catch (Exception ex)
            {
                FileLogger.Error(LogCategory.App,
                    $"[field-chain-converter-test] ★ [스텁 금액 범위 회귀 #{i}] 예외 발생(자리수 초과 등): {ex}");
                return false;
            }
        }

        FileLogger.Info($"[field-chain-converter-test] 스텁 금액 범위 회귀 — {iterations}회 반복, Pad 예외 없이 전부 통과");
        return true;
    }
}
