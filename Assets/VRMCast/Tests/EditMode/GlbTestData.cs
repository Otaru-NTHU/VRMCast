using System;
using System.Collections.Generic;
using System.Text;

namespace VRMCast.Core.Tests
{
    /// <summary>Builds tiny synthetic GLB containers so the inspector can be tested without real avatars.</summary>
    internal static class GlbTestData
    {
        public const string Vrm0Json =
            "{\"asset\":{\"version\":\"2.0\"},\"extensionsUsed\":[\"VRM\"],\"extensions\":{\"VRM\":{\"exporterVersion\":\"UniVRM-0.99\",\"specVersion\":\"0.0\",\"meta\":{\"title\":\"Alicia\",\"author\":\"Test Author\"}}}}";

        public const string Vrm1Json =
            "{\"asset\":{\"version\":\"2.0\"},\"extensionsUsed\":[\"VRMC_vrm\",\"VRMC_springBone\"],\"extensions\":{\"VRMC_vrm\":{\"specVersion\":\"1.0\",\"meta\":{\"name\":\"Seed-san\",\"authors\":[\"VRM Consortium\"]}}}}";

        public const string PlainGltfJson = "{\"asset\":{\"version\":\"2.0\"},\"nodes\":[]}";

        public static byte[] BuildGlb(string json, byte[] binary = null, uint version = 2, uint magic = 0x46546C67, bool padJson = true)
        {
            var jsonBytes = Encoding.UTF8.GetBytes(json);
            var jsonPadded = padJson ? Pad(jsonBytes, (byte)' ') : jsonBytes;
            var bin = binary ?? Array.Empty<byte>();
            var binPadded = Pad(bin, 0);

            var total = 12 + 8 + jsonPadded.Length + (bin.Length > 0 ? 8 + binPadded.Length : 0);
            var list = new List<byte>(total);
            list.AddRange(BitConverter.GetBytes(magic));
            list.AddRange(BitConverter.GetBytes(version));
            list.AddRange(BitConverter.GetBytes((uint)total));
            list.AddRange(BitConverter.GetBytes((uint)jsonPadded.Length));
            list.AddRange(BitConverter.GetBytes(0x4E4F534Au));
            list.AddRange(jsonPadded);
            if (bin.Length > 0)
            {
                list.AddRange(BitConverter.GetBytes((uint)binPadded.Length));
                list.AddRange(BitConverter.GetBytes(0x004E4942u));
                list.AddRange(binPadded);
            }
            return list.ToArray();
        }

        private static byte[] Pad(byte[] source, byte padByte)
        {
            var remainder = source.Length % 4;
            if (remainder == 0) return source;
            var padded = new byte[source.Length + (4 - remainder)];
            Buffer.BlockCopy(source, 0, padded, 0, source.Length);
            for (var i = source.Length; i < padded.Length; i++) padded[i] = padByte;
            return padded;
        }
    }
}
