using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LightTranslateHold
{
    internal static class AppPaths
    {
        public static string DataDir = "";
        public static string ConfigPath = "";
        public static string LogDir = "";

        public static void Initialize(string[] args)
        {
            string dataDir = null;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--data-dir" && i + 1 < args.Length) { dataDir = args[++i]; }
                else if (a.StartsWith("--data-dir=", StringComparison.OrdinalIgnoreCase)) { dataDir = a.Substring("--data-dir=".Length); }
            }

            if (String.IsNullOrWhiteSpace(dataDir))
            {
                dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LightTranslateHold");
            }
            else
            {
                dataDir = Path.GetFullPath(dataDir);
            }

            try
            {
                Directory.CreateDirectory(dataDir);
            }
            catch
            {
                // Portable fallback: keep the app usable even when roaming AppData is unavailable.
                dataDir = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "data");
                Directory.CreateDirectory(dataDir);
            }

            DataDir = dataDir;
            ConfigPath = Path.Combine(dataDir, "settings.json");
            LogDir = Path.Combine(dataDir, "logs");
        }
    }

    public sealed class Settings
    {
        public string Endpoint = "https://api.deepseek.com";
        public string Model = "deepseek-flash";
        public string ProtectedKey = "";
        public string Trigger = "Alt+1";
        public string TriggerMode = "hold";          // hold | tap
        public int HoldThresholdMs = 0;
        public string TargetLanguage = "auto";       // auto | zh | en
        public int MaxWidth = 560;
        public int FontSize = 11;
        public bool RestoreClipboard = true;
        public bool AutoStart = false;
        public string Theme = "auto";                // auto | light | dark

        public string ApiKey
        {
            get
            {
                if (String.IsNullOrEmpty(ProtectedKey)) return "";
                try
                {
                    byte[] data = Convert.FromBase64String(ProtectedKey);
                    byte[] plain = System.Security.Cryptography.ProtectedData.Unprotect(
                        data, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(plain);
                }
                catch { return ""; }
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value))
                {
                    ProtectedKey = "";
                }
                else
                {
                    byte[] plain = Encoding.UTF8.GetBytes(value.Trim());
                    byte[] data = System.Security.Cryptography.ProtectedData.Protect(
                        plain, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                    ProtectedKey = Convert.ToBase64String(data);
                }
            }
        }

        public Keys TriggerKeyCode
        {
            get
            {
                Keys key;
                int mods;
                string error;
                if (Hotkey.TryParse(Trigger, out key, out mods, out error)) return key;
                return Keys.D1;
            }
        }

        public int TriggerModifiers
        {
            get
            {
                Keys key;
                int mods;
                string error;
                if (Hotkey.TryParse(Trigger, out key, out mods, out error)) return mods;
                return Hotkey.MOD_ALT;
            }
        }

        public string HotkeyText
        {
            get { return String.IsNullOrEmpty(Trigger) ? "Alt+1" : Trigger; }
        }

        public Settings Clone()
        {
            Settings s = new Settings();
            s.Endpoint = Endpoint;
            s.Model = Model;
            s.ProtectedKey = ProtectedKey;
            s.Trigger = Trigger;
            s.TriggerMode = TriggerMode;
            s.HoldThresholdMs = HoldThresholdMs;
            s.TargetLanguage = TargetLanguage;
            s.MaxWidth = MaxWidth;
            s.FontSize = FontSize;
            s.RestoreClipboard = RestoreClipboard;
            s.AutoStart = AutoStart;
            s.Theme = Theme;
            return s;
        }

        public static Settings Load()
        {
            if (!File.Exists(AppPaths.ConfigPath)) return new Settings();
            string json = File.ReadAllText(AppPaths.ConfigPath, Encoding.UTF8);
            JavaScriptSerializer ser = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
            Dictionary<string, object> d = ser.Deserialize<Dictionary<string, object>>(json);
            if (d == null) throw new Exception("配置文件格式不正确：" + AppPaths.ConfigPath);
            Settings s = new Settings();
            s.Endpoint = GetString(d, "endpoint", s.Endpoint);
            s.Model = GetString(d, "model", s.Model);
            s.ProtectedKey = GetString(d, "protectedKey", "");
            s.Trigger = GetString(d, "trigger", GetString(d, "triggerKey", s.Trigger));
            s.TriggerMode = GetString(d, "triggerMode", s.TriggerMode);
            s.HoldThresholdMs = GetInt(d, "holdThresholdMs", s.HoldThresholdMs);
            s.TargetLanguage = GetString(d, "targetLanguage", s.TargetLanguage);
            s.MaxWidth = GetInt(d, "maxWidth", s.MaxWidth);
            s.FontSize = GetInt(d, "fontSize", s.FontSize);
            s.RestoreClipboard = GetBool(d, "restoreClipboard", s.RestoreClipboard);
            s.AutoStart = GetBool(d, "autoStart", s.AutoStart);
            s.Theme = GetString(d, "theme", s.Theme);
            s.Normalize();
            return s;
        }

        public void Normalize()
        {
            if (String.IsNullOrWhiteSpace(Endpoint)) Endpoint = "https://api.deepseek.com";
            if (String.IsNullOrWhiteSpace(Model)) Model = "deepseek-flash";
            Trigger = Hotkey.NormalizeOrFallback(Trigger);
            if (TriggerMode != "tap") TriggerMode = "hold";
            if (HoldThresholdMs < 0) HoldThresholdMs = 0;
            if (HoldThresholdMs > 2000) HoldThresholdMs = 2000;
            if (TargetLanguage != "zh" && TargetLanguage != "en") TargetLanguage = "auto";
            if (MaxWidth < 280) MaxWidth = 280;
            if (MaxWidth > 1000) MaxWidth = 1000;
            if (FontSize < 8) FontSize = 8;
            if (FontSize > 20) FontSize = 20;
            if (Theme != "light" && Theme != "dark") Theme = "auto";
        }

        public void Save()
        {
            Normalize();
            Directory.CreateDirectory(AppPaths.DataDir);
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["endpoint"] = Endpoint;
            d["model"] = Model;
            d["protectedKey"] = ProtectedKey;
            d["trigger"] = Trigger;
            d["triggerMode"] = TriggerMode;
            d["holdThresholdMs"] = HoldThresholdMs;
            d["targetLanguage"] = TargetLanguage;
            d["maxWidth"] = MaxWidth;
            d["fontSize"] = FontSize;
            d["restoreClipboard"] = RestoreClipboard;
            d["autoStart"] = AutoStart;
            d["theme"] = Theme;
            string json = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.Serialize(d);
            string tmp = AppPaths.ConfigPath + ".tmp";
            File.WriteAllText(tmp, json, new UTF8Encoding(false));
            if (File.Exists(AppPaths.ConfigPath)) File.Replace(tmp, AppPaths.ConfigPath, null);
            else File.Move(tmp, AppPaths.ConfigPath);
        }

        public void ApplyAutoStart()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key == null) return;
                    if (AutoStart)
                        key.SetValue("LightTranslateHold", "\"" + Application.ExecutablePath + "\" --tray");
                    else
                        key.DeleteValue("LightTranslateHold", false);
                }
            }
            catch { }
        }

        private static string GetString(Dictionary<string, object> d, string key, string fallback)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return fallback;
            return Convert.ToString(v);
        }

        private static int GetInt(Dictionary<string, object> d, string key, int fallback)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return fallback;
            int result;
            return Int32.TryParse(Convert.ToString(v), out result) ? result : fallback;
        }

        private static bool GetBool(Dictionary<string, object> d, string key, bool fallback)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return fallback;
            bool result;
            return Boolean.TryParse(Convert.ToString(v), out result) ? result : fallback;
        }
    }

    internal static class Log
    {
        private static readonly object Gate = new object();
        private static string _currentDate = "";

        public static void Init()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.LogDir);
                DateTime cutoff = DateTime.Now.AddDays(-7);
                foreach (string file in Directory.GetFiles(AppPaths.LogDir, "app-*.log"))
                {
                    try { if (File.GetLastWriteTime(file) < cutoff) File.Delete(file); } catch { }
                }
            }
            catch { }
        }

        public static void Error(Exception ex)
        {
            Write("ERROR", ex == null ? "unknown" : ex.ToString());
        }

        public static void Error(string message)
        {
            Write("ERROR", message);
        }

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        private static void Write(string level, string detail)
        {
            try
            {
                if (String.IsNullOrEmpty(detail)) detail = "";
                if (detail.Length > 8000) detail = detail.Substring(0, 8000);
                lock (Gate)
                {
                    Directory.CreateDirectory(AppPaths.LogDir);
                    string date = DateTime.Now.ToString("yyyyMMdd");
                    string path = Path.Combine(AppPaths.LogDir, "app-" + date + ".log");
                    FileInfo fi = new FileInfo(path);
                    if (fi.Exists && fi.Length > 2 * 1024 * 1024) return;
                    string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " | " + level + " | "
                        + detail.Replace("\r", " ").Replace("\n", " ") + Environment.NewLine;
                    File.AppendAllText(path, line, new UTF8Encoding(false));
                    _currentDate = date;
                }
            }
            catch { }
        }
    }
}
