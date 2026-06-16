using Devolutions.NowProto.Types;

namespace Devolutions.NowProto.Types
{
    /// <summary>
    /// A GUID (Globally Unique Identifier) represented as a lowercase string
    /// in the format "00112233-4455-6677-8899-aabbccddeeff"
    ///
    /// NOW-PROTO: NOW_GUID (encoded as NOW_VARSTR)
    /// </summary>
    internal static class NowGuid
    {
        public static uint LengthOf(Guid guid)
        {
            // Guid.ToString("D") format: "00112233-4455-6677-8899-aabbccddeeff"
            // Always 36 characters for a GUID
            return NowVarStr.LengthOf(guid.ToString("D").ToLowerInvariant());
        }

        public static Guid ReadGuid(this NowReadCursor cursor)
        {
            var guidStr = cursor.ReadVarStr();
            return Guid.Parse(guidStr);
        }

        public static void WriteGuid(this NowWriteCursor cursor, Guid guid)
        {
            // Convert to lowercase hyphenated format
            var guidStr = guid.ToString("D").ToLowerInvariant();
            cursor.WriteVarStr(guidStr);
        }
    }
}