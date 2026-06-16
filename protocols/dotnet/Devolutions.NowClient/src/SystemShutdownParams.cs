using Devolutions.NowProto.Messages;

namespace Devolutions.NowClient
{
    /// <summary>
    /// System shutdown parameters.
    /// </summary>
    public class SystemShutdownParams
    {
        /// <summary>
        /// Enable forced shutdown mode.
        /// </summary>
        public SystemShutdownParams Force()
        {
            _builder = _builder.Force(true);
            return this;
        }

        /// <summary>
        /// Reboot the system instead of shutting it down.
        /// </summary>
        public SystemShutdownParams Reboot()
        {
            _builder = _builder.Reboot(true);
            return this;
        }

        /// <summary>
        /// Set timeout before system shutdown.
        /// </summary>
        public SystemShutdownParams Timeout(TimeSpan timeout)
        {
            _builder = _builder.Timeout(timeout);
            return this;
        }

        /// <summary>
        /// Set optional shutdown message.
        /// </summary>
        public SystemShutdownParams Message(string message)
        {
            _builder = _builder.Message(message);
            return this;
        }

        internal NowMsgSystemShutdown NowMessage => _builder.Build();

        private NowMsgSystemShutdown.Builder _builder = new();
    }
}