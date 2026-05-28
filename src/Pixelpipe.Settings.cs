using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace Pixelpipe
{
    internal sealed partial class TrayContext
    {
        private List<RemoteProfile> LoadProfiles()
        {
            List<RemoteProfile> result = new List<RemoteProfile>();
            try
            {
                Dictionary<string, object> root = ReadSettingsRoot();
                object profilesObj;
                if (root.TryGetValue("Profiles", out profilesObj))
                {
                    object[] arr = profilesObj as object[];
                    if (arr != null)
                    {
                        for (int i = 0; i < arr.Length; i++)
                        {
                            Dictionary<string, object> d = arr[i] as Dictionary<string, object>;
                            if (d == null) continue;
                            RemoteProfile p = new RemoteProfile();
                            p.Id = ToStringValue(GetDictValue(d, "Id"), Guid.NewGuid().ToString("N"));
                            p.Label = ToStringValue(GetDictValue(d, "Label"), "Remote");
                            p.Provider = ToStringValue(GetDictValue(d, "Provider"), "custom");
                            p.Remote = ToStringValue(GetDictValue(d, "Remote"), DefaultRemoteName);
                            p.DriveLetter = ToStringValue(GetDictValue(d, "DriveLetter"), DefaultDriveLetter);
                            p.MountMode = ToStringValue(GetDictValue(d, "MountMode"), "network");
                            p.AutoMount = ToBool(GetDictValue(d, "AutoMount"));
                            p.FullCache = ToBool(GetDictValue(d, "FullCache"));
                            result.Add(p);
                        }
                    }
                }
            }
            catch { }

            if (result.Count == 0)
            {
                RemoteProfile p = new RemoteProfile();
                p.Label = "Pixeldrain";
                p.Provider = "pixeldrain";
                p.Remote = NormalizeRemoteName(LoadSetting("RemoteName", DefaultRemoteName));
                p.DriveLetter = NormalizeDriveLetter(LoadSetting("DriveLetter", DefaultDriveLetter));
                p.MountMode = NormalizeMountMode(LoadSetting("MountMode", "network"));
                p.AutoMount = false;
                result.Add(p);
            }
            return result;
        }

        private void SaveProfiles()
        {
            try
            {
                Dictionary<string, object> root = ReadSettingsRoot();
                List<object> list = new List<object>();
                for (int i = 0; i < profiles.Count; i++)
                {
                    RemoteProfile p = profiles[i];
                    Dictionary<string, object> d = new Dictionary<string, object>();
                    d["Id"] = p.Id;
                    d["Label"] = p.Label;
                    d["Provider"] = p.Provider;
                    d["Remote"] = NormalizeRemoteName(p.Remote);
                    d["DriveLetter"] = NormalizeDriveLetter(p.DriveLetter);
                    d["MountMode"] = NormalizeMountMode(p.MountMode);
                    d["AutoMount"] = p.AutoMount;
                    d["FullCache"] = p.FullCache;
                    list.Add(d);
                }
                root["Profiles"] = list.ToArray();
                root["BandwidthLimit"] = selectedBandwidth;
                WriteSettingsRoot(root);
            }
            catch { }
        }

        private Dictionary<string, object> ReadSettingsRoot()
        {
            try
            {
                if (!File.Exists(settingsFile)) return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                string json = File.ReadAllText(settingsFile, Encoding.UTF8);
                JavaScriptSerializer js = new JavaScriptSerializer();
                Dictionary<string, object> parsed = js.DeserializeObject(json) as Dictionary<string, object>;
                if (parsed != null) return new Dictionary<string, object>(parsed, StringComparer.OrdinalIgnoreCase);
            }
            catch { }
            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }

        private void WriteSettingsRoot(Dictionary<string, object> root)
        {
            try
            {
                Directory.CreateDirectory(settingsDir);
                JavaScriptSerializer js = new JavaScriptSerializer();
                File.WriteAllText(settingsFile, js.Serialize(root), Encoding.UTF8);
            }
            catch { }
        }

        private string LoadSettingRaw(string name)
        {
            try
            {
                Dictionary<string, object> root = ReadSettingsRoot();
                object v;
                if (root.TryGetValue(name, out v) && v != null) return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch { }
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\" + AppName, false))
                {
                    object v = key == null ? null : key.GetValue(name);
                    if (v != null) return v.ToString();
                }
            }
            catch { }
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\" + LegacyAppName, false))
                {
                    object v = key == null ? null : key.GetValue(name);
                    if (v != null) return v.ToString();
                }
            }
            catch { }
            return "";
        }

        private string LoadSetting(string name, string defaultValue)
        {
            string value = LoadSettingRaw(name);
            return String.IsNullOrEmpty(value) ? defaultValue : value;
        }

        private void SaveSetting(string name, string value)
        {
            try
            {
                Dictionary<string, object> root = ReadSettingsRoot();
                root[name] = value ?? "";
                WriteSettingsRoot(root);
            }
            catch { }
        }

        private void DeleteSetting(string name)
        {
            try
            {
                Dictionary<string, object> root = ReadSettingsRoot();
                if (root.ContainsKey(name)) root.Remove(name);
                WriteSettingsRoot(root);
            }
            catch { }
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey("Software\\" + AppName)) { key.DeleteValue(name, false); }
            }
            catch { }
        }
    }
}
