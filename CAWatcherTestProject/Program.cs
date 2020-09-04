using FileUtilities;
using System;
using System.Configuration;

namespace CAWatcherTestProject
{
    class Program
    {
        static void Main(string[] args)
        {

            IFileWatcher fileWatcher = null;

            try
            {
                fileWatcher = new FileWatcher($"App_Data{System.IO.Path.DirectorySeparatorChar}Files", "*.txt", OnFileChanged);
                fileWatcher.Start();

                Console.WriteLine("Нажмите 'q' для выхода из приложения.");
                while (Console.Read() != 'q') ;

                fileWatcher.Stop();
            }
            catch
            {
                fileWatcher?.SaveFileWatcherStateData();
            }
        }

        private static void OnFileChanged(string path, string fileName)
        {
            Console.WriteLine("path:{0}, file:{1}", path, fileName);
        }
    }
}