using System;
using System.Text;
using System.Threading.Tasks;
using KFTCOneCAP.Wpf.Security;
using KFTCOneCAP.Wpf.Services.Diagnostics;
using KFTCOneCAP.Wpf.Services.Van;

namespace KFTCOneCAP.Wpf.Services.Reader
{
    /// <summary>
    /// Phase 24(docs/operations/development_plan.md P24-4) — 키다운로드 5단계 시퀀스(PRD.md §3.2)의
    /// 오케스트레이션. 원캡은 <b>암호 연산을 하지 않는다</b>(PRD.md §3.3) — 이 클래스가 하는 일은
    /// 한쪽 응답에서 필요한 바이트열을 잘라 다른 쪽 요청에 그대로 붙이는 것뿐이다.
    ///
    /// <b>테스트 가능성</b>: <see cref="ReaderService"/>/<see cref="KeyDownloadVanClient"/>(구체
    /// 클래스)를 직접 잡지 않고 <see cref="IKeyDownloadReaderEndpoint"/>/<see cref="IKeyDownloadVanClient"/>
    /// 두 인터페이스만 받는다 — Phase 15의 <see cref="IReaderEndpoint"/> 선례와 동일한 이유다(P24-5
    /// 하네스가 실장비·서버 없이 이 클래스를 돌려 검증한다). 운영 배선(P24-6)은 진짜 인스턴스를
    /// 그대로 넘긴다 — 인터페이스 도입이 운영 동작을 바꾸지 않는다.
    ///
    /// <b>계층 규칙</b>: 이 클래스는 WPF 타입(Views/ViewModels)을 알지 못한다. <c>Services</c> 내부이므로
    /// 모든 await에 <c>ConfigureAwait(false)</c>를 유지한다.
    ///
    /// <b>DB에 저장하지 않는다</b>(PRD.md §3.1) — 무결성체크(<see cref="IntegrityCheckService"/>)와
    /// 달리 <c>IntegrityCheckStore</c>를 참조조차 하지 않는다.
    ///
    /// <b>자동 재시도 금지</b>(PRD.md §3.6, IPEK 소모) — 5단계 중 어느 하나라도 실패하면 그 즉시
    /// 멈추고 뒤 단계를 호출하지 않는다.
    ///
    /// <b>메모리 클리어(2026-09-02 사용자 확정, 2026-09-03 Phase 25 P25-2에서 방식 통일)</b> — P24-2
    /// (<see cref="ReaderService"/>)/P24-3(<see cref="KeyDownloadVanClient"/>)가 각자 지우는 원본
    /// 배열과 별개로, 이 클래스가 단계 사이에 <b>직접 들고 있는</b> relay용 중간 바이트 배열(예: [73]
    /// 응답에서 뽑아 0100 요청에 넘길 키버전+모듈ID 묶음)도 다음 단계 호출이 끝나면
    /// <see cref="Security.SecureClear.Clear(byte[])"/>(3회 덮어쓰기)로 지운다. <b>한계</b>(`PRD.md`
    /// §4.4): best-effort다 — GC 세대 압축이 그 전에 배열을 옮긴 적이 있다면 옛 위치의 잔여 바이트까지는
    /// 지우지 못하고, 이 프로젝트 타겟(.NET Framework 4.8)에는 <c>CryptographicOperations.ZeroMemory</c>
    /// 같은 상위 API도 없다. 문자열(<c>string</c>)은 불변이라 이 클래스 수준에서 지울 방법이 없다 —
    /// hash/rnd/sign/encryptedData 같은 값을 리더기 API에 넘길 때는 문자열이어야 하므로
    /// (<see cref="IKeyDownloadReaderEndpoint"/> 시그니처), 그 문자열 자체의 수명은 GC에 맡긴다
    /// (P24-2/P24-3과 동일한 캐비어트). GC 압축 복사본 대응을 위한 pin(<c>GCHandle.Alloc</c>)은
    /// 인증 기준이 요구하지 않아 Phase 25 범위 밖으로 확정됐다(`PRD.md` §4.4 #1/§4.7).
    /// </summary>
    internal sealed class KeyDownloadService
    {
        /// <summary>리더기 키다운로드 3종([63]/[64]/[65]) 전용 타임아웃(5초). 기존
        /// ReaderSetupViewModel.CommandTimeout(5초)을 그대로 재사용하지 않고 분리해 둔다 — 이 클래스는
        /// 구체 타입(ReaderService)을 참조하지 않으므로 값을 여기 둔다. docs/operations/
        /// development_plan.md "위험·미확정 #1"(`[64]` 상호인증에서 리더기의 RSA2048 검증이 DLL의
        /// 일반 명령 3초 타임아웃보다 오래 걸릴 가능성)이 실제로 걸리면 이 한 곳만 조정하면 된다
        /// (I-5, CP1 Opus 리뷰 — 예전에는 이 상수가 ReaderService.KeyDownloadCommandTimeout과
        /// 이름만 다르게 중복돼 있었다. 실제로 쓰이는 이 쪽만 남기고 죽은 쪽을 지웠다).</summary>
        internal static readonly TimeSpan DefaultReaderCommandTimeout = TimeSpan.FromSeconds(5);

        private readonly IKeyDownloadReaderEndpoint _reader;
        private readonly IKeyDownloadVanClient _vanClient;
        private readonly TimeSpan _readerTimeout;

        /// <summary>R-2(Phase 24 전체 Opus 리뷰) — 로그에 어느 리더기 구간인지 남기기 위한 라벨
        /// (예: "리더기1"). <see cref="ReaderSetupViewModel"/>이 이미 초기화/상태체크/무결성체크
        /// 로그에 붙이는 것과 같은 라벨을 그대로 받아 쓴다. 라벨이 없어도(빈 문자열) 동작에는
        /// 영향이 없다 — 로그 문구 접두사만 비게 된다(하네스 등 라벨을 모르는 호출자를 위한
        /// 방어).</summary>
        private readonly string _readerLabel;

        internal KeyDownloadService(IKeyDownloadReaderEndpoint reader, IKeyDownloadVanClient vanClient, string readerLabel = "")
            : this(reader, vanClient, DefaultReaderCommandTimeout, readerLabel)
        {
        }

        internal KeyDownloadService(IKeyDownloadReaderEndpoint reader, IKeyDownloadVanClient vanClient, TimeSpan readerTimeout, string readerLabel = "")
        {
            _reader = reader;
            _vanClient = vanClient;
            _readerTimeout = readerTimeout;
            _readerLabel = readerLabel ?? string.Empty;
        }

        /// <summary>R-2 — 모든 KEYDOWN 로그 문구 앞에 <c>[리더기라벨 키다운로드]</c>를 붙인다. 라벨이
        /// 비어 있으면(하네스 등) 접두사 없이 원래 문구만 남긴다.
        ///
        /// 개선권장 #5(Phase 24 2차 Opus 리뷰) — 원래 <c>[리더기1]</c> 형식이었는데, 기존
        /// <c>ReaderSetupViewModel.LogOutcome</c>(초기화/상태체크/무결성체크)이 남기는
        /// <c>[리더기1 초기화]</c> 형식과 명령명이 빠져 grep 패턴이 갈렸다 — 이 클래스도 명령명
        /// ("키다운로드")을 포함하도록 통일한다.</summary>
        private string Label(string message) =>
            string.IsNullOrEmpty(_readerLabel) ? message : $"[{_readerLabel} 키다운로드] {message}";

        /// <summary>5단계(①~⑤)를 순서대로 실행한다. 어느 단계든 실패하면 그 즉시 멈추고 뒤 단계를
        /// 호출하지 않는다(재시도 없음).</summary>
        internal async Task<KeyDownloadOutcome> RunAsync()
        {
            // ===================== ① [63] → [73] 키 다운로드 시작 =====================
            FileLogger.Info(LogCategory.Keydown, Label("① [63] 키 다운로드 시작 요청 전송"));
            var startOutcome = await _reader.SendKeyDownloadStartCommandAsync(_readerTimeout).ConfigureAwait(false);
            if (startOutcome.Kind != ReaderCommandOutcomeKind.Success)
            {
                FileLogger.Warn(LogCategory.Keydown,
                    Label($"① [73] 응답 실패 — {startOutcome.Kind} detail={startOutcome.Detail}"),
                    NullIfEmpty(startOutcome.ResponseCode), null);
                return KeyDownloadOutcome.ReaderFailure(
                    KeyDownloadStage.Start, startOutcome.FailureCategory, startOutcome.ResponseCode, string.Empty, startOutcome.Detail);
            }
            FileLogger.Info(LogCategory.Keydown,
                Label($"① [73] 응답 성공 — 키버전={startOutcome.KeyVersion} 모듈ID={startOutcome.ModuleId}"),
                startOutcome.ResponseCode, null);

            // ===================== ② 0100 → 0110 상호인증(Key Download) =====================
            byte[] p28Bytes = Encoding.ASCII.GetBytes(startOutcome.KeyVersion + startOutcome.ModuleId);
            KeyDownloadVanCallOutcome vanAuthOutcome;
            try
            {
                string p28 = Encoding.ASCII.GetString(p28Bytes);
                FileLogger.Info(LogCategory.Keydown, Label("② 0100 상호인증 요청 전송(FNAISCRDVAN)"));
                vanAuthOutcome = await _vanClient.SendKeyDownloadRequestAsync(p28).ConfigureAwait(false);
            }
            finally
            {
                // relay 목적의 중간 값(키버전+모듈ID) — 다음 단계 호출이 끝났으므로 지운다.
                SecureClear.Clear(p28Bytes);
            }

            if (!vanAuthOutcome.IsSuccess)
            {
                FileLogger.Warn(LogCategory.Keydown,
                    Label(BuildServerFailureLogMessage("②", "0110", vanAuthOutcome)),
                    NullIfEmpty(vanAuthOutcome.ResponseCode), null);
                return KeyDownloadOutcome.ServerFailure(KeyDownloadStage.ServerAuth, vanAuthOutcome, startOutcome.ModuleId);
            }
            FileLogger.Info(LogCategory.Keydown, Label($"② 0110 응답 성공 — 응답코드={vanAuthOutcome.ResponseCode}"), vanAuthOutcome.ResponseCode, null);

            // ===================== ③ [64] → [74] 키 다운로드 상호 인증 =====================
            // 0110 P-28(AN 610) = 키버전(2) + HASH(64) + RND(32) + SIGN(512). 앞 2byte(키버전)를
            // 떼고 나머지 608byte를 그대로 [64] data로 넘긴다(PRD.md §3.3 표 ③).
            byte[] authBytes = Encoding.ASCII.GetBytes(vanAuthOutcome.Payload.Substring(2));
            KeyDownloadAuthCommandOutcome authOutcome;
            try
            {
                string hash = Encoding.ASCII.GetString(authBytes, 0, KeyDownloadHashLength);
                string rnd = Encoding.ASCII.GetString(authBytes, KeyDownloadHashLength, KeyDownloadRndLength);
                string sign = Encoding.ASCII.GetString(authBytes, KeyDownloadHashLength + KeyDownloadRndLength, KeyDownloadSignLength);

                FileLogger.Info(LogCategory.Keydown,
                    Label($"③ [64] 키 다운로드 상호 인증 요청 전송 — HASH({hash.Length}) RND({rnd.Length}) SIGN({sign.Length}) (내용 미기록, 길이만 기록)"));
                authOutcome = await _reader.SendKeyDownloadAuthCommandAsync(hash, rnd, sign, _readerTimeout).ConfigureAwait(false);
            }
            finally
            {
                // relay 목적의 중간 값(HASH+RND+SIGN) — 다음 단계 호출이 끝났으므로 지운다.
                SecureClear.Clear(authBytes);
            }

            if (authOutcome.Kind != ReaderCommandOutcomeKind.Success)
            {
                FileLogger.Warn(LogCategory.Keydown,
                    Label($"③ [74] 응답 실패 — {authOutcome.Kind} detail={authOutcome.Detail}"),
                    NullIfEmpty(authOutcome.ResponseCode), null);
                return KeyDownloadOutcome.ReaderFailure(
                    KeyDownloadStage.Auth, authOutcome.FailureCategory, authOutcome.ResponseCode, authOutcome.ModuleId, authOutcome.Detail);
            }
            FileLogger.Info(LogCategory.Keydown,
                Label($"③ [74] 응답 성공 — 키버전={authOutcome.KeyVersion} 모듈ID={authOutcome.ModuleId} 암호화데이터({authOutcome.EncryptedData.Length}, 내용 미기록)"),
                authOutcome.ResponseCode, null);

            // ===================== ④ 0120 → 0130 Key Bundling =====================
            // [74] 응답 = 키버전(2) + 이름(16) + 버전(16) + 모듈ID(10) + 암호화데이터(512). 서버
            // 0120 P-29(AN 524)에는 키버전+모듈ID+암호화데이터만 붙인다(리더기이름/버전 제외,
            // PRD.md §3.3 표 ④).
            byte[] p29Bytes = Encoding.ASCII.GetBytes(authOutcome.KeyVersion + authOutcome.ModuleId + authOutcome.EncryptedData);
            KeyDownloadVanCallOutcome vanBundlingOutcome;
            try
            {
                string p29 = Encoding.ASCII.GetString(p29Bytes);
                FileLogger.Info(LogCategory.Keydown, Label("④ 0120 Key Bundling 요청 전송(FNAISCRDVAN)"));
                vanBundlingOutcome = await _vanClient.SendKeyBundlingRequestAsync(p29).ConfigureAwait(false);
            }
            finally
            {
                // relay 목적의 중간 값(키버전+모듈ID+암호화데이터) — 다음 단계 호출이 끝났으므로 지운다.
                SecureClear.Clear(p29Bytes);
            }

            if (!vanBundlingOutcome.IsSuccess)
            {
                FileLogger.Warn(LogCategory.Keydown,
                    Label(BuildServerFailureLogMessage("④", "0130", vanBundlingOutcome)),
                    NullIfEmpty(vanBundlingOutcome.ResponseCode), null);
                return KeyDownloadOutcome.ServerFailure(KeyDownloadStage.ServerKeyBundling, vanBundlingOutcome, authOutcome.ModuleId);
            }
            FileLogger.Info(LogCategory.Keydown, Label($"④ 0130 응답 성공 — 응답코드={vanBundlingOutcome.ResponseCode}"), vanBundlingOutcome.ResponseCode, null);

            // ===================== ⑤ [65] → [75] Using Key 전송 =====================
            // 0130 P-29(AN 146) = 키버전(2) + 암호화데이터(144). 앞 2byte(키버전)를 떼고 나머지
            // 144byte(암호화데이터(128)+MAC(16))를 그대로 [65] data로 넘긴다(PRD.md §3.3 표 ⑤).
            byte[] usingKeyBytes = Encoding.ASCII.GetBytes(vanBundlingOutcome.Payload.Substring(2));
            KeyDownloadUsingKeyCommandOutcome usingKeyOutcome;
            try
            {
                string encryptedData = Encoding.ASCII.GetString(usingKeyBytes, 0, KeyDownloadUsingKeyEncryptedDataLength);
                string mac = Encoding.ASCII.GetString(usingKeyBytes, KeyDownloadUsingKeyEncryptedDataLength, KeyDownloadUsingKeyMacLength);

                FileLogger.Info(LogCategory.Keydown,
                    Label($"⑤ [65] Using Key 전송 요청 전송 — 암호화데이터({encryptedData.Length}) MAC({mac.Length}) (내용 미기록, 길이만 기록)"));
                usingKeyOutcome = await _reader.SendKeyDownloadUsingKeyCommandAsync(encryptedData, mac, _readerTimeout).ConfigureAwait(false);
            }
            finally
            {
                // relay 목적의 중간 값(암호화데이터+MAC) — 다음 단계(마지막 단계) 호출이 끝났으므로 지운다.
                SecureClear.Clear(usingKeyBytes);
            }

            if (usingKeyOutcome.Kind != ReaderCommandOutcomeKind.Success)
            {
                FileLogger.Warn(LogCategory.Keydown,
                    Label($"⑤ [75] 응답 실패 — {usingKeyOutcome.Kind} detail={usingKeyOutcome.Detail}"),
                    NullIfEmpty(usingKeyOutcome.ResponseCode), null);
                return KeyDownloadOutcome.ReaderFailure(
                    KeyDownloadStage.UsingKey, usingKeyOutcome.FailureCategory, usingKeyOutcome.ResponseCode, usingKeyOutcome.ModuleId, usingKeyOutcome.Detail);
            }
            FileLogger.Info(LogCategory.Keydown, Label($"⑤ [75] 응답 성공 — 모듈ID={usingKeyOutcome.ModuleId} 키다운로드 완료"), usingKeyOutcome.ResponseCode, null);

            return KeyDownloadOutcome.Success(usingKeyOutcome.ModuleId);
        }

        // PRD.md §3.4 — [64] data 필드 길이(HASH 64 / RND 32 / SIGN 512), [65] data 필드 길이
        // (암호화데이터 128 / MAC 16). Protocol/Reader/KeyDownloadRequestBuilder의 상수와 동일한
        // 값이지만, 이 클래스는 구체 타입을 참조하지 않는 원칙(계층 규칙과 별개로 이 값은 SPEC
        // 상수라 여기 직접 둔다)에 따라 별도로 갖는다.
        private const int KeyDownloadHashLength = 64;
        private const int KeyDownloadRndLength = 32;
        private const int KeyDownloadSignLength = 512;
        private const int KeyDownloadUsingKeyEncryptedDataLength = 128;
        private const int KeyDownloadUsingKeyMacLength = 16;

        private static string BuildServerFailureLogMessage(string stepMarker, string responseMessageType, KeyDownloadVanCallOutcome outcome)
        {
            string deviceReplacementNote = outcome.ResponseCode == "395" ? " (단말기 교체 요망)" : string.Empty;
            return $"{stepMarker} {responseMessageType} 응답 실패 — {outcome.Kind} 응답코드={NullIfEmpty(outcome.ResponseCode) ?? "-"} detail={outcome.Detail}{deviceReplacementNote}";
        }

        private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;
    }
}
