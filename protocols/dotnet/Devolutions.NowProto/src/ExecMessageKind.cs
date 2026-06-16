namespace Devolutions.NowProto
{
    public enum ExecMessageKind : byte
    {
        Abort = 0x01,
        CancelReq = 0x02,
        CancelRsp = 0x03,
        Result = 0x04,
        Data = 0x05,
        Started = 0x06,
        Run = 0x10,
        Process = 0x11,
        Shell = 0x12,
        Batch = 0x13,
        Winps = 0x14,
        Pwsh = 0x15,
    }
}