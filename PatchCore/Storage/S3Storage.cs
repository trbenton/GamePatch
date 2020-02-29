using Amazon.S3;
using System.Collections.Generic;
using System.IO;
using Amazon.S3.Transfer;
using FileInfo = PatchCore.Files.FileInfo;

namespace PatchCore.Storage
{
    public class S3Storage : IStorage
    {
        private readonly AmazonS3Client _client;

        public S3Storage()
        {
            _client = new AmazonS3Client();
        }

        public void DownloadContent(string path, string name, string dest)
        {
            var transferUtility = new TransferUtility(_client);
            transferUtility.Download(dest, path, name);
        }

        public List<FileInfo> GetVersionHistory(string path)
        {
            var fileResults = new List<FileInfo>();
            var result = _client.ListObjects(path);
            if (result != null)
            {
                foreach (var obj in result.S3Objects)
                {
                    fileResults.Add(FileInfo.FromName(obj.Key));
                }
            }
            return fileResults;
        }

        public List<string> GetBaseFiles()
        {
            var files = new List<string>();
            var result = _client.ListBuckets();
            foreach (var bucket in result.Buckets)
            {
                files.Add(bucket.BucketName);
            }
            return files;
        }
    }
}
