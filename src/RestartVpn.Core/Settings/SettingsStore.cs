using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace RestartVpn.Core.Settings
{
    public enum IssueSeverity { Error, Warning, Info }

    public sealed class SettingsIssue
    {
        public SettingsIssue(IssueSeverity severity, string path, string message)
        {
            Severity = severity;
            Path = path ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public IssueSeverity Severity { get; }
        public string Path { get; }
        public string Message { get; }

        public override string ToString() =>
            (Path.Length > 0 ? Path + ": " : string.Empty) + Message;
    }

    public sealed class SettingsLoadResult
    {
        public AppSettings Settings { get; set; }
        public List<SettingsIssue> Issues { get; } = new List<SettingsIssue>();
        public bool Existed { get; set; }
        public string Path { get; set; }
        public bool HasErrors
        {
            get
            {
                foreach (var i in Issues) if (i.Severity == IssueSeverity.Error) return true;
                return false;
            }
        }
    }

    /// <summary>Loads/saves settings.json and validates it. Never throws on bad user input.</summary>
    public sealed class SettingsStore
    {
        private readonly AppPaths _paths;
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        public SettingsStore(AppPaths paths)
        {
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        }

        public AppPaths Paths => _paths;

        public bool Exists => File.Exists(_paths.SettingsPath);

        public SettingsLoadResult Load()
        {
            var result = new SettingsLoadResult { Path = _paths.SettingsPath };
            AppSettings settings;

            if (!File.Exists(_paths.SettingsPath))
            {
                settings = new AppSettings();
                result.Existed = false;
            }
            else
            {
                result.Existed = true;
                try
                {
                    string text = File.ReadAllText(_paths.SettingsPath, Encoding.UTF8);
                    settings = Deserialize(text);
                }
                catch (Exception ex)
                {
                    settings = new AppSettings();
                    result.Issues.Add(new SettingsIssue(IssueSeverity.Error, "settings.json",
                        "Cannot read settings: " + ex.Message + ". Using defaults; fix the file and reload."));
                }
            }

            settings.Normalize();
            result.Settings = settings;
            result.Issues.AddRange(SettingsValidator.Validate(settings));
            return result;
        }

        public void Save(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            settings.Normalize();
            _paths.EnsureDataDirectories();

            string json = Serialize(settings);
            string tmp = _paths.SettingsPath + ".tmp";
            File.WriteAllText(tmp, json, new UTF8Encoding(true));

            if (File.Exists(_paths.SettingsPath))
            {
                string backup = _paths.SettingsPath + ".bak";
                try
                {
                    File.Replace(tmp, _paths.SettingsPath, backup, true);
                    return;
                }
                catch (Exception)
                {
                    // Fall back to a plain copy below; e.g. settings file on a network share.
                }
            }

            File.Copy(tmp, _paths.SettingsPath, true);
            try { File.Delete(tmp); } catch { }
        }

        public static string Serialize(AppSettings settings)
        {
            string compact = Serializer.Serialize(settings);
            return JsonText.Prettify(compact) + Environment.NewLine;
        }

        public static AppSettings Deserialize(string json)
        {
            var settings = Serializer.Deserialize<AppSettings>(json);
            if (settings == null) settings = new AppSettings();
            settings.Normalize();
            return settings;
        }
    }
}
