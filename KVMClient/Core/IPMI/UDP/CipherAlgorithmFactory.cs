using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using static System.Collections.Specialized.BitVector32;

namespace KVMClient.Core.IPMI.UDP
{
    internal class CipherAlgorithmFactory
    {
        internal static RAKPAlgorithm createRAKPAlgorithm(int cipherSuite)
        {
            RAKPAlgorithm rakpAlgorithm = null;
            switch (cipherSuite)
            {
                case 0:
                    {
                        rakpAlgorithm = new RAKP_HMAC_NONE();
                        break;
                    }
                case 2:
                    {
                        rakpAlgorithm = new RAKP_HMAC_MD5();
                        break;
                    }
                case 1:
                    {
                        rakpAlgorithm = new RAKP_HMAC_SHA1();
                        break;
                    }
            }
            return rakpAlgorithm;
        }
    }

    public class Algorithm
    {
        public byte[] hexToBytes(String s)
        {
            byte[] ret = new byte[s.Length / 2];
            for (int i2 = 0; i2 < ret.Length; ++i2)
            {
                ret[i2] = (byte)int.Parse(s.Substring(i2 * 2, i2 * 2 + 2));
            }
            return ret;
        }
    }
    public abstract class RAKPAlgorithm : Algorithm
    {
        public abstract byte[] mac(byte[] key, byte[] plainText);
    }

    public class RAKP_HMAC_NONE : RAKPAlgorithm
    {
        public override byte[] mac(byte[] key, byte[] plainText)
        {
            return new byte[0];
        }
    }
    public class RAKP_HMAC_MD5 : RAKPAlgorithm
    {
        public override byte[] mac(byte[] key, byte[] plainText)
        {
            HMACMD5 hMACSHA1 = new HMACMD5(key);
            return hMACSHA1.ComputeHash(plainText);
        }
    }
    public class RAKP_HMAC_SHA1 : RAKPAlgorithm
    {
        public override byte[] mac(byte[] key, byte[] plainText)
        {
            HMACSHA1 hMACSHA1 = new HMACSHA1(key);
            return hMACSHA1.ComputeHash(plainText);
        }
    }

}
