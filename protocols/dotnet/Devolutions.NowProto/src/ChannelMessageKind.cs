namespace Devolutions.NowProto
{
    public enum ChannelMessageKind : byte
    {
        Capset = 0x01,
        Heartbeat = 0x02,
        Close = 0x03,
    }
}