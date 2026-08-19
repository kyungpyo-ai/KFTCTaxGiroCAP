using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KFTCOneCAP.Wpf.Protocol.Reader;
using KFTCOneCAP.Wpf.Services.Diagnostics;

namespace KFTCOneCAP.Wpf.Services.Reader
{
    /// <summary>P10-5 이중화 페일오버 결과. Winner가 null이면 참여 리더기가 하나도 없었거나
    /// (즉시 오류, PRD §2.2.3 "양쪽 모두 미사용") 참여한 리더기 전원이 송신 자체에 실패한 것이다
    /// (참조 구현 BroadcastFailover의 "두 리더기 모두 전송에 실패해 이번 라운드는 응답 대기 없이
    /// 종료됩니다"와 동일한 상황).</summary>
    internal sealed class CardReadBroadcastResult
    {
        internal bool HasWinner => Winner != null;
        internal ReaderService? Winner { get; }
        internal CardReadCommandOutcome? WinnerOutcome { get; }

        private CardReadBroadcastResult(ReaderService? winner, CardReadCommandOutcome? outcome)
        {
            Winner = winner;
            WinnerOutcome = outcome;
        }

        internal static CardReadBroadcastResult NoParticipants() => new CardReadBroadcastResult(null, null);

        internal static CardReadBroadcastResult Of(ReaderService winner, CardReadCommandOutcome outcome) =>
            new CardReadBroadcastResult(winner, outcome);
    }

    /// <summary>
    /// PRD §2.2.3/§4.3 리더기 이중화 — "참여 리더기 전체에 동일한 명령을 동시 전송 → 먼저 최종
    /// 응답한 쪽 채택 → 나머지는 0x60으로 무효화". 참조 구현
    /// vendor/ReaderSerial/MfcSample/ReaderSerialTestUIDlg.cpp의 BroadcastFailover()를 그대로
    /// 따른다(development_plan.md P10-5 — 새로 설계하지 않는다).
    ///
    /// 이 클래스는 "리더기가 몇 대인가"를 특별 취급하지 않는다 — participants가 1개면
    /// Task.WhenAny가 그 1개짜리 목록에서 즉시 첫(유일한) 완료를 반환하므로 별도 분기 없이 N=1이
    /// 자연스러운 축약이 된다(P10-4/P10-5 공통 요구사항).
    /// </summary>
    internal static class CardReadBroadcaster
    {
        /// <summary>
        /// participants는 호출자(Phase 15 결제 Flow)가 이미 "포트가 설정되어 있고 참여 가능한"
        /// 리더기만 걸러서 넘긴다고 가정한다("미사용" 리더기를 걸러내는 판단은 이 클래스의 책임이
        /// 아니다 — PRD §2.2.3 "양쪽 모두 미사용" 판정은 리스트를 비워서 넘기는 방식으로 표현한다).
        /// 동일한 request를 참여 리더기 전체에 동시에 보낸다(PRD §4.3 "두 리더기에 동일한 명령·
        /// 동일한 필드").
        /// </summary>
        internal static async Task<CardReadBroadcastResult> SendAsync(
            IReadOnlyList<ReaderService> participants, TransactionInfoRequest request, TimeSpan timeout)
        {
            if (participants.Count == 0)
            {
                FileLogger.Warn("[카드 리딩 페일오버 전송] 참여 가능한 리더기가 없어 전송하지 않습니다");
                return CardReadBroadcastResult.NoParticipants();
            }

            // 전원에게 동시에 SendCardReadCommandAsync를 시작한다 — 각 ReaderService가 자신의
            // SendCommandSafe(재연결 래퍼)/단일 유효 응답 게이트를 독립적으로 수행하므로, 여기서는
            // "누가 먼저 끝나는가"만 Task.WhenAny로 판정하면 된다.
            var tasks = participants.Select(p => p.SendCardReadCommandAsync(request, timeout)).ToArray();

            Task<CardReadCommandOutcome> firstDone = await Task.WhenAny(tasks).ConfigureAwait(false);
            int winnerIndex = Array.IndexOf(tasks, firstDone);
            ReaderService winner = participants[winnerIndex];
            CardReadCommandOutcome winnerOutcome = await firstDone.ConfigureAwait(false);

            FileLogger.Info($"[카드 리딩 페일오버 전송] 리더기[{winnerIndex}] 채택 (이번 라운드 최초 응답), Kind={winnerOutcome.Kind}");

            for (int i = 0; i < participants.Count; i++)
            {
                if (i == winnerIndex)
                    continue;

                // 아직 응답 대기 중인 나머지 참여 리더기를 초기화(0x60)로 무효화한다(PRD §2.2.3).
                // 결과를 기다리지 않는다(참조 구현과 동일 — Fire-and-forget이지만 로그는 남긴다).
                int invalidateResult = participants[i].SendInvalidationInit();
                FileLogger.Info($"[카드 리딩 페일오버 전송] 리더기[{i}]: 다른 리더기 응답 채택으로 초기화 요청(0x60) 전송해 무효화 -> result={invalidateResult}");
            }

            return CardReadBroadcastResult.Of(winner, winnerOutcome);
        }
    }
}
