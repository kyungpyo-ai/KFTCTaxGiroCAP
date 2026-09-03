using System;
using System.Runtime.CompilerServices;

namespace KFTCOneCAP.Wpf.Security;

/// <summary>
/// 민감 버퍼(카드정보·PIN·리더기 키 자재)를 지우는 유일한 지점(Phase 25 P25-1, PRD.md §4.3.1).
/// 이 클래스 밖에서 <see cref="Array.Clear(Array,int,int)"/>를 민감 데이터에 직접 호출하지 않는다.
///
/// <b>왜 3회인가</b> — 2026-09-03 인증 시험 기관 안내(PRD.md §4.1): 현행 기준은 DoD 방식 3회
/// 덮어쓰기, 개정 예정 기준은 "0 등 특정 문자로" 1회 + 할당 해제/GC. 개정 후 기준은 개정 전 기준의
/// 부분집합이므로 3회로 구현하면 둘 다 충족한다. 마지막 패스를 <c>0x00</c>으로 끝내 개정 기준의
/// "0으로 덮어쓰기" 문구까지 그대로 만족시킨다. 횟수는 <see cref="OverwritePasses"/> 하나로 빼뒀다 —
/// 기준이 1회로 완화되면 이 상수만 바꾼다.
///
/// <b>GC.Collect()를 부르지 않는다</b> — 기준 문구는 "GC가 이루어지도록"이지 "GC를 호출"이 아니다
/// (PRD.md §4.1). 덮어쓴 뒤 참조를 끊어 GC가 자연 회수하게 두는 것으로 충족한다. 결제 건마다
/// <c>GC.Collect()</c>를 강제하면 힙 전체를 훑어 수십 ms가 튈 수 있다.
///
/// <b>알려진 한계</b>(PRD.md §4.4) — GC 세대 압축이 이 메서드 호출 전에 배열을 다른 주소로 이미
/// 옮겼다면, 그 옛 위치의 내용은 이 메서드로 지울 수 없다. 이 클래스는 "우리가 지금 들고 있는
/// 참조가 가리키는 메모리"만 지운다.
/// </summary>
internal static class SecureClear
{
    /// <summary>덮어쓰기 횟수. 인증 기준이 완화되면 이 값만 바꾼다(PRD.md §4.3.1).</summary>
    private const int OverwritePasses = 3;

    /// <summary><c>byte[]</c> 패스별 덮어쓰기 값 — 마지막이 반드시 <c>0x00</c>으로 끝나야 한다(개정
    /// 기준의 "0 등의 특정 문자로 덮어쓰기" 문구를 만족시키는 근거, PRD.md §4.1).</summary>
    private static readonly byte[] OverwritePattern = { 0x00, 0xFF, 0x00 };

    /// <summary><c>char[]</c> 전용 패스별 덮어쓰기 값. <c>char</c>는 2바이트라 <see cref="OverwritePattern"/>
    /// (byte)를 그대로 캐스팅하면 상위 바이트가 항상 <c>0x00</c>으로 남아, 세 패스 내내 사실상
    /// 하위 바이트만 0x00/0xFF/0x00으로 교대하는 셈이 된다(CP1 Opus 리뷰 F1, 2026-09-03) — 최종
    /// 결과가 전부 0이라 보안 결함은 아니지만 "DoD 3회 교대 덮어쓰기" 설명과 어긋난다. 2바이트
    /// 전체를 채우도록 0x0000 → 0xFFFF → 0x0000을 명시적으로 쓴다.</summary>
    private static readonly char[] CharOverwritePattern = { (char)0x0000, (char)0xFFFF, (char)0x0000 };

    /// <summary>
    /// <paramref name="buffer"/>를 3회 덮어쓴다. <c>null</c>이거나 길이 0이면 아무 일도 하지 않는다
    /// (호출부에 조건 분기를 만들지 않기 위함 — Phase 24 <c>KeyDownloadVanClient</c>가 이미 쓰는
    /// 방식과 동일).
    ///
    /// <see cref="MethodImplOptions.NoOptimization"/>과 <see cref="MethodImplOptions.NoInlining"/>을
    /// 붙여, 이 메서드를 JIT가 MinOpts로만 컴파일하게 강제한다 — dead store elimination을 비롯한
    /// 최적화 패스 자체가 이 메서드 안에서는 돌지 않으므로, 호출부가 덮어쓴 값을 다시 읽는지와
    /// 무관하게 구조적으로 안전하다(CP1 Opus 리뷰 F2, 2026-09-03 — 검증용 셀프테스트는 "버퍼를
    /// 기능적으로 0으로 채운다"의 증거일 뿐, 그 자체가 JIT 방어의 증거는 아니다. 방어의 근거는
    /// 이 속성 조합이 MinOpts를 강제한다는 구조적 사실에 있다). net48에는
    /// <c>System.Security.Cryptography.CryptographicOperations.ZeroMemory</c>가 없다(.NET Core 3.0+
    /// 전용이라 이 프로젝트에서 쓸 수 없다).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
    internal static void Clear(byte[]? buffer)
    {
        if (buffer == null || buffer.Length == 0)
            return;

        for (int pass = 0; pass < OverwritePasses; pass++)
        {
            byte value = OverwritePattern[pass];
            for (int i = 0; i < buffer.Length; i++)
                buffer[i] = value;
        }
    }

    /// <summary><see cref="Clear(byte[])"/>의 <c>char[]</c> 버전 — PIN·카드정보처럼 문자 배열로
    /// 관리하는 필드용(Phase 25 P25-3/P25-4). 패턴은 <see cref="CharOverwritePattern"/>(2바이트
    /// 전체를 채움) — <see cref="OverwritePattern"/>을 캐스팅하지 않는다(F1 수정).</summary>
    [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
    internal static void Clear(char[]? buffer)
    {
        if (buffer == null || buffer.Length == 0)
            return;

        for (int pass = 0; pass < OverwritePasses; pass++)
        {
            char value = CharOverwritePattern[pass];
            for (int i = 0; i < buffer.Length; i++)
                buffer[i] = value;
        }
    }
}
