using System.IO;
using System.Security.Cryptography;

namespace KVMClient.Core.IPMI.UDP
{
    internal class AesCBC128
    {
        public byte[] Encrypt(byte[] iv, byte[] key, byte[] plainText)
        {
            using (var aes = Aes.Create())
            {
                // Set up the algorithm
                aes.Padding = PaddingMode.None;
                aes.Mode = CipherMode.CBC;
                aes.Key = key;
                aes.BlockSize = 128; // AES-128
                                     // You don't specify an IV in your procedure, so we
                                     // need to zero it
                aes.IV = iv;

                // Create a memorystream to store the result
                using (var ms = new MemoryStream())
                {
                    // create an encryptor transform, and wrap the memorystream in a cryptostream
                    using (var transform = aes.CreateEncryptor())
                    using (var cs = new CryptoStream(ms, transform, CryptoStreamMode.Write))
                    {
                        // write the password bytes
                        cs.Write(plainText, 0, plainText.Length);
                    }

                    // get the encrypted bytes and format it as a hex string and then return it
                    return ms.ToArray();
                }
            }
        }

        public byte[] Decrypt(byte[] iv, byte[] key, byte[] encryptText)
        {
            using (var aes = Aes.Create())
            {
                // Set up the algorithm
                aes.Padding = PaddingMode.None;
                aes.Mode = CipherMode.CBC;
                aes.Key = key;
                aes.BlockSize = 128; // AES-128
                                     // You don't specify an IV in your procedure, so we
                                     // need to zero it
                aes.IV = iv;

                // Create a memorystream to store the result
                using (var ms = new MemoryStream())
                {
                    // create an encryptor transform, and wrap the memorystream in a cryptostream
                    using (var transform = aes.CreateDecryptor())
                    using (var cs = new CryptoStream(ms, transform, CryptoStreamMode.Write))
                    {
                        // write the password bytes
                        cs.Write(encryptText, 0, encryptText.Length);
                    }

                    // get the encrypted bytes and format it as a hex string and then return it
                    return ms.ToArray();
                }
            }
        }
    }
}