using System;
using System.IO;
using System.Linq;

namespace RSBot.Core.Components;

/// <summary>
///     Deletes old log files so log folders do not grow forever.
/// </summary>
public static class FileRetention
{
    /// <summary>
    ///     Removes files older than <paramref name="keepDays" /> and the oldest files while the folder is above
    ///     <paramref name="maxFolderMB" />. Files still in use (e.g. by another bot instance) are skipped.
    /// </summary>
    /// <param name="directory">The folder, searched with its sub folders.</param>
    /// <param name="searchPattern">The files to consider, e.g. <c>*.txt</c>.</param>
    /// <param name="keepDays">The days to keep files, 0 to keep them regardless of age.</param>
    /// <param name="maxFolderMB">The folder size limit in megabytes.</param>
    public static void Cleanup(string directory, string searchPattern, int keepDays, int maxFolderMB)
    {
        if (!Directory.Exists(directory))
            return;

        FileInfo[] files;
        try
        {
            files = new DirectoryInfo(directory)
                .GetFiles(searchPattern, SearchOption.AllDirectories)
                .OrderBy(f => f.LastWriteTime)
                .ToArray();
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine($"Could not list old logs in {directory}: {e.Message}");
            return;
        }

        var maxBytes = (long)Math.Max(1, maxFolderMB) * 1024 * 1024;
        var expiry = keepDays > 0 ? DateTime.Now.AddDays(-keepDays) : DateTime.MinValue;
        var total = files.Sum(f => f.Length);

        foreach (var file in files)
        {
            if (file.LastWriteTime >= expiry && total <= maxBytes)
                break;

            var length = file.Length;

            try
            {
                file.Delete();
                total -= length;
            }
            catch
            {
                // still in use, e.g. by another bot instance
            }
        }
    }
}
