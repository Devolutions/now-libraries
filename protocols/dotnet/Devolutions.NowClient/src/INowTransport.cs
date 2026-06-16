namespace Devolutions.NowClient
{
    /// <summary>
    /// Interface for the NOW-proto transport layer (e.g. RDP DVC channel).
    /// </summary>
    public interface INowTransport : IDisposable
    {
        Task Write(byte[] data);
        Task<byte[]> Read(CancellationToken cancellationToken = default);
    }
}