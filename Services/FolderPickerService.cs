using System.IO;
using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;

namespace ConfigManager.Services;

public interface IFolderPickerService
{
    Task<string?> PickFolderAsync(string? initialDirectory = null);
    Task<string[]?> PickFilesAsync(string? initialDirectory = null);
    void OpenFolderInExplorer(string filePath);
}

public class FolderPickerService : IFolderPickerService
{
    public Task<string?> PickFolderAsync(string? initialDirectory = null)
    {
        return Application.Current.Dispatcher.Invoke(() =>
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select Target Root Folder to Scan",
                Multiselect = false
            };

            if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
            {
                dialog.InitialDirectory = initialDirectory;
            }

            bool? result = dialog.ShowDialog();
            return Task.FromResult(result == true ? dialog.FolderName : null);
        });
    }

    public Task<string[]?> PickFilesAsync(string? initialDirectory = null)
    {
        return Application.Current.Dispatcher.Invoke(() =>
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select web.config or appsettings.json file(s)",
                Multiselect = true,
                Filter = "Configuration Files (*.config;*.json)|*.config;*.json|Web Config (*.config)|*.config|JSON AppSettings (*.json)|*.json|All Files (*.*)|*.*"
            };

            if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
            {
                dialog.InitialDirectory = initialDirectory;
            }

            bool? result = dialog.ShowDialog();
            return Task.FromResult(result == true ? dialog.FileNames : null);
        });
    }

    public void OpenFolderInExplorer(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                Process.Start("explorer.exe", $"/select,\"{filePath}\"");
            }
            else if (Directory.Exists(filePath))
            {
                Process.Start("explorer.exe", $"\"{filePath}\"");
            }
        }
        catch
        {
            // Fallback: ignore if explorer can't be spawned
        }
    }
}
