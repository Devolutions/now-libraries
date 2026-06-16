using Devolutions.NowProto.Messages;

using static Devolutions.NowProto.Messages.NowMsgSessionMessageBoxReq;

namespace Devolutions.NowClient
{
    /// <summary>
    /// Message box parameters.
    /// </summary>
    public class MessageBoxParams(string message)
    {
        /// <summary>
        /// Sets the style of the message box.
        /// </summary>
        public MessageBoxParams Style(MessageBoxStyle style)
        {
            _style = style;
            return this;
        }

        /// <summary>
        /// Sets the timeout of the message box.
        /// </summary>
        public MessageBoxParams Timeout(TimeSpan timeout)
        {
            _timeout = timeout;
            return this;
        }

        /// <summary>
        /// Sets the title of the message box.
        /// </summary>
        public MessageBoxParams Title(string title)
        {
            _title = title;
            return this;
        }

        internal NowMsgSessionMessageBoxReq ToNowMessage(uint requestId, bool responseRequired)
        {
            var builder = new Builder(requestId, message);
            if (_style != null)
            {
                builder.Style(_style.Value);
            }

            if (_timeout != null)
            {
                builder.Timeout(_timeout.Value);
            }

            if (_title != null)
            {
                builder.Title(_title);
            }

            if (responseRequired)
            {
                builder.WithResponse();
            }

            return builder.Build();
        }

        private MessageBoxStyle? _style = null;
        private TimeSpan? _timeout = null;
        private string? _title = null;
    }
}