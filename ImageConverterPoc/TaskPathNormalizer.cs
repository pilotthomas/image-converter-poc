using System.IO;

namespace ImageConverterPoc
{
    internal static class TaskPathNormalizer
    {
        /// <summary>
        /// Resolves task directory paths relative to the folder that contains the task XML (typically the build output directory).
        /// </summary>
        public static void Apply(ConversionTask20 task, string taskFileDirectory)
        {
            if (task == null || string.IsNullOrWhiteSpace(taskFileDirectory))
                return;

            task.SourcePath = Resolve(task.SourcePath, taskFileDirectory);
            task.DestinationPath = Resolve(task.DestinationPath, taskFileDirectory);
            if (!string.IsNullOrWhiteSpace(task.ArchivePath))
                task.ArchivePath = Resolve(task.ArchivePath, taskFileDirectory);
        }

        private static string Resolve(string path, string baseDirectory)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            path = path.Trim();
            if (Path.IsPathRooted(path))
                return Path.GetFullPath(path);

            return Path.GetFullPath(Path.Combine(baseDirectory, path));
        }
    }
}
