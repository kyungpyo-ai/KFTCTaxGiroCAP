# KFTCReaderDLL 배포 산출물 (Win32/x86, Release)

이 폴더는 POS 프로그램(MFC/C/C++/C#) 연동을 위한 `ReaderSerial` DLL 배포 산출물이다.
Win32(x86) Release 빌드 기준이며, x64는 이번 패키징에 포함하지 않는다(ROADMAP/PRD 정책상 Win32가 1차 기준).

**버전: 2차 개발(핀패드 SPEC 연동) 반영판, 2026-08-11.** 1차 개발(리더기 전용) 산출물 대비 다음이 추가되었다.

- `Reader_OpenPort`가 4-인자에서 5-인자로 변경됨: `Reader_OpenPort(int portNumber, int baudRate, READER_CALLBACK readerCallback, PINPAD_CALLBACK pinpadCallback, void* userContext, int* outReaderId)`. `readerCallback`/`pinpadCallback`은 각각 단독으로 `nullptr`을 넘길 수 있으나(핀패드 미사용 포트/리더기 미사용 포트), 둘 다 `nullptr`이면 `READER_ERR_INVALID_ARGUMENT`.
- 공개 함수가 4개에서 5개로 늘어남: 기존 `Reader_OpenPort`/`Reader_ClosePort`/`Reader_IsPortOpen`/`Reader_SendCommand`에 `Pinpad_SendCommand`(핀패드 조합 명령 5종 전송) 추가.
- `PINPAD_CALLBACK` 델리게이트/함수 포인터 타입 신설(핀패드 조합 명령의 성공/실패/타임아웃을 1회만 통지).
- 리더기 슬롯 최대 개수가 2 → 8로 확대(리더기/핀패드 장비 종류 구분 없이 공유).
- 핀패드와 리더기가 한 포트를 공유하는 멀티패드 구성을 POS 선언 없이 자동 판별.

핀패드를 쓰지 않는 기존 연동은 `pinpadCallback`에 `nullptr`을 넘기면 이전과 동일하게 동작한다 — 이 갱신으로 인한 리더기 전용 연동 쪽 동작 변경은 없다.

## 구성

- `ReaderSerial.dll` — 배포용 바이너리 (Win32 Release, MSBuild `ReaderSerial.sln` `/p:Configuration=Release /p:Platform=Win32` 산출물)
- `ReaderSerial.lib` — 정적 링크용 임포트 라이브러리
- `ReaderSerial.h` — 공개 API 헤더 (5개 함수: `Reader_OpenPort`/`Reader_ClosePort`/`Reader_IsPortOpen`/`Reader_SendCommand`/`Pinpad_SendCommand`, `READER_CALLBACK`/`PINPAD_CALLBACK` 시그니처, `ReaderEventType`/`PinpadEventType`/`PinpadCommandCode` 등)
- `docs/ReaderSerial_연동가이드.docx` — **외부 업체 전달용 메인 문서.** API 5종/CALLBACK 2종/오류코드/핀패드 연동 절차/권장 연동 패턴만 간결하게 정리(샘플 소스와 함께 보는 것을 전제로 상세 근거는 생략)
- `docs/DLL연동가이드.md` — 위 docx의 원본(마크다운)
- `docs/API명세서.md`, `docs/오류코드정의서.md` — 내부 상세 레퍼런스(파일:줄 단위 근거 포함, 필요 시에만 참고 — 외부 배포 시 생략 가능)

## `.def` 파일에 대하여

이 프로젝트는 `ReaderSerial.h`의 `READER_API` 매크로(`__declspec(dllexport)`/`__declspec(dllimport)`)로 5개 공개 함수를 export하므로 `.def` 파일이 없어도 빌드/링크에 아무 문제가 없다(`ReaderSerial.lib`가 정상적으로 생성되어 이 폴더에 포함되어 있음). 별도로 `.def` 파일을 요구하는 특별한 사유(예: 특정 이름으로 ordinal export가 필요한 레거시 연동)가 생기기 전까지는 `.def` 파일을 추가하지 않는다 — CLAUDE.md "필요 이상으로 만들지 않는다" 원칙에 따른 결정이다.

## 연동 예제 소스 위치

이 폴더에는 예제 소스 전체를 복사하지 않았다. 실제 소스는 저장소의 다음 경로에 있다.

- MFC 연동 예제: `src/ReaderSerialTestUI/` (`ReaderSerialTestUI.vcxproj`, Platform Toolset v143) — 리더기 패널에 더해 핀패드 패널(명령 5종)을 포함하며, 실장비(별도 핀패드/멀티패드 양쪽)로 실제 키 입력까지 포함해 검증됨.
- C# P/Invoke 연동 예제: `src/ReaderSerialCSharpSample/` (`ReaderSerialCSharpSample.csproj`, `net48`/`PlatformTarget=x86`) — `PINPAD_CALLBACK` P/Invoke 델리게이트와 핀패드 명령 5종 UI가 구현·빌드 검증되어 있으나, PIN 입력 계열(PASSWORD/NUMBER/DES/SEED)의 실제 키 입력을 통한 실장비 동작 검증은 아직 이 예제에서는 완료되지 않았다(MFC 쪽은 완료됨) — 상세는 `DOC/개발문서/실행계획서_핀패드.md` P17-2 참조.

두 예제 모두 `DOC/개발문서/실행계획서.md`의 Phase 10(P10-1/P10-1b/P10-2)에서 실장비 대상으로 검증된 최소 시나리오(포트 열기 → 명령 전송 → CALLBACK 수신 → 닫기, 포트 계열 오류 시 자동 재연결 래퍼 `SendCommandSafe` 포함)를 담고 있고, `DOC/개발문서/실행계획서_핀패드.md`의 Phase 17(P17-1/P17-2)에서 핀패드 명령 5종 지원이 추가되었다.

## 참고

이 DLL은 시리얼 통신 제어(포트 열기/닫기, 전문 프레이밍, 명령 상태 관리)만 담당하며 승인/취소 등 업무 로직은 범위 밖이다. 자세한 요구사항은 저장소의 `DOC/개발문서/PRD.md`(1차 개발, 리더기)와 `DOC/개발문서/PRD_핀패드.md`(2차 개발, 핀패드)를 참고한다.
