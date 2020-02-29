using System.Collections.Generic;
using FileInfo = PatchCore.Files.FileInfo;

namespace PatchCore.Storage
{
    public interface IStorage
    {
        void DownloadContent(string path, string hash, string dest);
        List<FileInfo> GetVersionHistory(string path);
        List<string> GetBaseFiles();
    }
}
