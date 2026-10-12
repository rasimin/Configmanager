# Dokumentasi Arsitektur & Panduan Penggunaan: ConfigManager
**Aplikasi Desktop Portable Berbasis WPF + Blazor Hybrid (.NET 9)**
**Workspace:** `SetConfigNetCore`  
**Target OS:** `win-x64`

---

## 1. Ikhtisar Aplikasi
**ConfigManager** adalah aplikasi desktop modern yang dirancang khusus untuk mengelola, mereview, dan mengubah nilai Connection Strings secara massal (bulk replace) pada file konfigurasi:
- **XML:** `web.config` (.NET Framework / ASP.NET)
- **JSON:** `appsettings.json` dan `appsettings.*.json` (.NET Core / .NET 5/6/7/8/9)

Aplikasi ini dibangun menggunakan arsitektur **WPF dengan Blazor Hybrid (`Microsoft.AspNetCore.Components.WebView.Wpf`)**, menggabungkan keandalan akses sistem file Windows native dan tampilan UI web modern berbasis **shadcn/ui (Zinc Dark Theme & Geist/Inter Typography)**.

---

## 2. Struktur Proyek Lengkap

```
D:\Rasimin\Learn\SetConfigNetCore\
├── ConfigManager.sln                         # Visual Studio Solution File
├── ConfigManager.csproj                      # Konfigurasi Project & Single-File Publish
├── nuget.config                              # Konfigurasi feed NuGet (nuget.org)
├── publish-portable.ps1                      # Skrip PowerShell build & publish portable
├── publish-portable.bat                      # Skrip Windows Batch 1-click build
├── App.xaml / App.xaml.cs                    # Entry point WPF & Dependency Injection
├── MainWindow.xaml / MainWindow.xaml.cs      # Host Window WPF & BlazorWebView
│
├── Models/
│   └── ConfigModels.cs                       # Model data ConnectionStringEntry, enums & results
│
├── Services/
│   ├── ConfigParserService.cs                # Engine parser XML (XDocument) & JSON (JsonNode) + Backup
│   ├── ConfigScanService.cs                  # Engine scanner rekursif aman & batch saving
│   └── FolderPickerService.cs                # Integrasi dialog Windows native & Explorer
│
├── Components/
│   ├── App.razor                             # Blazor Router
│   ├── _Imports.razor                        # Global namespace imports
│   ├── Layout/
│   │   └── MainLayout.razor                  # Header aplikasi, brand badge, dan status bar
│   ├── Pages/
│   │   └── Home.razor                        # Halaman utama (2-Panel Workspace Layout)
│   └── Shared/
│       ├── RightSettingsPanel.razor          # Panel kanan: Kategori & Override Parameter Connection String
│       ├── ConfigTable.razor                 # Tabel data interaktif & active row inspector
│       ├── ConfirmSaveModal.razor            # Modal dialog konfirmasi simpan & info backup
│       ├── DiffModal.razor                   # Modal inspeksi perbandingan & parsing parameter
│       └── StatusBadge.razor                 # Badge status (Unchanged, Modified, Saved, Error)
│
├── wwwroot/
│   ├── index.html                            # Host page WebView
│   ├── css/app.css                           # Sistem desain modern (Dark Theme & Responsive)
│   └── js/app.js                             # Skrip bantuan clipboard & interaksi UI
│
├── sample_test_env/                          # Lingkungan contoh file konfigurasi untuk pengujian
│   ├── app1/web.config
│   ├── app2/appsettings.json
│   └── app2/appsettings.Production.json
│
└── tests/
    └── ConfigManager.Tests/                  # Pengujian otomatis xUnit (100% Pass)
        ├── ConfigManager.Tests.csproj
        └── ConfigServicesTests.cs
```

---

## 3. Fitur Utama & Cara Kerja Engine

### A. Scanning & Multi-Target Queue
1. **Input Root Folder:**
   - Pengguna dapat mengetik path folder secara langsung atau menekan tombol **Browse Folder** (menggunakan `Microsoft.Win32.OpenFolderDialog` native).
   - Pengguna juga dapat menekan **Browse File(s)** untuk memilih file spesifik dari drive mana pun (C:, D:, network shared drive).
2. **Pencarian Rekursif:**
   - Secara rekursif memindai direktori yang dipilih untuk mendeteksi `web.config`, `appsettings.json`, dan pola `appsettings.*.json`.
   - Mengabaikan direktori build seperti `bin`, `obj`, `.git`, `.vs`, `.idea`, `node_modules` secara otomatis agar tidak memodifikasi file sementara/output kompilasi.
   - Menggunakan mekanisme penanganan error per folder sehingga jika ada folder sistem yang diproteksi hak akses (access denied), scan tetap berjalan lancar untuk folder lainnya.

### B. Parsing & Non-Destructive Saving Engine
1. **XML (`web.config`):**
   - Menggunakan `System.Xml.Linq.XDocument` dengan opsi `LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo`.
   - Menemukan elemen `<connectionStrings>` dan tag anak `<add name="..." connectionString="..." providerName="..." />` secara case-insensitive.
   - Saat proses simpan, atribut `connectionString` diperbarui, sementara seluruh formatting XML, spasi, indentasi, atribut lain (`providerName`), deklarasi XML, dan komentar (`<!-- ... -->`) tetap utuh tanpa rusak.
2. **JSON (`appsettings.json`):**
   - Menggunakan `System.Text.Json.Nodes` (`JsonNode` dan `JsonObject`).
   - Membaca properti `"ConnectionStrings"` secara case-insensitive.
   - Saat proses tulis balik, struktur diatur menggunakan `JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }` agar struktur JSON tetap rapi dan tidak merusak hierarki JSON lain (seperti `Logging`, `AllowedHosts`, dsb).
3. **Mekanisme Auto Backup:**
   - Sebelum file asli di-overwrite, engine secara otomatis membuat salinan cadangan di direktori yang sama dengan format:
     `[NamaFileAsli].bak_yyyyMMddHHmmss`
     *(Contoh: `web.config.bak_20261012101500`)*.
   - Pengguna dapat memverifikasi atau merestore file cadangan kapan saja jika diperlukan.

### C. 2-Panel Workspace & Right Settings Panel (Categorized)
1. **Layout 2-Panel:**
   - **Panel Kiri (Main Workspace):** Command bar scanner, list antrian target, dan tampilan data konfigurasi dengan sistem **Stacked Diff Card View** (default) serta opsi beralih ke **Compact Table View**.
   - **Panel Kanan (Settings & Inspector):** Panel pengaturan modular berbasis kategori.
2. **Stacked Diff Card View (Tampilan Awal / Default):**
   - **Full Application Path:** Menampilkan path direktori lengkap file konfigurasi secara utuh dan jelas (tanpa terpotong elipsis), sehingga pengguna dapat langsung mengetahui aplikasi dan subfolder yang sedang dimodifikasi (`web.config` / `appsettings.json`), dilengkapi tombol copy path instan dan tombol buka di Windows Explorer.
   - **Perbandingan Before & After Bertumpuk (Atas-Bawah):**
     - **Baris Atas (`BEFORE`):** Menampilkan connection string original/aktif saat ini dengan badge abu-abu dan tombol copy.
     - **Baris Bawah (`AFTER` / `PREVIEW`):** Menampilkan connection string hasil perubahan dengan badge highlight amber/emas, penyorotan teks, dan tombol inline edit langsung.
   - **Horizontal Scrollable Code:** Masing-masing baris kode Before dan After dapat di-scroll ke kanan secara independen untuk melihat string koneksi yang sangat panjang tanpa harus membuka modal dialog.
   - **Detail Modal Diff:** Tombol kaca pembesar tetap tersedia untuk analisis breakdown parameter (Server, Database, User ID, Password).
3. **Sistem Kategori Modular:**
   - Tab **"Connection Strings"** (Kategori aktif saat ini).
   - Tab **"App Settings"** & **"Env Vars"** (Disiapkan untuk ekspansi modular berikutnya).
4. **Override Parameter Terstruktur (Structured Parameters):**
   - Mengubah parameter spesifik tanpa merusak parameter lain:
     - **Host / Server:** Mengupdate `Data Source`, `Server`, `Host`, atau `Addr`.
     - **Database:** Mengupdate `Initial Catalog` atau `Database`.
     - **Username:** Mengupdate `User ID`, `User Id`, `Username`, atau `Uid`.
     - **Password:** Mengupdate `Password` atau `Pwd` (dilengkapi toggle show/hide).
   - Nilai parameter yang dibiarkan kosong tidak akan diubah pada connection string target.
   - Tersedia sub-mode **"Raw Find & Replace"** untuk penyesuaian string global atau format khusus (Redis, MongoDB).
5. **Live Active Entry Inspector:**
   - Mengklik item mana saja pada panel kiri akan memuat rincian parameter string koneksi tersebut secara instan di panel kanan.
6. **Konfirmasi Simpan Aman:**
   - Tombol **Save All Changes** di bagian bawah panel kanan menampilkan modal konfirmasi yang merinci total file yang terpengaruh, daftar path lengkap, dan konfirmasi pembuatan backup otomatis sebelum penulisan ke disk dilakukan.

---

## 4. Konfigurasi Publish Single-File Portable

File `ConfigManager.csproj` telah dikonfigurasi untuk mendukung penerbitan sebagai executable tunggal:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net9.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <RootNamespace>ConfigManager</RootNamespace>
    <AssemblyName>ConfigManager</AssemblyName>
    
    <!-- Default publish settings for portable single-file executable -->
    <PublishSingleFile Condition="'$(PublishSingleFile)' == ''">true</PublishSingleFile>
    <RuntimeIdentifier Condition="'$(RuntimeIdentifier)' == ''">win-x64</RuntimeIdentifier>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <IncludeAllContentForSelfExtract>true</IncludeAllContentForSelfExtract>
    <EnableCompressionInSingleFile Condition="'$(SelfContained)' == 'true'">true</EnableCompressionInSingleFile>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Components.WebView.Wpf" Version="9.0.40" />
  </ItemGroup>

  <ItemGroup>
    <Compile Remove="tests\**" />
    <Compile Remove="sample_test_env\**" />
    <Compile Remove="publish\**" />
    <Content Remove="tests\**" />
    <Content Remove="sample_test_env\**" />
    <Content Remove="publish\**" />
    <None Remove="tests\**" />
    <None Remove="sample_test_env\**" />
    <None Remove="publish\**" />
  </ItemGroup>

</Project>
```

### Opsi Penerbitan (Publishing):
1. **Self-Contained (Portable Mandiri, Direkomendasikan):**
   ```powershell
   .\publish-portable.ps1
   # atau via dotnet cli:
   dotnet publish ConfigManager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish\ConfigManager-Portable-SelfContained
   ```
   - Hasil: File `ConfigManager.exe` (~64 MB) sudah memuat .NET 9 Runtime di dalamnya.
   - Dapat langsung dijalankan di komputer Windows 64-bit mana pun tanpa perlu menginstal .NET Runtime terlebih dahulu.

2. **Framework-Dependent (Ukuran Ringan):**
   ```powershell
   .\publish-portable.ps1 -FrameworkDependent
   # atau via dotnet cli:
   dotnet publish ConfigManager.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish\ConfigManager-Portable-FrameworkDependent
   ```
   - Hasil: File `ConfigManager.exe` (~3.8 MB). Memerlukan .NET 9 Desktop Runtime pada komputer target.

---

## 5. Menjalankan Automated Test
Proyek dilengkapi pengujian unit dan integrasi otomatis pada folder `tests/ConfigManager.Tests/`:
```powershell
dotnet test tests/ConfigManager.Tests/ConfigManager.Tests.csproj
```
Hasil pengujian: **4/4 Tests Passed (100% Berhasil)**:
- `ParseWebConfig_ExtractsAllConnectionStringsAndProviders`: Lolos.
- `ParseAppSettingsJson_ExtractsAllConnectionStrings`: Lolos.
- `ScanPathsAsync_FindsAllConfigFilesRecursively`: Lolos.
- `SaveBatchAsync_CreatesBackupsAndUpdatesValuesCorrectly`: Lolos.
