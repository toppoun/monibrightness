using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MoniBrightness;

public sealed class MonitorPreset
{
    public string Name { get; set; } = "";
    public List<MonitorPresetEntry> Monitors { get; set; } = new();
}

public sealed class MonitorPresetEntry
{
    public string MonitorId { get; set; } = "";
    public double Brightness { get; set; }
    public double Contrast { get; set; }
}

public sealed class PresetService
{
    private readonly string _path;

    private readonly JsonSerializerOptions _jsonOptions =
        new()
        {
            WriteIndented = true
        };

    public PresetService()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "MoniBrightness");

        Directory.CreateDirectory(directory);

        _path = Path.Combine(directory, "presets.json");
    }

    public List<MonitorPreset> Load()
    {
        if (!File.Exists(_path))
            return new();

        try
        {
            string json = File.ReadAllText(_path);

            return JsonSerializer.Deserialize<List<MonitorPreset>>(
                json,
                _jsonOptions) ?? new();
        }
        catch
        {
            return new();
        }
    }

    public void Save(IEnumerable<MonitorPreset> presets)
    {
        string json = JsonSerializer.Serialize(
            presets.ToList(),
            _jsonOptions);

        string tempPath = _path + ".tmp";

        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _path, true);
    }

    public MonitorPreset Capture(
        string name,
        IList<MonitorDevice> monitors)
    {
        var preset = new MonitorPreset
        {
            Name = name
        };

        foreach (MonitorDevice monitor in monitors)
        {
            string monitorId =
                monitor.Id
                ?? throw new InvalidOperationException(
                    $"Stable ID is missing for {monitor.SystemName}.");

            preset.Monitors.Add(
                new MonitorPresetEntry
                {
                    MonitorId = monitorId,
                    Brightness = monitor.Brightness,
                    Contrast = monitor.Contrast
                });
        }

        return preset;
    }
}