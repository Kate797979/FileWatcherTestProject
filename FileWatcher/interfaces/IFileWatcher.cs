namespace FileUtilities
{
    public interface IFileWatcher
    {

        void Start();

        void Stop();

        void SaveFileWatcherStateData();

    }
}