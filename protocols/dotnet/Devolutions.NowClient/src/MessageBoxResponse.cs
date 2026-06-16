using Devolutions.NowProto.Messages;

using MessageBoxResponseKind = Devolutions.NowProto.Messages.NowMsgSessionMessageBoxRsp.MessageBoxResponse;

namespace Devolutions.NowClient
{
    /// <summary>
    /// Message box response handler.
    /// </summary>
    public class MessageBoxResponse : IMessageBoxRspHandler
    {
        void IMessageBoxRspHandler.HandleMessageBoxRsp(NowMsgSessionMessageBoxRsp response)
        {
            if (_response != null)
            {
                throw new NowClientException("Invalid use of IMessageBoxRspHandler");
            }

            _response = response;
            _responseReceivedEvent.Release();
        }

        /// <summary>
        /// Returns the response to the message box.
        /// </summary>
        public async Task<MessageBoxResponseKind> GetResponse()
        {
            // Already received response prior to this call.
            if (_response != null)
            {
                return _response.GetResponseOrThrow();
            }

            await _responseReceivedEvent.WaitAsync();

            return _response?.GetResponseOrThrow()
                   ?? throw new NowClientException("No message box response has been received.");
        }

        internal MessageBoxResponse(uint requestId)
        {
            RequestId = requestId;
        }

        public uint RequestId { get; }

        private NowMsgSessionMessageBoxRsp? _response;

        // SemaphoreSlim is explicitly supports async operation via WaitAsync and don't
        // require semaphore.Release() to be called in the same thread as semaphore.Wait()
        private readonly SemaphoreSlim _responseReceivedEvent = new(0, 1);
    }
}