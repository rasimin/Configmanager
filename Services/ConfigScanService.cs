using System.IO;
using ConfigManager.Models;

namespace ConfigManager.Services;

public interface IConfigScanService
{
    Task<List<ConnectionStringEntry>> ScanPathsAsync(
        IEnumerable<string> paths,
        bool recursive = true,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    Task<SaveBatchResult> SaveBatchAsync(
        IEnumerable<ConnectionStringEntry> entriesToSave,
        CancellationToken cancellationToken = default);
}

public class ConfigScanService : IConfigScanService
{
    private readonly IConfigParserService _parserService;

    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".git", ".vs", ".idea", "node_modules", "dist", "build", "TestResults", "packages", ".dotnet"
    };

    public ConfigScanService(IConfigParserService parserService)
    {
        _parserService = parserService;
    }

    public Task<List<ConnectionStringEntry>> ScanPathsAsync(
        IEnumerable<string> paths,
        bool recursive = true,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var discoveredFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var rawPath in paths)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var cleanPath = rawPath?.Trim('"', ' ', '\t', '\r', '\n');
                if (string.IsNullOrWhiteSpace(cleanPath))
                    continue;

                progress?.Report($"Inspecting: {cleanPath}");

                if (File.Exists(cleanPath))
                {
                    if (IsTargetConfigFile(cleanPath))
                    {
                        discoveredFiles.Add(Path.GetFullPath(cleanPath));
                    }
                }
                else if (Directory.Exists(cleanPath))
                {
                    ScanDirectory(cleanPath, recursive, discoveredFiles, progress, cancellationToken);
                }
            }

            var allEntries = new List<ConnectionStringEntry>();
            int processedCount = 0;

            foreach (var file in discoveredFiles)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                processedCount++;
                progress?.Report($"Parsing ({processedCount}/{discoveredFiles.Count}): {Path.GetFileName(file)}");

                try
                {
                    var fileEntries = _parserService.ParseFile(file);
                    allEntries.AddRange(fileEntries);
                }
                catch (Exception ex)
                {
                    allEntries.Add(new ConnectionStringEntry
                    {
                        FilePath = file,
                        FileType = file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                            ? ConfigFileType.AppSettingsJson
                            : ConfigFileType.WebConfig,
                        KeyName = "[Read Error]",
                        Status = ConfigEntryStatus.Error,
                        ErrorMessage = ex.Message,
                        IsSelected = false
                    });
                }
            }

            progress?.Report($"Scan finished. Found {allEntries.Count} connection string(s) across {discoveredFiles.Count} file(s).");
            return allEntries;
        }, cancellationToken);
    }

    private void ScanDirectory(
        string dirPath,
        bool recursive,
        HashSet<string> discoveredFiles,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var stack = new Stack<string>();
        stack.Push(dirPath);

        while (stack.Count > 0)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var currentDir = stack.Pop();
            var dirName = Path.GetFileName(currentDir);

            if (IgnoredDirectoryNames.Contains(dirName))
                continue;

            // Search files in current directory
            try
            {
                var files = Directory.EnumerateFiles(currentDir, "*.*", SearchOption.TopDirectoryOnly);
                foreach (var file in files)
                {
                    if (IsTargetConfigFile(file))
                    {
                        discoveredFiles.Add(Path.GetFullPath(file));
                    }
                }
            }
            catch (Exception)
            {
                // Silently skip inaccessible directories
            }

            // Recurse into subdirectories if requested
            if (recursive)
            {
                try
                {
                    var subDirs = Directory.EnumerateDirectories(currentDir, "*", SearchOption.TopDirectoryOnly);
                    foreach (var subDir in subDirs)
                    {
                        var subName = Path.GetFileName(subDir);
                        if (!IgnoredDirectoryNames.Contains(subName))
                        {
                            stack.Push(subDir);
                        }
                    }
                }
                catch (Exception)
                {
                    // Silently skip inaccessible directories
                }
            }
        }
    }

    private static bool IsTargetConfigFile(string path)
    {
        var name = Path.GetFileName(path);
        if (name.Equals("web.config", StringComparison.OrdinalIgnoreCase))
            return true;

        if (name.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase) &&
            name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    public Task<SaveBatchResult> SaveBatchAsync(
        IEnumerable<ConnectionStringEntry> entriesToSave,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var result = new SaveBatchResult();
            var groupedByFile = entriesToSave.GroupBy(e => e.FilePath, StringComparer.OrdinalIgnoreCase);

            foreach (var group in groupedByFile)
            {
                var filePath = group.Key;
                var entriesList = group.ToList();

                try
                {
                    // Step 1: Create backup first
                    var backupPath = _parserService.CreateBackup(filePath);
                    result.BackupsCreated.Add(backupPath);

                    // Step 2: Apply updates
                    var firstEntry = entriesList.First();
                    if (firstEntry.FileType == ConfigFileType.WebConfig)
                    {
                        _parserService.SaveWebConfig(filePath, entriesList);
                    }
                    else
                    {
                        _parserService.SaveAppSettings(filePath, entriesList);
                    }

                    // Step 3: Update entry statuses
                    foreach (var entry in entriesList)
                    {
                        entry.CurrentValue = entry.NewValue;
                        entry.Status = ConfigEntryStatus.Saved;
                        entry.BackupPath = backupPath;
                        entry.ErrorMessage = null;
                        entry.IsInlineEditing = false;
                    }

                    result.FilesSaved++;
                    result.EntriesSaved += entriesList.Count;
                }
                catch (Exception ex)
                {
                    var errorMsg = $"Error saving {Path.GetFileName(filePath)}: {ex.Message}";
                    result.Errors.Add(errorMsg);

                    foreach (var entry in entriesList)
                    {
                        entry.Status = ConfigEntryStatus.Error;
                        entry.ErrorMessage = ex.Message;
                    }
                }
            }

            return result;
        }, cancellationToken);
    }
}
