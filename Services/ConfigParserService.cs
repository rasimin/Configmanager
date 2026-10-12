using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using ConfigManager.Models;

namespace ConfigManager.Services;

public interface IConfigParserService
{
    List<ConnectionStringEntry> ParseFile(string filePath);
    string CreateBackup(string filePath);
    void SaveWebConfig(string filePath, IEnumerable<ConnectionStringEntry> entriesToUpdate);
    void SaveAppSettings(string filePath, IEnumerable<ConnectionStringEntry> entriesToUpdate);
}

public class ConfigParserService : IConfigParserService
{
    public List<ConnectionStringEntry> ParseFile(string filePath)
    {
        var result = new List<ConnectionStringEntry>();
        if (!File.Exists(filePath))
            return result;

        var fileName = Path.GetFileName(filePath);
        if (fileName.Equals("web.config", StringComparison.OrdinalIgnoreCase))
        {
            result.AddRange(ParseWebConfig(filePath));
        }
        else if (fileName.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase) &&
                 fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            result.AddRange(ParseAppSettings(filePath));
        }

        return result;
    }

    public string CreateBackup(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Cannot backup missing file: {filePath}");

        var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        var backupPath = $"{filePath}.bak_{timestamp}";

        // If file already exists with same timestamp within the same second, append counter
        int counter = 1;
        while (File.Exists(backupPath))
        {
            backupPath = $"{filePath}.bak_{timestamp}_{counter++}";
        }

        File.Copy(filePath, backupPath, overwrite: false);
        return backupPath;
    }

    public void SaveWebConfig(string filePath, IEnumerable<ConnectionStringEntry> entriesToUpdate)
    {
        var encoding = DetectEncoding(filePath);
        var xDoc = XDocument.Load(filePath, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);

        var connEntries = entriesToUpdate.Where(e => e.Category == ConfigCategory.ConnectionStrings).ToList();
        var endpointEntries = entriesToUpdate.Where(e => e.Category == ConfigCategory.ClientEndpoints).ToList();

        // 1. Update Connection Strings
        if (connEntries.Count > 0)
        {
            var csElement = xDoc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName.Equals("connectionStrings", StringComparison.OrdinalIgnoreCase));

            if (csElement != null)
            {
                var updateMap = connEntries.ToDictionary(e => e.KeyName, e => e.NewValue, StringComparer.OrdinalIgnoreCase);

                var addElements = csElement.Elements()
                    .Where(e => e.Name.LocalName.Equals("add", StringComparison.OrdinalIgnoreCase));

                foreach (var add in addElements)
                {
                    var nameAttr = add.Attributes()
                        .FirstOrDefault(a => a.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase));

                    if (nameAttr != null && updateMap.TryGetValue(nameAttr.Value, out var newConnStr))
                    {
                        var connStrAttr = add.Attributes()
                            .FirstOrDefault(a => a.Name.LocalName.Equals("connectionString", StringComparison.OrdinalIgnoreCase));

                        if (connStrAttr != null)
                        {
                            connStrAttr.Value = newConnStr;
                        }
                        else
                        {
                            add.SetAttributeValue("connectionString", newConnStr);
                        }
                    }
                }
            }
        }

        // 2. Update Client / Service Endpoints (<system.serviceModel><client><endpoint address="..." />)
        if (endpointEntries.Count > 0)
        {
            var epUpdateMap = endpointEntries.ToDictionary(e => e.KeyName, e => e.NewValue, StringComparer.OrdinalIgnoreCase);

            var endpointElements = xDoc.Descendants()
                .Where(e => e.Name.LocalName.Equals("endpoint", StringComparison.OrdinalIgnoreCase) &&
                            e.Parent != null && e.Parent.Name.LocalName.Equals("client", StringComparison.OrdinalIgnoreCase));

            foreach (var ep in endpointElements)
            {
                var nameAttr = ep.Attributes()
                    .FirstOrDefault(a => a.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase));
                var contractAttr = ep.Attributes()
                    .FirstOrDefault(a => a.Name.LocalName.Equals("contract", StringComparison.OrdinalIgnoreCase));

                var keyName = nameAttr?.Value ?? contractAttr?.Value;
                if (keyName != null && epUpdateMap.TryGetValue(keyName, out var newAddress))
                {
                    var addressAttr = ep.Attributes()
                        .FirstOrDefault(a => a.Name.LocalName.Equals("address", StringComparison.OrdinalIgnoreCase));

                    if (addressAttr != null)
                    {
                        addressAttr.Value = newAddress;
                    }
                    else
                    {
                        ep.SetAttributeValue("address", newAddress);
                    }
                }
            }
        }

        using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, encoding);
        xDoc.Save(writer, SaveOptions.DisableFormatting);
    }

    public void SaveAppSettings(string filePath, IEnumerable<ConnectionStringEntry> entriesToUpdate)
    {
        var encoding = DetectEncoding(filePath);
        var text = File.ReadAllText(filePath, encoding);

        var docOptions = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        var rootNode = JsonNode.Parse(text, new JsonNodeOptions { PropertyNameCaseInsensitive = true }, docOptions);
        if (rootNode is not JsonObject rootObj)
        {
            throw new InvalidOperationException($"Invalid JSON root object in {filePath}");
        }

        var updateList = entriesToUpdate.ToList();

        // 1. Update ConnectionStrings entries
        var csEntries = updateList.Where(e => e.Category == ConfigCategory.ConnectionStrings).ToList();
        if (csEntries.Count > 0)
        {
            var csKey = rootObj.Select(p => p.Key)
                .FirstOrDefault(k => k.Equals("ConnectionStrings", StringComparison.OrdinalIgnoreCase));

            if (csKey != null && rootObj[csKey] is JsonObject csObj)
            {
                foreach (var entry in csEntries)
                {
                    var existingKey = csObj.Select(p => p.Key)
                        .FirstOrDefault(k => k.Equals(entry.KeyName, StringComparison.OrdinalIgnoreCase)) ?? entry.KeyName;

                    csObj[existingKey] = JsonValue.Create(entry.NewValue);
                }
            }
            else
            {
                throw new InvalidOperationException($"No ConnectionStrings object found in {filePath}");
            }
        }

        // 2. Update ClientEndpoints / Service URLs (e.g. SSOLibConfigs, Endpoints)
        var otherEntries = updateList.Where(e => e.Category != ConfigCategory.ConnectionStrings).ToList();
        foreach (var entry in otherEntries)
        {
            bool updated = false;

            // Method A: Follow JsonPath if present
            if (!string.IsNullOrEmpty(entry.JsonPath))
            {
                var segments = entry.JsonPath.Split(':');
                JsonObject? curObj = rootObj;

                for (int i = 0; i < segments.Length - 1; i++)
                {
                    var seg = segments[i];
                    var matchingKey = curObj?.Select(p => p.Key)
                        .FirstOrDefault(k => k.Equals(seg, StringComparison.OrdinalIgnoreCase));

                    if (matchingKey != null && curObj?[matchingKey] is JsonObject nextObj)
                    {
                        curObj = nextObj;
                    }
                    else
                    {
                        curObj = null;
                        break;
                    }
                }

                if (curObj != null)
                {
                    var targetKey = segments[^1];
                    var existingKey = curObj.Select(p => p.Key)
                        .FirstOrDefault(k => k.Equals(targetKey, StringComparison.OrdinalIgnoreCase)) ?? targetKey;

                    curObj[existingKey] = JsonValue.Create(entry.NewValue);
                    updated = true;
                }
            }

            // Method B: Fallback recursive search if JsonPath wasn't resolved
            if (!updated)
            {
                UpdateJsonNodeRecursive(rootObj, entry.KeyName, entry.NewValue);
            }
        }

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        var outputJson = rootNode.ToJsonString(jsonOptions);
        File.WriteAllText(filePath, outputJson, encoding);
    }

    private List<ConnectionStringEntry> ParseWebConfig(string filePath)
    {
        var list = new List<ConnectionStringEntry>();
        try
        {
            var xDoc = XDocument.Load(filePath, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);

            // 1. Parse <connectionStrings>
            var csElement = xDoc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName.Equals("connectionStrings", StringComparison.OrdinalIgnoreCase));

            if (csElement != null)
            {
                var addElements = csElement.Elements()
                    .Where(e => e.Name.LocalName.Equals("add", StringComparison.OrdinalIgnoreCase));

                foreach (var add in addElements)
                {
                    var nameAttr = add.Attributes()
                        .FirstOrDefault(a => a.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase));
                    var connStrAttr = add.Attributes()
                        .FirstOrDefault(a => a.Name.LocalName.Equals("connectionString", StringComparison.OrdinalIgnoreCase));
                    var providerAttr = add.Attributes()
                        .FirstOrDefault(a => a.Name.LocalName.Equals("providerName", StringComparison.OrdinalIgnoreCase));

                    if (nameAttr != null)
                    {
                        var connStr = connStrAttr?.Value ?? string.Empty;
                        list.Add(new ConnectionStringEntry
                        {
                            FilePath = filePath,
                            FileType = ConfigFileType.WebConfig,
                            Category = ConfigCategory.ConnectionStrings,
                            KeyName = nameAttr.Value,
                            ProviderName = providerAttr?.Value,
                            CurrentValue = connStr,
                            NewValue = connStr,
                            IsSelected = true,
                            Status = ConfigEntryStatus.Unchanged
                        });
                    }
                }
            }

            // 2. Parse <system.serviceModel><client><endpoint address="..." />
            var endpointElements = xDoc.Descendants()
                .Where(e => e.Name.LocalName.Equals("endpoint", StringComparison.OrdinalIgnoreCase) &&
                            e.Parent != null && e.Parent.Name.LocalName.Equals("client", StringComparison.OrdinalIgnoreCase));

            foreach (var ep in endpointElements)
            {
                var addressAttr = ep.Attributes()
                    .FirstOrDefault(a => a.Name.LocalName.Equals("address", StringComparison.OrdinalIgnoreCase));
                var nameAttr = ep.Attributes()
                    .FirstOrDefault(a => a.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase));
                var contractAttr = ep.Attributes()
                    .FirstOrDefault(a => a.Name.LocalName.Equals("contract", StringComparison.OrdinalIgnoreCase));
                var bindingAttr = ep.Attributes()
                    .FirstOrDefault(a => a.Name.LocalName.Equals("binding", StringComparison.OrdinalIgnoreCase));

                if (addressAttr != null && !string.IsNullOrWhiteSpace(addressAttr.Value))
                {
                    var keyName = nameAttr?.Value;
                    if (string.IsNullOrWhiteSpace(keyName))
                    {
                        keyName = contractAttr?.Value ?? "Endpoint";
                    }

                    list.Add(new ConnectionStringEntry
                    {
                        FilePath = filePath,
                        FileType = ConfigFileType.WebConfig,
                        Category = ConfigCategory.ClientEndpoints,
                        KeyName = keyName,
                        ProviderName = bindingAttr?.Value ?? contractAttr?.Value,
                        CurrentValue = addressAttr.Value,
                        NewValue = addressAttr.Value,
                        IsSelected = true,
                        Status = ConfigEntryStatus.Unchanged
                    });
                }
            }
        }
        catch (Exception ex)
        {
            list.Add(new ConnectionStringEntry
            {
                FilePath = filePath,
                FileType = ConfigFileType.WebConfig,
                Category = ConfigCategory.ConnectionStrings,
                KeyName = "[Parse Error]",
                CurrentValue = string.Empty,
                NewValue = string.Empty,
                IsSelected = false,
                Status = ConfigEntryStatus.Error,
                ErrorMessage = $"XML Parsing failed: {ex.Message}"
            });
        }

        return list;
    }

    private static readonly HashSet<string> KnownEndpointSectionKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "ssolibconfigs",
        "ssoconfigs",
        "sso",
        "endpoints",
        "clientendpoints",
        "serviceendpoints",
        "services",
        "wcfservices",
        "wcfendpoints",
        "apiservices",
        "apiendpoints"
    };

    private List<ConnectionStringEntry> ParseAppSettings(string filePath)
    {
        var list = new List<ConnectionStringEntry>();
        try
        {
            var encoding = DetectEncoding(filePath);
            var text = File.ReadAllText(filePath, encoding);

            var docOptions = new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };

            var rootNode = JsonNode.Parse(text, new JsonNodeOptions { PropertyNameCaseInsensitive = true }, docOptions);
            if (rootNode is JsonObject rootObj)
            {
                // 1. ConnectionStrings
                var csKey = rootObj.Select(p => p.Key)
                    .FirstOrDefault(k => k.Equals("ConnectionStrings", StringComparison.OrdinalIgnoreCase));

                if (csKey != null && rootObj[csKey] is JsonObject csObj)
                {
                    foreach (var kvp in csObj)
                    {
                        var key = kvp.Key;
                        var val = kvp.Value?.ToString() ?? string.Empty;

                        list.Add(new ConnectionStringEntry
                        {
                            FilePath = filePath,
                            FileType = ConfigFileType.AppSettingsJson,
                            Category = ConfigCategory.ConnectionStrings,
                            KeyName = key,
                            JsonPath = $"{csKey}:{key}",
                            CurrentValue = val,
                            NewValue = val,
                            IsSelected = true,
                            Status = ConfigEntryStatus.Unchanged
                        });
                    }
                }

                // 2. Client Endpoints & Service URLs (e.g. SSOLibConfigs, Endpoints, etc.)
                ExtractEndpointsFromJson(filePath, rootObj, string.Empty, null, list);
            }
        }
        catch (Exception ex)
        {
            list.Add(new ConnectionStringEntry
            {
                FilePath = filePath,
                FileType = ConfigFileType.AppSettingsJson,
                Category = ConfigCategory.ConnectionStrings,
                KeyName = "[Parse Error]",
                CurrentValue = string.Empty,
                NewValue = string.Empty,
                IsSelected = false,
                Status = ConfigEntryStatus.Error,
                ErrorMessage = $"JSON Parsing failed: {ex.Message}"
            });
        }

        return list;
    }

    private void ExtractEndpointsFromJson(
        string filePath,
        JsonObject currentObj,
        string currentPath,
        string? parentSectionName,
        List<ConnectionStringEntry> list)
    {
        foreach (var kvp in currentObj)
        {
            var key = kvp.Key;
            var valNode = kvp.Value;
            var childPath = string.IsNullOrEmpty(currentPath) ? key : $"{currentPath}:{key}";

            if (valNode is JsonObject childObj)
            {
                // Skip ConnectionStrings since it is handled separately
                if (key.Equals("ConnectionStrings", StringComparison.OrdinalIgnoreCase))
                    continue;

                ExtractEndpointsFromJson(filePath, childObj, childPath, key, list);
            }
            else if (valNode is JsonValue)
            {
                var strVal = valNode.ToString()?.Trim() ?? string.Empty;
                if (IsCandidateEndpoint(key, parentSectionName, strVal))
                {
                    list.Add(new ConnectionStringEntry
                    {
                        FilePath = filePath,
                        FileType = ConfigFileType.AppSettingsJson,
                        Category = ConfigCategory.ClientEndpoints,
                        KeyName = key,
                        ProviderName = parentSectionName ?? "Endpoint",
                        JsonPath = childPath,
                        CurrentValue = strVal,
                        NewValue = strVal,
                        IsSelected = true,
                        Status = ConfigEntryStatus.Unchanged
                    });
                }
            }
        }
    }

    private static bool IsCandidateEndpoint(string key, string? parentSectionName, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        // Must start with http:// or https:// (URL endpoint format)
        if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Check if property key indicates an endpoint
        if (key.Contains("endpoint", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("serviceurl", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("soapurl", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("apiurl", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("wcfurl", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("svcurl", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Check if parent section is a known endpoint/service container
        if (parentSectionName != null)
        {
            if (KnownEndpointSectionKeywords.Contains(parentSectionName))
                return true;

            if (parentSectionName.Contains("sso", StringComparison.OrdinalIgnoreCase) ||
                parentSectionName.Contains("endpoint", StringComparison.OrdinalIgnoreCase) ||
                parentSectionName.Contains("service", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool UpdateJsonNodeRecursive(JsonObject obj, string targetKey, string newValue)
    {
        var existingKey = obj.Select(p => p.Key)
            .FirstOrDefault(k => k.Equals(targetKey, StringComparison.OrdinalIgnoreCase));

        if (existingKey != null && obj[existingKey] is not JsonObject)
        {
            obj[existingKey] = JsonValue.Create(newValue);
            return true;
        }

        foreach (var child in obj.Select(p => p.Value).OfType<JsonObject>())
        {
            if (UpdateJsonNodeRecursive(child, targetKey, newValue))
                return true;
        }

        return false;
    }

    private static Encoding DetectEncoding(string filename)
    {
        // Default to UTF8 without BOM
        var bom = new byte[4];
        using (var file = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            int read = file.Read(bom, 0, 4);
            if (read >= 3 && bom[0] == 0xef && bom[1] == 0xbb && bom[2] == 0xbf)
                return new UTF8Encoding(true); // UTF8 with BOM
            if (read >= 2 && bom[0] == 0xff && bom[1] == 0xfe)
                return Encoding.Unicode; // UTF-16 LE
            if (read >= 2 && bom[0] == 0xfe && bom[1] == 0xff)
                return Encoding.BigEndianUnicode; // UTF-16 BE
            if (read >= 4 && bom[0] == 0 && bom[1] == 0 && bom[2] == 0xfe && bom[3] == 0xff)
                return Encoding.UTF32;
        }

        return new UTF8Encoding(false);
    }

    private static InvalidOperationException excitingInvalidOperationException(string message) => new(message);
}
