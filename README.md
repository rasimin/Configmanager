# ConfigManager - Bulk Connection String Manager

A modern, high-performance desktop application built with **WPF & Blazor Hybrid (.NET 9)** to scan, manage, preview, and update connection strings in bulk across both `web.config` (XML) and `appsettings.json` (JSON) files.

---

## 🌟 Key Features

1. **Intelligent Recursive Scanning & Multi-Path Queue:**
   - Recursively search any root folder for `web.config`, `appsettings.json`, and `appsettings.*.json`.
   - Native Windows folder & multi-file pickers (`OpenFolderDialog` & `OpenFileDialog`).
   - Add targets from multiple drives or remote network locations into a unified multi-target queue.
   - Automatically ignores `bin`, `obj`, `.git`, `.vs`, and other build artifacts.

2. **2-Panel Workspace Layout (shadcn/ui Zinc Minimalist):**
   - **Left Panel (Workspace & Table):** Minimalist scanner command bar, multi-target queue, and data table with active-row inspection.
   - **Right Panel (Categorized Settings & Inspector):**
     - Modular category tabs (**Connection Strings**, **App Settings**, **Env Vars**).
     - **Structured Parameter Overrides:** Dedicated inputs for **Host/Server**, **Database**, **Username**, and **Password**. Blank fields remain safely untouched.
     - **Raw Find & Replace Mode:** Text search & replace for custom connection string formats.
     - **Live Selection Inspector:** Real-time parameter breakdown for the clicked row.
     - Integrated **Save All Changes** bottom dock.

3. **Global Find & Replace Engine:**
   - Bulk string replacement (e.g. migrating `OLD-SRV-01` &rarr; `NEW-SRV-02` or updating credentials).
   - "Apply Preview to Selected" with instant visual feedback and modified counters.
   - "Revert Previews" to reset pending changes.

4. **Non-Destructive Parsing & Safety Engine:**
   - **XML Engine (`System.Xml.Linq` / `XDocument`):** Reads and modifies `<connectionStrings><add name="..." connectionString="..." /></connectionStrings>` while strictly preserving whitespace, comments, indentation, and encoding.
   - **JSON Engine (`System.Text.Json.Nodes` / `JsonNode`):** Reads and modifies `"ConnectionStrings"` while preserving comments, root layout, and formatting (`WriteIndented = true`).
   - **Automated Backups:** Automatically creates `.bak_{yyyyMMddHHmmss}` backup copies in the same directory before overwriting any file.
   - **Confirmation Modal:** Displays affected file count and backup notice before disk commit.

5. **Portable Single-File Deployment:**
   - Pre-configured `.csproj` for publishing as a standalone Windows executable (`win-x64`).
   - Generates a self-contained `.exe` with zero external dependencies required on client machines.

---

## 📁 Project Structure

```
SetConfigNetCore/
├── ConfigManager.sln                       # Solution file
├── ConfigManager.csproj                    # WPF + Blazor Hybrid project file
├── nuget.config                            # Package sources configuration
├── App.xaml / App.xaml.cs                  # WPF application host & DI setup
├── MainWindow.xaml / MainWindow.xaml.cs    # WPF main window hosting BlazorWebView
├── publish-portable.ps1                    # PowerShell build & publish script
├── publish-portable.bat                    # Batch script for one-click build
├── Models/
│   └── ConfigModels.cs                     # Data models, enums & result objects
├── Services/
│   ├── ConfigParserService.cs              # XML (XDocument) & JSON (JsonNode) parser + backup
│   ├── ConfigScanService.cs                # Recursive directory walker & batch save
│   └── FolderPickerService.cs              # Native Windows dialogs & Explorer integration
├── Components/
│   ├── App.razor                           # Blazor router
│   ├── _Imports.razor                      # Global Razor imports
│   ├── Layout/
│   │   └── MainLayout.razor                # App header, badge & footer layout
│   ├── Pages/
│   │   └── Home.razor                      # Scanner, controls, and main view
│   └── Shared/
│       ├── BulkActionPanel.razor           # Find & replace engine panel
│       ├── ConfigTable.razor               # Interactive data table
│       ├── ConfirmSaveModal.razor          # Pre-save confirmation dialog
│       ├── DiffModal.razor                 # Side-by-side & parameter breakdown modal
│       └── StatusBadge.razor               # Status pill badges
├── wwwroot/
│   ├── index.html                          # Host HTML page
│   ├── css/app.css                         # Dark theme design system
│   └── js/app.js                           # Clipboard & UI helpers
├── sample_test_env/                        # Sample XML & JSON files for testing
└── tests/
    └── ConfigManager.Tests/                # Automated xUnit test suite
```

---

## 🚀 How to Run & Build

### Development Mode
```powershell
# Run using .NET CLI
dotnet run --project ConfigManager.csproj
```

### Run Automated Tests
```powershell
dotnet test tests/ConfigManager.Tests/ConfigManager.Tests.csproj
```

### Publish Single-File Portable Executable
Run the helper script:
```powershell
# Self-contained (~64 MB, runs on ANY Windows x64 machine without .NET installed)
.\publish-portable.ps1

# Framework-dependent (~3.8 MB, requires .NET 9 on target machine)
.\publish-portable.ps1 -FrameworkDependent
```
Or directly via the .NET CLI:
```powershell
dotnet publish ConfigManager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish\portable
```
The output file `ConfigManager.exe` can be copied and run anywhere!
