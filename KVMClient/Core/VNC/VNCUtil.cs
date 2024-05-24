namespace KVMClient.Core.VNC
{
    internal static class VncUtility
    {
        /// <summary>
        /// Allocates a byte array of a given size.
        /// </summary>
        /// <param name="bytes">
        /// The minimum required length of the byte array.
        /// </param>
        /// <param name="scratch">
        /// A current byte array which can be re-used, if has enough size.
        /// </param>
        /// <returns>
        /// A byte array with at least <paramref name="bytes"/> of space.
        /// </returns>
        public static byte[] AllocateScratch(int bytes, ref byte[] scratch)
        {
            if (scratch.Length < bytes)
            {
                scratch = new byte[bytes];
            }

            return scratch;
        }

        /// <summary>
        /// Decodes a <see cref="ushort"/> from a byte-array, in big-endian encoding.
        /// </summary>
        /// <param name="buffer">
        /// A <see cref="byte"/> array which contains the <see cref="ushort"/>.
        /// </param>
        /// <param name="offset">
        /// The index in <paramref name="buffer"/> at which the <see cref="ushort"/> starts.
        /// </param>
        /// <returns>
        /// The requested <see cref="ushort"/>.
        /// </returns>
        public static ushort DecodeUInt16BE(byte[] buffer, int offset)
        {
            return (ushort)(buffer[offset + 0] << 8 | buffer[offset + 1]);
        }

        /// <summary>
        /// Encodes a <see cref="ushort"/> as a <see cref="byte"/> array in big-endian
        /// encoding.
        /// </summary>
        /// <param name="value">
        /// The <see cref="ushort"/> to encode.
        /// </param>
        /// <returns>
        /// A <see cref="byte"/> array which represents the <paramref name="value"/>.
        /// </returns>
        public static byte[] EncodeUInt16BE(ushort value)
        {
            var buffer = new byte[2];
            EncodeUInt16BE(buffer, 0, value);
            return buffer;
        }

        /// <summary>
        /// Encodes a <see cref="ushort"/> as a <see cref="byte"/> array in big-endian
        /// encoding.
        /// </summary>
        /// <param name="buffer">
        /// The <see cref="byte"/> array in which to store the <paramref name="value"/>.
        /// </param>
        /// <param name="offset">
        /// The index of the first byte in <paramref name="buffer"/> at which the
        /// <paramref name="value"/> should be stored.
        /// </param>
        /// <param name="value">
        /// The <see cref="ushort"/> to encode.
        /// </param>
        public static void EncodeUInt16BE(byte[] buffer, int offset, ushort value)
        {
            buffer[offset + 0] = (byte)(value >> 8);
            buffer[offset + 1] = (byte)value;
        }

        /// <summary>
        /// Decodes a <see cref="uint"/> from a byte-array, in big-endian encoding.
        /// </summary>
        /// <param name="buffer">
        /// A <see cref="byte"/> array which contains the <see cref="uint"/>.
        /// </param>
        /// <param name="offset">
        /// The index in <paramref name="buffer"/> at which the <see cref="uint"/> starts.
        /// </param>
        /// <returns>
        /// The requested <see cref="uint"/>.
        /// </returns>
        public static uint DecodeUInt32BE(byte[] buffer, int offset)
        {
            return (uint)(buffer[offset + 0] << 24 | buffer[offset + 1] << 16 | buffer[offset + 2] << 8 | buffer[offset + 3]);
        }

        public static uint DecodeUInt32BE2(byte[] buffer, int offset)
        {
            return (uint)((buffer[offset + 0] << 24) + (buffer[offset + 1] << 16) + (buffer[offset + 2] << 8) + buffer[offset + 3]);
        }

        /// <summary>
        /// Encodes a <see cref="uint"/> as a <see cref="byte"/> array in big-endian
        /// encoding.
        /// </summary>
        /// <param name="value">
        /// The <see cref="uint"/> to encode.
        /// </param>
        /// <returns>
        /// A <see cref="byte"/> array which represents the <paramref name="value"/>.
        /// </returns>
        public static byte[] EncodeUInt32BE(uint value)
        {
            var buffer = new byte[4];
            EncodeUInt32BE(buffer, 0, value);
            return buffer;
        }

        /// <summary>
        /// Encodes a <see cref="uint"/> as a <see cref="byte"/> array in big-endian
        /// encoding.
        /// </summary>
        /// <param name="buffer">
        /// The <see cref="byte"/> array in which to store the <paramref name="value"/>.
        /// </param>
        /// <param name="offset">
        /// The index of the first byte in <paramref name="buffer"/> at which the
        /// <paramref name="value"/> should be stored.
        /// </param>
        /// <param name="value">
        /// The <see cref="uint"/> to encode.
        /// </param>
        public static void EncodeUInt32BE(byte[] buffer, int offset, uint value)
        {
            buffer[offset + 0] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }
    }
}
