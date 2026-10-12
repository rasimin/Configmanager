using System.IO;
using ConfigManager.Models;
using ConfigManager.Services;
using Xunit;

namespace ConfigManager.Tests;

public class ConfigServicesTests
{
    private readonly string _testEnvPath;
    private readonly ConfigParserService _parserService;
    private readonly ConfigScanService _scanService;

    public ConfigServicesTests()
    {
        // Find sample_test_env by searching upwards
        var currentDir = new DirectoryInfo(AppContext.BaseDirectory);
        string? foundPath = null;
        while (currentDir != null)
        {
            var candidate = Path.Combine(currentDir.FullName, "sample_test_env");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "app1", "web.config")))
            {
                foundPath = candidate;
                break;
            }
            currentDir = currentDir.Parent;
        }

        _testEnvPath = foundPath ?? throw new DirectoryNotFoundException("sample_test_env not found");
        _parserService = new ConfigParserService();
        _scanService = new ConfigScanService(_parserService);
    }

    [Fact]
    public void ParseWebConfig_ExtractsAllConnectionStringsAndProviders()
    {
        var webConfigPath = Path.Combine(_testEnvPath, "app1", "web.config");
        Assert.True(File.Exists(webConfigPath), $"Test file not found: {webConfigPath}");

        var entries = _parserService.ParseFile(webConfigPath);

        Assert.Equal(2, entries.Count);
        var defConn = entries.FirstOrDefault(e => e.KeyName == "DefaultConnection");
        Assert.NotNull(defConn);
        Assert.Equal(ConfigFileType.WebConfig, defConn.FileType);
        Assert.Equal("System.Data.SqlClient", defConn.ProviderName);
        Assert.Contains("OLD-SRV-01", defConn.CurrentValue);

        var authConn = entries.FirstOrDefault(e => e.KeyName == "AuthDatabase");
        Assert.NotNull(authConn);
        Assert.Equal("Microsoft.Data.SqlClient", authConn.ProviderName);
    }

    [Fact]
    public void ParseAppSettingsJson_ExtractsAllConnectionStrings()
    {
        var appsettingsPath = Path.Combine(_testEnvPath, "app2", "appsettings.json");
        Assert.True(File.Exists(appsettingsPath), $"Test file not found: {appsettingsPath}");

        var entries = _parserService.ParseFile(appsettingsPath);

        Assert.Equal(3, entries.Count);
        Assert.Contains(entries, e => e.KeyName == "DefaultConnection" && e.FileType == ConfigFileType.AppSettingsJson);
        Assert.Contains(entries, e => e.KeyName == "RedisCache");
        Assert.Contains(entries, e => e.KeyName == "AuditLogDb");
    }

    [Fact]
    public async Task ScanPathsAsync_FindsAllConfigFilesRecursively()
    {
        Assert.True(Directory.Exists(_testEnvPath), $"Test directory not found: {_testEnvPath}");

        var entries = await _scanService.ScanPathsAsync(new[] { _testEnvPath }, recursive: true);

        // 2 from web.config + 3 from appsettings.json + 2 from appsettings.Production.json = 7
        Assert.Equal(7, entries.Count);
    }

    [Fact]
    public async Task SaveBatchAsync_CreatesBackupsAndUpdatesValuesCorrectly()
    {
        // Create an isolated temp directory for write tests
        var tempDir = Path.Combine(Path.GetTempPath(), "ConfigManager_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var tempWebConfig = Path.Combine(tempDir, "web.config");
            var originalXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<!-- Pre-save comment -->
<configuration>
  <connectionStrings>
    <add name=""MyConn"" connectionString=""Server=OLD-SRV-01;Database=Test;"" providerName=""System.Data.SqlClient"" />
  </connectionStrings>
</configuration>";
            File.WriteAllText(tempWebConfig, originalXml);

            var tempAppSettings = Path.Combine(tempDir, "appsettings.json");
            var originalJson = @"{
  ""Logging"": { ""LogLevel"": { ""Default"": ""Information"" } },
  ""ConnectionStrings"": {
    ""MyConn"": ""Server=OLD-SRV-01;Database=Test;""
  }
}";
            File.WriteAllText(tempAppSettings, originalJson);

            // Scan temp directory
            var entries = await _scanService.ScanPathsAsync(new[] { tempDir }, recursive: true);
            Assert.Equal(2, entries.Count);

            // Apply preview replacement: OLD-SRV-01 -> NEW-SRV-02
            foreach (var entry in entries)
            {
                entry.ApplyPreview(entry.CurrentValue.Replace("OLD-SRV-01", "NEW-SRV-02"));
            }

            // Save batch
            var saveResult = await _scanService.SaveBatchAsync(entries);

            Assert.True(saveResult.Success);
            Assert.Equal(2, saveResult.FilesSaved);
            Assert.Equal(2, saveResult.EntriesSaved);
            Assert.Equal(2, saveResult.BackupsCreated.Count);

            // Verify backup files exist
            foreach (var backupFile in saveResult.BackupsCreated)
            {
                Assert.True(File.Exists(backupFile));
                var backupContent = File.ReadAllText(backupFile);
                Assert.Contains("OLD-SRV-01", backupContent);
            }

            // Verify updated files on disk have new value
            var updatedXml = File.ReadAllText(tempWebConfig);
            Assert.Contains("NEW-SRV-02", updatedXml);
            Assert.DoesNotContain("OLD-SRV-01", updatedXml);
            Assert.Contains("<!-- Pre-save comment -->", updatedXml); // Comment preserved!

            var updatedJson = File.ReadAllText(tempAppSettings);
            Assert.Contains("NEW-SRV-02", updatedJson);
            Assert.DoesNotContain("OLD-SRV-01", updatedJson);
            Assert.Contains("Logging", updatedJson); // Other JSON structure preserved!
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void ConnectionStringHelper_ParsesComponentsCorrectly()
    {
        var connStr = "Data Source=MY-SRV-01;Initial Catalog=SalesDb;User ID=admin_user;Password=SuperSecret123!;Encrypt=True;";
        var parsed = ConnectionStringHelper.Parse(connStr);

        Assert.Equal("MY-SRV-01", parsed.Server);
        Assert.Equal("SalesDb", parsed.Database);
        Assert.Equal("admin_user", parsed.UserId);
        Assert.Equal("SuperSecret123!", parsed.Password);
    }

    [Fact]
    public void ConnectionStringHelper_UpdatesSpecificComponentsOnly()
    {
        var connStr = "Data Source=OLD-HOST;Initial Catalog=OriginalDb;User ID=sa;Password=OldPass;Encrypt=True;";
        var args = new StructuredBulkUpdateArgs
        {
            Server = "NEW-HOST",
            Password = "NewPass456!"
        };

        var updated = ConnectionStringHelper.UpdateComponents(connStr, args);

        Assert.Contains("Data Source=NEW-HOST", updated);
        Assert.Contains("Initial Catalog=OriginalDb", updated); // Unchanged!
        Assert.Contains("User ID=sa", updated);                 // Unchanged!
        Assert.Contains("Password=NewPass456!", updated);       // Changed!
        Assert.Contains("Encrypt=True", updated);               // Preserved!
    }
}
