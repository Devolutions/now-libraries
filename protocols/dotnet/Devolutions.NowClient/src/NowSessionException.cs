namespace Devolutions.NowClient
{
    internal class NowSessionException(uint sessionId, NowSessionException.NowSessionExceptionKind kind)
        : NowClientException(GetDisplayMessage(kind))
    {
        public enum NowSessionExceptionKind
        {
            ExitedSessionInteraction = 1,
            Terminated = 2,
            StdinClosed = 3,
        }

        private static string GetDisplayMessage(NowSessionExceptionKind kind)
        {
            return kind switch
            {
                NowSessionExceptionKind.ExitedSessionInteraction => "Can't interact with already exited session.",
                NowSessionExceptionKind.Terminated => "Session has been cancelled.",
                NowSessionExceptionKind.StdinClosed => "Can't send data to already closed stdin stream.",
                _ => $"Unknown session exception."
            };
        }

        public NowSessionExceptionKind Kind => kind;
        public uint SessionId => sessionId;
    }
}