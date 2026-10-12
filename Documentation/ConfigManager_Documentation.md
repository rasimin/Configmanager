# Dokumentasi Arsitektur & Panduan Penggunaan: ConfigManager
**Aplikasi Desktop Portable Berbasis WPF + Blazor Hybrid (.NET 9)**
**Workspace:** `SetConfigNetCore`  
**Target OS:** `win-x64`

---

## 1. Ikhtisar Aplikasi
**ConfigManager** adalah aplikasi desktop modern yang dirancang khusus untuk mengelola, mereview, dan mengubah nilai Connection Strings dan Client/WCF Endpoints secara massal (bulk replace) pada file konfigurasi:
- **XML:** `web.config` (.NET Framework / ASP.NET / WCF)
- **JSON:** `appsettings.json` dan `appsettings.*.json` (.NET Core / .NET 5/6/7/8/9)

Aplikasi ini dibangun menggunakan arsitektur **WPF dengan Blazor Hybrid (`Microsoft.AspNetCore.Components.WebView.Wpf`)**, menggabungkan keandalan akses sistem file Windows native dan tampilan UI web modern berbasis **Minimalist Warm Ivory Design System** (mengadopsi `sample/Config-Manager-Minimal.html` dengan palet warna off-white `#FCFCF9`, pure white cards `#FFFFFF` beresolusi tinggi, accordion collapsible per file, token-level diff amber highlighting, dan typography Inter).


---

## 2. Struktur Proyek Lengkap

```
D:\Rasimin\Learn\SetConfigNetCore\
├── ConfigManager.sln                         # Visual Studio Solution File
├── ConfigManager.csproj                      # Konfigurasi Project & Single-File Publish
├── nuget.config                              # Konfigurasi feed NuGet (nuget.org)
├── publish.bat                               # Skrip Batch 1-Click Publish Portable Executable (.exe murni)
├── publish-portable.bat                      # Skrip Windows Batch 1-click build portable
├── publish-portable.ps1                      # Skrip PowerShell build & publish portable
├── App.xaml / App.xaml.cs                    # Entry point WPF & Dependency Injection
├── MainWindow.xaml / MainWindow.xaml.cs      # Host Window WPF & BlazorWebView
│
├── Models/
│   └── ConfigModels.cs                       # Model data ConnectionStringEntry, enums & results
│
├── Assets/
│   ├── app.ico                               # Windows Icon multi-resolusi (256, 128, 64, 48, 32, 16 px)
│   └── app.png                               # Master high-res transparent PNG (512x512 px)
│
├── Services/
│   ├── LoggerService.cs                      # Logging & Error Tracing persistent log file
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
   - **Database Connection Strings:** Membaca dan mengupdate properti `"ConnectionStrings"` secara case-insensitive.
   - **Service & Client Endpoints:** Otomatis mendeteksi endpoint layanan/API pada bagian `"SSOLibConfigs"` (seperti `SSOLib_SvcEndPoint`, `SSOLib_ApiEndPoint`), `"Endpoints"`, `"ClientEndpoints"`, `"ServiceEndpoints"`, atau properti URL endpoint lainnya yang mengarah ke layanan SSO/WCF/REST.
   - Menggunakan penanda rute internal `JsonPath` (misal `SSOLibConfigs:SSOLib_SvcEndPoint`) untuk navigasi dan penulisan balik yang presisi.
   - Saat proses tulis balik, struktur diatur menggunakan `JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }` agar struktur JSON tetap rapi dan tidak merusak hierarki JSON lain (seperti `Logging`, `AllowedHosts`, dsb).
3. **Mekanisme Auto Backup:**
   - Sebelum file asli di-overwrite, engine secara otomatis membuat salinan cadangan di direktori yang sama dengan format:
     `[NamaFileAsli].bak_yyyyMMddHHmmss`
     *(Contoh: `web.config.bak_20261012101500` atau `appsettings.json.bak_20261012101500`)*.
   - Pengguna dapat memverifikasi atau merestore file cadangan kapan saja jika diperlukan.

### C. 2-Panel Workspace & Right Settings Panel (Categorized)
1. **Layout 2-Panel:**
   - **Panel Kiri (Main Workspace):** Command bar scanner, list antrian target, dan tampilan data konfigurasi dengan sistem **Pengelompokan Per File (File Grouping)** pada **Stacked Diff Card View** (default) serta opsi beralih ke **Compact Table View**.
   - **Panel Kanan (Settings & Inspector):** Panel pengaturan modular berbasis kategori.
2. **Pengelompokan Entri Per File (File Grouping View - Mengatasi Duplikasi Path):**
   - **Satu Kontainer File Terpadu:** Setting yang berada di dalam file fisik yang sama (misal satu `Web.config` yang memiliki Connection String dan WCF Endpoint, atau satu `appsettings.json` yang memiliki `DefaultConnection` dan `SSOLib_SvcEndPoint`/`SSOLib_ApiEndPoint`) kini secara otomatis dikelompokkan dalam satu kartu file (`.file-group-card`), bukan lagi terpisah menjadi kartu ganda yang membingungkan.
   - **Path Lengkap Ditampilkan 1 Kali:** Header kartu file menampilkan nama file (`Web.config` / `appsettings.json`), tipe format, counter jumlah setting (misal `3 settings`), shortcut Windows Explorer, dan banner path lengkap aplikasi (`D:\...\appsettings.json`) tepat 1 kali di bagian atas.
   - **Master Checkbox File:** Memungkinkan memilih atau membatalkan pilihan seluruh setting di dalam file tersebut hanya dengan satu klik pada header file.
   - **Sub-Item Setting Bertingkat (Nested Items):** Masing-masing setting di dalam file tersebut (Connection String maupun Client/Service Endpoint) ditampilkan secara elegan di bawahnya dengan:
     - Checkbox mandiri per setting.
     - Pill kategori penanda jelas: `DB Connection` (biru cyan) atau `Service Endpoint` / `WCF Endpoint` (ungu).
     - Pill nama key/endpoint dan provider/binding/section.
     - Status per entri (Modified, Unchanged, Saved, Error).
     - Tombol inspeksi modal (`🔍`) dan tombol revert mandiri (`↺`).
   - **Perbandingan Before & After Bertumpuk (Atas-Bawah):**
     - **Baris Atas (`BEFORE`):** Menampilkan nilai original dengan tombol copy.
     - **Baris Bawah (`AFTER` / `PREVIEW`):** Menampilkan nilai baru dengan badge highlight amber dan tombol inline edit langsung.
     - **Horizontal Scrollable Code:** Masing-masing baris kode Before dan After dapat di-scroll ke kanan secara independen untuk melihat string koneksi/URL panjang.
   - **Pengelompokan Pada Compact Table View:** Pada mode Compact Table, baris-baris setting juga dikelompokkan dengan subheader baris file (`.table-group-header-row`) yang memuat nama file, counter, dan path direktori.
3. **Sistem Kategori Modular & Dukungan Client Endpoints (WCF & JSON Service Endpoints):**
   - Tab **"Connection Strings"**: Mengelola dan mengubah connection string database.
   - Tab **"Client Endpoints"**: Mendeteksi dan mengelola tag WCF `<system.serviceModel><client><endpoint address="..." />` pada `web.config` **DAN** endpoint layanan JSON pada `appsettings.json` (seperti `SSOLib_SvcEndPoint`, `SSOLib_ApiEndPoint` pada `SSOLibConfigs`).
   - Tab **"App Settings"** & **"Env Vars"**: Disiapkan untuk ekspansi modular berikutnya.
4. **Override Sekaligus (Simultaneous Replacement - Opsional):**
   - Saat mengubah Connection Strings di panel kanan, tersedia 2 opsi endpoint terpisah:
     1. **`Update SSO Endpoints (Optional)`**:
        - Khusus menargetkan endpoint ekosistem SSO dengan 2 URL spesifik:
          - **SSO Service URL (ASMX):** Menargetkan `SSOSoap` (pada `web.config`) dan `SSOLib_SvcEndPoint` (pada `appsettings.json`), misalnya `http://192.168.26.21/VICTOR/SSO/Service/SSO.asmx`.
          - **SSO API URL (SVC/REST):** Menargetkan `SSOLib_ApiEndPoint` (pada `appsettings.json`), misalnya `http://192.168.26.21/VICTOR/SSO/api/v1.svc`.
        - Jika opsi ini tidak dicentang, seluruh endpoint SSO aman dan tidak akan disentuh.
     2. **`Update Other Endpoints (Non-SSO)`**:
        - Menggunakan logika pencarian dan penggantian berbasis pola (`Find in Endpoint URL` & `Replace with`) atau target URL spesifik untuk endpoint layanan selain SSO (Payment, Reporting, Custom Service).
   - Sekali klik **"Apply to Selected"**, perubahan connection string dan perubahan endpoint diaplikasikan dan di-preview bersamaan.
   - Saat **"Save All Changes"** dijalankan, seluruh perubahan dituliskan ke file (`web.config` maupun `appsettings.json`) dalam satu pass penulisan atomik tanpa merusak formatting atau komentar.
5. **Override Parameter Terstruktur (Structured Parameters) & EntityClient / EF Support:**
   - Mengubah parameter spesifik database tanpa merusak parameter lain:
     - **Host / Server:** Mengupdate `Data Source`, `Server`, `Host`, atau `Addr`.
     - **Database:** Mengupdate `Initial Catalog` atau `Database`.
     - **Username:** Mengupdate `User ID`, `User Id`, `Username`, atau `Uid`.
     - **Password:** Mengupdate `Password` atau `Pwd` (dilengkapi toggle show/hide).
   - **Dukungan Penuh Entity Framework / EntityClient:**
     - Connection string bertipe EF (`metadata=...;provider=System.Data.SqlClient;provider connection string="data source=...;initial catalog=...;"`) otomatis diidentifikasi.
     - Engine mengekstrak inner ADO.NET connection string di dalam `provider connection string="..."` sehingga parameter `data source`, `initial catalog`, `user id`, dan `password` dapat dibaca dan diupdate dengan presisi tanpa merusak metadata maupun formatting EF.
   - Nilai parameter yang dibiarkan kosong tidak akan diubah pada connection string target.
   - Tersedia sub-mode **"Raw Find & Replace"** untuk penyesuaian string global atau format khusus (Redis, MongoDB).
6. **Live Active Entry Inspector:**
   - Mengklik item mana saja pada panel kiri akan memuat rincian parameter (Connection String maupun Client Endpoint) secara instan di panel kanan.
7. **Konfirmasi Simpan Aman:**
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
