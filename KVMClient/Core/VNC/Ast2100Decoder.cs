using System.IO;
using System;
using BitStream;

namespace KVMClient.Core.VNC
{
    /// <summary>
    /// Port of https://github.com/kelleyk/noVNC/blob/bmc-support/core/ast2100/ast2100.js
    /// </summary>
    public partial class Ast2100Decoder
    {
        private int _mcuPosX = 0;
        private int _mcuPosY = 0;
        private int subsamplingMode = -1;
        private int[] _loadedQuantTables = new int[] { -1, -1 };
        private int[][] quantTables = new int[64][];
        private BitStream.BitStream? AstStream;

        public Ast2100Decoder()
        {
            for (int i = 0; i < quantTables.Length; i++)
            {
                quantTables[i] = new int[64];
            }
        }

        // Bakes in C(u)*C(v) and the cosine terms (from the IDCT formula).
        private void loadQuantTable(int slot, int[] srcTable)
        {
            for (var y = 0; y < 8; ++y)
            {
                for (var x = 0; x < 8; ++x)
                {
                    this.quantTables[slot][y * 8 + x] = ~~(int)(srcTable[y * 8 + x] * AAN_IDCT_SCALING_FACTORS[x] * AAN_IDCT_SCALING_FACTORS[y] * 65536.0);
                }
            }
        }

        private bool inRangeIncl(int x, int a, int b)
        {
            return x >= a && x <= b;
        }
        public void Decode(byte[] data)
        {
            _mcuPosX = 0;

            var quantTableSelectorLuma = data[0];  // 0 <= x <= 0xB
            var quantTableSelectorChroma = data[1];  // 0 <= x <= 0xB
            var subsamplingMode = data[2] << 8 | data[3];  // 422u or 444u

            bool settingsChanged = false;

            if (this.subsamplingMode != subsamplingMode)
            {
                Console.WriteLine("AST2100: new subsampling mode: " + subsamplingMode);
                this.subsamplingMode = subsamplingMode;
                settingsChanged = true;
            }


            if (quantTableSelectorLuma != _loadedQuantTables[0])
            {
                if (!inRangeIncl(quantTableSelectorLuma, 0, 0x0B))
                {
                    Console.WriteLine("AST2100: ERROR: Out-of-range selector for luma quant table: " + quantTableSelectorLuma);
                    throw new Exception("Out-of-range selector for luma quant table: " + quantTableSelectorLuma);
                }

                loadQuantTable(0, ATEN_QT_LUMA[quantTableSelectorLuma]);
                _loadedQuantTables[0] = quantTableSelectorLuma;
                settingsChanged = true;
            }

            if (quantTableSelectorChroma != this._loadedQuantTables[1])
            {
                if (!inRangeIncl(quantTableSelectorChroma, 0, 0xB))
                {
                    Console.WriteLine("AST2100: ERROR: Out-of-range selector for chroma quant table: " + quantTableSelectorChroma);
                    throw new Exception("Out-of-range selector for chroma quant table: " + quantTableSelectorChroma);
                }

                this.loadQuantTable(1, ATEN_QT_CHROMA[quantTableSelectorChroma]);
                this._loadedQuantTables[1] = quantTableSelectorChroma;
                settingsChanged = true;
            }

            if (this.subsamplingMode != 422 && this.subsamplingMode != 444)
            {
                Console.WriteLine("AST2100: ERROR: Out-of-range selector for chroma quant table: " + quantTableSelectorChroma);
                throw new Exception("Out-of-range selector for chroma quant table: " + quantTableSelectorChroma);
            }


            // The remainder of the stream is byte-swapped in four-byte chunks.
            AstStream = new BitStream.BitStream(new MemoryStream(data));
            AstStream.ReadByte();
            AstStream.ReadByte();
            AstStream.ReadByte();
            AstStream.ReadByte();

           // while (true)
            //{
                var controlFlag = Read4Bit() & 15; // read 4 bits

                if (controlFlag == 0 || controlFlag == 4 || controlFlag == 8 || controlFlag == 0xC)
                {
                    Console.WriteLine("AST2100: DCU Compressed data");
                    if (controlFlag == 8 || controlFlag == 0xC)
                    {
                        this._mcuPosX = AstStream.ReadByte();  // uint8z
                        this._mcuPosY = AstStream.ReadByte();  // uint8
                    }

                    if (controlFlag == 4 || controlFlag == 0xC)
                    {
                        Console.WriteLine("AST2100: ERROR: Unexpected control flag: alternate quant table");
                       // throw new Exception("AST2100: ERROR: Unexpected control flag: alternate quant table");

                    }
                }
                else if (inRangeIncl((int)controlFlag, 5, 7) || inRangeIncl((int)controlFlag, 0xD, 0xF))
                {
                    Console.WriteLine("AST2100: VQ-compressed data");
                }
                else if (controlFlag == 9)
                {
                    Console.WriteLine("AST2100: end of frame");
                  //  break;
                }
                else
                {
                    Console.WriteLine("AST2100: ERROR: Unexpected control flag: " + controlFlag);
                   // throw new Exception("AST2100: ERROR: Unexpected control flag: " + controlFlag);
                }
           // }
        }

        private byte Read4Bit()
        {
            byte val = 0;
            AstStream.ReadBits(out val, new BitNum(4));
            return val;
        }
    }
}