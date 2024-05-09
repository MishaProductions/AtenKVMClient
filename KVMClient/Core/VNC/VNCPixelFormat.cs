using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KVMClient.Core.VNC
{
    public sealed class VNCPixelFormat
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="VncPixelFormat"/> class,
        /// with 8 bits each of red, green, and blue channels.
        /// </summary>
        public VNCPixelFormat()
            : this(32, 24, 8, 16, 8, 8, 8, 0)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VncPixelFormat"/> class.
        /// </summary>
        /// <param name="bitsPerPixel">The number of bits used to store a pixel. Currently, this must be 8, 16, or 32.</param>
        /// <param name="bitDepth">The bit depth of the pixel. Currently, this must be 24.</param>
        /// <param name="redBits">The number of bits used to represent red.</param>
        /// <param name="redShift">The number of bits left the red value is shifted.</param>
        /// <param name="greenBits">The number of bits used to represent green.</param>
        /// <param name="greenShift">The number of bits left the green value is shifted.</param>
        /// <param name="blueBits">The number of bits used to represent blue.</param>
        /// <param name="blueShift">The number of bits left the blue value is shifted.</param>
        /// <param name="isLittleEndian"><c>true</c> if the pixel is little-endian, or <c>false</c> if it is big-endian.</param>
        /// <param name="isPalettized"><c>true</c> if the framebuffer stores palette indices, or <c>false</c> if it stores colors.</param>
        public VNCPixelFormat(
            int bitsPerPixel,
            int bitDepth,
            int redBits,
            int redShift,
            int greenBits,
            int greenShift,
            int blueBits,
            int blueShift,
            bool isLittleEndian = true,
            bool isPalettized = false)
        {
            if (bitsPerPixel != 8 && bitsPerPixel != 16 && bitsPerPixel != 32)
            {
                throw new ArgumentOutOfRangeException(nameof(bitsPerPixel));
            }

            if (bitDepth != 6 && bitDepth != 24)
            {
                throw new ArgumentOutOfRangeException(nameof(bitDepth));
            }

            if (!(redBits >= 0 && redShift >= 0 && redBits <= bitDepth && redShift <= bitDepth))
            {
                throw new ArgumentOutOfRangeException(nameof(redBits));
            }

            if (!(greenBits >= 0 && greenShift >= 0 && greenBits <= bitDepth && greenShift <= bitDepth))
            {
                throw new ArgumentOutOfRangeException(nameof(greenBits));
            }

            if (!(blueBits >= 0 && blueShift >= 0 && blueBits <= bitDepth && blueShift <= bitDepth))
            {
                throw new ArgumentOutOfRangeException(nameof(blueBits));
            }

            BitsPerPixel = bitsPerPixel;
            BytesPerPixel = bitsPerPixel / 8;
            BitDepth = bitDepth;
            RedBits = redBits;
            RedShift = redShift;
            GreenBits = greenBits;
            GreenShift = greenShift;
            BlueBits = blueBits;
            BlueShift = blueShift;
            IsLittleEndian = isLittleEndian;
            IsPalettized = isPalettized;
        }

        /// <summary>
        /// Gets a <see cref="VncPixelFormat"/> with 8 bits of red, green and blue channels.
        /// </summary>
        public static VNCPixelFormat RGB32 { get; } = new VNCPixelFormat();

        /// <summary>
        /// Gets the number of bits used to store a pixel.
        /// </summary>
        public int BitsPerPixel { get; set; }

        /// <summary>
        /// Gets the number of bytes used to store a pixel.
        /// </summary>
        public int BytesPerPixel { get; set; }

        /// <summary>
        /// Gets the bit depth of the pixel.
        /// </summary>
        public int BitDepth { get; set; }

        /// <summary>
        /// Gets the number of bits used to represent red.
        /// </summary>
        public int RedBits { get; set; }

        /// <summary>
        /// Gets the number of bits left the red value is shifted.
        /// </summary>
        public int RedShift { get; set; }

        /// <summary>
        /// Gets the number of bits used to represent green.
        /// </summary>
        public int GreenBits { get; set; }

        private ushort overrideredmax;

        /// <summary>
        /// Gets the maximum value of the red color.
        /// </summary>
        public ushort RedMax
        {
            get
            {
                if (overrideredmax != 0)
                    return overrideredmax;
                return (ushort)((1 << RedBits) - 1);
            }
            set
            {
                overrideredmax = value;
            }
        }
        private ushort overridebluemax;
        /// <summary>
        /// Gets the maximum value of the blue color.
        /// </summary>
        public ushort BlueMax
        {
            get
            {
                if (overridebluemax != 0)
                    return overridebluemax;
                return (ushort)((1 << BlueBits) - 1);
            }
            set
            {
                overridebluemax = value;
            }
        }

        private ushort overridegreenmax = 0;
        /// <summary>
        /// Gets the maximum value of the green color.
        /// </summary>
        ///
        public ushort GreenMax
        {
            get
            {
                if (overridegreenmax != 0)
                    return overridegreenmax;
                return (ushort)((1 << GreenBits) - 1);
            }
            set
            {
                overridegreenmax = value;
            }
        }

        /// <summary>
        /// Gets the number of bits left the green value is shifted.
        /// </summary>
        public int GreenShift { get; set; }

        /// <summary>
        /// Gets the number of bits used to represent blue.
        /// </summary>
        public int BlueBits { get; set; }

        /// <summary>
        /// Gets the number of bits left the blue value is shifted.
        /// </summary>
        public int BlueShift { get; set; }

        /// <summary>
        /// Gets a value indicating whether the pixel is little-endian.
        /// </summary>
        /// <value>
        /// <c>true</c> if the pixel is little-endian, or <c>false</c> if it is big-endian.
        /// </value>
        public bool IsLittleEndian { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the framebuffer stores palette indices.
        /// </summary>
        /// <value>
        /// <c>true</c> if the framebuffer stores palette indices, or <c>false</c> if it stores colors.
        /// </value>
        public bool IsPalettized { get; private set; }

        /// <summary>
        /// Gets the size of a <see cref="VncPixelFormat"/> when serialized to a <see cref="byte"/> array.
        /// </summary>
        internal static int Size
        {
            get { return 16; }
        }

        /// <summary>
        /// Copies pixels between two byte arrays. A format conversion is performed if necessary.
        ///
        /// Be sure to lock <see cref="VncFramebuffer.SyncRoot"/> first to avoid tearing,
        /// if the connection is active.
        /// </summary>
        /// <param name="source">A pointer to the upper-left corner of the source.</param>
        /// <param name="sourceWidth">The width of the source image.</param>
        /// <param name="sourceStride">The offset in the source between one Y coordinate and the next.</param>
        /// <param name="sourceFormat">The source pixel format.</param>
        /// <param name="sourceRectangle">The rectangle in the source to decode.</param>
        /// <param name="target">A pointer to the upper-left corner of the target.</param>
        /// <param name="targetWidth">The width of the target image.</param>
        /// <param name="targetStride">The offset in the target between one Y coordinate and the next.</param>
        /// <param name="targetFormat">The target pixel format.</param>
        /// <param name="targetX">The X coordinate in the target that the leftmost pixel should be placed into.</param>
        /// <param name="targetY">The Y coordinate in the target that the topmost pixel should be placed into.</param>
        //public static unsafe void Copy(
        //    byte[] source,
        //    int sourceWidth,
        //    int sourceStride,
        //    VNCPixelFormat sourceFormat,
        //    VncRectangle sourceRectangle,
        //    byte[] target,
        //    int targetWidth,
        //    int targetStride,
        //    VNCPixelFormat targetFormat,
        //    int targetX = 0,
        //    int targetY = 0)
        //{
        //    if (source == null)
        //    {
        //        throw new ArgumentNullException(nameof(source));
        //    }

        //    if (target == null)
        //    {
        //        throw new ArgumentNullException(nameof(target));
        //    }

        //    if (sourceRectangle.IsEmpty)
        //    {
        //        return;
        //    }

        //    int x = sourceRectangle.X, w = sourceRectangle.Width;
        //    int y = sourceRectangle.Y, h = sourceRectangle.Height;

        //    if (sourceFormat.Equals(targetFormat))
        //    {
        //        if (sourceRectangle.Width == sourceWidth
        //            && sourceWidth == targetWidth
        //            && sourceStride == targetStride)
        //        {
        //            int sourceStart = sourceStride * y;
        //            int length = targetStride * h;

        //            Buffer.BlockCopy(source, sourceStart, target, 0, length);
        //        }
        //        else
        //        {
        //            for (int iy = 0; iy < h; iy++)
        //            {
        //                int sourceStart = (sourceStride * (iy + y)) + (x * sourceFormat.BitsPerPixel / 8);
        //                int targetStart = targetStride * iy;

        //                int length = w * sourceFormat.BitsPerPixel / 8;

        //                Buffer.BlockCopy(source, sourceStart, target, targetStart, length);
        //            }
        //        }
        //    }
        //}

        /// <summary>
        /// <para>
        /// Copies pixels. A format conversion is performed if necessary.
        /// </para>
        /// <para>
        /// Be sure to lock <see cref="VncFramebuffer.SyncRoot"/> first to avoid tearing,
        /// if the connection is active.
        /// </para>
        /// </summary>
        /// <param name="source">A pointer to the upper-left corner of the source.</param>
        /// <param name="sourceStride">The offset in the source between one Y coordinate and the next.</param>
        /// <param name="sourceFormat">The source pixel format.</param>
        /// <param name="sourceRectangle">The rectangle in the source to decode.</param>
        /// <param name="target">A pointer to the upper-left corner of the target.</param>
        /// <param name="targetStride">The offset in the target between one Y coordinate and the next.</param>
        /// <param name="targetFormat">The target pixel format.</param>
        /// <param name="targetX">The X coordinate in the target that the leftmost pixel should be placed into.</param>
        /// <param name="targetY">The Y coordinate in the target that the topmost pixel should be placed into.</param>
        //public static unsafe void Copy(
        //    IntPtr source,
        //    int sourceStride,
        //    VNCPixelFormat sourceFormat,
        //    VncRectangle sourceRectangle,
        //    IntPtr target,
        //    int targetStride,
        //    VNCPixelFormat targetFormat,
        //    int targetX = 0,
        //    int targetY = 0)
        //{
        //    if (source == IntPtr.Zero)
        //    {
        //        throw new ArgumentOutOfRangeException(nameof(source));
        //    }

        //    if (target == IntPtr.Zero)
        //    {
        //        throw new ArgumentOutOfRangeException(nameof(target));
        //    }

        //    if (sourceFormat == null)
        //    {
        //        throw new ArgumentOutOfRangeException(nameof(sourceFormat));
        //    }

        //    if (targetFormat == null)
        //    {
        //        throw new ArgumentNullException(nameof(targetFormat));
        //    }

        //    if (sourceRectangle.IsEmpty)
        //    {
        //        return;
        //    }

        //    int x = sourceRectangle.X, w = sourceRectangle.Width;
        //    int y = sourceRectangle.Y, h = sourceRectangle.Height;

        //    var sourceData = (byte*)(void*)source + (y * sourceStride) + (x * sourceFormat.BytesPerPixel);
        //    var targetData = (byte*)(void*)target + (targetY * targetStride) + (targetX * targetFormat.BytesPerPixel);

        //    if (sourceFormat.Equals(targetFormat))
        //    {
        //        for (int iy = 0; iy < h; iy++)
        //        {
        //            if (sourceFormat.BytesPerPixel == 4)
        //            {
        //                uint* sourceDataX0 = (uint*)sourceData, targetDataX0 = (uint*)targetData;
        //                for (int ix = 0; ix < w; ix++)
        //                {
        //                    *targetDataX0++ = *sourceDataX0++;
        //                }
        //            }
        //            else
        //            {
        //                int bytes = w * sourceFormat.BytesPerPixel;
        //                byte* sourceDataX0 = (byte*)sourceData, targetDataX0 = (byte*)targetData;
        //                for (int ib = 0; ib < bytes; ib++)
        //                {
        //                    *targetDataX0++ = *sourceDataX0++;
        //                }
        //            }

        //            sourceData += sourceStride;
        //            targetData += targetStride;
        //        }
        //    }
        //}

        ///// <summary>
        ///// Copies a region of the framebuffer into a bitmap.
        ///// </summary>
        ///// <param name="source">The framebuffer to read.</param>
        ///// <param name="sourceRectangle">The framebuffer region to copy.</param>
        ///// <param name="scan0">The bitmap buffer start address.</param>
        ///// <param name="stride">The bitmap width stride.</param>
        ///// <param name="targetX">The leftmost X coordinate of the bitmap to draw to.</param>
        ///// <param name="targetY">The topmost Y coordinate of the bitmap to draw to.</param>
        //public static unsafe void CopyFromFramebuffer(
        //    VNCPixelFormat source,
        //    VncRectangle sourceRectangle,
        //    IntPtr scan0,
        //    int stride,
        //    int targetX,
        //    int targetY)
        //{
        //    if (source == null)
        //    {
        //        throw new ArgumentNullException(nameof(source));
        //    }

        //    if (sourceRectangle.IsEmpty)
        //    {
        //        return;
        //    }

        //    fixed (byte* framebufferData = source.GetBuffer())
        //    {
        //        VncPixelFormat.Copy(
        //            (IntPtr)framebufferData,
        //            source.Stride,
        //            source.PixelFormat,
        //            sourceRectangle,
        //            scan0,
        //            stride,
        //            new VncPixelFormat());
        //    }
        //}

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            var format = obj as VNCPixelFormat;

            if (format != null)
            {
                if (BitsPerPixel == format.BitsPerPixel && BitDepth == format.BitDepth &&
                    RedBits == format.RedBits && RedShift == format.RedShift &&
                    GreenBits == format.GreenBits && GreenShift == format.GreenShift &&
                    BlueBits == format.BlueBits && BlueShift == format.BlueShift &&
                    IsLittleEndian == format.IsLittleEndian && IsPalettized == format.IsPalettized)
                {
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return BitsPerPixel ^ RedBits;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"{BitsPerPixel} bpp; {BitDepth} depth: R {RedBits} {RedShift}; G {GreenBits} {GreenShift}; B {BlueBits} {BlueShift}; LE: {IsLittleEndian}; Palettized {IsPalettized}";
        }

        /// <summary>
        /// Decodes a <see cref="VncPixelFormat"/> from a <see cref="byte"/> array.
        /// </summary>
        /// <param name="buffer">
        /// The <see cref="byte"/> array which contains the <see cref="VncPixelFormat"/> data.
        /// </param>
        /// <param name="offset">
        /// The first index in the <paramref name="buffer"/> which contains the <see cref="VncPixelFormat"/>
        /// data.
        /// </param>
        /// <returns>
        /// A <see cref="VncPixelFormat"/> object.
        /// </returns>
        internal static VNCPixelFormat Decode(byte[] buffer, int offset)
        {
            var bitsPerPixel = buffer[offset + 0];
            var depth = buffer[offset + 1];
            var isLittleEndian = buffer[offset + 2] == 0;
            var isPalettized = buffer[offset + 3] == 0;
            var redBits = BitsFromMax(VncUtility.DecodeUInt16BE(buffer, offset + 4));
            var greenBits = BitsFromMax(VncUtility.DecodeUInt16BE(buffer, offset + 6));
            var blueBits = BitsFromMax(VncUtility.DecodeUInt16BE(buffer, offset + 8));
            var redShift = buffer[offset + 10];
            var greenShift = buffer[offset + 11];
            var blueShift = buffer[offset + 12];

            return new VNCPixelFormat(
                bitsPerPixel,
                depth,
                redBits,
                redShift,
                greenBits,
                greenShift,
                blueBits,
                blueShift,
                isLittleEndian,
                isPalettized);
        }

        /// <summary>
        /// Serializes this <see cref="VncPixelFormat"/> to a <see cref="byte"/> array.
        /// </summary>
        /// <param name="buffer">
        /// The <see cref="byte"/> array to which to encode the <see cref="VncPixelFormat"/> object.
        /// </param>
        /// <param name="offset">
        /// The first <see cref="byte"/> at which to store the <see cref="VncPixelFormat"/> data.
        /// </param>
        internal void Encode(byte[] buffer, int offset)
        {
            buffer[offset + 0] = (byte)BitsPerPixel;
            buffer[offset + 1] = (byte)BitDepth;
            buffer[offset + 2] = (byte)(IsLittleEndian ? 0 : 1);
            buffer[offset + 3] = (byte)(IsPalettized ? 0 : 1);
            VncUtility.EncodeUInt16BE(buffer, offset + 4, RedMax);
            VncUtility.EncodeUInt16BE(buffer, offset + 6, GreenMax);
            VncUtility.EncodeUInt16BE(buffer, offset + 8, BlueMax);
            buffer[offset + 10] = (byte)RedShift;
            buffer[offset + 11] = (byte)GreenShift;
            buffer[offset + 12] = (byte)BlueShift;
        }

        private static int BitsFromMax(int max)
        {
            if (max == 0 || (max & max + 1) != 0)
            {
                throw new ArgumentException();
            }

            return (int)Math.Round(Math.Log(max + 1) / Math.Log(2));
        }
    }
}
