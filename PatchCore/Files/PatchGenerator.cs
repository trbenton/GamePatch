using PatchCore.Storage;
using PatchCore.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Octodiff.Core;
using Octodiff.Diagnostics;
using FileStream = System.IO.FileStream;

namespace PatchCore.Files
{
    public class PatchGenerator
    {
        private readonly string _newBinaryPath;
        private readonly IStorage _storage;
        private readonly ILogger _logger;
        private readonly IProgressTracker _progressTracker;
        private readonly INotifier _notifier;

        public PatchGenerator(string newBinaryPath, IStorage storage, ILogger logger, IProgressTracker progressTracker, INotifier notifier)
        {
            _newBinaryPath = newBinaryPath;
            _storage = storage;
            _logger = logger;
            _progressTracker = progressTracker;
            _notifier = notifier;
        }

        public void Generate()
        {
            //use FileProcessor to generate latest binary from remote...
            var oldBinaryPath = Path.Combine(Environment.CurrentDirectory, "Old");
            if (Directory.Exists(oldBinaryPath))
            {
                RemoveDirectory(oldBinaryPath);
                Directory.Delete(oldBinaryPath);
            }
            Directory.CreateDirectory(oldBinaryPath);

            var processor = new FileProcessor(oldBinaryPath, _storage, _logger, _progressTracker, _notifier);
            processor.Process();

            //create patch dir
            var newPatchPath = Path.Combine(Environment.CurrentDirectory, "Patch");
            if (Directory.Exists(newPatchPath))
            {
                RemoveDirectory(newPatchPath);
                Directory.Delete(newPatchPath);
            }
            Directory.CreateDirectory(newPatchPath);

            //from new provided files iterate over each one
            var dirInfo = new DirectoryInfo(_newBinaryPath);
            var processedFiles = new HashSet<string>();
            foreach (var file in dirInfo.EnumerateFiles())
            {
                var fullNewPath = Path.Combine(file.DirectoryName, file.Name);
                var relativePath = fullNewPath.Replace(_newBinaryPath, "");
                var fullOldPath = Path.Combine(oldBinaryPath, relativePath);
                var remotePath = Convert.ToBase64String(Encoding.UTF8.GetBytes(relativePath));
                CalculateDiff(newPatchPath, fullNewPath, fullOldPath, remotePath);
                processedFiles.Add(remotePath);
            }

            //check remote for files that weren't deleted yet remotely, but were deleted locally...
            var remoteFiles = _storage.GetBaseFiles();
            foreach (var remotePath in remoteFiles)
            {
                if (!processedFiles.Contains(remotePath))
                {
                    ProcessDeletedVersion(remotePath, newPatchPath);
                }
            }
        }

        private void CalculateDiff(string newPatchPath, string fullNewPath, string fullOldPath, string remotePath)
        {
            //output diffs to a new directory
            var versions = _storage.GetVersionHistory(remotePath);
            if (versions == null || versions.Count == 0)
            {
                ProcessBaseVersion(0, remotePath, newPatchPath, fullNewPath);
                return;
            }

            //sort the versions
            versions = versions.OrderBy(v => v.Version).ToList();
            int version = versions.Max(v => v.Version) + 1;
            if (WasLastVersionDeleted(versions))
            {
                ProcessBaseVersion(version, remotePath, newPatchPath, fullNewPath);
                return;
            }

            ProcessUpdatedVersion(version, remotePath, newPatchPath, fullNewPath, fullOldPath);
        }

        private void ProcessBaseVersion(int version, string remotePath, string newPatchPath, string fullNewPath)
        {
            var fileName = $"{version}_{FileState.BaseVersion}_{DateTime.UtcNow.Ticks}_{FileUtility.CalculateFileHash(fullNewPath)}";
            var finalDestPath = Path.Combine(newPatchPath, remotePath, fileName);
            File.Copy(fullNewPath, finalDestPath);
        }

        private void ProcessUpdatedVersion(int version, string remotePath, string newPatchPath, string fullNewPath, string fullOldPath)
        {
            var fileName = $"{version}_{FileState.BaseVersion}_{DateTime.UtcNow.Ticks}_{FileUtility.CalculateFileHash(fullNewPath)}";
            var finalDestPath = Path.Combine(newPatchPath, remotePath, fileName);
            CreatePatchFile(fullNewPath, fullOldPath, finalDestPath);
        }

        private void ProcessDeletedVersion(string remotePath, string newPatchPath)
        {
            var versions = _storage.GetVersionHistory(remotePath);
            int version = versions.Max(v => v.Version) + 1;
            var fileName = $"{version}_{FileState.BaseVersion}_{DateTime.UtcNow.Ticks}_{FileUtility.CalculateFileHash(string.Empty)}";
            var finalDestPath = Path.Combine(newPatchPath, remotePath, fileName);
            File.Create(finalDestPath);
        }

        private void CreatePatchFile(string fullNewPath, string fullOldPath, string dest)
        {
            //create signature file
            var sigFilePath = Path.GetTempFileName();
            var signatureBuilder = new SignatureBuilder();
            using (var fileStream = new FileStream(fullOldPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                using (var signatureStream = new FileStream(sigFilePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
                {
                    signatureBuilder.Build(fileStream, new SignatureWriter(signatureStream));
                }
            }

            //create delta
            var deltaBuilder = new DeltaBuilder();
            using (var newFileStream = new FileStream(fullNewPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                using (var signatureFileStream = new FileStream(sigFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    using (var deltaStream = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        deltaBuilder.BuildDelta(newFileStream, new SignatureReader(signatureFileStream, new NullProgressReporter()), new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)));
                    }
                }
            }

            //cleanup sig file
            File.Delete(sigFilePath);
        }

        private bool WasLastVersionDeleted(List<FileInfo> versions)
        {
            return versions.Last().State == FileState.Deleted;
        }

        private void RemoveDirectory(string path)
        {
            var dirInfo = new DirectoryInfo(path);
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
    }
}
