using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace FileUtilities
{
    public class FileWatcher : IFileWatcher
    {
        private const string STATE_DATA_FILE_NAME = "FileWatcherStateData.json";

        private string path;
        private string filter;

        private readonly Action<string, string> onFileChanged;

        private DirectoryInfo appDirectory;
        private DirectoryInfo fileWatchDirectory;

        private FileSystemWatcher fileWatcher;
        private FileSystemWatcher pathWatcher;

        private readonly object watcherLockObject = new object();
        private readonly static object staticStateSaveLockObject = new object();

        public FileWatcher(string path, string filter, Action<string, string> onFileChanged)
        {
            this.path = path;
            this.filter = filter;
            this.onFileChanged = onFileChanged;

            appDirectory = new DirectoryInfo(Directory.GetCurrentDirectory());
            fileWatchDirectory = new DirectoryInfo(path);
        }

        public void Start()
        {
            if (File.Exists(STATE_DATA_FILE_NAME))
            {
                var stateData = JsonSerializer.Deserialize<FileWatcherStateData>(File.ReadAllText(STATE_DATA_FILE_NAME));
                var pathFulterData = stateData.GetPathFilterData(path, filter);
                if (pathFulterData != null)
                    CheckForChanges(pathFulterData);
            }

            pathWatcher = new FileSystemWatcher(appDirectory.FullName)
            {
                IncludeSubdirectories = true
            };

            if (fileWatchDirectory.Exists)
            {
                pathWatcher.Deleted += OnPathDeleted;
                pathWatcher.Renamed += OnPathRenamed;

                CreateFileWatcher();
            }
            else
            {
                pathWatcher.Created += OnPathCreated;
                pathWatcher.Renamed += OnPathRenamed;
            }

            pathWatcher.EnableRaisingEvents = true;
        }

        private void CheckForChanges(FileWatcherPathFilterStateData stateData)
        {
            //Директорию удалили
            if (stateData.PathExists && !fileWatchDirectory.Exists)
            {
                onFileChanged?.Invoke(path, fileWatchDirectory.Name);
                return;
            }
            //

            //Директорию создали
            if (!stateData.PathExists && fileWatchDirectory.Exists)
            {
                onFileChanged?.Invoke(path, fileWatchDirectory.Name);
                return;
            }
            //

            if (stateData.PathExists && fileWatchDirectory.Exists)
            {
                //Директорию удаляли-создавали, когда приложение не работало
                if (fileWatchDirectory.CreationTime > stateData.LastWatchDateTame)
                    onFileChanged?.Invoke(path, fileWatchDirectory.Name);
                //

                //Изменённые и созданные файлы
                var changedFiles = new List<FileInfo>(fileWatchDirectory.EnumerateFiles(filter).
                            Where<FileInfo>(fileInfo => fileInfo.LastWriteTime > stateData.LastWatchDateTame));
                foreach (FileInfo fileInfo in changedFiles)
                    onFileChanged?.Invoke(path, fileInfo.Name);
                //

                //Удалённые файлы
                var allFiles = new List<string>(fileWatchDirectory.EnumerateFiles(filter).Select<FileInfo, string>(file => file.Name));
                var deletedFiles = stateData.Files.Where<string>(fileName => !allFiles.Contains(fileName));
                foreach (string fileName in deletedFiles)
                    onFileChanged?.Invoke(path, fileName);
                //
            }
        }

        private void CreateFileWatcher()
        {
            if (fileWatcher != null)
                fileWatcher.EnableRaisingEvents = false;

            fileWatcher = new FileSystemWatcher(fileWatchDirectory.FullName, filter)
            {
                NotifyFilter = NotifyFilters.LastWrite
                             | NotifyFilters.CreationTime
                             | NotifyFilters.FileName
            };

            fileWatcher.Changed += OnChanged;
            fileWatcher.Deleted += OnChanged;
            fileWatcher.Created += OnChanged;
            fileWatcher.Renamed += OnFileRenamed;

            fileWatcher.EnableRaisingEvents = true;
        }

        private void OnPathCreated(object sender, FileSystemEventArgs e)
        {
            if (fileWatchDirectory.FullName.Equals(e.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                lock (watcherLockObject)
                {
                    pathWatcher.EnableRaisingEvents = false;

                    pathWatcher.Deleted += OnPathDeleted;
                    pathWatcher.Created -= OnPathCreated;

                    pathWatcher.EnableRaisingEvents = true;

                    CreateFileWatcher();

                    onFileChanged?.Invoke(path, e.Name);
                }
            }
        }

        private void OnPathRenamed(object sender, RenamedEventArgs e)
        {
            if (fileWatchDirectory.FullName.Equals(e.OldFullPath, StringComparison.OrdinalIgnoreCase))
            {
                onFileChanged?.Invoke(path, e.OldName);
                fileWatcher.EnableRaisingEvents = false;
            }
            else
                OnPathCreated(sender, e);
        }

        private void OnPathDeleted(object sender, FileSystemEventArgs e)
        {
            if (!Directory.Exists(fileWatchDirectory.FullName))
            {
                lock (watcherLockObject)
                {
                    if (fileWatcher != null)
                        fileWatcher.EnableRaisingEvents = false;

                    pathWatcher.EnableRaisingEvents = false;

                    pathWatcher.Deleted -= OnPathDeleted;
                    pathWatcher.Created += OnPathCreated;

                    pathWatcher.EnableRaisingEvents = true;

                    onFileChanged?.Invoke(path, e.Name);
                }
            }
        }

        private void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            onFileChanged?.Invoke(path, e.OldName);
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            onFileChanged?.Invoke(path, e.Name);
        }

        public void Stop()
        {
            fileWatcher?.Dispose();
            fileWatcher = null;

            pathWatcher?.Dispose();
            pathWatcher = null;

            SaveFileWatcherStateData();
        }

        public void SaveFileWatcherStateData()
        {
            lock (staticStateSaveLockObject)
            {
                DateTime lastWatchDateTame = DateTime.Now;

                fileWatchDirectory.Refresh();

                List<string> files = fileWatchDirectory.Exists
                                    ? new List<string>(fileWatchDirectory.EnumerateFiles(filter).Select<FileInfo, string>(file => file.Name))
                                    : new List<string>();

                var stateData = new FileWatcherPathFilterStateData(path, filter)
                {
                    LastWatchDateTame = lastWatchDateTame,
                    Files = files,
                    PathExists = fileWatchDirectory.Exists
                };

                //
                FileWatcherStateData savedStateData = null;
                if (File.Exists(STATE_DATA_FILE_NAME))
                    savedStateData = JsonSerializer.Deserialize<FileWatcherStateData>(File.ReadAllText(STATE_DATA_FILE_NAME));
                else
                    savedStateData = new FileWatcherStateData();

                savedStateData.SetPathFilterData(stateData);
                //

                //
                byte[] jsonUtf8Bytes = JsonSerializer.SerializeToUtf8Bytes(savedStateData, new JsonSerializerOptions { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All), WriteIndented = true });
                File.WriteAllBytes(STATE_DATA_FILE_NAME, jsonUtf8Bytes);
                //
            }
        }
    }


    class FileWatcherStateData
    {

        public IList<FileWatcherPathFilterStateData> PathFilterStateData { get; set; }

        public FileWatcherStateData() { }

        public FileWatcherPathFilterStateData GetPathFilterData(string path, string filter)
        {
            for (int i = 0; i < PathFilterStateData.Count; i++)
            {
                var pathFilterData = PathFilterStateData[i];
                if (pathFilterData.Path.Equals(path, StringComparison.OrdinalIgnoreCase) && pathFilterData.Filter == filter)
                    return pathFilterData;
            }

            return null;
        }

        public void SetPathFilterData(FileWatcherPathFilterStateData data)
        {
            if (PathFilterStateData != null)
            {
                for (int i = 0; i < PathFilterStateData.Count; i++)
                {
                    var oldPathFilterData = PathFilterStateData[i];
                    if (oldPathFilterData.Path.Equals(data.Path, StringComparison.OrdinalIgnoreCase) && oldPathFilterData.Filter == data.Filter)
                    {
                        PathFilterStateData.RemoveAt(i);
                        break;
                    }
                }
            }
            else
                PathFilterStateData = new List<FileWatcherPathFilterStateData>();

            PathFilterStateData.Add(data);
        }

    }

    class FileWatcherPathFilterStateData
    {

        public string Path { get; set; }

        public string Filter { get; set; }

        public DateTime LastWatchDateTame { get; set; }

        public IList<string> Files { get; set; }

        public bool PathExists { get; set; }

        public FileWatcherPathFilterStateData() { }

        public FileWatcherPathFilterStateData(string path, string filter)
        {
            Path = path;
            Filter = filter;
        }

    }
}
