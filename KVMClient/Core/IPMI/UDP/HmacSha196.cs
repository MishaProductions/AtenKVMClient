using System;
using System.Security.Cryptography;

namespace KVMClient.Core.IPMI.UDP
{
    internal class HmacSha196
    {
        public HmacSha196()
        {
        }

        internal byte[] Encode(byte[] key, byte[] plainText)
        {
            HMACSHA1 hMACSHA1 = new HMACSHA1(key);
            byte[] r = hMACSHA1.ComputeHash(plainText);
            byte[] result = new byte[12];
            Array.Copy(r, 0, result, 0, 12);
            return result;
        }
    }
}