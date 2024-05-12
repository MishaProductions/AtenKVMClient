using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace KVMClient.Core.VNC
{
    /// <summary>
    /// Provides methods for reading and sending VNC data over a <see cref="Stream"/>.
    /// </summary>
    internal sealed class VncStream
    {
        internal bool isOpen = false;
        /// <summary>
        /// Initializes a new instance of the <see cref="VncStream"/> class.
        /// </summary>
        public VncStream()
        {
            SyncRoot = new object();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VncStream"/> class.
        /// </summary>
        /// <param name="stream">
        /// The underlying <see cref="Stream"/>.
        /// </param>
        public VncStream(Stream stream)
            : this()
        {
            Stream = stream;
        }

        /// <summary>
        /// Gets or sets the underlying <see cref="Stream"/>.
        /// </summary>
        public Stream Stream
        {
            get;
            set;
        }

        /// <summary>
        /// Gets an <see cref="object"/> that can be used to synchronize access to the <see cref="VncStream"/>.
        /// </summary>
        public object SyncRoot
        {
            get;
            private set;
        }

        /// <summary>
        /// Decodes a sequence of bytes from the specified <see cref="byte"/> array into a <see cref="string"/>.
        /// </summary>
        /// <param name="buffer">
        /// The <see cref="byte"/> array containing the sequence of bytes to decode.
        /// </param>
        /// <param name="offset">
        /// The index of the first byte to decode.
        /// </param>
        /// <param name="count">
        /// The number of bytes to decode.
        /// </param>
        /// <returns>
        /// A <see cref="string"/> containing the results of decoding the specified sequence of bytes.
        /// </returns>
        public static string DecodeString(byte[] buffer, int offset, int count)
        {
            return Encoding.GetEncoding("iso-8859-1").GetString(buffer, offset, count);
        }

        /// <summary>
        /// Decodes a sequence of bytes from the specified <see cref="char"/> array into a <see cref="string"/>.
        /// </summary>
        /// <param name="chars">
        /// The <see cref="char"/> array containing the sequence of bytes to decode.
        /// </param>
        /// <param name="offset">
        /// The index of the first byte to decode.
        /// </param>
        /// <param name="count">
        /// The number of bytes to decode.
        /// </param>
        /// <returns>
        /// A <see cref="string"/> containing the results of decoding the specified sequence of bytes.
        /// </returns>
        public static byte[] EncodeString(char[] chars, int offset, int count)
        {
            return Encoding.GetEncoding("iso-8859-1").GetBytes(chars, offset, count);
        }

        /// <summary>
        /// Encodes all the characters in the specified <see cref="string"/> into a sequence of bytes.
        /// </summary>
        /// <param name="string">
        /// The <see cref="string"/> to encode.
        /// </param>
        /// <returns>
        /// A <see cref="byte"/> array containing the results of encoding the specified set of characters.
        /// </returns>
        public static byte[] EncodeString(string @string)
        {
            return Encoding.GetEncoding("iso-8859-1").GetBytes(@string);
        }

        /// <summary>
        /// Throws a <see cref="VncException"/> if a specific condition is not met.
        /// </summary>
        /// <param name="condition">
        /// The condition which should be met.
        /// </param>
        /// <param name="message">
        /// A <see cref="string"/> that describes the error.
        /// </param>
        /// <param name="reason">
        /// A <see cref="VncFailureReason"/> that describes the error.
        /// </param>
        public static void Require(bool condition, string message, VncFailureReason reason)
        {
            if (!condition)
            {
                throw new Exception(message + "is false");
            }
        }

        /// <summary>
        /// Throws a <see cref="VncException"/> if a specific condition is not met.
        /// </summary>
        /// <param name="condition">
        /// The condition which should be met.
        /// </param>
        public static void SanityCheck(bool condition)
        {
            Require(condition, "Sanity check failed.", VncFailureReason.SanityCheckFailed);
        }

        /// <summary>
        /// Closes the current stream and releases any resources
        /// associated with the current stream.
        /// </summary>
        public void Close()
        {
            var stream = Stream;
            if (stream != null)
            {
                stream.Dispose();
            }
            isOpen = false;
        }

        /// <summary>
        /// Reads a sequence of bytes from the current stream and advances the position within the
        /// stream by the number of bytes read.
        /// </summary>
        /// <param name="count">
        /// The number of bytes to read.
        /// </param>
        /// <returns>
        /// A <see cref="byte"/> array containing the bytes that have been read.
        /// </returns>
        public byte[] Receive(int count)
        {
            var buffer = new byte[count];
            Receive(buffer, 0, buffer.Length);
            return buffer;
        }

        /// <summary>
        /// Reads a sequence of bytes from the current stream and advances the position within the
        /// stream by the number of bytes read.
        /// </summary>
        /// <param name="buffer">
        /// An array of bytes. When this method returns, the buffer contains the specified byte array with
        /// the values between <paramref name="offset"/> and (<paramref name="offset"/> + <paramref name="count"/>- 1)
        /// replaced by the bytes read from the current source.
        /// </param>
        /// <param name="offset">
        /// The zero-based byte offset in <paramref name="buffer"/> at which to begin storing the data
        /// read from the current stream.
        /// </param>
        /// <param name="count">
        /// The maximum number of bytes to be read from the current stream.
        /// </param>
        public void Receive(byte[] buffer, int offset, int count)
        {
            for (int i = 0; i < count;)
            {
                int bytes = Stream.Read(buffer, offset + i, count - i);
                Require(bytes > 0, "Lost connection.", VncFailureReason.NetworkError);
                i += bytes;
            }
        }

        /// <summary>
        /// Reads a byte from the stream and advances the position within the stream by one byte.
        /// </summary>
        /// <returns>
        /// The unsigned byte cast to an <see cref="int"/>.
        /// </returns>
        public byte ReceiveByte()
        {
            int value = Stream.ReadByte();
            Require(value >= 0, "Lost connection.", VncFailureReason.NetworkError);
            return (byte)value;
        }

        /// <summary>
        /// Reads a <see cref="VncRectangle"/> from the stream and advances the position within
        /// the stream by 8 bytes.
        /// </summary>
        /// <returns>
        /// The <see cref="VncRectangle"/> which was read from the stream.
        /// </returns>
        public VncRectangle ReceiveRectangle()
        {
            int x = ReceiveUInt16BE();
            int y = ReceiveUInt16BE();
            int w = ReceiveUInt16BE();
            int h = ReceiveUInt16BE();
            return new VncRectangle(x, y, w, h);
        }

        /// <summary>
        /// Reads a <see cref="string"/> from the stream.
        /// </summary>
        /// <param name="maxLength">
        /// The maximum length of the <see cref="string"/> to
        /// read.
        /// </param>
        /// <returns>
        /// The <see cref="string"/> which was read from the stream.
        /// </returns>
        public string ReceiveString(int maxLength = 0xfff)
        {
            var length = (int)ReceiveUInt32BE();
            SanityCheck(length >= 0 && length <= maxLength);
            var value = DecodeString(Receive(length), 0, length);
            return value;
        }

        /// <summary>
        /// Reads a <see cref="ushort"/> in big-endian encoding from the stream and advances the
        /// position within the stream by two bytes.
        /// </summary>
        /// <returns>
        /// The <see cref="ushort"/> which was read from the stream.
        /// </returns>
        public ushort ReceiveUInt16BE()
        {
            return VncUtility.DecodeUInt16BE(Receive(2), 0);
        }

        /// <summary>
        /// Reads a <see cref="uint"/> in big-endian encoding from the stream and advances the
        /// position within the stream by two bytes.
        /// </summary>
        /// <returns>
        /// The <see cref="uint"/> which was read from the stream.
        /// </returns>
        public uint ReceiveUInt32BE()
        {
            return VncUtility.DecodeUInt32BE(Receive(4), 0);
        }

        public uint ReceiveUInt32BE2()
        {
            return VncUtility.DecodeUInt32BE2(Receive(4), 0);
        }

        /// <summary>
        /// Receives version information from the stream.
        /// </summary>
        /// <returns>
        /// The <see cref="Version"/>.
        /// </returns>
        public Version ReceiveVersion()
        {
            var version = Encoding.ASCII.GetString(Receive(12));
            var versionRegex = Regex.Match(
                    version,
                    @"^RFB (?<maj>[0-9]{3})\.(?<min>[0-9]{3})\n",
                    RegexOptions.Singleline | RegexOptions.CultureInvariant);
            Require(
                versionRegex.Success,
                "Not using VNC protocol.",
                VncFailureReason.WrongKindOfServer);

            int major = int.Parse(versionRegex.Groups["maj"].Value);
            int minor = int.Parse(versionRegex.Groups["min"].Value);
            return new Version(major, minor);
        }
        public void Flush()
        {
            Stream.Flush();
        }
        /// <summary>
        /// Writes a sequence of bytes to the current stream and advances the current position within this
        /// stream by the number of bytes written.
        /// </summary>
        /// <param name="buffer">
        /// The bytes to write to the stream.
        /// </param>
        public void Send(byte[] buffer)
        {
            Send(buffer, 0, buffer.Length);
        }

        /// <summary>
        /// Writes a sequence of bytes to the current stream and advances the current position within this stream by the number of bytes written.
        /// </summary>
        /// <param name="buffer">
        /// An array of bytes. This method copies <paramref name="count"/> bytes from <paramref name="buffer"/> to the current stream.
        /// </param>
        /// <param name="offset">
        /// The zero-based byte offset in <paramref name="buffer"/> at which to begin copying bytes to the current stream.
        /// </param>
        /// <param name="count">
        /// The number of bytes to be written to the current stream.
        /// </param>
        public void Send(byte[] buffer, int offset, int count)
        {
            if (Stream == null || !isOpen)
            {
                return;
            }

            lock (SyncRoot)
            {
                var stream = Stream;
                if (stream == null)
                {
                    return;
                }

                try
                {
                    stream.Write(buffer, offset, count);
                }
                catch (ObjectDisposedException)
                {
                    isOpen = false;
                }
                catch (IOException)
                {
                }
            }
        }

        /// <summary>
        /// Writes a <see cref="byte"/> to the current position in the stream and advances the position within the stream by one byte.
        /// </summary>
        /// <param name="value">
        /// The <see cref="byte"/> to write to the stream.
        /// </param>
        public void SendByte(byte value)
        {
            Send(new[] { value });
        }

        /// <summary>
        /// Writes a <see cref="VncRectangle"/> to the current position in the stream and advances the position within the stream by 8 bytes.
        /// </summary>
        /// <param name="region">
        /// The <see cref="VncRectangle"/> to write to the stream.
        /// </param>
        public void SendRectangle(VncRectangle region)
        {
            var buffer = new byte[8];
            VncUtility.EncodeUInt16BE(buffer, 0, (ushort)region.X);
            VncUtility.EncodeUInt16BE(buffer, 2, (ushort)region.Y);
            VncUtility.EncodeUInt16BE(buffer, 4, (ushort)region.Width);
            VncUtility.EncodeUInt16BE(buffer, 6, (ushort)region.Height);
            Send(buffer);
        }

        /// <summary>
        /// Writes a <see cref="ushort"/> in big endian encoding to the current position in the stream and advances the position within the stream by two bytes.
        /// </summary>
        /// <param name="value">
        /// The <see cref="ushort"/> to write to the stream.
        /// </param>
        public void SendUInt16BE(ushort value)
        {
            Send(VncUtility.EncodeUInt16BE(value));
        }

        /// <summary>
        /// Writes a <see cref="uint"/> in big endian encoding to the current position in the stream and advances the position within the stream by four bytes.
        /// </summary>
        /// <param name="value">
        /// The <see cref="uint"/> to write to the stream.
        /// </param>
        public void SendUInt32BE(uint value)
        {
            Send(VncUtility.EncodeUInt32BE(value));
        }

        /// <summary>
        /// Writes a <see cref="string"/> in big endian encoding to the current position in the stream.
        /// </summary>
        /// <param name="string">
        /// The <see cref="string"/> to write to the stream.
        /// </param>
        /// <param name="includeLength">
        /// <see langword="true"/> to write the current length to the stream; otherwise,
        /// <see langword="false"/>.
        /// </param>
        public void SendString(string @string, bool includeLength = false)
        {
            var encodedString = EncodeString(@string);
            using (new AutoClear(encodedString))
            {
                if (includeLength)
                {
                    SendUInt32BE((uint)encodedString.Length);
                }

                Send(EncodeString(@string));
            }
        }

        /// <summary>
        /// Writes a <see cref="Version"/> in big endian encoding to the current position in the stream.
        /// </summary>
        /// <param name="version">
        /// The <see cref="Version"/> to write to the stream.
        /// </param>
        public void SendVersion(Version version)
        {
            SendString(string.Format("RFB {0:000}.{1:000}\n", version.Major, version.Minor));
        }
    }
    public enum VncFailureReason
    {
        /// <summary>
        /// Unknown reason.
        /// </summary>
        Unknown,

        /// <summary>
        /// The server isn't a VNC server.
        /// </summary>
        WrongKindOfServer,

        /// <summary>
        /// RemoteViewing can't speak the protocol versions this server offers.
        /// </summary>
        UnsupportedProtocolVersion,

        /// <summary>
        /// The server offered no authentication methods. This could mean that VNC is temporarily disabled.
        /// </summary>
        ServerOfferedNoAuthenticationMethods,

        /// <summary>
        /// The server offered no supported authentication methods.
        /// </summary>
        NoSupportedAuthenticationMethods,

        /// <summary>
        /// A password was required to authenticate but wasn't supplied.
        /// </summary>
        PasswordRequired,

        /// <summary>
        /// Authentication failed. This could mean you supplied an incorrect password.
        /// </summary>
        AuthenticationFailed,

        /// <summary>
        /// The server specified a pixel format RemoteViewing doesn't support.
        /// </summary>
        UnsupportedPixelFormat,

        /// <summary>
        /// A network error occured. The connection may have been lost.
        /// </summary>
        NetworkError,

        /// <summary>
        /// The server sent a value that seems unreasonable. This shouldn't happen in normal conditions.
        /// </summary>
        SanityCheckFailed,

        /// <summary>
        /// The server sent an unrecognized protocol element. This shouldn't happen in normal conditions.
        /// </summary>
        UnrecognizedProtocolElement,
    }
    internal struct AutoClear : IDisposable
    {
        private Array array;

        /// <summary>
        /// Initializes a new instance of the <see cref="AutoClear"/> struct.
        /// </summary>
        /// <param name="array">
        /// The <see cref="Array"/> which should be cleared when this <see cref="AutoClear"/>
        /// object is disposed of.
        /// </param>
        public AutoClear(Array array)
        {
            this.array = array;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (array == null)
            {
                return;
            }

            Array.Clear(array, 0, array.Length);
            array = null;
        }
    }
    public struct VncRectangle : IEquatable<VncRectangle>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="VncRectangle"/> structure.
        /// </summary>
        /// <param name="x">The X coordinate of the leftmost changed pixel.</param>
        /// <param name="y">The Y coordinate of the topmost changed pixel.</param>
        /// <param name="width">The width of the changed region.</param>
        /// <param name="height">The height of the changed region.</param>
        public VncRectangle(int x, int y, int width, int height)
            : this()
        {
            if (x < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(x));
            }

            if (y < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(y));
            }

            if (width < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        /// <summary>
        /// Gets the number of pixels.
        /// </summary>
        public int Area
        {
            get { return Width * Height; }
        }

        /// <summary>
        /// Gets or sets the X coordinate of the leftmost changed pixel.
        /// </summary>
        public int X { get; set; }

        /// <summary>
        /// Gets or sets the Y coordinate of the topmost changed pixel.
        /// </summary>
        public int Y { get; set; }

        /// <summary>
        /// Gets or sets the width of the changed region.
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// Gets or sets the height of the changed region.
        /// </summary>
        public int Height { get; set; }

        /// <summary>
        /// Gets a value indicating whether the region is empty.
        /// </summary>
        /// <value>
        /// <c>true</c> if the region contains no pixels.
        /// </value>
        public bool IsEmpty
        {
            get { return Width == 0 || Height == 0; }
        }

        /// <summary>
        /// Compares two rectangles for equality.
        /// </summary>
        /// <param name="rect1">The first rectangle.</param>
        /// <param name="rect2">The second rectangle.</param>
        /// <returns><c>true</c> if the rectangles are equal.</returns>
        public static bool operator ==(VncRectangle rect1, VncRectangle rect2)
        {
            return rect1.Equals(rect2);
        }

        /// <summary>
        /// Compares two rectangles for inequality.
        /// </summary>
        /// <param name="rect1">The first rectangle.</param>
        /// <param name="rect2">The second rectangle.</param>
        /// <returns><c>true</c> if the rectangles are not equal.</returns>
        public static bool operator !=(VncRectangle rect1, VncRectangle rect2)
        {
            return !rect1.Equals(rect2);
        }

        /// <summary>
        /// Intersects two rectangles.
        /// </summary>
        /// <param name="rect1">The first rectangle.</param>
        /// <param name="rect2">The second rectangle.</param>
        /// <returns>The intersection of the two.</returns>
        public static VncRectangle Intersect(VncRectangle rect1, VncRectangle rect2)
        {
            if (rect1.IsEmpty)
            {
                return rect1;
            }
            else if (rect2.IsEmpty)
            {
                return rect2;
            }

            int x = Math.Max(rect1.X, rect2.X), y = Math.Max(rect1.Y, rect2.Y);
            int w = Math.Min(rect1.X + rect1.Width, rect2.X + rect2.Width) - x;
            int h = Math.Min(rect1.Y + rect1.Height, rect2.Y + rect2.Height) - y;
            return w > 0 && h > 0 ? new VncRectangle(x, y, w, h) : default;
        }

        /// <summary>
        /// Finds a region that contains both rectangles.
        /// </summary>
        /// <param name="rect1">The first rectangle.</param>
        /// <param name="rect2">The second rectangle.</param>
        /// <returns>The union of the two.</returns>
        public static VncRectangle Union(VncRectangle rect1, VncRectangle rect2)
        {
            if (rect1.IsEmpty)
            {
                return rect2;
            }
            else if (rect2.IsEmpty)
            {
                return rect1;
            }

            int x = Math.Min(rect1.X, rect2.X), y = Math.Min(rect1.Y, rect2.Y);
            int w = Math.Max(rect1.X + rect1.Width, rect2.X + rect2.Width) - x;
            int h = Math.Max(rect1.Y + rect1.Height, rect2.Y + rect2.Height) - y;
            return new VncRectangle(x, y, w, h);
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is VncRectangle && Equals((VncRectangle)obj);
        }

        /// <summary>
        /// Compares the rectangle with another rectangle for equality.
        /// </summary>
        /// <param name="other">The other rectangle.</param>
        /// <returns><c>true</c> if the rectangles are equal.</returns>
        public bool Equals(VncRectangle other)
        {
            return X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return X | Y << 16;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return string.Format("{0}x{1} at {2}, {3}", Width, Height, X, Y);
        }
    }
}
