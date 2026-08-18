# ReaderSerial DLL 연동 참조 자료

이 폴더(및 `vendor/ReaderSerial/`)는 **`C:\Project\KFTCReaderDLL`** 프로젝트(별도 저장소, 이 저장소 밖)가 만든
`ReaderSerial.dll`(암호화 리더기·핀패드 시리얼 통신 제어 Win32 DLL, 2026-08-13 기준 최신판)의 연동용 산출물을
그대로 복사해 온 것이다. **이 폴더의 파일들은 참조용 스냅샷이며, 원본이 아니다** — 최신 정보나 소스 레벨 근거가
필요하면 원본 저장소(`C:\Project\KFTCReaderDLL`)를 직접 확인한다. DLL 자체의 동작을 바꾸는 작업(버그 수정, API
추가 등)은 이 저장소가 아니라 그쪽에서 한다.

## 이 폴더 구성

| 경로 | 내용 |
|---|---|
| `DLL연동가이드.md` | **가장 먼저 읽을 문서.** API 5종(`Reader_OpenPort`/`Reader_ClosePort`/`Reader_IsPortOpen`/`Reader_SendCommand`/`Pinpad_SendCommand`), CALLBACK 2종, 오류 코드, 핀패드 연동 절차, 권장 재연결 패턴을 간결하게 정리한 외부 연동 개발자용 가이드 |
| `API명세서.md` | 내부 상세 레퍼런스(파일:줄 단위 근거 포함) — `DLL연동가이드.md`로 부족할 때만 참고 |
| `오류코드정의서.md` | `ReaderResult`(DLL 오류 코드)·`PinpadEventType` 등 오류/이벤트 코드 상세 정의 |
| `PACKAGE_README.md` | 원본 `package/` 폴더의 README — 버전 이력, 배포 산출물 구성 설명 |
| `spec/암호화리더기설계서_20250122.pdf` | 리더기 제조사 SPEC 원문(전문/명령 코드, Data 필드, LRC 범위, 응답 코드 00~23 등의 최종 근거) |
| `spec/PINPAD-20251021.pdf` | 핀패드 제조사 SPEC 원문(Fc/subCode, 프레임 구조, 체크섬 범위 등의 최종 근거) |
| `spec/샘플 데이터.txt`, `spec/핀패드 정리.txt` | 원본 저장소 개발 중 참고용으로 쓰인 보조 메모/샘플 |

`vendor/ReaderSerial/`에는 실제 바이너리(`ReaderSerial.dll`/`.h`/`.lib`, Win32/x86 Release)와 C# P/Invoke 연동
예제(`CSharpSample/`, `net48`/`PlatformTarget=x86`)가 있다 — 상세는 그 폴더의 안내를 참고.

## 핵심 요약 (자주 참조할 값)

- **공개 API는 C ABI + `__stdcall` 5개뿐.** `Reader_Initialize`/`Shutdown`/`GetLastError` 같은 건 없다 — 최초
  `Reader_OpenPort()` 호출이 초기화를 겸한다.
- **DLL은 Win32(x86) 전용, x64 미제공.** 이 저장소(`KFTCOneCAP.Wpf.csproj`)가 이 DLL을 P/Invoke로 물려면
  프로젝트의 `PlatformTarget`을 **`x86`으로 명시**해야 한다 — 현재 `net48`만 지정돼 있고 `PlatformTarget`이
  없어 기본값(AnyCPU)이므로, 실제 연동 작업 시 반드시 이 설정을 먼저 바꿔야 한다(빠뜨리면 `BadImageFormatException`).
- **CALLBACK 데이터 수명 규칙**: `READER_CALLBACK`/`PINPAD_CALLBACK`의 `data` 포인터는 콜백 실행 중에만
  유효하다. 콜백 반환 직후 DLL이 내부 버퍼를 0으로 지우므로, 이후 필요한 데이터는 **콜백 내부에서 즉시 복사**
  해야 한다. 또한 콜백은 리더기별 수신 스레드에서 **동기 호출**되므로, 콜백 안에서 WPF UI(Dispatcher 필요)를
  직접 건드리면 안 되고 복사한 데이터를 `Dispatcher.Invoke`/`BeginInvoke` 등으로 넘겨야 한다(MFC 예제의
  `PostMessage` 패턴과 동일한 이유).
- **자동 재연결 없음.** 케이블 분리 등 포트 오류 시 POS가 직접 `Reader_ClosePort()` → `Reader_OpenPort()`를
  다시 호출해야 한다. 권장 패턴(`SendCommandSafe`)은 `DLL연동가이드.md` §5 참고.
- **리더기·핀패드 슬롯 최대 8개**, 서로 완전히 독립. 별도 핀패드(2개 COM 포트) 구성과 멀티패드(1개 COM 포트
  공유) 구성이 있으며 동작 차이(크로스 BUSY, 핀패드 진행 중 리더기 응답 유실 등)가 있다 — `DLL연동가이드.md`
  §6을 반드시 읽는다.
- **두 가지 응답 코드 체계를 혼동하지 말 것**: `ReaderResult`(함수 반환값/DLL 오류, 음수 `int`)와 SPEC 업무
  응답 코드(`data`의 첫 2byte, ASCII `"00"`~`"23"`)는 완전히 별개다(`DLL연동가이드.md` §3).

## 다음 단계

이 자료 수집은 선행 준비 작업이며, 아직 이 저장소에 실제 P/Invoke 연동 코드는 없다. 실제 개발은 새 PRD 문서
(`docs/reader_dll_integration/PRD.md` 등, 아직 미작성 — CLAUDE.md 안내대로 `docs/home_reader_setup/ROADMAP.md`를
Phase 7부터 이어서 사용)로 요구사항을 정의한 뒤 시작한다. SPEC 확인/DLL 연동 관련 서브에이전트는
`.claude/agents/reader-pinpad-spec-expert.md`, `.claude/agents/reader-dll-integration-developer.md` 참고.
