namespace PatchCore.Utility
{
    public interface IProgressTracker
    {
        void SetMessage(string message);
        void SetProgress(float progress);
    }
}
