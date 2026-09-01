using System;
using System.Collections.Generic;
using System.Linq;

namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 22(docs/operations/development_plan.md P22-4, PRD.md §1.3-d) 최근 로그 레코드를 메모리에
/// 보관하는 고정 크기 링버퍼.
///
/// <see cref="RingBufferSink"/>가 <see cref="ILogSink"/> 파이프라인에 끼워 넣는 얇은 기록 어댑터
/// 역할을 하고, 조회는 이 클래스의 정적 메서드로 직접 한다 — 장래 장애 보고 기능이 로그 파이프라인
/// 구조를 몰라도 "최근 로그"/"거래ID로 필터링한 로그"를 곧바로 꺼낼 수 있어야 하기 때문이다. 정적
/// 메서드로 노출하는 것은 이 구현의 판단이며, 근거는 PRD.md §1.3-d(장애 발생 순간 직전 로그를 즉시
/// 첨부할 수 있어야 한다는 요구)와 §1.6.1(장애 보고 봉투에 로그 조각을 거래ID로 슬라이스해 싣는다는
/// 취지)다.
///
/// <b>렌더링된 문자열이 아니라 <see cref="LogRecord"/> 자체를 담는다</b>(PRD.md §1.3-d) — 장래 원격
/// 싱크가 JSON으로 보낼 때 파일 싱크가 만든 텍스트를 정규식으로 되파싱하지 않게 하기 위해서다.
/// <see cref="RingBufferSink"/>는 <see cref="FileLogger"/> 파이프라인의 마지막 단계(마스킹 이후)에서
/// 호출되므로, 여기 담기는 내용은 항상 <see cref="LogMessageMasker.Mask"/>를 거친 상태다.
///
/// 스레드 안전: 기록(<see cref="Add"/>)과 조회(<see cref="Snapshot"/> /
/// <see cref="SnapshotByTransactionId"/>) 모두 하나의 lock으로 짧게 보호한다 — 조회는 배열을
/// 복사해 즉시 반환할 뿐이라(파일 I/O 등 느린 작업이 없다) 동시 기록을 실질적으로 막지 않는다.
/// </summary>
public static class LogRingBuffer
{
    /// <summary>보관 건수(development_plan.md P22-4, PRD.md §1.3-d "기본 500건"). 상수로 한 곳에 둔다.</summary>
    public const int Capacity = 500;

    private static readonly object SyncRoot = new();
    private static readonly LogRecord?[] Buffer = new LogRecord?[Capacity];

    /// <summary>다음에 쓸 슬롯 인덱스(원형으로 순환).</summary>
    private static int _nextIndex;

    /// <summary>현재 보관 중인 건수(<see cref="Capacity"/>에서 포화).</summary>
    private static int _count;

    /// <summary>레코드 한 건을 추가한다. 가득 찬 상태에서는 가장 오래된 1건을 밀어낸다.</summary>
    public static void Add(LogRecord record)
    {
        if (record is null)
        {
            throw new ArgumentNullException(nameof(record));
        }

        lock (SyncRoot)
        {
            Buffer[_nextIndex] = record;
            _nextIndex = (_nextIndex + 1) % Capacity;
            if (_count < Capacity)
            {
                _count++;
            }
        }
    }

    /// <summary>현재 보관 중인 레코드를 오래된 것부터 시간순으로 스냅샷해 반환한다.</summary>
    public static IReadOnlyList<LogRecord> Snapshot()
    {
        lock (SyncRoot)
        {
            var result = new LogRecord[_count];
            // 아직 포화되지 않았으면(_count < Capacity) 항상 인덱스 0부터가 가장 오래된 값이다.
            // 포화된 뒤에는 _nextIndex가 가리키는 슬롯이 다음에 덮어써질 자리 = 가장 오래된 값이다.
            int oldest = _count < Capacity ? 0 : _nextIndex;
            for (int i = 0; i < _count; i++)
            {
                result[i] = Buffer[(oldest + i) % Capacity]!;
            }

            return result;
        }
    }

    /// <summary>
    /// 거래ID가 일치하는 레코드만 시간순으로 꺼낸다(장애 보고가 "그 거래의 로그"를 첨부하기 위함).
    /// <paramref name="transactionId"/>가 <c>null</c>이거나 빈 문자열이면 빈 컬렉션을 반환한다 — 전건
    /// 반환은 "필터링 안 함"과 같은 뜻이 되어 호출부가 실수로 전체 로그를 첨부하게 만들 수 있다.
    /// </summary>
    public static IReadOnlyList<LogRecord> SnapshotByTransactionId(string transactionId)
    {
        if (string.IsNullOrEmpty(transactionId))
        {
            return Array.Empty<LogRecord>();
        }

        return Snapshot().Where(r => r.TransactionId == transactionId).ToList();
    }
}
