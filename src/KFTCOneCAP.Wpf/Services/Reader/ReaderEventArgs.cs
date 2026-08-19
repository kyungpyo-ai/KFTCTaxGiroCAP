using System;

namespace KFTCOneCAP.Wpf.Services.Reader
{
    /// <summary>
    /// READER_CALLBACK 1회 호출을 표현하는 순수 데이터(POCO). Data는 이미 콜백 안에서
    /// Marshal.Copy로 복사된 byte[]다 — 이 이벤트를 구독하는 쪽은 DLL의 원본 버퍼 수명(콜백 반환
    /// 즉시 0으로 지워짐)을 신경 쓸 필요가 없다(Services/Reader/ReaderService 콜백 처리부 참고).
    ///
    /// 이 타입은 WPF 타입(Dispatcher 등)을 알지 못한다 — ReaderService가 이 이벤트를 raise하는
    /// 스레드는 네이티브 콜백 스레드 그대로이며, UI 스레드로의 마샬링은 구독자(ViewModel) 책임이다
    /// (docs/payment_relay/ROADMAP.md "계층 구조" 의존 방향 규칙).
    /// </summary>
    internal sealed class ReaderEventArgs : EventArgs
    {
        internal int ReaderId { get; }
        internal int EventType { get; }
        internal byte CommandCode { get; }
        internal byte[] Data { get; }

        internal ReaderEventArgs(int readerId, int eventType, byte commandCode, byte[] data)
        {
            ReaderId = readerId;
            EventType = eventType;
            CommandCode = commandCode;
            Data = data;
        }
    }
}
