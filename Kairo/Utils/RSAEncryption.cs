using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Kairo.Utils
{
    internal class RSAEncryption
    {   
        public RSAEncryption() {
            string publicKey, privateKey;
            using (var rsa = new RSACryptoServiceProvider(4096))
            {
                publicKey = rsa.ToXmlString(false); // Public key
                privateKey = rsa.ToXmlString(true); // Private key
            }

        }
        public static byte[] EncryptData(string dataToEncrypt, string publicKey)
        {
            byte[] encryptedData;
            using (var rsa = new RSACryptoServiceProvider(4096))
            {
                rsa.FromXmlString(publicKey);
                var dataToEncryptBytes = Encoding.UTF8.GetBytes(dataToEncrypt);
                encryptedData = rsa.Encrypt(dataToEncryptBytes, false);
            }
            return encryptedData;
        }

        public static string DecryptData(byte[] dataToDecrypt, string privateKey)
        {
            byte[] decryptedData;
            using (var rsa = new RSACryptoServiceProvider(4096))
            {
                rsa.FromXmlString(privateKey);
                decryptedData = rsa.Decrypt(dataToDecrypt, false);
            }
            return Encoding.UTF8.GetString(decryptedData);
        }
    }
}
