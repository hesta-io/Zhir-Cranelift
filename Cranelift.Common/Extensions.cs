using Cranelift.Common.Abstractions;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cranelift.Common
{
    public static class BlobStorageExtensions
    {
        public static async Task DownloadBlobs(this IBlobStorage storage, string prefix, string directory, CancellationToken cancellationToken)
        {
            await storage.DownloadBlobs(prefix, key =>
            {
                var path = Path.Combine(directory, key);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                return File.OpenWrite(path);
            }, cancellationToken);
        }

        public static async Task<bool> UploadBlob(
            this IBlobStorage storage,
            string key,
            string filePath,
            string contentType = null,
            CancellationToken cancellationToken = default)
        {
            if (contentType is null)
            {
                if (filePath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || filePath.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                {
                    contentType = "image/jpeg";
                }
                else if (filePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    contentType = "image/png";
                }
            }

            using (var stream = File.OpenRead(filePath))
            {
                return await storage.UploadBlob(key, stream, contentType, cancellationToken);
            }
        }
    }

    
}

namespace System.Linq
{
    public static class LinqExtensions
    {
        // https://www.extensionmethod.net/csharp/ienumerable/ienumerable-chunk
        public static IEnumerable<IEnumerable<T>> Chunk<T>(this IEnumerable<T> list, int chunkSize)
        {
            if (chunkSize <= 0)
            {
                throw new ArgumentException("chunkSize must be greater than 0.");
            }

            while (list.Any())
            {
                yield return list.Take(chunkSize);
                list = list.Skip(chunkSize);
            }
        }

    }
}

namespace System
{
    public static class StringExtensions
    {
        public static int CountWords(this string text)
        {
            var removedCharacters = new[] {
                '٠', '١', '٢', '٣', '٤', '٥', '٦', '٧', '٨', '٩',
                '0', '1', '2', '3', '4', '5', '6', '7', '8', '9',
                '-', '(', ')', '*', '&', '%', '$', '#', '@', '!',
            };

            foreach (var c in removedCharacters)
            {
                text = text.Replace(c.ToString(), "");
            }

            return text.Split(new[] { ' ', '\n', '\r', '.', '،', ',', '؛', ':', ';' }, StringSplitOptions.RemoveEmptyEntries)
                            .Count(t => t.Length >= 3);
        }
    }
}