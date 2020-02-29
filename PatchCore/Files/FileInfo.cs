namespace PatchCore.Files
{
    public class FileInfo
    {
        public int Version { get; }
        public FileState State { get; }
        public long Timestamp { get; }
        public string Hash { get; }

        public FileInfo(int version, FileState state, long timestamp, string hash)
        {
            Version = version;
            State = state;
            Timestamp = timestamp;
            Hash = hash;
        }

        public static FileInfo FromName(string path)
        {
            var parts = path.Split('_');
            return new FileInfo(int.Parse(parts[0]), (FileState)int.Parse(parts[1]), long.Parse(parts[2]), parts[3]);
        }

        public static string ToName(FileInfo info)
        {
            return $"{info.Version}_{info.State}_{info.Timestamp}_{info.Hash}";
        }
    }
}
