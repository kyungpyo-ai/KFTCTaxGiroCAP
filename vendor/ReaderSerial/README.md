# ReaderSerial.dll (vendor 스냅샷)

`C:\Project\KFTCReaderDLL`(별도 저장소)의 `package/` 배포 산출물을 그대로 복사한 것이다(Win32/x86 Release,
2026-08-13 기준). 이 저장소가 소스를 소유하지 않는다 — 갱신이 필요하면 원본 저장소에서 새 `package/`를 받아
이 폴더 전체를 덮어쓴다.

- `ReaderSerial.dll` / `.lib` / `.h` — 배포 바이너리 및 공개 API 헤더(5개 함수: `Reader_OpenPort`/
  `Reader_ClosePort`/`Reader_IsPortOpen`/`Reader_SendCommand`/`Pinpad_SendCommand`).
- `CSharpSample/` — 원본 저장소의 `src/ReaderSerialCSharpSample/` 예제 소스(참조용, 이 프로젝트 솔루션에
  포함되지 않음). P/Invoke 선언(`ReaderSerialNative.cs`)과 사용 패턴(`MainForm.cs`)의 검증된 참고 구현이다.
  **포트 오류 재시도 래퍼 `SendCommandSafe()`**가 여기 있다(`MainForm.cs`) — 이 프로젝트에서 필수 구현
  대상(`docs/payment_relay/PRD.md` §2.2.4).
- `MfcSample/` — 원본 저장소 `src/ReaderSerialTestUI/`의 다이얼로그 소스 2개(`ReaderSerialTestUIDlg.cpp/.h`).
  **리더기 2대 이중화(페일오버) 참조 구현 `BroadcastFailover()`**가 여기에만 있다 — 양쪽에 동시 전송 →
  먼저 최종 응답한 쪽 채택 → 나머지에 `0x60` 무효화. 원본 프로젝트에서 동일 명령/상이 명령 양쪽 모두
  실장비 검증을 마친 코드이므로, 이 프로젝트의 이중화(`PRD.md` §2.2.3)는 새로 설계하지 말고 이것을 따른다.
  (C# 샘플은 "리더기 1대 연동 수준"으로 만들어져 이중화가 빠져 있다 — 그래서 MFC 쪽을 함께 가져왔다.)

문서는 `docs/reader_dll/`(연동 가이드, API 명세, 오류 코드, SPEC PDF) 참고.
