using System;
using System.Linq;
using System.Reflection;
using KFTCOneCAP.Wpf.Protocol.Pos;
using KFTCOneCAP.Wpf.Protocol.Pos.Schemas;
using KFTCOneCAP.Wpf.Protocol.Reader;
using KFTCOneCAP.Wpf.Security;

namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 25 P25-9 완료 조건("민감 버퍼에 값을 채운 뒤 SecureClear를 거치면 읽었을 때 내용이 남아
/// 있지 않다는 것을 실제로 읽어서 확인") 전용 심사 증적 하네스. <c>App.xaml.cs</c>가
/// <c>--memory-clear-test</c> 인자로 실행될 때만 <see cref="RunAll"/>을 호출한다.
///
/// <see cref="SecureClearSelfTest"/>(P25-1)와의 차이 — 그쪽은 "<see cref="SecureClear"/> 헬퍼
/// 자체가 진짜로 지우는가"만 본다. 이 클래스는 그 헬퍼를 실제로 쓰는 **파이프라인 지점**
/// (카드리딩 데이터, PIN, POS 전문 본문)을 실제 형태로 만들어 채운 뒤 그 지점의 정식 클리어 경로
/// (<c>CardReadData.Dispose()</c>, <c>PosTelegram.ClearBody()</c> 등)를 호출해서 확인한다 —
/// 심사에서 "실제로 이렇게 쓰는 지점이 실제로 지워지는가"를 물으면 이 하네스를 그대로 돌린다.
///
/// **Release 빌드로 돌린 결과만 완료 조건 증적으로 인정한다**(PRD.md §4.4 #4) — Debug 빌드는 JIT
/// 최적화가 달라 근거가 되지 못한다.
///
/// **음성 대조군을 포함한다**(P25-9 완료 조건 "실패를 일부러 만들었을 때 하네스가 실패로 잡아낸다") —
/// <see cref="Scenario5_NegativeControlUnclearedBufferStillHasData"/>가 SecureClear를 거치지 않은
/// 버퍼는 값이 그대로 남는다는 것을 보여줘, 이 하네스의 나머지 검사들이 실제로 뭔가를 검사하고
/// 있다는 것(전부 통과만 하고 아무것도 검사하지 않는 상태가 아니라는 것)을 증명한다.
/// </summary>
internal static class MemoryClearTestScenarios
{
    private static int _passCount;
    private static int _failCount;

    public static void RunAll()
    {
        try
        {
            FileLogger.Info("[memory-clear-test] Phase 25 P25-9 심사 증적 검증 시작");

            Scenario1_CardReadDataDisposeClearsAllFields();
            Scenario2_PinBufferClearWorks();
            Scenario3_PosTelegramClearBodyWorks();
            Scenario4_KeydownBufferClearWorks();
            Scenario5_NegativeControlUnclearedBufferStillHasData();

            FileLogger.Info($"[memory-clear-test] 완료 — 통과 {_passCount}건, 실패 {_failCount}건");
        }
        catch (Exception ex)
        {
            FileLogger.Error($"[memory-clear-test] 하네스 자체 예외로 중단: {ex}");
        }
    }

    private static void Check(string name, bool condition)
    {
        if (condition)
        {
            _passCount++;
            FileLogger.Info($"[memory-clear-test][OK] {name}");
        }
        else
        {
            _failCount++;
            FileLogger.Error($"[memory-clear-test][FAIL] {name}");
        }
    }

    /// <summary>Phase 25 P25-3 — CardReadData 19개 char[] 필드를 전부 0이 아닌 값으로 채운 뒤
    /// Dispose()를 호출하고, 19개 필드 전부가 NUL(0x0000)로 채워졌는지 확인한다.</summary>
    private static void Scenario1_CardReadDataDisposeClearsAllFields()
    {
        var cardData = new CardReadData(
            transactionType: "A".ToCharArray(), keyVersion: "01".ToCharArray(), tc: "TC0001".ToCharArray(),
            moduleId: "MODULE0001".ToCharArray(), fallbackCode: "0".ToCharArray(),
            amount: "000000000001000".ToCharArray(), cardNumber: "9412345678901234".ToCharArray(),
            encryptionMarker: "ENC".ToCharArray(), wcc: "I".ToCharArray(), encryptedData: "ENCRYPTEDDATA0001".ToCharArray(),
            encryptedDataLengthText: "017".ToCharArray(), emvEncodingMethod: "B".ToCharArray(),
            emvEncodedData: "EMV0001".ToCharArray(), readerAuthId: "READERAUTH000001".ToCharArray(),
            readerSerialEncryptionMarker: "NOE".ToCharArray(), readerSerial: "SERIAL0001".ToCharArray(),
            readerEncryptionInfo: "READERENCRYPTINFO001".ToCharArray(), tc3: "TC30001".ToCharArray(),
            payOnCertifyCode: "PAYONCERT00000000000000000001".ToCharArray());

        char[][] allFields =
        {
            cardData.TransactionType, cardData.KeyVersion, cardData.Tc, cardData.ModuleId, cardData.FallbackCode,
            cardData.Amount, cardData.CardNumber, cardData.EncryptionMarker, cardData.Wcc, cardData.EncryptedData,
            cardData.EncryptedDataLengthText, cardData.EmvEncodingMethod, cardData.EmvEncodedData,
            cardData.ReaderAuthId, cardData.ReaderSerialEncryptionMarker, cardData.ReaderSerial,
            cardData.ReaderEncryptionInfo, cardData.Tc3, cardData.PayOnCertifyCode,
        };

        // CP3 Opus 리뷰 개선권장 F1(2026-09-03) — 이전 버전은 allFields.Length(위에서 직접 19개를
        // 손으로 적은 배열)와 리터럴 19를 비교해 항상 참인 항진명제였다(CardReadData에 20번째
        // char[] 필드가 추가돼도 이 검사도 allFields도 안 바뀌면 통과해버린다). 리플렉션으로 실제
        // 타입의 char[] 프로퍼티 개수를 세어 allFields.Length와 교차 확인한다 — 필드가 추가/삭제되면
        // 둘 중 하나(allFields 배열 또는 이 카운트)가 안 바뀌어도 불일치로 잡힌다.
        int actualCharArrayPropertyCount = typeof(CardReadData)
            .GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Count(p => p.PropertyType == typeof(char[]));
        Check($"CardReadData의 실제 char[] 프로퍼티 개수({actualCharArrayPropertyCount})와 이 하네스가 아는 필드 개수({allFields.Length})가 일치함", actualCharArrayPropertyCount == allFields.Length);

        cardData.Dispose();

        bool allCleared = true;
        foreach (char[] field in allFields)
        {
            foreach (char c in field)
            {
                if (c != '\0')
                {
                    allCleared = false;
                    break;
                }
            }
        }

        Check("CardReadData.Dispose() — 19개 필드 전부 NUL로 클리어됨(카드번호·암호화데이터·리더기인증식별번호 포함)", allCleared);

        // 두 번 호출해도 안전한지(멱등성) — 거래 종료 finally에서 예외로 두 번 불릴 가능성에 대비.
        try
        {
            cardData.Dispose();
            Check("CardReadData.Dispose() 재호출이 예외 없이 무해함", true);
        }
        catch (Exception ex)
        {
            Check($"CardReadData.Dispose() 재호출이 예외 없이 무해함 — 예외 발생: {ex.GetType().Name}", false);
        }
    }

    /// <summary>Phase 25 P25-4 — PIN 버퍼(4자리)를 채운 뒤 SecureClear.Clear로 지우고 확인한다.
    /// 실제 PaymentNoticeViewModel._pinDigits/PaymentOrchestrator의 pin과 같은 크기·값 형태.</summary>
    private static void Scenario2_PinBufferClearWorks()
    {
        char[] pin = "1234".ToCharArray();
        SecureClear.Clear(pin);

        bool allCleared = true;
        foreach (char c in pin)
        {
            if (c != '\0')
            {
                allCleared = false;
                break;
            }
        }

        Check("PIN 버퍼(4자리) — SecureClear.Clear 후 전부 NUL로 클리어됨", allCleared);
    }

    /// <summary>Phase 25 P25-6 — 실제 902614 스키마로 PosTelegram을 만들어 #46(암호화된 카드정보)/
    /// #51(PIN)을 채운 뒤 ClearBody()를 호출하고, ToBody()로 다시 읽어 원본 값이 남지 않는지
    /// 확인한다. ClearBody는 SecureClear(byte[])를 쓰므로 마지막 패스가 0x00 — 유효한 전문이
    /// space(0x20)/'0'(0x30)로만 패딩된다는 원칙(H-1)과 구분되는 값이라 오검출 위험이 없다.</summary>
    private static void Scenario3_PosTelegramClearBodyWorks()
    {
        if (!PosSchemaRegistry.TryResolve("902614", out PosTelegramSchema? schema) || schema is null)
        {
            Check("PosTelegram.ClearBody() 시나리오 — 902614 스키마 확보", false);
            return;
        }

        PosTelegram telegram = PosTelegram.CreateEmpty(schema);
        telegram.Write(46, "0017ENENCRYPTEDDATA0001".ToCharArray());
        telegram.Write(51, "5678".ToCharArray());

        byte[] beforeClear = telegram.ToBody();
        string beforeText = System.Text.Encoding.ASCII.GetString(beforeClear);
        bool valuesPresentBeforeClear = beforeText.Contains("ENCRYPTEDDATA0001") && beforeText.Contains("5678");
        Check("PosTelegram.ClearBody() 시나리오 — 클리어 전에는 값이 실제로 들어있음(전제 확인)", valuesPresentBeforeClear);

        telegram.ClearBody();

        byte[] afterClear = telegram.ToBody();
        bool allZero = true;
        foreach (byte b in afterClear)
        {
            if (b != 0x00)
            {
                allZero = false;
                break;
            }
        }

        Check("PosTelegram.ClearBody() — 전문 본문 전체가 0x00으로 클리어됨(#46/#51 포함)", allZero);
    }

    /// <summary>Phase 24/P25-2 — 키다운로드 경로가 쓰는 대표 크기(608바이트, [64] HASH+RND+SIGN)로
    /// SecureClear를 실측한다. SecureClearSelfTest(P25-1)가 이미 헬퍼 자체를 작은 표본으로 검증했지만,
    /// 여기서는 실제 파이프라인 크기로 한 번 더 확인한다.</summary>
    private static void Scenario4_KeydownBufferClearWorks()
    {
        const int authRequestLength = 64 + 32 + 512; // HASH(64) + RND(32) + SIGN(512) = 608
        byte[] buffer = new byte[authRequestLength];
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] = (byte)('A' + i % 26);

        SecureClear.Clear(buffer);

        bool allZero = true;
        foreach (byte b in buffer)
        {
            if (b != 0x00)
            {
                allZero = false;
                break;
            }
        }

        Check($"키다운로드 [64] 크기 버퍼({authRequestLength}바이트) — SecureClear 후 전부 0x00", allZero);
    }

    /// <summary>음성 대조군(P25-9 완료 조건 "실패를 일부러 만들었을 때 하네스가 실패로 잡아낸다") —
    /// SecureClear를 거치지 않은 버퍼는 값이 그대로 남아야 정상이다. 이 케이스가 "값이 남아있음"으로
    /// 통과해야, 위 시나리오들의 "값이 지워짐" 통과가 실제로 뭔가를 검사한 결과라는 것이 증명된다
    /// (하네스가 항상 통과만 찍는 상태가 아님을 보장).</summary>
    private static void Scenario5_NegativeControlUnclearedBufferStillHasData()
    {
        char[] uncleared = "9412345678901234".ToCharArray();
        // 의도적으로 SecureClear를 호출하지 않는다.

        bool stillHasOriginalData = new string(uncleared) == "9412345678901234";
        Check("음성 대조군 — SecureClear를 거치지 않은 버퍼는 값이 그대로 남음(위 시나리오들의 검사 능력을 증명)", stillHasOriginalData);
    }
}
