using System.IO;
using NUnit.Framework;
using VRMCast.Core.Vrm;

namespace VRMCast.Core.Tests
{
    public class VrmFileInspectorTests
    {
        [Test]
        public void DetectsVrm0FromGlb()
        {
            var result = VrmFileInspector.Inspect(GlbTestData.BuildGlb(GlbTestData.Vrm0Json));

            Assert.That(result.IsValidGlb, Is.True);
            Assert.That(result.Version, Is.EqualTo(VrmVersion.Vrm0));
            Assert.That(result.Title, Is.EqualTo("Alicia"));
            Assert.That(result.Author, Is.EqualTo("Test Author"));
            Assert.That(result.SpecVersion, Is.EqualTo("0.0"));
            Assert.That(result.Error, Is.Null);
        }

        [Test]
        public void DetectsVrm1FromGlb()
        {
            var result = VrmFileInspector.Inspect(GlbTestData.BuildGlb(GlbTestData.Vrm1Json, new byte[] { 1, 2, 3 }));

            Assert.That(result.IsValidGlb, Is.True);
            Assert.That(result.Version, Is.EqualTo(VrmVersion.Vrm1));
            Assert.That(result.Title, Is.EqualTo("Seed-san"));
            Assert.That(result.Author, Is.EqualTo("VRM Consortium"));
            Assert.That(result.SpecVersion, Is.EqualTo("1.0"));
        }

        [Test]
        public void Vrm1WinsWhenBothExtensionsPresent()
        {
            const string json = "{\"extensions\":{\"VRM\":{\"meta\":{\"title\":\"old\"}},\"VRMC_vrm\":{\"specVersion\":\"1.0\",\"meta\":{\"name\":\"new\"}}}}";
            var result = VrmFileInspector.InspectJson(json);

            Assert.That(result.Version, Is.EqualTo(VrmVersion.Vrm1));
            Assert.That(result.Title, Is.EqualTo("new"));
        }

        [Test]
        public void DetectsFromExtensionsUsedWhenExtensionsObjectMissing()
        {
            var result = VrmFileInspector.InspectJson("{\"extensionsUsed\":[\"VRM\"]}");
            Assert.That(result.Version, Is.EqualTo(VrmVersion.Vrm0));
            Assert.That(result.Title, Is.Null);
        }

        [Test]
        public void PlainGltfIsValidGlbButNotVrm()
        {
            var result = VrmFileInspector.Inspect(GlbTestData.BuildGlb(GlbTestData.PlainGltfJson));

            Assert.That(result.IsValidGlb, Is.True);
            Assert.That(result.Version, Is.EqualTo(VrmVersion.Unknown));
            Assert.That(result.Error, Is.EqualTo(VrmLoadErrors.NotVrm));
        }

        [Test]
        public void RejectsWrongMagic()
        {
            var result = VrmFileInspector.Inspect(GlbTestData.BuildGlb(GlbTestData.Vrm0Json, magic: 0x12345678));
            Assert.That(result.IsValidGlb, Is.False);
            Assert.That(result.Error, Is.EqualTo(VrmLoadErrors.NotVrm));
        }

        [Test]
        public void RejectsUnsupportedGlbVersion()
        {
            var result = VrmFileInspector.Inspect(GlbTestData.BuildGlb(GlbTestData.Vrm0Json, version: 1));
            Assert.That(result.IsValidGlb, Is.False);
            Assert.That(result.Error, Is.EqualTo(VrmLoadErrors.UnsupportedGlbVersion));
        }

        [Test]
        public void RejectsTruncatedFile()
        {
            var glb = GlbTestData.BuildGlb(GlbTestData.Vrm0Json);
            var truncated = new byte[40];
            System.Array.Copy(glb, truncated, truncated.Length);

            var result = VrmFileInspector.Inspect(truncated);
            Assert.That(result.IsValidGlb, Is.False);
        }

        [Test]
        public void RejectsEmptyAndGarbage()
        {
            Assert.That(VrmFileInspector.Inspect(new byte[0]).IsValidGlb, Is.False);
            Assert.That(VrmFileInspector.Inspect(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 }).IsValidGlb, Is.False);
            Assert.That(VrmFileInspector.Inspect((byte[])null).IsValidGlb, Is.False);
        }

        [Test]
        public void MalformedJsonReportsMetadataError()
        {
            var result = VrmFileInspector.Inspect(GlbTestData.BuildGlb("{\"extensions\":{\"VRM\":"));
            Assert.That(result.IsValidGlb, Is.False);
            Assert.That(result.Error, Is.EqualTo(VrmLoadErrors.MetadataUnreadable));
        }

        [Test]
        public void MissingFileReportsFriendlyError()
        {
            var result = VrmFileInspector.InspectFile(Path.Combine(Path.GetTempPath(), "vrmcast-does-not-exist-" + Path.GetRandomFileName() + ".vrm"));
            Assert.That(result.IsValidGlb, Is.False);
            Assert.That(result.Error, Is.EqualTo(VrmLoadErrors.FileMissing));

            Assert.That(VrmFileInspector.InspectFile(null).Error, Is.EqualTo(VrmLoadErrors.NoFile));
            Assert.That(VrmFileInspector.InspectFile("  ").Error, Is.EqualTo(VrmLoadErrors.NoFile));
        }

        [Test]
        public void InspectsRealFileOnDisk()
        {
            var path = Path.Combine(Path.GetTempPath(), "vrmcast-test-" + Path.GetRandomFileName() + ".vrm");
            try
            {
                File.WriteAllBytes(path, GlbTestData.BuildGlb(GlbTestData.Vrm1Json, new byte[64]));
                var result = VrmFileInspector.InspectFile(path);
                Assert.That(result.Version, Is.EqualTo(VrmVersion.Vrm1));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void OverrideResolvesVersion()
        {
            Assert.That(VrmFileInspector.Resolve(VrmVersion.Unknown, VrmVersionOverride.AutoDetect), Is.EqualTo(VrmVersion.Unknown));
            Assert.That(VrmFileInspector.Resolve(VrmVersion.Vrm1, VrmVersionOverride.AutoDetect), Is.EqualTo(VrmVersion.Vrm1));
            Assert.That(VrmFileInspector.Resolve(VrmVersion.Vrm1, VrmVersionOverride.ForceVrm0), Is.EqualTo(VrmVersion.Vrm0));
            Assert.That(VrmFileInspector.Resolve(VrmVersion.Unknown, VrmVersionOverride.ForceVrm1), Is.EqualTo(VrmVersion.Vrm1));
        }
    }
}
