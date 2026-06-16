using Devolutions.NowProto.Messages;

namespace Devolutions.NowClient
{
    /// <summary>
    /// Handler interface for window recording events.
    /// </summary>
    public interface IWindowRecEventHandler
    {
        /// <summary>
        /// Called when a window recording event is received from the server.
        /// </summary>
        /// <param name="eventMsg">The window recording event message</param>
        void HandleWindowRecEvent(NowMsgSessionWindowRecEvent eventMsg);
    }
}