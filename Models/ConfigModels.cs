namespace ConfigManager.Models;

public enum ConfigFileType
{
    WebConfig,
    AppSettingsJson
}

public enum ConfigEntryStatus
{
    Unchanged,
    Modified,
    Saved,
    Error
}

public enum ConfigCategory
{
    ConnectionStrings,
    ClientEndpoints,
    AppSettings,
    EnvironmentVariables
}

public class ConnectionStringEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FilePath { get; set; } = string.Empty;
    public string FileName => System.IO.Path.GetFileName(FilePath);
    public string DirectoryPath => System.IO.Path.GetDirectoryName(FilePath) ?? string.Empty;
    public ConfigFileType FileType { get; set; }
    public ConfigCategory Category { get; set; } = ConfigCategory.ConnectionStrings;
    public string KeyName { get; set; } = string.Empty;
    public string? ProviderName { get; set; }
    public string? JsonPath { get; set; }
    public string CurrentValue { get; set; } = string.Empty;
    public string NewValue { get; set; } = string.Empty;
    public bool IsSelected { get; set; } = true;
    public ConfigEntryStatus Status { get; set; } = ConfigEntryStatus.Unchanged;
    public string? ErrorMessage { get; set; }
    public string? BackupPath { get; set; }
    public bool IsInlineEditing { get; set; }

    public bool HasChanges => !string.Equals(CurrentValue, NewValue, StringComparison.Ordinal);

    public void ResetPreview()
    {
        NewValue = CurrentValue;
        if (Status == ConfigEntryStatus.Modified)
        {
            Status = ConfigEntryStatus.Unchanged;
        }
        ErrorMessage = null;
    }

    public void ApplyPreview(string previewValue)
    {
        NewValue = previewValue;
        if (HasChanges)
        {
            Status = ConfigEntryStatus.Modified;
        }
        else
        {
            Status = ConfigEntryStatus.Unchanged;
        }
        ErrorMessage = null;
    }
}

public class StructuredBulkUpdateArgs
{
    // Database Connection String parameters
    public string? Server { get; set; }
    public string? Database { get; set; }
    public string? UserId { get; set; }
    public string? Password { get; set; }

    // Client / WCF Endpoint parameters (Optional)
    public bool UpdateEndpoints { get; set; } = false;
    public string? EndpointFindPattern { get; set; }
    public string? EndpointReplacePattern { get; set; }
    public string? EndpointNewAddress { get; set; }
    public string? TargetEndpointName { get; set; }
}

public static class ConnectionStringHelper
{
    private static readonly string[] ServerKeys = { "data source", "server", "host", "addr", "address", "network address" };
    private static readonly string[] DatabaseKeys = { "initial catalog", "database", "db" };
    private static readonly string[] UserKeys = { "user id", "uid", "user", "username" };
    private static readonly string[] PasswordKeys = { "password", "pwd" };

    public class ParsedComponents
    {
        public string? Server { get; set; }
        public string? Database { get; set; }
        public string? UserId { get; set; }
        public string? Password { get; set; }
        public List<KeyValuePair<string, string>> AllPairs { get; set; } = new();
    }

    /// <summary>
    /// Detects Entity Framework / EntityClient connection strings (e.g. metadata=...;provider=...;provider connection string="...")
    /// and extracts the inner database connection string.
    /// </summary>
    public static bool TryGetEntityClientInnerConnectionString(
        string connStr,
        out string prefix,
        out string innerConnStr,
        out string postfix)
    {
        prefix = string.Empty;
        innerConnStr = string.Empty;
        postfix = string.Empty;

        if (string.IsNullOrWhiteSpace(connStr)) return false;

        const string marker = "provider connection string=";
        var markerIdx = connStr.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIdx < 0) return false;

        var eqIdx = markerIdx + marker.Length;
        if (eqIdx >= connStr.Length) return false;

        int startQuote = eqIdx;
        while (startQuote < connStr.Length && char.IsWhiteSpace(connStr[startQuote]))
        {
            startQuote++;
        }

        if (startQuote >= connStr.Length) return false;

        char quoteChar = connStr[startQuote];
        if (quoteChar == '"' || quoteChar == '\'')
        {
            var endQuote = connStr.LastIndexOf(quoteChar);
            if (endQuote > startQuote)
            {
                prefix = connStr.Substring(0, startQuote + 1);
                innerConnStr = connStr.Substring(startQuote + 1, endQuote - startQuote - 1);
                postfix = connStr.Substring(endQuote);
                return true;
            }
        }
        else if (connStr.Substring(startQuote).StartsWith("&quot;", StringComparison.OrdinalIgnoreCase))
        {
            var openQuoteLen = 6;
            var startInner = startQuote + openQuoteLen;
            var endQuote = connStr.LastIndexOf("&quot;", StringComparison.OrdinalIgnoreCase);
            if (endQuote > startInner)
            {
                prefix = connStr.Substring(0, startInner);
                innerConnStr = connStr.Substring(startInner, endQuote - startInner);
                postfix = connStr.Substring(endQuote);
                return true;
            }
        }
        else
        {
            prefix = connStr.Substring(0, eqIdx);
            innerConnStr = connStr.Substring(eqIdx);
            postfix = string.Empty;
            return true;
        }

        return false;
    }

    public static ParsedComponents Parse(string connStr)
    {
        var result = new ParsedComponents();
        if (string.IsNullOrWhiteSpace(connStr)) return result;

        // If this is an Entity Framework connection string, parse the inner connection string
        if (TryGetEntityClientInnerConnectionString(connStr, out _, out var innerConnStr, out _))
        {
            var innerParsed = Parse(innerConnStr);
            result.Server = innerParsed.Server;
            result.Database = innerParsed.Database;
            result.UserId = innerParsed.UserId;
            result.Password = innerParsed.Password;
            result.AllPairs = innerParsed.AllPairs;
            return result;
        }

        var tokens = connStr.Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            var eqIdx = token.IndexOf('=');
            if (eqIdx > 0)
            {
                var k = token.Substring(0, eqIdx).Trim();
                var v = token.Substring(eqIdx + 1).Trim();
                result.AllPairs.Add(new KeyValuePair<string, string>(k, v));

                var lowerKey = k.ToLowerInvariant();
                if (ServerKeys.Contains(lowerKey) && result.Server == null) result.Server = v;
                else if (DatabaseKeys.Contains(lowerKey) && result.Database == null) result.Database = v;
                else if (UserKeys.Contains(lowerKey) && result.UserId == null) result.UserId = v;
                else if (PasswordKeys.Contains(lowerKey) && result.Password == null) result.Password = v;
            }
        }

        return result;
    }

    public static string UpdateComponents(string originalConnStr, StructuredBulkUpdateArgs args)
    {
        if (string.IsNullOrWhiteSpace(originalConnStr)) return originalConnStr;

        // If this is an Entity Framework connection string, update the inner connection string
        if (TryGetEntityClientInnerConnectionString(originalConnStr, out var prefix, out var innerConnStr, out var postfix))
        {
            var updatedInner = UpdateComponents(innerConnStr, args);
            return $"{prefix}{updatedInner}{postfix}";
        }

        var tokens = originalConnStr.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var pairs = new List<KeyValuePair<string, string>>();
        foreach (var token in tokens)
        {
            var eqIdx = token.IndexOf('=');
            if (eqIdx > 0)
            {
                var k = token.Substring(0, eqIdx).Trim();
                var v = token.Substring(eqIdx + 1).Trim();
                var lowerK = k.ToLowerInvariant();

                if (!string.IsNullOrEmpty(args.Server) && ServerKeys.Contains(lowerK))
                {
                    pairs.Add(new KeyValuePair<string, string>(k, args.Server));
                }
                else if (!string.IsNullOrEmpty(args.Database) && DatabaseKeys.Contains(lowerK))
                {
                    pairs.Add(new KeyValuePair<string, string>(k, args.Database));
                }
                else if (!string.IsNullOrEmpty(args.UserId) && UserKeys.Contains(lowerK))
                {
                    pairs.Add(new KeyValuePair<string, string>(k, args.UserId));
                }
                else if (!string.IsNullOrEmpty(args.Password) && PasswordKeys.Contains(lowerK))
                {
                    pairs.Add(new KeyValuePair<string, string>(k, args.Password));
                }
                else
                {
                    pairs.Add(new KeyValuePair<string, string>(k, v));
                }
            }
            else
            {
                pairs.Add(new KeyValuePair<string, string>(token.Trim(), string.Empty));
            }
        }

        // Reconstruct string
        var parts = pairs.Select(p => string.IsNullOrEmpty(p.Value) ? p.Key : $"{p.Key}={p.Value}");
        var result = string.Join(";", parts);
        if (originalConnStr.EndsWith(";"))
        {
            result += ";";
        }
        return result;
    }
}

public class SaveConfirmationDetails
{
    public int TotalFiles { get; set; }
    public int TotalEntries { get; set; }
    public List<string> AffectedFiles { get; set; } = new();
    public List<ConnectionStringEntry> EntriesToSave { get; set; } = new();
}

public class SaveBatchResult
{
    public int FilesSaved { get; set; }
    public int EntriesSaved { get; set; }
    public List<string> BackupsCreated { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public bool Success => Errors.Count == 0;
}
