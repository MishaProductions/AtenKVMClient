using System;
using System.Diagnostics;

namespace KVMClient.Core.VNC
{
    /// <summary>
    /// Port of supermicro html ikvm viewer
    /// </summary>
    public partial class Ast2100Decoder
    {
        private int mWidth;
        private int mHeight;
        private byte[] buffer = new byte[0];
        private int mIndex;
        private int mCodebuf;
        private int mNewbuf;
        private int mYSelector;
        private int mUVSelector;
        private int mYUVMode;
        private float[] mNegPow2;
        private int mYQnr;
        private int mCbQnr;
        private int mCrQnr;
        private int mYDCnr;
        private int mCbDCnr;
        private int mCrDCnr;
        private int mYACnr;
        private int mCbACnr;
        private int mCrACnr;
        private int mMapping;
        private int mOldMode420;
        private int mMode420 = 1;
        private int mOldYSelector;
        private int mScaleFactor;
        private int mScaleFactorUV;
        private int mAdvanceSelector;
        private int mSharpModeSelection;
        private int mAdvanceScaleFactor;
        private int mAdvanceScaleFactorUV;
        private int mTxb;
        private int mTyb;
        private int mNewbits = 0;
        private int mDCY;
        private int mDCCb;
        private int mDCCr;
        private int mHuffPerf;
        private int mIDCTPerf;
        private int mYUVPerf;
        private int mUpbufPerf;
        private int mMovePerf;
        private int mDecompPerf;
        private int mCurBytePos;
        private int[] std_luminance_qt;
        private int[] std_chrominance_qt;
        private int mGreyMode = 0;
        private byte[] mTileYuv = new byte[768];
        private int previous_DC = 0;

        private ushort[] mRlimitTable = new ushort[704];
        private int mRlimitTable_index = 256;
        private int[] mCrToR = new int[256];
        private int[] mCbToB = new int[256];
        private int[] mCrToG = new int[256];
        private int[] mCbToG = new int[256];
        private int[] m_Y = new int[256];
        private double[][] mQT = new double[4][];

        private int[] mColorBuf = new int[4];
        private int[] decodeColorIndex = new int[4];
        //private int bitmapBits;
        private bool HT_init = false;
        private int mTmpWidth;
        private int mTmpHeight;
        private int[] mDCTCoeff = new int[384];
        private Ast2100HuffmanTable HT_ref = new Ast2100HuffmanTable();

        private uint[] mDecode_Color_Index = new uint[4];
        private uint mDecode_Color_BitMapBits = 0;

        private int[] mWorkspace = new int[64];

        public byte[] mOutBuffer = new byte[0];
        private int mTwb;
        private int mThb;
        private int mThw;
        public Ast2100Decoder()
        {
            mNegPow2 = new float[] { 0, -1, -3, -7, -15, -31, -63, -127, -255, -511, -1023, -2047, -4095, -8191, -16383, -32767 };
            for (var i = 0; i < 4; i++)
                this.mQT[i] = new double[64];
        }

        private int GetQBytesFromBuffer(int len)
        {
            int result = 0;
            for (int i = 0; i < len; i++)
            {
                if (mIndex < buffer.Length)
                {
                    result |= (int)buffer[mIndex++] << (8 * i); ;
                }
                else
                {
                    result |= 0;
                }
            }
            return result;
        }

        private int properWidth;
        private int properHeight;
        public bool Decode(byte[] data, int width, int height)
        {
            this.mWidth = width;
            this.mHeight = height;
            this.buffer = data;

            int MB = width * height / 64;
            if (MB < 4096)
                MB = 4096;

            SetOptions();
            VQInitialize();
            set_tmp_width_height(mMode420, width, height, mTmpWidth, mTmpHeight);

            if (properWidth != width || properHeight != height)
            {
                mOutBuffer = new byte[width * height * 4];

                properWidth = width;
                properHeight = height;
            }
            mAdvanceScaleFactor = 16;
            mAdvanceScaleFactorUV = 16;
            mAdvanceSelector = 0;
            mMapping = 0;
            mSharpModeSelection = 0;
            if (mOldYSelector != mYSelector)
            {
                InitJpgDecoding();
                mOldYSelector = mYSelector;
            }

            mIndex = 4;
            mCodebuf = GetQBytesFromBuffer(4);
            mNewbuf = GetQBytesFromBuffer(4);
            this.mTxb = 0;
            this.mTyb = 0;
            this.mNewbits = 32;
            this.mDCY = 0;
            this.mDCCb = 0;
            this.mDCCr = 0;
            this.mHuffPerf = 0;
            this.mIDCTPerf = 0;
            this.mYUVPerf = 0;
            this.mUpbufPerf = 0;
            this.mMovePerf = 0;
            this.mDecompPerf = 0;
            int block_index = 0;
            do
            {
                if ((mCodebuf >>> 28 & BLOCK_HEADER_MASK) == JPEG_NO_SKIP_CODE)
                {
                    //Console.WriteLine("JPEG_NO_SKIP_CODE");
                    updatereadbuf(BLOCK_AST2100_START_LENGTH);
                    Decompress(this.mTxb, this.mTyb, this.mOutBuffer, 0);
                    MoveBlockIndex();

                }
                else if ((mCodebuf >>> 28 & BLOCK_HEADER_MASK) == FRAME_END_CODE)
                {
                    // end of frame
                    return true;
                }
                else if ((mCodebuf >>> 28 & BLOCK_HEADER_MASK) == JPEG_SKIP_CODE)
                {
                    //Console.WriteLine("JPEG_SKIP_CODE");
                    this.mTxb = (this.mCodebuf & 0xFF00000) >>> 20;
                    this.mTyb = (this.mCodebuf & 0xFF000) >>> 12;

                    updatereadbuf(BLOCK_AST2100_SKIP_LENGTH);
                    Decompress(this.mTxb, this.mTyb, this.mOutBuffer, 0);
                    MoveBlockIndex();
                }
                else if ((mCodebuf >>> 28 & BLOCK_HEADER_MASK) == VQ_NO_SKIP_1_COLOR_CODE)
                {
                    Console.WriteLine("VQ_NO_SKIP_1_COLOR_CODE");

                    updatereadbuf(BLOCK_AST2100_START_LENGTH);
                    mDecode_Color_BitMapBits = 0;
                    this.VQ_ColorUpdate(1);
                    this.VQ_Decompress(this.mTxb, this.mTyb, this.mOutBuffer, 0);
                    this.MoveBlockIndex();
                }
                else if ((mCodebuf >>> 28 & BLOCK_HEADER_MASK) == VQ_SKIP_1_COLOR_CODE)
                {
                    Console.WriteLine("VQ_SKIP_1_COLOR_CODE");

                    this.mTxb = (this.mCodebuf & 267386880) >>> 20;
                    this.mTyb = (this.mCodebuf & 1044480) >>> 12;
                    this.updatereadbuf(BLOCK_AST2100_SKIP_LENGTH);
                    mDecode_Color_BitMapBits = 0;
                    this.VQ_ColorUpdate(1);
                    this.VQ_Decompress(this.mTxb, this.mTyb, this.mOutBuffer, 0);
                    this.MoveBlockIndex();
                }
                else if ((mCodebuf >>> 28 & BLOCK_HEADER_MASK) == VQ_NO_SKIP_2_COLOR_CODE)
                {
                    Console.WriteLine("VQ_NO_SKIP_2_COLOR_CODE");

                    updatereadbuf(BLOCK_AST2100_START_LENGTH);
                    mDecode_Color_BitMapBits = 1;
                    this.VQ_ColorUpdate(2);
                    this.VQ_Decompress(this.mTxb, this.mTyb, this.mOutBuffer, 0);
                    this.MoveBlockIndex();
                }
                else if ((mCodebuf >>> 28 & BLOCK_HEADER_MASK) == VQ_SKIP_2_COLOR_CODE)
                {
                    Console.WriteLine("VQ_SKIP_2_COLOR_CODE");

                    this.mTxb = (this.mCodebuf & 267386880) >>> 20;
                    this.mTyb = (this.mCodebuf & 1044480) >>> 12;
                    this.updatereadbuf(BLOCK_AST2100_SKIP_LENGTH);
                    mDecode_Color_BitMapBits = 1;
                    this.VQ_ColorUpdate(2);
                    this.VQ_Decompress(this.mTxb, this.mTyb, this.mOutBuffer, 0);
                    this.MoveBlockIndex();
                }
                else if ((mCodebuf >>> 28 & BLOCK_HEADER_MASK) == VQ_NO_SKIP_4_COLOR_CODE)
                {
                    Console.WriteLine("VQ_NO_SKIP_4_COLOR_CODE");

                    this.updatereadbuf(BLOCK_AST2100_START_LENGTH);
                    mDecode_Color_BitMapBits = 2;
                    this.VQ_ColorUpdate(4);
                    this.VQ_Decompress(this.mTxb, this.mTyb, this.mOutBuffer, 0);
                    this.MoveBlockIndex();
                }
                else if ((mCodebuf >>> 28 & BLOCK_HEADER_MASK) == VQ_SKIP_4_COLOR_CODE)
                {
                    Console.WriteLine("VQ_SKIP_4_COLOR_CODE");

                    this.mTxb = (this.mCodebuf & 0xFF00000) >>> 20;
                    this.mTyb = (this.mCodebuf & 0xFF000) >>> 12;
                    this.updatereadbuf(BLOCK_AST2100_SKIP_LENGTH);
                    mDecode_Color_BitMapBits = 2;
                    this.VQ_ColorUpdate(4);
                    this.VQ_Decompress(this.mTxb, this.mTyb, this.mOutBuffer, 0);
                    this.MoveBlockIndex();
                }
                else if ((mCodebuf >>> 28 & BLOCK_HEADER_MASK) == LOW_JPEG_NO_SKIP_CODE)
                {
                    Console.WriteLine("LOW_JPEG_NO_SKIP_CODE");

                    this.updatereadbuf(BLOCK_AST2100_START_LENGTH);
                    this.Decompress(this.mTxb, this.mTyb, this.mOutBuffer, 2);
                    this.MoveBlockIndex();
                }
                else if ((mCodebuf >>> 28 & BLOCK_HEADER_MASK) == LOW_JPEG_SKIP_CODE)
                {
                    Console.WriteLine("LOW_JPEG_SKIP_CODE");

                    this.mTxb = (this.mCodebuf & 267386880) >>> 20;
                    this.mTyb = (this.mCodebuf & 1044480) >>> 12;
                    this.updatereadbuf(BLOCK_AST2100_SKIP_LENGTH);
                    this.Decompress(this.mTxb, this.mTyb, this.mOutBuffer, 2);
                    this.MoveBlockIndex();
                }
                else
                {
                    throw new Exception("unknown mCodebuf value: " + (mCodebuf >> 28 & BLOCK_HEADER_MASK));
                }
                block_index++;
            } while (block_index <= MB);

            return true;
        }

        private void VQ_Decompress(int txb, int tyb, byte[] outBuf, int QT_TableSelection)
        {
            int ptr_index, i;
            var byTileYuv = this.mTileYuv;
            ushort Data;
            ptr_index = 0;
            if (mDecode_Color_BitMapBits == 0)
                for (i = 0; i < 64; i++)
                {
                    byTileYuv[ptr_index + 0] = (byte)((mColorBuf[mDecode_Color_Index[0]] & 16711680) >> 16);
                    byTileYuv[ptr_index + 64] = (byte)((mColorBuf[mDecode_Color_Index[0]] & 65280) >> 8);
                    byTileYuv[ptr_index + 128] = (byte)(mColorBuf[mDecode_Color_Index[0]] & 255);
                    ptr_index += 1;
                }
            else
            {
                for (i = 0; i < 64; i++)
                {
                    Data = (ushort)(65535 & this.mCodebuf >> 32 - (int)mDecode_Color_BitMapBits);
                    var e = mDecode_Color_Index[Data];
                    var b = mColorBuf[e];
                    byTileYuv[ptr_index + 0] = (byte)((b & 16711680) >> 16);
                    byTileYuv[ptr_index + 64] = (byte)((b & 65280) >> 8);
                    byTileYuv[ptr_index + 128] = (byte)(b & 255);
                    ptr_index += 1;
                    this.skipKbits((int)mDecode_Color_BitMapBits);
                }
            }
        }

        private void skipKbits(int mDecode_Color_BitMapBits)
        {
            updatereadbuf(mDecode_Color_BitMapBits);
        }
        private short getKbits(int k)
        {
            short signed_wordvalue = (short)(65535 & this.mCodebuf >>> 32 - k);
            if ((1 << k - 1 & signed_wordvalue) == 0)
                signed_wordvalue = (short)(signed_wordvalue + this.mNegPow2[k]);
            this.skipKbits(k);
            return signed_wordvalue;
        }

        private void VQ_ColorUpdate(int skip_bits)
        {
            for (int i = 0; i < skip_bits; i++)
            {
                mDecode_Color_Index[i] = (uint)(this.mCodebuf >> 29 & VQ_INDEX_MASK);
                if ((this.mCodebuf >> 31 & VQ_HEADER_MASK) == VQ_NO_UPDATE_HEADER)
                    this.updatereadbuf(VQ_NO_UPDATE_LENGTH);
                else
                {
                    mColorBuf[mDecode_Color_Index[i]] = (int)((this.mCodebuf >> 5) & VQ_COLOR_MASK);
                    this.updatereadbuf(VQ_UPDATE_LENGTH);
                }
            }
        }

        private void Decompress(int txb, int tyb, byte[] outBuf, int QT_TableSelection)
        {
            var byTileYuv = this.mTileYuv;
            var ptr_index = 0;
            var mGreyMode = this.mGreyMode;

            for (var i = 0; i < this.mDCTCoeff.Length; i++)
            {
                byTileYuv[i] = 0;
                byTileYuv[i + 384] = 0;
                this.mDCTCoeff[i] = 0;
            }

            if (mGreyMode == 0)
            {
                ptr_index = 0;
                this.process_Huffman_data_unit(this.mYDCnr, this.mYACnr, this.mDCY, ptr_index);
                this.mDCY = this.previous_DC;
                ptr_index += 64;
                if (this.mMode420 == 1)
                {
                    this.process_Huffman_data_unit(this.mYDCnr, this.mYACnr, this.mDCY, ptr_index);
                    this.mDCY = this.previous_DC;
                    ptr_index += 64;
                    this.process_Huffman_data_unit(this.mYDCnr, this.mYACnr, this.mDCY, ptr_index);
                    this.mDCY = this.previous_DC;
                    ptr_index += 64;
                    this.process_Huffman_data_unit(this.mYDCnr, this.mYACnr, this.mDCY, ptr_index);
                    this.mDCY = this.previous_DC;
                    ptr_index += 64;
                    this.process_Huffman_data_unit(this.mCbDCnr, this.mCbACnr, this.mDCCb, ptr_index);
                    this.mDCCb = this.previous_DC;
                    ptr_index += 64;
                    this.process_Huffman_data_unit(this.mCrDCnr, this.mCrACnr, this.mDCCr, ptr_index);
                    this.mDCCr = this.previous_DC;
                }
                else
                {
                    this.process_Huffman_data_unit(this.mCbDCnr, this.mCbACnr, this.mDCCb, ptr_index);
                    this.mDCCb = this.previous_DC;
                    ptr_index += 64;
                    this.process_Huffman_data_unit(this.mCrDCnr, this.mCrACnr, this.mDCCr, ptr_index);
                    this.mDCCr = this.previous_DC;
                }
            }
            else
            {
                // grey mode is never assigned
                ptr_index = 0;
                this.process_Huffman_data_unit(this.mYDCnr, this.mYACnr, this.mDCY, ptr_index);
                this.mDCY = this.previous_DC;
                ptr_index += 64;
                if (this.mMode420 == 1)
                {
                    this.process_Huffman_data_unit(this.mYDCnr, this.mYACnr, this.mDCY, ptr_index);
                    this.mDCY = this.previous_DC;
                    ptr_index += 64;
                    this.process_Huffman_data_unit(this.mYDCnr, this.mYACnr, this.mDCY, ptr_index);
                    this.mDCY = this.previous_DC;
                    ptr_index += 64;
                    this.process_Huffman_data_unit(this.mYDCnr, this.mYACnr, this.mDCY, ptr_index);
                    this.mDCY = this.previous_DC;
                    ptr_index += 64;
                    this.process_Huffman_data_unit(this.mCbDCnr, this.mCbACnr, this.mDCCb, ptr_index);
                    this.mDCCb = this.previous_DC;
                    ptr_index += 64;
                    this.process_Huffman_data_unit(this.mCrDCnr, this.mCrACnr, this.mDCCr, ptr_index);
                    this.mDCCr = this.previous_DC;
                }
                else
                {
                    this.process_Huffman_data_unit(this.mCbDCnr, this.mCbACnr, this.mDCCb, ptr_index);
                    this.mDCCb = this.previous_DC;
                    ptr_index += 64;
                    this.process_Huffman_data_unit(this.mCrDCnr, this.mCrACnr, this.mDCCr, ptr_index);
                    this.mDCCr = this.previous_DC;
                }
            }


            if (mGreyMode == 0)
            {
                byte[] ptr = byTileYuv;
                ptr_index = 0;
                IDCT_transform(this.mDCTCoeff, ptr, ptr_index, QT_TableSelection);
                ptr_index += 64;
                if (this.mMode420 == 1)
                {
                    IDCT_transform(this.mDCTCoeff, ptr, ptr_index, QT_TableSelection);
                    ptr_index += 64;
                    IDCT_transform(this.mDCTCoeff, ptr, ptr_index, QT_TableSelection);
                    ptr_index += 64;
                    IDCT_transform(this.mDCTCoeff, ptr, ptr_index, QT_TableSelection);
                    ptr_index += 64;
                    IDCT_transform(this.mDCTCoeff, ptr, ptr_index, QT_TableSelection + 1);
                    ptr_index += 64;
                    IDCT_transform(this.mDCTCoeff, ptr, ptr_index, QT_TableSelection + 1);
                }
                else
                {
                    IDCT_transform(this.mDCTCoeff, ptr, ptr_index, QT_TableSelection + 1);
                    ptr_index += 64;
                    IDCT_transform(this.mDCTCoeff, ptr, ptr_index, QT_TableSelection + 1);
                }
            }
            else
            {
                // grey mode is never assigned

                byte[] ptr = byTileYuv;
                ptr_index = 0;
                this.IDCT_transform(this.mDCTCoeff, ptr, ptr_index, QT_TableSelection);
                ptr_index += 64;
                if (this.mMode420 == 1)
                {
                    this.IDCT_transform(this.mDCTCoeff, ptr, ptr_index, QT_TableSelection);
                    ptr_index += 64;
                    this.IDCT_transform(this.mDCTCoeff, ptr, ptr_index, QT_TableSelection);
                    ptr_index += 64;
                    this.IDCT_transform(this.mDCTCoeff, ptr, ptr_index, QT_TableSelection + 1);
                    ptr_index += 64;
                    this.IDCT_transform(this.mDCTCoeff, ptr, ptr_index, QT_TableSelection + 1);
                    ptr_index += 64;
                    this.IDCT_transform(this.mDCTCoeff, ptr, ptr_index, QT_TableSelection + 1);
                }
                else
                {
                    this.IDCT_transform(this.mDCTCoeff, ptr, ptr_index, QT_TableSelection + 1);
                    ptr_index += 64;
                    this.IDCT_transform(this.mDCTCoeff, ptr, ptr_index, QT_TableSelection + 1);
                }
            }

            YUVToRGB(txb, tyb, outBuf);
        }
        private void YUVToRGB(int txb, int tyb, byte[] pBgr)
        {
            uint cb, cr, m, n;
            if (mMode420 == 0)
            {

            }
            else
            {
                int[] mPy420Index = new int[4];

                int pixel_x = txb << 4;
                int pixel_y = tyb << 4;
                uint pos = (uint)(pixel_y * this.mWidth + pixel_x);

                for (uint j = 0; j < 16; j++)
                {
                    for (uint i = 0; i < 16; i++)
                    {
                        var index = (j >> 3) * 2 + (i >> 3);

                        var off = GetmPy420Offset(index);

                        var y = mTileYuv[off + mPy420Index[index]++];

                        if (mGreyMode == 0)
                        {
                            m = (j >> 1 << 3) + (i >> 1);
                            cb = mTileYuv[256 + m]; // pcb420
                            cr = mTileYuv[320 + m]; // pcr420
                        }
                        else
                        {
                            cb = 128;
                            cr = 128;
                        }

                        n = pos + i;

                        // added by misha
                        if (((n << 2) + 2) >= pBgr.Length)
                        {
                            break;
                        }

                        pBgr[(n << 2) + 2] = (byte)this.mRlimitTable[256 + this.m_Y[y] + this.mCbToB[cb]];
                        pBgr[(n << 2) + 1] = (byte)this.mRlimitTable[256 + this.m_Y[y] + this.mCbToG[cb] + this.mCrToG[cr]];
                        pBgr[(n << 2) + 0] = (byte)this.mRlimitTable[256 + this.m_Y[y] + this.mCrToR[cr]];
                        pBgr[(n << 2) + 3] = 255;
                    }

                    pos += (uint)mWidth;
                }
            }
        }

        private int GetmPy420Offset(uint index)
        {
            switch (index)
            {
                case 0:
                    return 0;
                case 1:
                    return 64;
                case 2:
                    return 128;
                case 3:
                    return 192;
                default:
                    throw new NotImplementedException();
                    break;
            }
        }

        private void IDCT_transform(int[] coef, byte[] data, int index, int nBlock)
        {
            var FIX_1_082392200 = 277;
            var FIX_1_414213562 = 362;
            var FIX_1_847759065 = 473;
            var FIX_2_613125930 = 669;



            var inptr = coef;
            var wsptr = new int[64];// this.mWorkspace;
            int ctr, dcval;
            var quantptr = this.mQT[nBlock];
            var ptr_index = 0;

            int tmp0, tmp1, tmp2, tmp3, tmp4, tmp5, tmp6, tmp7;
            int tmp10, tmp11, tmp12, tmp13;
            int z5, z10, z11, z12, z13;
            for (ctr = 0; ctr < 8; ctr++)
            {
                if ((inptr[index + ctr + 8] | inptr[index + ctr + 16] | inptr[index + ctr + 24] | inptr[index + ctr + 32] | inptr[index + ctr + 40] | inptr[index + ctr + 48] | inptr[index + ctr + 56]) == 0)
                {
                    dcval = (int)(((int)(inptr[index + ctr] * quantptr[ctr]) >> 16));
                    wsptr[ctr] = dcval;
                    wsptr[ctr + 8] = dcval;
                    wsptr[ctr + 16] = dcval;
                    wsptr[ctr + 24] = dcval;
                    wsptr[ctr + 32] = dcval;
                    wsptr[ctr + 40] = dcval;
                    wsptr[ctr + 48] = dcval;
                    wsptr[ctr + 56] = dcval;
                    continue;
                }
                tmp0 = (int)(inptr[index + ctr] * quantptr[ctr]) >> 16;
                tmp1 = (int)(inptr[index + ctr + 16] * quantptr[ctr + 16]) >> 16;
                tmp2 = (int)(inptr[index + ctr + 32] * quantptr[ctr + 32]) >> 16;
                tmp3 = (int)(inptr[index + ctr + 48] * quantptr[ctr + 48]) >> 16;
                tmp10 = tmp0 + tmp2;
                tmp11 = tmp0 - tmp2;
                tmp13 = tmp1 + tmp3;
                tmp12 = this.MULTIPLY(tmp1 - tmp3, FIX_1_414213562) - tmp13;
                tmp0 = tmp10 + tmp13;
                tmp3 = tmp10 - tmp13;
                tmp1 = tmp11 + tmp12;
                tmp2 = tmp11 - tmp12;
                tmp4 = (int)(inptr[index + ctr + 8] * quantptr[ctr + 8]) >> 16;
                tmp5 = (int)(inptr[index + ctr + 24] * quantptr[ctr + 24]) >> 16;
                tmp6 = (int)(inptr[index + ctr + 40] * quantptr[ctr + 40]) >> 16;
                tmp7 = (int)(inptr[index + ctr + 56] * quantptr[ctr + 56]) >> 16;
                z13 = tmp6 + tmp5;
                z10 = tmp6 - tmp5;
                z11 = tmp4 + tmp7;
                z12 = tmp4 - tmp7;
                tmp7 = z11 + z13;
                tmp11 = this.MULTIPLY(z11 - z13, FIX_1_414213562);
                z5 = this.MULTIPLY(z10 + z12, FIX_1_847759065);
                tmp10 = this.MULTIPLY(z12, FIX_1_082392200) - z5;
                tmp12 = this.MULTIPLY(z10, -FIX_2_613125930) + z5;
                tmp6 = tmp12 - tmp7;
                tmp5 = tmp11 - tmp6;
                tmp4 = tmp10 + tmp5;
                wsptr[ctr] = (int)(tmp0 + tmp7);
                wsptr[ctr + 56] = (int)(tmp0 - tmp7);
                wsptr[ctr + 8] = (int)(tmp1 + tmp6);
                wsptr[ctr + 48] = (int)(tmp1 - tmp6);
                wsptr[ctr + 16] = (int)(tmp2 + tmp5);
                wsptr[ctr + 40] = (int)(tmp2 - tmp5);
                wsptr[ctr + 32] = (int)(tmp3 + tmp4);
                wsptr[ctr + 24] = (int)(tmp3 - tmp4);
            }


            var outptr = data;
            int outptr_index;
            for (ctr = 0; ctr < 8; ctr++)
            {
                outptr_index = ctr * 8;
                tmp10 = (wsptr[outptr_index + 0] + wsptr[outptr_index + 4]);
                tmp11 = (int)(wsptr[outptr_index + 0] - wsptr[outptr_index + 4]);
                tmp13 = (wsptr[outptr_index + 2] + wsptr[outptr_index + 6]);
                tmp12 = (int)MULTIPLY((int)(wsptr[outptr_index + 2] - wsptr[outptr_index + 6]), FIX_1_414213562) - tmp13;
                tmp0 = tmp10 + tmp13;
                tmp3 = tmp10 - tmp13;
                tmp1 = tmp11 + tmp12;
                tmp2 = tmp11 - tmp12;
                z13 = (int)(wsptr[outptr_index + 5] + wsptr[outptr_index + 3]);
                z10 = (int)(wsptr[outptr_index + 5] - wsptr[outptr_index + 3]);
                z11 = (int)(wsptr[outptr_index + 1] + wsptr[outptr_index + 7]);
                z12 = (int)(wsptr[outptr_index + 1] - wsptr[outptr_index + 7]);
                tmp7 = z11 + z13;
                tmp11 = (int)this.MULTIPLY((int)z11 - (int)z13, FIX_1_414213562);
                z5 = (int)this.MULTIPLY((int)z10 + (int)z12, FIX_1_847759065);
                tmp10 = (int)this.MULTIPLY((int)z12, FIX_1_082392200) - z5;
                tmp12 = (int)this.MULTIPLY((int)z10, -FIX_2_613125930) + z5;
                tmp6 = tmp12 - tmp7;
                tmp5 = tmp11 - tmp6;
                tmp4 = tmp10 + tmp5;

                outptr[index + outptr_index + 0] = (byte)this.mRlimitTable[384 + IDESCALE(tmp0 + tmp7, 3) & 1023];
                outptr[index + outptr_index + 7] = (byte)this.mRlimitTable[384 + IDESCALE(tmp0 - tmp7, 3) & 1023];
                outptr[index + outptr_index + 1] = (byte)this.mRlimitTable[384 + IDESCALE(tmp1 + tmp6, 3) & 1023];
                outptr[index + outptr_index + 6] = (byte)this.mRlimitTable[384 + IDESCALE(tmp1 - tmp6, 3) & 1023];
                outptr[index + outptr_index + 2] = (byte)this.mRlimitTable[384 + IDESCALE(tmp2 + tmp5, 3) & 1023];
                outptr[index + outptr_index + 5] = (byte)this.mRlimitTable[384 + IDESCALE(tmp2 - tmp5, 3) & 1023];
                outptr[index + outptr_index + 4] = (byte)this.mRlimitTable[384 + IDESCALE(tmp3 + tmp4, 3) & 1023];
                outptr[index + outptr_index + 3] = (byte)this.mRlimitTable[384 + IDESCALE(tmp3 - tmp4, 3) & 1023];
            }

        }

        private int MULTIPLY(int vari, int cons)
        {
            return (int)((vari * cons) >> 8);
        }
        private int IDESCALE(int x, uint n)
        {
            return (int)x >> (int)n;
        }

        private void process_Huffman_data_unit(int DC_nr, int AC_nr, int previous_DC, int position)
        {
            int nr, k;
            int size_val, count_0;
            ushort[] min_code;
            byte[] huff_values;
            int byte_temp;
            ushort tmp_Hcode;
            var HT_ref = this.HT_ref;
            var HTDC = HT_ref.HTDC;
            var HTAC = HT_ref.HTAC;


            min_code = HTDC[DC_nr].minor_code;
            huff_values = HTDC[DC_nr].V;
            nr = 0;
            k = HTDC[DC_nr].table_len[this.mCodebuf >>> 16];
            tmp_Hcode = (ushort)(65535 & this.mCodebuf >>> 32 - k);
            this.skipKbits(k);
            var x = tmp_Hcode - min_code[k];
            var index_to_huff = this.WORD_hi_lo((byte)k, (byte)x);
            size_val = huff_values[index_to_huff];
            if (size_val == 0)
            {
                this.mDCTCoeff[position + 0] = previous_DC;
                this.previous_DC = previous_DC;
            }
            else
            {
                this.mDCTCoeff[position + 0] = previous_DC + this.getKbits(size_val);
                this.previous_DC = this.mDCTCoeff[position + 0];
            }
            min_code = HTAC[AC_nr].minor_code;
            huff_values = HTAC[AC_nr].V;
            nr = 1;
            do
            {
                k = HTAC[AC_nr].table_len[65535 & this.mCodebuf >>> 16];
                tmp_Hcode = (ushort)(65535 & this.mCodebuf >>> 32 - k);
                this.skipKbits(k);
                byte_temp = huff_values[this.WORD_hi_lo((byte)k, (byte)(255 & tmp_Hcode - min_code[k]))];
                size_val = byte_temp & 15;
                count_0 = byte_temp >>> 4;
                if (size_val == 0)
                {
                    if (count_0 != 15)
                        break;
                    nr += 16;
                }
                else
                {
                    nr += count_0;
                    this.mDCTCoeff[position + dezigzag[nr++]] = this.getKbits(size_val);
                }
            } while (nr < 64);
        }

        private void updatereadbuf(int walks)
        {
            var newbits = this.mNewbits - walks;
            if (newbits <= 0)
            {
                int readbuf = this.GetQBytesFromBuffer(4);
                this.mCodebuf = this.mCodebuf << walks | (this.mNewbuf | readbuf >>> this.mNewbits) >>> 32 - walks;
                this.mNewbuf = readbuf << walks - this.mNewbits;
                this.mNewbits = 32 + newbits;
            }
            else
            {
                this.mCodebuf = (this.mCodebuf << walks) | this.mNewbuf >>> (32 - walks);
                this.mNewbuf = this.mNewbuf << walks;
                this.mNewbits = newbits;
            }
        }

        private void VQInitialize()
        {
            var index = 0;
            for (index = 0; index < 4; index++)
                decodeColorIndex[index] = index;
            mColorBuf[0] = 32896;
            mColorBuf[1] = 16744576;
            mColorBuf[2] = 8421504;
            mColorBuf[3] = 12615808;
        }

        private void InitJpgDecoding()
        {
            mCurBytePos = 0;
            this.load_quant_table(this.mQT[0]);
            this.load_quant_tableCb(this.mQT[1]);
            this.load_advance_quant_table(this.mQT[2]);
            this.load_advance_quant_tableCb(this.mQT[3]);
        }

        private void set_quant_table(int[] basic_table, int scale_factor, ref int[] newtable)
        {
            int i;
            int temp;
            for (i = 0; i < 64; i++)
            {
                temp = basic_table[i] * 16 / scale_factor;
                if (temp <= 0)
                    temp = 1;
                if (temp > 255)
                    temp = 255;
                newtable[zigzag[i]] = 255 & temp;
            }
        }
        private void load_quant_table(double[] quant_table)
        {
            int j, row, col;
            int[] tempQT = new int[64];
            switch (this.mYSelector)
            {
                case 0:
                    this.std_luminance_qt = Tbl_000Y;
                    break;
                case 1:
                    this.std_luminance_qt = Tbl_014Y;
                    break;
                case 2:
                    this.std_luminance_qt = Tbl_029Y;
                    break;
                case 3:
                    this.std_luminance_qt = Tbl_043Y;
                    break;
                case 4:
                    this.std_luminance_qt = Tbl_057Y;
                    break;
                case 5:
                    this.std_luminance_qt = Tbl_071Y;
                    break;
                case 6:
                    this.std_luminance_qt = Tbl_086Y;
                    break;
                case 7:
                    this.std_luminance_qt = Tbl_100Y;
                    break;
                case 8:
                    this.std_luminance_qt = Tbl_Q08Y;
                    break;
                case 9:
                    this.std_luminance_qt = Tbl_Q09Y;
                    break;
                case 10:
                    this.std_luminance_qt = Tbl_Q10Y;
                    break;
                case 11:
                    this.std_luminance_qt = Tbl_Q11Y;
                    break;
            }
            set_quant_table(this.std_luminance_qt, this.mScaleFactor, ref tempQT);

            for (j = 0; j <= 63; j++)
                quant_table[j] = tempQT[zigzag[j]];
            j = 0;
            for (row = 0; row <= 7; row++)
                for (col = 0; col <= 7; col++)
                {
                    quant_table[j] = quant_table[j] * scalefactor[row] * scalefactor[col] * 65536;
                    j++;
                }
            this.mCurBytePos += 64;
        }
        private void load_quant_tableCb(double[] quant_table)
        {
            int j, row, col;
            int[] tempQT = new int[64];
            if (this.mMapping == 1)
            {
                switch (this.mUVSelector)
                {
                    case 0:
                        std_chrominance_qt = Tbl_000Y;
                        break;
                    case 1:
                        std_chrominance_qt = Tbl_014Y;
                        break;
                    case 2:
                        std_chrominance_qt = Tbl_029Y;
                        break;
                    case 3:
                        std_chrominance_qt = Tbl_043Y;
                        break;
                    case 4:
                        std_chrominance_qt = Tbl_057Y;
                        break;
                    case 5:
                        std_chrominance_qt = Tbl_071Y;
                        break;
                    case 6:
                        std_chrominance_qt = Tbl_086Y;
                        break;
                    case 7:
                        std_chrominance_qt = Tbl_100Y;
                        break;
                }
            }
            else
            {
                switch (this.mUVSelector)
                {
                    case 0:
                        std_chrominance_qt = Tbl_000UV;
                        break;
                    case 1:
                        std_chrominance_qt = Tbl_014UV;
                        break;
                    case 2:
                        std_chrominance_qt = Tbl_029UV;
                        break;
                    case 3:
                        std_chrominance_qt = Tbl_043UV;
                        break;
                    case 4:
                        std_chrominance_qt = Tbl_057UV;
                        break;
                    case 5:
                        std_chrominance_qt = Tbl_071UV;
                        break;
                    case 6:
                        std_chrominance_qt = Tbl_086UV;
                        break;
                    case 7:
                        std_chrominance_qt = Tbl_100UV;
                        break;
                    case 8:
                        std_chrominance_qt = Tbl_Q08UV;
                        break;
                    case 9:
                        std_chrominance_qt = Tbl_Q09UV;
                        break;
                    case 10:
                        std_chrominance_qt = Tbl_Q10UV;
                        break;
                    case 11:
                        std_chrominance_qt = Tbl_Q11UV;
                        break;
                }
            }

            this.set_quant_table(this.std_chrominance_qt, this.mAdvanceScaleFactorUV, ref tempQT);
            for (j = 0; j <= 63; j++)
                quant_table[j] = tempQT[zigzag[j]];
            j = 0;
            for (row = 0; row <= 7; row++)
                for (col = 0; col <= 7; col++)
                {
                    quant_table[j] = quant_table[j] * scalefactor[row] * scalefactor[col] * 65536;
                    j++;
                }
            this.mCurBytePos += 64;
        }
        private void MoveBlockIndex()
        {
            if (this.mMode420 == 0)
            {
                this.mTxb++;
                if (this.mTxb >= this.mTmpWidth / 8)
                {
                    this.mTyb++;
                    if (this.mTyb >= this.mTmpHeight / 8)
                        this.mTyb = 0;
                    this.mTxb = 0;
                }
            }
            else
            {
                this.mTxb++;
                if (this.mTxb >= this.mTmpWidth / 16)
                {
                    this.mTyb++;
                    if (this.mTyb >= this.mTmpHeight / 16)
                        this.mTyb = 0;
                    this.mTxb = 0;
                }
            }
        }
        private void load_advance_quant_table(double[] quant_table)
        {
            int j, row, col;
            int[] tempQT = new int[64];
            switch (mAdvanceSelector)
            {
                case 0:
                    std_luminance_qt = Tbl_000Y;
                    break;
                case 1:
                    std_luminance_qt = Tbl_014Y;
                    break;
                case 2:
                    std_luminance_qt = Tbl_029Y;
                    break;
                case 3:
                    std_luminance_qt = Tbl_043Y;
                    break;
                case 4:
                    std_luminance_qt = Tbl_057Y;
                    break;
                case 5:
                    std_luminance_qt = Tbl_071Y;
                    break;
                case 6:
                    std_luminance_qt = Tbl_086Y;
                    break;
                case 7:
                    std_luminance_qt = Tbl_100Y;
                    break;
            }

            set_quant_table(std_luminance_qt, mAdvanceScaleFactor, ref tempQT);
            for (j = 0; j <= 63; j++)
                quant_table[j] = tempQT[zigzag[j]];
            j = 0;
            for (row = 0; row <= 7; row++)
                for (col = 0; col <= 7; col++)
                {
                    quant_table[j] = quant_table[j] * scalefactor[row] * scalefactor[col] * 65536;
                    j++;
                }
            this.mCurBytePos += 64;
        }
        private void load_advance_quant_tableCb(double[] quant_table)
        {
            int j, row, col;
            int[] tempQT = new int[64];
            if (this.mMapping == 1)
            {
                switch (this.mAdvanceSelector)
                {
                    case 0:
                        std_chrominance_qt = Tbl_000Y;
                        break;
                    case 1:
                        std_chrominance_qt = Tbl_014Y;
                        break;
                    case 2:
                        std_chrominance_qt = Tbl_029Y;
                        break;
                    case 3:
                        std_chrominance_qt = Tbl_043Y;
                        break;
                    case 4:
                        std_chrominance_qt = Tbl_057Y;
                        break;
                    case 5:
                        std_chrominance_qt = Tbl_071Y;
                        break;
                    case 6:
                        std_chrominance_qt = Tbl_086Y;
                        break;
                    case 7:
                        std_chrominance_qt = Tbl_100Y;
                        break;
                }
            }
            else
            {
                switch (this.mAdvanceSelector)
                {
                    case 0:
                        std_chrominance_qt = Tbl_000UV;
                        break;
                    case 1:
                        std_chrominance_qt = Tbl_014UV;
                        break;
                    case 2:
                        std_chrominance_qt = Tbl_029UV;
                        break;
                    case 3:
                        std_chrominance_qt = Tbl_043UV;
                        break;
                    case 4:
                        std_chrominance_qt = Tbl_057UV;
                        break;
                    case 5:
                        std_chrominance_qt = Tbl_071UV;
                        break;
                    case 6:
                        std_chrominance_qt = Tbl_086UV;
                        break;
                    case 7:
                        std_chrominance_qt = Tbl_100UV;
                        break;
                }
            }
            this.set_quant_table(std_chrominance_qt, this.mAdvanceScaleFactorUV, ref tempQT);
            for (j = 0; j <= 63; j++)
                quant_table[j] = tempQT[zigzag[j]];
            j = 0;
            for (row = 0; row <= 7; row++)
                for (col = 0; col <= 7; col++)
                {
                    quant_table[j] = quant_table[j] * scalefactor[row] * scalefactor[col] * 65536;
                    j++;
                }
            this.mCurBytePos += 64;
        }
        private void SetOptions()
        {
            mYSelector = buffer[0];
            mUVSelector = buffer[1];
            mYUVMode = buffer[2] * 256 + buffer[3];

            mNegPow2 = [0, -1, -3, -7, -15, -31, -63, -127, -255, -511, -1023, -2047, -4095, -8191, -16383, -32767];
            mYQnr = 0;
            mCbQnr = 1;
            mCrQnr = 1;
            mYDCnr = 0;
            mCbDCnr = 1;
            mCrDCnr = 1;
            mYACnr = 0;
            mCbACnr = 1;
            mCrACnr = 1;

            InitJpegTable();
            InitParameter();

            // for some reason initparameter overwrites these
            mTmpWidth = mWidth;
            mTmpHeight = mHeight;
            mYSelector = buffer[0];
            mUVSelector = buffer[1];
        }
        private void set_tmp_width_height(int mMode420, int width, int height, int mTmpWidth, int mTmpHeight)
        {
            if (mMode420 == 1)
            {
                if ((this.mWidth % 16) != 0)
                    this.mWidth = this.mWidth + 16 - this.mWidth % 16;
                if ((this.mHeight % 16) != 0)
                    this.mHeight = this.mHeight + 16 - this.mHeight % 16;
                this.mTwb = 16;
                this.mThb = 16;
            }
            else
            {
                if ((this.mWidth % 8) != 0)
                    this.mWidth = this.mWidth + 8 - this.mWidth % 8;
                if ((this.mHeight % 8) != 0)
                    this.mHeight = this.mHeight + 8 - this.mHeight % 8;
                this.mTwb = 8;
                this.mThw = 8;
            }
            if (mMode420 == 1)
            {
                if ((this.mTmpWidth % 16) != 0)
                    this.mTmpWidth = this.mTmpWidth + 16 - this.mTmpWidth % 16;
                if ((this.mTmpHeight % 16) != 0)
                    this.mTmpHeight = this.mTmpHeight + 16 - this.mTmpHeight % 16;
            }
            else
            {
                if ((this.mTmpWidth % 8) != 0)
                    this.mTmpWidth = this.mTmpWidth + 8 - this.mTmpWidth % 8;
                if ((this.mTmpHeight % 8) != 0)
                    this.mTmpHeight = this.mTmpHeight + 8 - this.mTmpHeight % 8;
            }
        }

        private void InitParameter()
        {
            if (this.mYUVMode == 422)
            {
                this.mYSelector = 4;
                this.mUVSelector = 7;
                this.mMapping = 0;
                this.mOldMode420 = 1;
                this.mMode420 = 1;
                this.mOldYSelector = 255;
            }
            else if (this.mYUVMode == 444)
            {
                this.mYSelector = 7;
                this.mUVSelector = 7;
                this.mMapping = 0;
                this.mOldMode420 = 0;
                this.mMode420 = 0;
                this.mOldYSelector = 255;
            }
            mTmpWidth = mWidth;
            mTmpHeight = mHeight;

            // todo is rfc key needed


            this.mScaleFactor = 16;
            this.mScaleFactorUV = 16;
            this.mAdvanceScaleFactor = 16;
            this.mAdvanceScaleFactorUV = 16;
            this.mAdvanceSelector = 7;
            this.mMapping = 0;
            this.mSharpModeSelection = 0;
        }


        private void prepare_range_limit_table()
        {
            ushort j = 0;
            for (j = 0; j < this.mRlimitTable_index; j++)
                this.mRlimitTable[j] = 0;
            for (j = 0; j < 256; j++)
                this.mRlimitTable[this.mRlimitTable_index + j] = j;
            for (j = 256; j < mRlimitTable.Length - mRlimitTable_index; j++)
                this.mRlimitTable[this.mRlimitTable_index + j] = 255;
            //for (j = 0; j < 384; j++)
            //    this.mRlimitTable[this.mRlimitTable_index + j + 640] = 0;
            //for (j = 0; j < 128; j++)
            //    this.mRlimitTable[this.mRlimitTable_index + j + 1024] = j;
        }

        private void InitJpegTable()
        {
            InitQT();
            if (!HT_init)
            {
                Init_Color_Table();
                this.prepare_range_limit_table();
                this.load_Huffman_table(HT_ref.HTDC[0], std_dc_luminance_nrcodes, std_dc_luminance_values, DC_LUMINANCE_HUFFMANCODE);
                this.load_Huffman_table(HT_ref.HTAC[0], std_ac_luminance_nrcodes, std_ac_luminance_values, AC_LUMINANCE_HUFFMANCODE);
                this.load_Huffman_table(HT_ref.HTDC[1], std_dc_chrominance_nrcodes, std_dc_chrominance_values, DC_CHROMINANCE_HUFFMANCODE);
                this.load_Huffman_table(HT_ref.HTAC[1], std_ac_chrominance_nrcodes, std_ac_chrominance_values, AC_CHROMINANCE_HUFFMANCODE);
                HT_init = true;
            }
        }
        private ushort WORD_hi_lo(byte byte_high, byte byte_low)
        {
            return (ushort)(byte_high + (byte_low << 8));
        }
        private void load_Huffman_table(Huffman_Table HT, int[] nrcode, int[] value, int[] Huff_code)
        {
            int k, j, i;
            int code_index;
            int code;
            for (j = 1; j <= 16; j++)
                HT.table_length[j] = (byte)nrcode[j];
            for (i = 0, k = 1; k <= 16; k++)
            {
                for (j = 0; j < HT.table_length[k]; j++)
                {
                    HT.V[this.WORD_hi_lo((byte)k, (byte)j)] = (byte)value[i];
                    i++;
                }
            }
            code = 0;
            for (k = 1; k <= 16; k++)
            {
                HT.minor_code[k] = (ushort)(65535 & code);
                for (j = 1; j <= HT.table_length[k]; j++)
                    code++;
                HT.major_code[k] = (ushort)(65535 & code - 1);
                code *= 2;
                if (HT.table_length[k] == 0)
                {
                    HT.minor_code[k] = 65535;
                    HT.major_code[k] = 0;
                }
            }
            HT.table_len[0] = 2;
            i = 2;
            for (code_index = 1; code_index < 65535; code_index++)
            {
                if (code_index < Huff_code[i])
                    HT.table_len[code_index] = (byte)(255 & Huff_code[i + 1]);
                else
                {
                    i = i + 2;
                    HT.table_len[code_index] = (byte)(255 & Huff_code[i + 1]);
                }
            }
        }

        private int FIX(double x)
        {
            var nScale = 1 << 16;
            var nHalf = nScale >> 1;
            double result = x * nScale + .5;
            var r = (int)result;
            return r;
        }
        private void Init_Color_Table()
        {
            int i, x;
            var nScale = 1 << 16;
            var nHalf = nScale >> 1;

            for (i = 0, x = -128; i < 256; i++, x++)
            {
                this.mCrToR[i] = (FIX(1.597656) * x + nHalf) >> 16;
                this.mCbToB[i] = (FIX(2.015625) * x + nHalf) >> 16;
                this.mCrToG[i] = (-FIX(.8125) * x + nHalf) >> 16;
                this.mCbToG[i] = (-FIX(.390625) * x + nHalf) >> 16;
            }

            for (i = 0, x = -16; i < 256; i++, x++)
                this.m_Y[i] = FIX(1.164) * x + nHalf >> 16;
        }

        private void InitQT()
        {
            // empty
        }
    }

    public class Ast2100HuffmanTable
    {
        public Huffman_Table[] HTDC = new Huffman_Table[4];
        public Huffman_Table[] HTAC = new Huffman_Table[4];

        public Ast2100HuffmanTable()
        {
            for (int i = 0; i < 4; i++)
            {
                HTDC[i] = new Huffman_Table();
                HTAC[i] = new Huffman_Table();
            }
        }
    }
    public class Huffman_Table
    {
        public byte[] table_length = new byte[256];
        public byte[] table_len = new byte[65536];
        public byte[] V = new byte[65536];
        public ushort[] minor_code = new ushort[17];
        public ushort[] major_code = new ushort[17];
    }
}