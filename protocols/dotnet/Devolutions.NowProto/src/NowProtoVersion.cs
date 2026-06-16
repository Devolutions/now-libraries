namespace Devolutions.NowProto
{
    public readonly record struct NowProtoVersion(ushort Major, ushort Minor) : IComparable<NowProtoVersion>
    {
        public static NowProtoVersion Current => new(1, 6);

        // -- IComparable<NowProtoVersion> --
        public int CompareTo(NowProtoVersion other)
        {
            return Major != other.Major
                ? Major.CompareTo(other.Major)
                : Minor.CompareTo(other.Minor);
        }

        // -- Comparison operators --
        public static bool operator <(NowProtoVersion left, NowProtoVersion right) => left.CompareTo(right) < 0;
        public static bool operator <=(NowProtoVersion left, NowProtoVersion right) => left.CompareTo(right) <= 0;
        public static bool operator >(NowProtoVersion left, NowProtoVersion right) => left.CompareTo(right) > 0;
        public static bool operator >=(NowProtoVersion left, NowProtoVersion right) => left.CompareTo(right) >= 0;
    }
}