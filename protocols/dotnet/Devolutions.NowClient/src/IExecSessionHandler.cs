using Devolutions.NowProto.Messages;

namespace Devolutions.NowClient
{
    internal interface IExecSessionHandler
    {
        void HandleOutput(NowMsgExecData msg);
        void HandleStarted(NowMsgExecStarted msg);
        void HandleCancelRsp(NowMsgExecCancelRsp msg);
        void HandleResult(NowMsgExecResult msg);
    }
}