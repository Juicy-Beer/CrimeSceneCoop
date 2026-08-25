namespace CrimeSceneCoop;

internal interface ITransport : IDisposable
{
    bool IsConnected { get; }
    string Status { get; }
    event Action<byte[]> Message;
    void Send(byte[] data, bool reliable);
    void Pump();
}
