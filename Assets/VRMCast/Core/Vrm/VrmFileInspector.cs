using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace VRMCast.Core.Vrm
{
    /// <summary>
    /// Result of inspecting a candidate VRM file without loading it into the engine.
    /// </summary>
    public sealed class VrmInspection
    {
        public bool IsValidGlb { get; }
        public VrmVersion Version { get; }
        public string Title { get; }
        public string Author { get; }
        public string SpecVersion { get; }
        public string Error { get; }

        private VrmInspection(bool isValidGlb, VrmVersion version, string title, string author, string specVersion, string error)
        {
            IsValidGlb = isValidGlb;
            Version = version;
            Title = title;
            Author = author;
            SpecVersion = specVersion;
            Error = error;
        }

        public static VrmInspection Invalid(string error) => new VrmInspection(false, VrmVersion.Unknown, null, null, null, error);

        public static VrmInspection Detected(VrmVersion version, string title, string author, string specVersion) =>
            new VrmInspection(true, version, title, author, specVersion, null);

        public static VrmInspection GlbWithoutVrm() =>
            new VrmInspection(true, VrmVersion.Unknown, null, null, null, VrmLoadErrors.NotVrm);
    }

    /// <summary>
    /// Pure C# inspection of a .vrm (GLB) file header. Reads only the JSON chunk, never the binary
    /// buffers, so it is cheap and safe to run before handing the file to UniVRM.
    /// </summary>
    public static class VrmFileInspector
    {
        public const uint GlbMagic = 0x46546C67; // "glTF"
        public const uint GlbVersion = 2;
        public const uint JsonChunkType = 0x4E4F534A; // "JSON"

        public const string Vrm0ExtensionName = "VRM";
        public const string Vrm1ExtensionName = "VRMC_vrm";

        /// <summary>Reads only the GLB header and JSON chunk from a file path.</summary>
        public static VrmInspection InspectFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return VrmInspection.Invalid(VrmLoadErrors.NoFile);
            if (!File.Exists(path)) return VrmInspection.Invalid(VrmLoadErrors.FileMissing);

            try
            {
                using (var stream = File.OpenRead(path))
                {
                    return Inspect(stream);
                }
            }
            catch (IOException)
            {
                return VrmInspection.Invalid(VrmLoadErrors.Unreadable);
            }
            catch (UnauthorizedAccessException)
            {
                return VrmInspection.Invalid(VrmLoadErrors.Unreadable);
            }
        }

        /// <summary>Inspects an in-memory GLB.</summary>
        public static VrmInspection Inspect(byte[] bytes)
        {
            if (bytes == null) return VrmInspection.Invalid(VrmLoadErrors.NotVrm);
            using (var ms = new MemoryStream(bytes, writable: false))
            {
                return Inspect(ms);
            }
        }

        /// <summary>Inspects a readable stream positioned at the start of a GLB.</summary>
        public static VrmInspection Inspect(Stream stream)
        {
            var header = new byte[12];
            if (!ReadExactly(stream, header, 12)) return VrmInspection.Invalid(VrmLoadErrors.NotVrm);

            var magic = BitConverter.ToUInt32(header, 0);
            var version = BitConverter.ToUInt32(header, 4);
            var totalLength = BitConverter.ToUInt32(header, 8);
            if (magic != GlbMagic) return VrmInspection.Invalid(VrmLoadErrors.NotVrm);
            if (version != GlbVersion) return VrmInspection.Invalid(VrmLoadErrors.UnsupportedGlbVersion);
            if (totalLength < 20) return VrmInspection.Invalid(VrmLoadErrors.NotVrm);

            var chunkHeader = new byte[8];
            if (!ReadExactly(stream, chunkHeader, 8)) return VrmInspection.Invalid(VrmLoadErrors.NotVrm);
            var chunkLength = BitConverter.ToUInt32(chunkHeader, 0);
            var chunkType = BitConverter.ToUInt32(chunkHeader, 4);
            if (chunkType != JsonChunkType) return VrmInspection.Invalid(VrmLoadErrors.NotVrm);
            if (chunkLength == 0 || chunkLength > totalLength - 20) return VrmInspection.Invalid(VrmLoadErrors.NotVrm);
            if (chunkLength > int.MaxValue) return VrmInspection.Invalid(VrmLoadErrors.NotVrm);

            var jsonBytes = new byte[chunkLength];
            if (!ReadExactly(stream, jsonBytes, (int)chunkLength)) return VrmInspection.Invalid(VrmLoadErrors.NotVrm);

            string json;
            try
            {
                json = Encoding.UTF8.GetString(jsonBytes).TrimEnd('\0', ' ');
            }
            catch (ArgumentException)
            {
                return VrmInspection.Invalid(VrmLoadErrors.MetadataUnreadable);
            }

            return InspectJson(json);
        }

        /// <summary>Detects the VRM version from the glTF JSON document text.</summary>
        public static VrmInspection InspectJson(string json)
        {
            object root;
            try
            {
                root = MiniJson.Parse(json);
            }
            catch (FormatException)
            {
                return VrmInspection.Invalid(VrmLoadErrors.MetadataUnreadable);
            }

            var rootObj = root as Dictionary<string, object>;
            if (rootObj == null) return VrmInspection.Invalid(VrmLoadErrors.MetadataUnreadable);

            var extensions = Get<Dictionary<string, object>>(rootObj, "extensions");
            var used = new HashSet<string>(StringComparer.Ordinal);
            if (Get<List<object>>(rootObj, "extensionsUsed") is List<object> usedList)
            {
                foreach (var item in usedList)
                {
                    if (item is string s) used.Add(s);
                }
            }

            // VRM 1.0 wins if both are present: UniVRM treats VRMC_vrm as authoritative.
            if (HasExtension(extensions, used, Vrm1ExtensionName))
            {
                var vrm = Get<Dictionary<string, object>>(extensions, Vrm1ExtensionName);
                var meta = Get<Dictionary<string, object>>(vrm, "meta");
                var title = Get<string>(meta, "name");
                var authors = Get<List<object>>(meta, "authors");
                var author = authors != null && authors.Count > 0 ? authors[0] as string : null;
                var spec = Get<string>(vrm, "specVersion");
                return VrmInspection.Detected(VrmVersion.Vrm1, title, author, spec);
            }

            if (HasExtension(extensions, used, Vrm0ExtensionName))
            {
                var vrm = Get<Dictionary<string, object>>(extensions, Vrm0ExtensionName);
                var meta = Get<Dictionary<string, object>>(vrm, "meta");
                var title = Get<string>(meta, "title");
                var author = Get<string>(meta, "author");
                var spec = Get<string>(vrm, "specVersion");
                return VrmInspection.Detected(VrmVersion.Vrm0, title, author, spec);
            }

            return VrmInspection.GlbWithoutVrm();
        }

        /// <summary>
        /// Applies the advanced-dialog override to a detection result. Returns the version that the loader
        /// should use, or Unknown when nothing can be decided.
        /// </summary>
        public static VrmVersion Resolve(VrmVersion detected, VrmVersionOverride @override)
        {
            switch (@override)
            {
                case VrmVersionOverride.ForceVrm0: return VrmVersion.Vrm0;
                case VrmVersionOverride.ForceVrm1: return VrmVersion.Vrm1;
                default: return detected;
            }
        }

        private static bool HasExtension(Dictionary<string, object> extensions, HashSet<string> used, string name)
        {
            if (extensions != null && extensions.ContainsKey(name)) return true;
            return used.Contains(name);
        }

        private static T Get<T>(Dictionary<string, object> obj, string key) where T : class
        {
            if (obj == null) return null;
            return obj.TryGetValue(key, out var value) ? value as T : null;
        }

        private static bool ReadExactly(Stream stream, byte[] buffer, int count)
        {
            var offset = 0;
            while (offset < count)
            {
                var read = stream.Read(buffer, offset, count - offset);
                if (read <= 0) return false;
                offset += read;
            }
            return true;
        }
    }
}
