using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MoniBrightness;

public sealed class MonitorNameService
{
    private readonly string _path;

    private readonly JsonSerializerOptions _jsonOptions =
        new()
        {
            WriteIndented = true
        };

    private readonly Dictionary<string, string> _names;

    public MonitorNameService()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "MoniBrightness");

        Directory.CreateDirectory(directory);

        _path = Path.Combine(
            directory,
            "monitor-names.json");

        _names = Load();
    }

    public string? GetName(string monitorId)
    {
        return _names.TryGetValue(
            monitorId,
            out string? name)
                ? name
                : null;
    }

    public void SetName(
        string monitorId,
        string? name)
    {
        string? normalized =
            string.IsNullOrWhiteSpace(name)
                ? null
                : name.Trim();

        if (normalized is null)
        {
            _names.Remove(monitorId);
        }
        else
        {
            _names[monitorId] =
                normalized;
        }

        Save();
    }

    private Dictionary<string, string> Load()
    {
        if (!File.Exists(_path))
        {
            return new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            string json =
                File.ReadAllText(_path);

            var loaded =
                JsonSerializer.Deserialize<
                    Dictionary<string, string>>(
                        json,
                        _jsonOptions);

            return loaded is null
                ? new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(
                    loaded,
                    StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
        }
    }

    private void Save()
    {
        string json =
            JsonSerializer.Serialize(
                _names,
                _jsonOptions);

        string tempPath =
            _path + ".tmp";

        File.WriteAllText(
            tempPath,
            json);

        File.Move(
            tempPath,
            _path,
            true);
    }
}