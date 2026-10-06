using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;

namespace RSBot.Updater;

internal static class Program
{
    private static int Main(string[] args)
    {
        var directory = AppContext.BaseDirectory;
        try
        {
            if (args.Length < 2 || !int.TryParse(args[1], out var parentId) || parentId < 0)
                throw new ArgumentException("Expected installation directory and RSBot process ID.");
            directory = Path.GetFullPath(args[0]);
            RejectLinks(directory);
            if (!File.Exists(Path.Combine(directory, "RSBot.exe")))
                throw new IOException("The installation does not contain RSBot.exe.");
            if (parentId > 0)
                WaitForBot(parentId, directory);

            var temp = Path.Combine(directory, "update_temp");
            RejectLinks(temp);
            var stage = Path.Combine(temp, "extracted-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            // Framework extraction rejects archive paths that escape the staging directory.
            ZipFile.ExtractToDirectory(Path.Combine(temp, "update.zip"), stage);
            if (!File.Exists(Path.Combine(stage, "RSBot.exe")) ||
                !File.Exists(Path.Combine(stage, "RSBot.dll")) ||
                !File.Exists(Path.Combine(stage, "RSBot.runtimeconfig.json")) ||
                !File.Exists(Path.Combine(stage, "RSBot.Updater.exe")))
                throw new IOException("Invalid update package: expected application files at the ZIP root.");

            Apply(stage, directory, temp);
            Directory.Delete(stage, true);
            File.Delete(Path.Combine(temp, "update.zip"));
            if (args.Length < 3 || args[2] != "--no-restart")
                Process.Start(new ProcessStartInfo(Path.Combine(directory, "RSBot.exe"))
                {
                    WorkingDirectory = directory,
                    UseShellExecute = true
                });
            return 0;
        }
        catch (Exception ex)
        {
            var log = Path.Combine(directory, "updater_error.log");
            try
            {
                File.WriteAllText(log, $"{DateTime.UtcNow:O}\n{ex}\n");
                if (args.Length < 3 || args[2] != "--no-restart")
                    Process.Start(new ProcessStartInfo(log) { UseShellExecute = true });
            }
            catch { /* Preserve the original failure exit code even if logging fails. */ }
            return 1;
        }
    }

    private static void WaitForBot(int id, string directory)
    {
        Process process;
        try { process = Process.GetProcessById(id); }
        catch (ArgumentException) { return; } // The caller already exited.
        using (process)
        {
            if (process.HasExited)
                return;
            if (!string.Equals(process.MainModule?.FileName, Path.Combine(directory, "RSBot.exe"),
                StringComparison.OrdinalIgnoreCase))
                throw new IOException("The waiting process is not this RSBot installation.");
            if (!process.WaitForExit(60000))
                throw new IOException("RSBot is still running. Close it before retrying the update.");
        }
    }

    private static void RejectLinks(string path)
    {
        // Do not follow junctions/symlinks out of the installation or into user folders.
        for (var current = new DirectoryInfo(path); current != null; current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Update path contains a link: {current.FullName}");
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Update file is a link: {path}");
    }

    private static bool IsProtected(string relative)
    {
        var root = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return root.Equals("User", StringComparison.OrdinalIgnoreCase) ||
            root.Equals("Logs", StringComparison.OrdinalIgnoreCase) ||
            root.Equals("update_temp", StringComparison.OrdinalIgnoreCase) ||
            root.Equals("updater_error.log", StringComparison.OrdinalIgnoreCase);
    }

    private static void Apply(string stage, string directory, string temp)
    {
        var backup = Path.Combine(temp, "backup-" + Guid.NewGuid().ToString("N"));
        var files = new List<(string Source, string Target, string Backup, bool Existed)>();
        foreach (var source in Directory.GetFiles(stage, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(stage, source);
            if (IsProtected(relative))
                continue;
            var target = Path.Combine(directory, relative);
            RejectLinks(target);
            files.Add((source, target, Path.Combine(backup, relative), File.Exists(target)));
        }
        // Back up every existing file before changing anything in the installation.
        foreach (var file in files)
        {
            if (!file.Existed)
                continue;
            Directory.CreateDirectory(Path.GetDirectoryName(file.Backup)!);
            File.Copy(file.Target, file.Backup);
        }
        var changed = new List<(string Source, string Target, string Backup, bool Existed)>();
        try
        {
            foreach (var file in files)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file.Target)!);
                // Record before copying so even a partially written file can be restored.
                changed.Add(file);
                File.Copy(file.Source, file.Target, true);
            }
        }
        catch (Exception installError)
        {
            var errors = new List<Exception> { installError };
            changed.Reverse();
            foreach (var file in changed)
            {
                try
                {
                    if (file.Existed)
                        File.Copy(file.Backup, file.Target, true);
                    else if (File.Exists(file.Target))
                        File.Delete(file.Target);
                }
                catch (Exception rollbackError) { errors.Add(rollbackError); }
            }
            throw new AggregateException($"Update failed; rollback attempted. Backup: {backup}", errors);
        }
        // Keep the backup for manual recovery; do not remove user data or obsolete plugins.
    }
}
