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

        var csElement = xDoc.Descendants()
            .FirstOrDefault(e => e.Name.LocalName.Equals("connectionStrings", StringComparison.OrdinalIgnoreCase));

        if (csElement == null)
        {
            throw excitingInvalidOperationException($"No <connectionStrings> section found in {filePath}");
        }

        var updateMap = entriesToUpdate.ToDictionary(e => e.KeyName, e => e.NewValue, StringComparer.OrdinalIgnoreCase);

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

        // Find ConnectionStrings property case-insensitively
        var csKey = rootObj.Select(p => p.Key)
            .FirstOrDefault(k => k.Equals("ConnectionStrings", StringComparison.OrdinalIgnoreCase));

        if (csKey == null || rootObj[csKey] is not JsonObject csObj)
        {
            throw new InvalidOperationException($"No ConnectionStrings object found in {filePath}");
        }

        foreach (var entry in entriesToUpdate)
        {
            // Case-insensitive match within ConnectionStrings
            var existingKey = csObj.Select(p => p.Key)
                .FirstOrDefault(k => k.Equals(entry.KeyName, StringComparison.OrdinalIgnoreCase)) ?? entry.KeyName;

            csObj[existingKey] = JsonValue.Create(entry.NewValue);
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
        }
        catch (Exception ex)
        {
            list.Add(new ConnectionStringEntry
            {
                FilePath = filePath,
                FileType = ConfigFileType.WebConfig,
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
                            KeyName = key,
                            CurrentValue = val,
                            NewValue = val,
                            IsSelected = true,
                            Status = ConfigEntryStatus.Unchanged
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            list.Add(new ConnectionStringEntry
            {
                FilePath = filePath,
                FileType = ConfigFileType.AppSettingsJson,
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
