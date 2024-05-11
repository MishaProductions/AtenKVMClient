using System;

namespace KVMClient.Core.VNC
{
    /// <summary>
    /// Port of supermicro html ikvm viewer
    /// </summary>
    public partial class Ast2100Decoder
    {
        private int width;
        private int height;
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
        private int mMode420;
        private int mOldYSelector;
        private int mScaleFactor;
        private int mScaleFactorUV;
        private int mAdvanceSelector;
        private int mSharpModeSelection;
        private int mAdvanceScaleFactor;
        private int mAdvanceScaleFactorUV;
        private int mTxb;
        private int mTyb;
        private int mNewbits;
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

        private ushort[] mRlimitTable = new ushort[5 * 256 + 128];
        private int mRlimitTable_index = 256;
        private int[] mCrToR = new int[256 * 4];
        private int[] mCbToB = new int[256 * 4];
        private int[] mCrToG = new int[256 * 4];
        private int[] mCbToG = new int[256 * 4];
        private int[] m_Y = new int[256];
        private double[][] mQT = new double[4][];

        private int[] mColorBuf = new int[4 * 4];
        private int[] decodeColorIndex = new int[4];
        private int bitmapBits;
        private bool HT_init = false;
        private int mTmpWidth;
        private int mTmpHeight;
        private int[] mDCTCoeff = new int[384];
        private Ast2100HuffmanTable HT_ref = new Ast2100HuffmanTable();

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
                    result |= buffer[mIndex++] << 8 * i;
                }
                else
                {
                    result |= 0;
                }
            }
            return result;
        }
        private bool inRangeIncl(int x, int a, int b)
        {
            return x >= a && x <= b;
        }
        private byte[] mOutBuffer = new byte[0];
        public void Decode(byte[] data, int width, int height)
        {
            this.width = width;
            this.height = height;
            this.buffer = data;

            int MB = width * height / 64;
            if (MB < 4096)
                MB = 4096;

            SetOptions();
            VQInitialize();

            mOutBuffer = new byte[width * height];
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
                if ((mCodebuf >> 28 & BLOCK_HEADER_MASK) == JPEG_NO_SKIP_CODE)
                {
                    updatereadbuf(BLOCK_AST2100_START_LENGTH);
                    Decompress(this.mTxb, this.mTyb, this.mOutBuffer, 0);
                    MoveBlockIndex();

                    Console.WriteLine("JPEG_NO_SKIP_CODE");
                }
                else if ((mCodebuf >> 28 & BLOCK_HEADER_MASK) == FRAME_END_CODE)
                {
                    // end of frame
                    return;
                }
                else if ((mCodebuf >> 28 & BLOCK_HEADER_MASK) == JPEG_SKIP_CODE)
                {
                    Console.WriteLine("JPEG_SKIP_CODE");

                    updatereadbuf(BLOCK_AST2100_SKIP_LENGTH);
                    Decompress(this.mTxb, this.mTyb, this.mOutBuffer, 0);
                    MoveBlockIndex();
                }
                else if ((mCodebuf >> 28 & BLOCK_HEADER_MASK) == VQ_NO_SKIP_1_COLOR_CODE)
                {
                    Console.WriteLine("VQ_NO_SKIP_1_COLOR_CODE");

                    updatereadbuf(BLOCK_AST2100_START_LENGTH);
                    throw new NotImplementedException();
                }
                else if ((mCodebuf >> 28 & BLOCK_HEADER_MASK) == VQ_SKIP_1_COLOR_CODE)
                {
                    Console.WriteLine("VQ_SKIP_1_COLOR_CODE");

                    updatereadbuf(BLOCK_AST2100_SKIP_LENGTH);
                    throw new NotImplementedException();
                }
                else if ((mCodebuf >> 28 & BLOCK_HEADER_MASK) == VQ_NO_SKIP_2_COLOR_CODE)
                {
                    Console.WriteLine("VQ_NO_SKIP_2_COLOR_CODE");

                    updatereadbuf(BLOCK_AST2100_START_LENGTH);
                    throw new NotImplementedException();
                }
                else if ((mCodebuf >> 28 & BLOCK_HEADER_MASK) == VQ_SKIP_2_COLOR_CODE)
                {
                    Console.WriteLine("VQ_SKIP_2_COLOR_CODE");

                    updatereadbuf(BLOCK_AST2100_SKIP_LENGTH);
                    throw new NotImplementedException();
                }
                else if ((mCodebuf >> 28 & BLOCK_HEADER_MASK) == VQ_NO_SKIP_4_COLOR_CODE)
                {
                    Console.WriteLine("VQ_NO_SKIP_4_COLOR_CODE");

                    updatereadbuf(BLOCK_AST2100_START_LENGTH);
                    throw new NotImplementedException();
                }
                else if ((mCodebuf >> 28 & BLOCK_HEADER_MASK) == VQ_SKIP_4_COLOR_CODE)
                {
                    Console.WriteLine("VQ_SKIP_4_COLOR_CODE");

                    updatereadbuf(BLOCK_AST2100_SKIP_LENGTH);
                    throw new NotImplementedException();
                }
                else if ((mCodebuf >> 28 & BLOCK_HEADER_MASK) == LOW_JPEG_NO_SKIP_CODE)
                {
                    Console.WriteLine("LOW_JPEG_NO_SKIP_CODE");

                    this.updatereadbuf(BLOCK_AST2100_START_LENGTH);
                    this.Decompress(this.mTxb, this.mTyb, this.mOutBuffer, 2);
                    this.MoveBlockIndex();
                }
                else if ((mCodebuf >> 28 & BLOCK_HEADER_MASK) == LOW_JPEG_SKIP_CODE)
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
        }

        private void Decompress(int txb, int tyb, byte[] outBuf, int QT_TableSelection)
        {
            var ptr = 0;
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

            }
            else
            {
                // grey mode is never assigned
                throw new NotImplementedException();
            }


            if (mGreyMode == 0)
            {

            }
            else
            {
                // grey mode is never assigned
                throw new NotImplementedException();
            }

            // TODO:   this.YUVToRGB(txb, tyb, outBuf);
        }

        private void updatereadbuf(int walks)
        {
            var newbits = this.mNewbits - walks;
            if (newbits <= 0)
            {
                int readbuf = this.GetQBytesFromBuffer(4);
                this.mCodebuf = this.mCodebuf << walks | (this.mNewbuf | readbuf >> this.mNewbits) >>> 32 - walks;
                this.mNewbuf = readbuf << walks - this.mNewbits;
                this.mNewbits = 32 + newbits;
            }
            else
            {
                this.mCodebuf = this.mCodebuf << walks | this.mNewbuf >> 32 - walks;
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

        private void set_quant_table(int[] basic_table, int scale_factor, int[] newtable)
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
            set_quant_table(this.std_luminance_qt, this.mScaleFactor, tempQT);
        }
        private void load_quant_tableCb(double[] quant_table)
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

            this.set_quant_table(this.std_chrominance_qt, this.mAdvanceScaleFactorUV, tempQT);
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

            set_quant_table(std_luminance_qt, mAdvanceScaleFactor, tempQT);
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
            this.set_quant_table(std_chrominance_qt, this.mAdvanceScaleFactorUV, tempQT);
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
            mTmpWidth = width;
            mTmpHeight = height;

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
            for (j = 256; j < 640; j++)
                this.mRlimitTable[this.mRlimitTable_index + j] = 255;
            for (j = 0; j < 384; j++)
                this.mRlimitTable[this.mRlimitTable_index + j + 640] = 0;
            for (j = 0; j < 128; j++)
                this.mRlimitTable[this.mRlimitTable_index + j + 1024] = j;
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
        private int WORD_hi_lo(int byte_high, int byte_low)
        {
            return byte_high + (byte_low << 8);
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
                    HT.V[this.WORD_hi_lo(k, j)] = (byte)value[i];
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
            return (int)Math.Ceiling(result);
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
        public ushort[] minor_code = new ushort[17 * 2];
        public ushort[] major_code = new ushort[17 * 2];
    }
}