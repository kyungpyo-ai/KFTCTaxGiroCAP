# ReaderSerial.dll (vendor 스냅샷)

`C:\Project\KFTCReaderDLL`(별도 저장소)의 `package/` 배포 산출물을 그대로 복사한 것이다(Win32/x86 Release,
2026-08-13 기준). 이 저장소가 소스를 소유하지 않는다 — 갱신이 필요하면 원본 저장소에서 새 `package/`를 받아
이 폴더 전체를 덮어쓴다.

- `ReaderSerial.dll` / `.lib` / `.h` — 배포 바이너리 및 공개 API 헤더(5개 함수: `Reader_OpenPort`/
  `Reader_ClosePort`/`Reader_IsPortOpen`/`Reader_SendCommand`/`Pinpad_SendCommand`).
- `CSharpSample/` — 원본 저장소의 `src/ReaderSerialCSharpSample/` 예제 소스(참조용, 이 프로젝트 솔루션에
  포함되지 않음). P/Invoke 선언(`ReaderSerialNative.cs`)과 사용 패턴(`MainForm.cs`)의 검증된 참고 구현이다.

문서는 `docs/reader_dll/`(연동 가이드, API 명세, 오류 코드, SPEC PDF) 참고.
