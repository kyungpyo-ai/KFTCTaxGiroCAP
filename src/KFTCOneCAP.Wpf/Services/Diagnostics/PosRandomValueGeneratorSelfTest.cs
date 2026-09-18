using System;
using System.Collections.Generic;
using System.Linq;
using KFTCOneCAP.Wpf.Protocol.Pos;
using KFTCOneCAP.Wpf.Protocol.Pos.Schemas;

namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 29 P29-4 완료 조건 전용 셀프테스트(docs/payment_relay/development_plan.md) —
/// <see cref="PosRandomValueGenerator"/>가 실제 3전문 스키마(501008/800000/902614)를 예외 없이 채우는지,
/// 그리고 <b>CP949 바이트 길이 경계</b>(홀수 길이 <c>AHN</c> 필드에서 한글 2바이트가 남은 1바이트 예산을
/// 넘기지 않는지)를 확인한다. 실제 스키마에는 홀수 길이 AHN 필드가 없을 수 있어, 경계 케이스는 이 클래스가
/// 임시 스키마를 직접 만들어 확인한다(합성 케이스 — 실제 SPEC 필드가 아니다).
///
/// <c>App.xaml.cs</c>가 <c>--random-value-generator-test</c> 인자로 실행될 때만 <see cref="RunAll"/>을
/// 호출한다. 소켓·리더기·UI가 전혀 필요 없는 순수 로직 테스트라 <see cref="SecureClearSelfTest"/>와 같은
/// 성격이다.
/// </summary>
internal static class PosRandomValueGeneratorSelfTest
{
    public static void RunAll()
    {
        try
        {
            FileLogger.Info("[random-value-generator-test] 시작");

            bool realSchemasOk = RunRealSchemasCase();
            bool oddLengthBoundaryOk = RunOddLengthHangulBoundaryCase();
            bool digitOnlyOk = RunNFieldDigitOnlyCase();

            bool allPassed = realSchemasOk && oddLengthBoundaryOk && digitOnlyOk;
            FileLogger.Info(
                $"[random-value-generator-test] 완료 — 실제 3전문={(realSchemasOk ? "통과" : "실패")}, " +
                $"홀수길이 한글 경계={(oddLengthBoundaryOk ? "통과" : "실패")}, " +
                $"N필드 숫자전용={(digitOnlyOk ? "통과" : "실패")}, 종합={(allPassed ? "통과" : "실패")}");
        }
        catch (Exception ex)
        {
            FileLogger.Error($"[random-value-generator-test] 예외로 중단: {ex}");
        }
    }

    /// <summary>
    /// 501008/800000/902614 세 스키마 전부에서: 생성 → Write(Pad 경유) → ToBody()가 예외 없이 끝나고,
    /// 본문 길이가 스키마 총 길이와 일치하며(PosTelegram 생성자가 이미 강제), 0x00 바이트가 없고,
    /// 원캡 담당 필드는 공백으로 남아 있는지 확인한다(완료 조건 그대로).
    /// </summary>
    private static bool RunRealSchemasCase()
    {
        var random = new Random();
        var schemas = new[]
        {
            NoticeInquirySchema.Create(),
            CardInfoInquirySchema.Create(),
            CardApprovalSchema.Create(),
        };

        bool allOk = true;

        foreach (PosTelegramSchema schema in schemas)
        {
            PosTelegram telegram = PosRandomValueGenerator.GenerateRandomRequest(schema, random);
            byte[] body = telegram.ToBody();

            if (body.Length != schema.TotalLength)
            {
                FileLogger.Error(LogCategory.App,
                    $"[random-value-generator-test] ★ [{schema.TransactionTypeCode}] 본문 길이({body.Length})가 " +
                    $"스키마 총 길이({schema.TotalLength})와 다름");
                allOk = false;
            }

            if (body.Any(b => b == 0x00))
            {
                FileLogger.Error(LogCategory.App,
                    $"[random-value-generator-test] ★ [{schema.TransactionTypeCode}] 본문에 0x00 바이트가 있음");
                allOk = false;
            }

            var ownedByOneCap = new HashSet<int>(schema.FieldsOwnedByOneCap().Select(f => f.Number));
            foreach (int fieldNumber in ownedByOneCap)
            {
                string value = telegram.Read(fieldNumber);
                if (value.Length != 0)
                {
                    FileLogger.Error(LogCategory.App,
                        $"[random-value-generator-test] ★ [{schema.TransactionTypeCode}] 원캡 담당 필드 " +
                        $"#{fieldNumber}가 공백이 아님(값=\"{value}\") — 생성기가 제외 목록을 지키지 않음");
                    allOk = false;
                }
            }

            FileLogger.Info(
                $"[random-value-generator-test] [{schema.TransactionTypeCode}] 필드 {schema.Fields.Count}개 " +
                $"생성 완료 — 본문 {body.Length}바이트, 원캡 담당 {ownedByOneCap.Count}개 공백 유지 확인");
        }

        return allOk;
    }

    /// <summary>
    /// 합성 케이스 — 길이 7(홀수)짜리 AHN 필드 하나로 된 임시 스키마를 100회 생성해, 매번 인코딩 결과가
    /// 정확히 7바이트를 넘지 않는지(=Pad 예외가 나지 않는지) 확인한다. 한글(2바이트)이 홀수 예산에서
    /// 마지막 1바이트를 남기는 경계를 반복 실행으로 실제로 때리게 한다(50% 확률 분기라 100회면 충분히
    /// 자주 걸린다).
    /// </summary>
    private static bool RunOddLengthHangulBoundaryCase()
    {
        var field = new PosField(1, "합성 테스트 필드(홀수 길이 AHN)", PosFieldType.AHN, length: 7, position: 0, PosFieldOwner.Kiosk);
        var schema = new PosTelegramSchema("TEST-ODD", new[] { field }, totalLength: 7);
        var random = new Random();

        for (int i = 0; i < 100; i++)
        {
            try
            {
                string value = PosRandomValueGenerator.GenerateValue(PosFieldType.AHN, 7, random);
                PosTelegram telegram = PosTelegram.CreateEmpty(schema);
                telegram.Write(1, value); // PosField.Pad가 여기서 7바이트 초과 시 예외를 던진다.

                int encodedLength = PosMessageEncoding.Value.GetByteCount(value);
                if (encodedLength > 7)
                {
                    FileLogger.Error(LogCategory.App,
                        $"[random-value-generator-test] ★ 홀수 길이 경계 {i}회차 — 인코딩 결과가 7바이트를 넘음: {encodedLength}바이트");
                    return false;
                }
            }
            catch (Exception ex)
            {
                FileLogger.Error(LogCategory.App,
                    $"[random-value-generator-test] ★ 홀수 길이 경계 {i}회차 — 예외 발생: {ex.Message}");
                return false;
            }
        }

        FileLogger.Info("[random-value-generator-test] 홀수 길이(7바이트) AHN 필드 100회 생성 — 전부 Pad 예외 없이 통과");
        return true;
    }

    /// <summary>N 필드는 숫자만 생성돼야 한다(완료 조건).</summary>
    private static bool RunNFieldDigitOnlyCase()
    {
        var random = new Random();
        for (int i = 0; i < 50; i++)
        {
            string value = PosRandomValueGenerator.GenerateValue(PosFieldType.N, 10, random);
            if (value.Length != 10 || !value.All(char.IsDigit))
            {
                FileLogger.Error(LogCategory.App,
                    $"[random-value-generator-test] ★ N 필드 생성값이 숫자 10자리가 아님: \"{value}\"");
                return false;
            }
        }

        return true;
    }
}
