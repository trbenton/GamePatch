using PatchCore.Storage;
using PatchCore.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PatchCore.Files
{
    public class FileProcessor
    {
        private readonly string _root;
        private readonly IStorage _storage;
        private readonly ILogger _logger;
        private readonly IProgressTracker _progressTracker;
        private readonly INotifier _notifier;

        public FileProcessor(string root, IStorage storage, ILogger logger, IProgressTracker progressTracker, INotifier notifier)
        {
            _root = root;
            _storage = storage;
            _logger = logger;
            _progressTracker = progressTracker;
            _notifier = notifier;
        }

        public bool Process()
        {
            _progressTracker.SetMessage("Patching files...");
            _progressTracker.SetProgress(0.0f);

            bool succeeded = true;
            List<string> files;
            try
            {
                files = GetFiles();
            }
            catch (Exception)
            {
                _notifier.ShowNotification("Failed to load patch data. Ensure you are connected to the internet and try again.");
                _logger.LogMessage("Unable to complete patching.");
                return false;
            }

            int processed = 0;
            foreach (var file in files)
            {
                try
                {
                    ProcessFile(file);
                }
                catch (Exception)
                {
                    _logger.LogMessage($"Failed to validate file {file}.");
                    succeeded = false;
                }
                finally
                {
                    //just to be safe, try to cleanup temp directory...
                    CleanupTempDirectory();
                }
                _progressTracker.SetProgress((float)processed / files.Count);
            }

            return succeeded;
        }

        private void ProcessFile(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                _logger.LogMessage("Skipping invalid empty file.");
                return;
            }

            var shortName = GetShortName(path);
            var versions = _storage.GetVersionHistory(path);
            if (versions == null || versions.Count == 0)
            {
                _logger.LogMessage($"Invalid version history for file {shortName}.");
                throw new InvalidOperationException("Received a null or eempty response from get versions.");
            }

            //sort the versions
            versions = versions.OrderBy(v => v.Version).ToList();

            _logger.LogMessage($"Checking file {path} version.");

            var fullPath = Path.Combine(_root, Encoding.UTF8.GetString(Convert.FromBase64String(path)));
            if (WasDeleted(versions))
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(fullPath);
                        _logger.LogMessage($"Removing old file {shortName}.");
                    }
                }
                catch (Exception)
                {
                    _logger.LogMessage($"Unable to remove file {shortName}, ensure you have the correct permissions for this directory.");
                    _logger.LogMessage(Path.GetDirectoryName(fullPath));
                }
                return;
            }

            string localHash;
            try
            {
                localHash = CalculateFileHash(fullPath);
            }
            catch (Exception)
            {
                _logger.LogMessage($"Unable to calclulate hash for {shortName}, ensure you have the correct permissions for this directory.");
                _logger.LogMessage(Path.GetDirectoryName(fullPath));
                return;
            }

            if (!HasNewerVersion(versions, localHash))
            {
                return;
            }

            var currentVersion = GetCurrentVersion(versions, localHash);
            var latestBaseVersion = GetLatestBaseVersion(versions);
            if (currentVersion == null || latestBaseVersion.Version > currentVersion.Version)
            {
                //handle updating a new file to the latest
                var tempFilePath = FetchRemoteToTemp(path, latestBaseVersion);
                PatchToLatest(path, tempFilePath, fullPath, versions, latestBaseVersion);

            }
            else if (!HasLatestVersion(versions, currentVersion.Version))
            {
                //handle updating an existing local file...
                var tempFilePath = CopyFromLocalToTemp(fullPath);
                PatchToLatest(path, tempFilePath, fullPath, versions, currentVersion);
            }

            //do cleanup
            CleanupTempDirectory();
        }

        private void PatchToLatest(string path, string tempLocation, string finalLocation, List<FileInfo> versions, FileInfo currentInfo)
        {
            var upgradeVersion = GetUpgradeVersion(versions, currentInfo.Version);
            while (upgradeVersion != null)
            {
                //handle patch
                string patchPath = FetchRemoteToTemp(path, upgradeVersion);
                PatchFile(tempLocation, patchPath, upgradeVersion.Hash);
                
                //get next
                currentInfo = upgradeVersion;
                upgradeVersion = GetUpgradeVersion(versions, currentInfo.Version);
            }
            //copy from temp to final location
            File.Copy(tempLocation, finalLocation);
        }

        private void PatchFile(string filePath, string patchPath, string hash)
        {

        }

        private string CopyFromLocalToTemp(string fullPath)
        {
            var tempPath = Path.Combine(GetTempDirectory(), GetTempFileName());
            File.Copy(fullPath, tempPath, true);
            return tempPath;
        }

        private string FetchRemoteToTemp(string path, FileInfo info)
        {
            var tempPath = Path.Combine(GetTempDirectory(), GetTempFileName());
            _storage.DownloadContent(path, FileInfo.ToName(info), tempPath);
            return tempPath;
        }

        private bool HasNewerVersion(List<FileInfo> versions, string hash)
        {
            if (string.IsNullOrEmpty(hash))
            {
                return true;
            }

            var currentVersion = GetCurrentVersion(versions, hash);
            if (currentVersion == null)
            {
                return true;
            }

            var latestBaseVersion = GetLatestBaseVersion(versions);
            if (latestBaseVersion.Version > currentVersion.Version)
            {
                return true;
            }

            return GetLatestVersionNumber(versions) != currentVersion.Version;
        }

        private bool WasDeleted(List<FileInfo> versions)
        {
            return versions.Last().State == FileState.Deleted;
        }

        private FileInfo GetCurrentVersion(List<FileInfo> versions, string hash)
        {
            return versions.SingleOrDefault(v => v.Hash == hash);
        }

        private FileInfo GetLatestBaseVersion(List<FileInfo> versions)
        {
            return versions.Where(v => v.State == FileState.BaseVersion).OrderByDescending(v => v.Version).First();
        }

        private FileInfo GetUpgradeVersion(List<FileInfo> versions, int currentVersion)
        {
            var versionNumbers = versions.Select(v => v.Version).OrderBy(v => v).ToList();
            int index = versionNumbers.IndexOf(currentVersion);
            if (index < 0)
            {
                return null;
            }

            int newVersionIndex = index + 1;
            if (newVersionIndex < 0 || newVersionIndex > versionNumbers.Count - 1)
            {
                return null;
            }

            int versionNumber = versionNumbers[newVersionIndex];
            return versions.SingleOrDefault(v => v.Version == versionNumber);
        }

        private bool HasLatestVersion(List<FileInfo> versions, int currentVersion)
        {
            return GetUpgradeVersion(versions, currentVersion) == null;
        }

        private int GetLatestVersionNumber(List<FileInfo> versions)
        {
            return versions.Select(v => v.Version).OrderByDescending(v => v).First();
        }

        private void CleanupTempDirectory()
        {
            var tempDir = GetTempDirectory();
            try
            {
                var dirInfo = new DirectoryInfo(tempDir);
                //cleaup files
                foreach (var file in dirInfo.EnumerateFiles())
                {
                    file.Delete();
                }
                //cleanup directories
                foreach (var dir in dirInfo.EnumerateDirectories())
                {
                    dir.Delete(true);
                }
            }
            catch (Exception)
            {
                _logger.LogMessage("Unable to cleanup temp directory, ensure you have the correct permissions for this directory.");
                _logger.LogMessage(tempDir);
                return;
            }
        }

        private string GetTempDirectory()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Syndicant", "Patch");
        }

        private string GetTempFileName()
        {
            return $"{Guid.NewGuid().ToString().Replace("-", "")}.tmp";
        }

        private List<string> GetFiles()
        {
            var files = _storage.GetBaseFiles();
            if (files == null)
            {
                throw new InvalidOperationException("Received a null response from get base files.");
            }
            return files;
        }

        private string CalculateFileHash(string fullPath)
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

        private string GetShortName(string path)
        {
            var md5 = MD5.Create();
            var inputBytes = Encoding.UTF8.GetBytes(path);
            var hash = md5.ComputeHash(inputBytes);

            var sb = new StringBuilder();
            for (int i = 0; i < hash.Length; i++)
            {
                sb.Append(hash[i].ToString("X2"));
            }
            return sb.ToString();
        }

        private byte[] CalculateFileHash(Stream input, HashAlgorithm algorithm)
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
