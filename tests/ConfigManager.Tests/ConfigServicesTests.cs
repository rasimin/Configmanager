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

    [Fact]
    public void ParseWebConfig_ExtractsClientEndpoints()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ConfigManager_Test_Endpoint_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tempWebConfig = Path.Combine(tempDir, "web.config");

        try
        {
            var xmlContent = @"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <connectionStrings>
    <add name=""MyDb"" connectionString=""Server=10.0.0.1;Database=TestDb;"" />
  </connectionStrings>
  <system.serviceModel>
    <client>
      <endpoint address=""http://localhost/VICTOR/SSO/Service/SSO.asmx"" binding=""basicHttpBinding"" bindingConfiguration=""SSOSoap"" contract=""SSOSvc.SSOSoap"" name=""SSOSoap"" />
      <endpoint address=""http://192.168.1.10/Services/wsTransaction.asmx"" binding=""basicHttpBinding"" contract=""SIARTransaction.wsTransactionSoap"" name=""wsTransactionSoap"" />
    </client>
  </system.serviceModel>
</configuration>";
            File.WriteAllText(tempWebConfig, xmlContent);

            var entries = _parserService.ParseFile(tempWebConfig);

            Assert.Equal(3, entries.Count); // 1 conn string + 2 endpoints
            var conn = entries.FirstOrDefault(e => e.Category == ConfigCategory.ConnectionStrings);
            Assert.NotNull(conn);
            Assert.Equal("MyDb", conn.KeyName);

            var ssoEndpoint = entries.FirstOrDefault(e => e.KeyName == "SSOSoap" && e.Category == ConfigCategory.ClientEndpoints);
            Assert.NotNull(ssoEndpoint);
            Assert.Equal("http://localhost/VICTOR/SSO/Service/SSO.asmx", ssoEndpoint.CurrentValue);
            Assert.Equal("basicHttpBinding", ssoEndpoint.ProviderName);

            var wsEndpoint = entries.FirstOrDefault(e => e.KeyName == "wsTransactionSoap" && e.Category == ConfigCategory.ClientEndpoints);
            Assert.NotNull(wsEndpoint);
            Assert.Equal("http://192.168.1.10/Services/wsTransaction.asmx", wsEndpoint.CurrentValue);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void SaveWebConfig_UpdatesBothConnectionStringsAndClientEndpoints()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ConfigManager_Test_Save_EP_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tempWebConfig = Path.Combine(tempDir, "web.config");

        try
        {
            var xmlContent = @"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <!-- Main Database Connection -->
  <connectionStrings>
    <add name=""MyDb"" connectionString=""Server=10.0.0.1;Database=TestDb;"" providerName=""System.Data.SqlClient"" />
  </connectionStrings>
  <!-- Client Endpoints -->
  <system.serviceModel>
    <client>
      <endpoint address=""http://localhost/VICTOR/SSO/Service/SSO.asmx"" binding=""basicHttpBinding"" contract=""SSOSvc.SSOSoap"" name=""SSOSoap"" />
    </client>
  </system.serviceModel>
</configuration>";
            File.WriteAllText(tempWebConfig, xmlContent);

            var entries = _parserService.ParseFile(tempWebConfig);
            var connEntry = entries.First(e => e.Category == ConfigCategory.ConnectionStrings);
            connEntry.ApplyPreview("Server=192.168.26.21;Database=TestDb_New;");

            var epEntry = entries.First(e => e.Category == ConfigCategory.ClientEndpoints);
            epEntry.ApplyPreview("http://192.168.26.21/VICTOR/SSO/Service/SSO.asmx");

            _parserService.SaveWebConfig(tempWebConfig, new[] { connEntry, epEntry });

            var updatedXml = File.ReadAllText(tempWebConfig);
            Assert.Contains("Server=192.168.26.21;Database=TestDb_New;", updatedXml);
            Assert.Contains("http://192.168.26.21/VICTOR/SSO/Service/SSO.asmx", updatedXml);
            Assert.Contains("<!-- Main Database Connection -->", updatedXml); // Comment preserved!
            Assert.Contains("<!-- Client Endpoints -->", updatedXml);            // Comment preserved!
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ParseAppSettingsJson_ExtractsSSOLibEndpointsAndConnectionStrings()
    {
        var sampleJsonPath = Path.Combine(Directory.GetCurrentDirectory(), "sample", "angenttree", "appsettings.json");
        if (!File.Exists(sampleJsonPath))
        {
            // Fallback to relative test directory
            var currentDir = new DirectoryInfo(AppContext.BaseDirectory);
            while (currentDir != null)
            {
                var candidate = Path.Combine(currentDir.FullName, "sample", "angenttree", "appsettings.json");
                if (File.Exists(candidate))
                {
                    sampleJsonPath = candidate;
                    break;
                }
                currentDir = currentDir.Parent;
            }
        }

        Assert.True(File.Exists(sampleJsonPath), $"Sample file not found: {sampleJsonPath}");

        var entries = _parserService.ParseFile(sampleJsonPath);

        // 1 Connection String: DefaultConnection
        var conn = entries.FirstOrDefault(e => e.Category == ConfigCategory.ConnectionStrings);
        Assert.NotNull(conn);
        Assert.Equal("DefaultConnection", conn.KeyName);

        // 2 Client Endpoints: SSOLib_SvcEndPoint & SSOLib_ApiEndPoint
        var svcEndpoint = entries.FirstOrDefault(e => e.KeyName == "SSOLib_SvcEndPoint" && e.Category == ConfigCategory.ClientEndpoints);
        Assert.NotNull(svcEndpoint);
        Assert.Equal("http://192.168.69.26/DEV_SSO/Service/SSO.asmx", svcEndpoint.CurrentValue);
        Assert.Equal("SSOLibConfigs", svcEndpoint.ProviderName);

        var apiEndpoint = entries.FirstOrDefault(e => e.KeyName == "SSOLib_ApiEndPoint" && e.Category == ConfigCategory.ClientEndpoints);
        Assert.NotNull(apiEndpoint);
        Assert.Equal("http://192.168.69.26/DEV_SSO/api/v1.svc", apiEndpoint.CurrentValue);
        Assert.Equal("SSOLibConfigs", apiEndpoint.ProviderName);
    }

    [Fact]
    public void SaveAppSettings_UpdatesBothConnectionStringsAndJsonEndpoints()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ConfigManager_Test_Save_JsonEP_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tempJsonPath = Path.Combine(tempDir, "appsettings.json");

        try
        {
            var jsonContent = @"{
  ""ConnectionStrings"": {
    ""DefaultConnection"": ""Server=OLD_SRV;Database=MyDb;""
  },
  ""SSOLibConfigs"": {
    ""SSOLib_SvcEndPoint"": ""http://192.168.69.26/DEV_SSO/Service/SSO.asmx"",
    ""SSOLib_ApiEndPoint"": ""http://192.168.69.26/DEV_SSO/api/v1.svc"",
    ""SSOLib_LSDExpireTimeInSeconds"": 90
  }
}";
            File.WriteAllText(tempJsonPath, jsonContent);

            var entries = _parserService.ParseFile(tempJsonPath);
            Assert.Equal(3, entries.Count); // 1 connection string + 2 endpoints

            var connEntry = entries.First(e => e.Category == ConfigCategory.ConnectionStrings);
            connEntry.ApplyPreview("Server=192.168.26.21;Database=MyDb_New;");

            var svcEntry = entries.First(e => e.KeyName == "SSOLib_SvcEndPoint");
            svcEntry.ApplyPreview("http://192.168.26.21/VICTOR/SSO/Service/SSO.asmx");

            var apiEntry = entries.First(e => e.KeyName == "SSOLib_ApiEndPoint");
            apiEntry.ApplyPreview("http://192.168.26.21/VICTOR/SSO/api/v1.svc");

            _parserService.SaveAppSettings(tempJsonPath, new[] { connEntry, svcEntry, apiEntry });

            var updatedJson = File.ReadAllText(tempJsonPath);
            Assert.Contains("Server=192.168.26.21;Database=MyDb_New;", updatedJson);
            Assert.Contains("http://192.168.26.21/VICTOR/SSO/Service/SSO.asmx", updatedJson);
            Assert.Contains("http://192.168.26.21/VICTOR/SSO/api/v1.svc", updatedJson);
            Assert.Contains("90", updatedJson); // Non-endpoint integer property preserved!
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void UpdateComponents_HandlesEntityClientConnectionStringCorrectly()
    {
        var originalEf = @"metadata=res://*/Model.SSOModel.csdl|res://*/Model.SSOModel.ssdl|res://*/Model.SSOModel.msl;provider=System.Data.SqlClient;provider connection string=""data source=(local)\sql2019;initial catalog=VICTOR_SSO;Persist Security Info=True;User ID=appims;Password=P4ssw0rd;multipleactiveresultsets=True;App=EntityFramework""";

        // 1. Verify Parse
        var parsed = ConnectionStringHelper.Parse(originalEf);
        Assert.Equal(@"(local)\sql2019", parsed.Server);
        Assert.Equal("VICTOR_SSO", parsed.Database);
        Assert.Equal("appims", parsed.UserId);
        Assert.Equal("P4ssw0rd", parsed.Password);

        // 2. Verify UpdateComponents with new host and password
        var args = new StructuredBulkUpdateArgs
        {
            Server = @"192.168.26.21\sql2019",
            Password = "P4ssw0rd2020#"
        };

        var updatedEf = ConnectionStringHelper.UpdateComponents(originalEf, args);

        Assert.Contains(@"data source=192.168.26.21\sql2019;", updatedEf);
        Assert.Contains("Password=P4ssw0rd2020#;", updatedEf);
        Assert.Contains("initial catalog=VICTOR_SSO;", updatedEf);
        Assert.Contains("User ID=appims;", updatedEf);
        Assert.StartsWith("metadata=", updatedEf);
        Assert.Contains(@"provider connection string=""data source=192.168.26.21\sql2019;", updatedEf);
        Assert.EndsWith(@"""", updatedEf);
    }

    [Fact]
    public void LoggerService_CreatesLogFileAndWritesExceptionDetails()
    {
        var logger = new LoggerService();
        var testMessage = "Test error message for diagnostics";
        var testException = new InvalidOperationException("Test inner exception details");

        logger.LogError(testMessage, testException);

        var logFile = logger.CurrentLogFilePath;
        Assert.True(File.Exists(logFile), $"Log file was not created: {logFile}");

        var content = File.ReadAllText(logFile);
        Assert.Contains(testMessage, content);
        Assert.Contains("InvalidOperationException", content);
        Assert.Contains("Test inner exception details", content);
    }

    [Fact]
    public void ConnectionStringHelper_HandlesDuplicateKeysWithoutException()
    {
        // Connection string with duplicate 'Persist Security Info' parameter
        var duplicateKeyStr = "Data Source=192.168.1.1;Initial Catalog=MyDb;Persist Security Info=True;User ID=sa;Password=secret;Persist Security Info=True;";

        var parsed = ConnectionStringHelper.Parse(duplicateKeyStr);
        Assert.Equal("192.168.1.1", parsed.Server);
        Assert.Equal("MyDb", parsed.Database);

        var args = new StructuredBulkUpdateArgs
        {
            Server = "192.168.26.21"
        };

        var updated = ConnectionStringHelper.UpdateComponents(duplicateKeyStr, args);
        Assert.Contains("Data Source=192.168.26.21;", updated);
        Assert.Contains("Initial Catalog=MyDb;", updated);
    }
}

