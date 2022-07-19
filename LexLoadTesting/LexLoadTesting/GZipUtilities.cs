using Amazon.LexRuntimeV2.Model;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LexLoadTesting
{
    public static class GZipUtilities
    {

        public static string[] unzipCompressedBase64Messages(string encodedBase64)
        {
            string jsonString = unzipCompressedBase64ToString(encodedBase64);
            List<Message> messages = JsonConvert.DeserializeObject<List<Message>>(jsonString);
            List<String> message = new List<string>();
            foreach (var item in messages)
            {
                message.Add(item.Content);
            }
            return message.ToArray();
        }

        public static string unzipCompressedBase64ToString(string encodedBase64)
        {
            if (encodedBase64 == null)
            {
                return null;
            }
            var compressStream = new MemoryStream(Convert.FromBase64String(encodedBase64));
            using GZipStream decompressor = new GZipStream(compressStream, CompressionMode.Decompress);
            var decompressedStream = new MemoryStream();
            decompressor.CopyTo(decompressedStream);
            string outputstring = Encoding.UTF8.GetString(decompressedStream.ToArray());
            return outputstring;

        }
    }
}
