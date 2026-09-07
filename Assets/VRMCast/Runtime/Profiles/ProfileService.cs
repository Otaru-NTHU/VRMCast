using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VRMCast.Core.Profiles;

namespace VRMCast.Profiles
{
    /// <summary>
    /// Profile files (PRD 23): one JSON per profile under persistentDataPath/Profiles. New / Save / Save As /
    /// Duplicate / Rename / Delete plus optional debounced auto-save. Capturing and applying the live settings is
    /// delegated to the bootstrap through callbacks so this class stays free of the service graph.
    /// </summary>
    public sealed class ProfileService
    {
        public const string FolderName = "Profiles";
        public const string Extension = ".json";
        public const string LastProfilePrefKey = "vrmcast.profile.last";
        public const float AutoSaveDelaySeconds = 2f;

        private readonly string _directory;
        private Func<ProfileData, ProfileData> _capture;
        private Action<ProfileData> _apply;
        private float _dirtySince = -1f;
        private bool _applying;

        public ProfileData Current { get; private set; }
        public string CurrentPath => PathFor(Current?.name ?? ProfileData.DefaultName);
        public bool IsDirty => _dirtySince >= 0f;
        public string LastError { get; private set; }

        /// <summary>Set by the bootstrap when the profile's avatar / background file is gone (PRD 23.2 badge).</summary>
        public string MissingVrmPath { get; set; }
        public string MissingImagePath { get; set; }

        public event Action ProfileChanged;
        public event Action ProfileListChanged;

        public ProfileService(string rootDirectory)
        {
            _directory = Path.Combine(rootDirectory, FolderName);
            Directory.CreateDirectory(_directory);
        }

        /// <param name="capture">Fills the given profile from the live services and returns it.</param>
        /// <param name="apply">Pushes a profile into the live services.</param>
        public void Bind(Func<ProfileData, ProfileData> capture, Action<ProfileData> apply)
        {
            _capture = capture;
            _apply = apply;
        }

        public IReadOnlyList<string> ListNames()
        {
            var names = new List<string>();
            try
            {
                foreach (var file in Directory.GetFiles(_directory, "*" + Extension))
                {
                    names.Add(Path.GetFileNameWithoutExtension(file));
                }
            }
            catch (IOException e)
            {
                Debug.LogException(e);
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        public bool Exists(string name) => File.Exists(PathFor(name));

        /// <summary>Loads the last used profile, or creates Default. Applies it to the services.</summary>
        public void LoadStartupProfile()
        {
            var last = PlayerPrefs.GetString(LastProfilePrefKey, ProfileData.DefaultName);
            if (!Load(last) && !Load(ProfileData.DefaultName))
            {
                NewProfile(ProfileData.DefaultName, fromCurrent: false);
            }
        }

        public bool Load(string name)
        {
            var path = PathFor(name);
            if (!File.Exists(path)) return false;
            try
            {
                var json = File.ReadAllText(path);
                var data = JsonUtility.FromJson<ProfileData>(json);
                if (data == null) return false;
                data.name = ProfileMapper.SanitizeName(string.IsNullOrEmpty(data.name) ? name : data.name);
                SetCurrent(data);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                LastError = "profile.error.load";
                return false;
            }
        }

        /// <summary>Creates a profile, either from the current settings (Duplicate-like) or from defaults.</summary>
        public ProfileData NewProfile(string name, bool fromCurrent)
        {
            var data = fromCurrent && Current != null && _capture != null ? _capture(new ProfileData()) : new ProfileData();
            data.name = UniqueName(ProfileMapper.SanitizeName(name));
            data.createdUtc = DateTime.UtcNow.ToString("o");
            data.modifiedUtc = data.createdUtc;
            SetCurrent(data);
            Save();
            ProfileListChanged?.Invoke();
            return data;
        }

        /// <summary>Captures the live settings into the current profile and writes it.</summary>
        public bool Save()
        {
            if (Current == null) return false;
            if (_capture != null && !_applying) Current = _capture(Current);
            Current.modifiedUtc = DateTime.UtcNow.ToString("o");
            _dirtySince = -1f;
            try
            {
                Directory.CreateDirectory(_directory);
                File.WriteAllText(CurrentPath, JsonUtility.ToJson(Current, prettyPrint: true));
                PlayerPrefs.SetString(LastProfilePrefKey, Current.name);
                PlayerPrefs.Save();
                LastError = null;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                LastError = "profile.error.save";
                return false;
            }
        }

        public bool SaveAs(string name)
        {
            if (Current == null) return false;
            var target = UniqueName(ProfileMapper.SanitizeName(name));
            Current.name = target;
            Current.createdUtc = DateTime.UtcNow.ToString("o");
            var ok = Save();
            ProfileListChanged?.Invoke();
            return ok;
        }

        public bool Duplicate()
        {
            if (Current == null) return false;
            Save();
            var copy = JsonUtility.FromJson<ProfileData>(JsonUtility.ToJson(Current));
            copy.name = UniqueName(Current.name + " copy");
            copy.createdUtc = DateTime.UtcNow.ToString("o");
            SetCurrent(copy, applyToServices: false);
            var ok = Save();
            ProfileListChanged?.Invoke();
            return ok;
        }

        public bool Rename(string newName)
        {
            if (Current == null) return false;
            var target = ProfileMapper.SanitizeName(newName);
            if (target == Current.name) return true;
            target = UniqueName(target);
            var oldPath = CurrentPath;
            Current.name = target;
            var ok = Save();
            if (ok && File.Exists(oldPath))
            {
                try { File.Delete(oldPath); }
                catch (IOException e) { Debug.LogException(e); }
            }
            ProfileListChanged?.Invoke();
            return ok;
        }

        /// <summary>Deletes the current profile and loads another one (Default is recreated if nothing is left).</summary>
        public bool Delete()
        {
            if (Current == null) return false;
            var path = CurrentPath;
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException e)
            {
                Debug.LogException(e);
                LastError = "profile.error.delete";
                return false;
            }
            _dirtySince = -1f;
            var names = ListNames();
            if (names.Count > 0) Load(names[0]);
            else NewProfile(ProfileData.DefaultName, fromCurrent: false);
            ProfileListChanged?.Invoke();
            return true;
        }

        /// <summary>Call whenever a setting changed; auto-save writes after a short quiet period.</summary>
        public void MarkDirty()
        {
            if (_applying || Current == null) return;
            if (_dirtySince < 0f) _dirtySince = Time.realtimeSinceStartup;
        }

        public void Tick()
        {
            if (Current == null || !Current.autoSave || _dirtySince < 0f) return;
            if (Time.realtimeSinceStartup - _dirtySince >= AutoSaveDelaySeconds) Save();
        }

        public void SetAutoSave(bool enabled)
        {
            if (Current == null) return;
            Current.autoSave = enabled;
            Save();
        }

        /// <summary>Flushes pending changes (call on quit).</summary>
        public void Flush()
        {
            if (Current != null && (_dirtySince >= 0f || Current.autoSave)) Save();
        }

        private void SetCurrent(ProfileData data, bool applyToServices = true)
        {
            Current = data;
            _dirtySince = -1f;
            if (applyToServices && _apply != null)
            {
                _applying = true;
                try { _apply(data); }
                finally { _applying = false; }
            }
            PlayerPrefs.SetString(LastProfilePrefKey, data.name);
            ProfileChanged?.Invoke();
        }

        private string UniqueName(string name)
        {
            if (!Exists(name)) return name;
            for (var i = 2; i < 1000; i++)
            {
                var candidate = $"{name} {i}";
                if (!Exists(candidate)) return candidate;
            }
            return name + " " + DateTime.UtcNow.Ticks;
        }

        private string PathFor(string name) => Path.Combine(_directory, ProfileMapper.SanitizeName(name) + Extension);
    }
}
