using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PatchCore.Files
{
    public class FileUtility
    {
        public static string CalculateHash(string str)
        {
            var md5 = MD5.Create();
            var inputBytes = Encoding.UTF8.GetBytes(str);
            var hash = md5.ComputeHash(inputBytes);

            var sb = new StringBuilder();
            for (int i = 0; i < hash.Length; i++)
            {
                sb.Append(hash[i].ToString("X2"));
            }
            return sb.ToString();
        }

        public static string CalculateFileHash(string fullPath)
        {
            using (var inputStream = new FileStream(fullPath, FileMode.Open))
            {
                using (var md5 = MD5.Create())
                {
                    var md5Hash = CalculateFileHash(inputStream, md5);
                    return string.Join(string.Empty, md5Hash.Select(b => b.ToString("X2")));
                }
            }
        }

        private static byte[] CalculateFileHash(Stream input, HashAlgorithm algorithm)
        {
            const int bufferSize = ushort.MaxValue;

            byte[] buffer = new byte[bufferSize];
            int readCount;
            while ((readCount = input.Read(buffer, 0, bufferSize)) > 0)
            {
                algorithm.TransformBlock(buffer, 0, readCount, buffer, 0);
            }

            algorithm.TransformFinalBlock(buffer, 0, readCount);
            return algorithm.Hash;
        }
    }
}
