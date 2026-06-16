using Devolutions.NowProto.Messages;

namespace Devolutions.NowClient
{
    internal interface IMessageBoxRspHandler
    {
        void HandleMessageBoxRsp(NowMsgSessionMessageBoxRsp response);
    }
}