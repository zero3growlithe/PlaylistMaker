using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PlaylistMaker.Services
{
    /// <summary>One column's remembered state: stable key, visibility and width.</summary>
    public sealed class ColumnSetting
    {
        public string Key { get; set; } = string.Empty;

        public bool Visible { get; set; } = true;

        public double Width { get; set; }
    }

    /// <summary>Everything that has to survive a restart. Stored in %APPDATA%\PlaylistMaker\settings.json.</summary>
    public sealed class UserSettings
    {
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        /// <summary>Column order, visibility and widths, in display order.</summary>
        public List<ColumnSetting> Columns { get; set; } = new List<ColumnSetting>();

        public static string SettingsFilePath
        {
            get
            {
                string folder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(folder, "PlaylistMaker", "settings.json");
            }
        }

        public static UserSettings Load()
        {
            try
            {
                string path = SettingsFilePath;
                if (!File.Exists(path))
                {
                    return new UserSettings();
                }

                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return new UserSettings();
                }

                UserSettings? loaded = JsonSerializer.Deserialize<UserSettings>(json, SerializerOptions);
                if (loaded == null)
                {
                    return new UserSettings();
                }
                if (loaded.Columns == null)
                {
                    loaded.Columns = new List<ColumnSetting>();
                }
                return loaded;
            }
            catch (IOException)
            {
                return new UserSettings();
            }
            catch (UnauthorizedAccessException)
            {
                return new UserSettings();
            }
            catch (JsonException)
            {
                return new UserSettings();
            }
            catch (NotSupportedException)
            {
                return new UserSettings();
            }
        }

        public void Save()
        {
            try
            {
                string path = SettingsFilePath;
                string? folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder))
                {
                    Directory.CreateDirectory(folder);
                }
                File.WriteAllText(path, JsonSerializer.Serialize(this, SerializerOptions));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (NotSupportedException)
            {
            }
        }
    }
}
