using System;
using System.Collections.Generic;
using NUnit.Framework;
using VRMCast.Core.Vrm;

namespace VRMCast.Core.Tests
{
    public class MiniJsonTests
    {
        [Test]
        public void ParsesNestedDocument()
        {
            var root = (Dictionary<string, object>)MiniJson.Parse("{\"a\":[1,2.5,-3e2,true,false,null,\"s\"],\"b\":{\"c\":\"d\"}}");
            var a = (List<object>)root["a"];
            Assert.That(a.Count, Is.EqualTo(7));
            Assert.That(a[0], Is.EqualTo(1.0));
            Assert.That(a[1], Is.EqualTo(2.5));
            Assert.That(a[2], Is.EqualTo(-300.0));
            Assert.That(a[3], Is.EqualTo(true));
            Assert.That(a[4], Is.EqualTo(false));
            Assert.That(a[5], Is.Null);
            Assert.That(a[6], Is.EqualTo("s"));
            Assert.That(((Dictionary<string, object>)root["b"])["c"], Is.EqualTo("d"));
        }

        [Test]
        public void ParsesEscapesAndUnicode()
        {
            var root = (Dictionary<string, object>)MiniJson.Parse("{\"t\":\"a\\\"b\\\\c\\n\\u00e9\\/\"}");
            Assert.That(root["t"], Is.EqualTo("a\"b\\c\né/"));
        }

        [Test]
        public void ToleratesWhitespaceAndEmptyContainers()
        {
            var root = (Dictionary<string, object>)MiniJson.Parse(" \n{ \"x\" : { } , \"y\" : [ ] }\t");
            Assert.That(((Dictionary<string, object>)root["x"]).Count, Is.EqualTo(0));
            Assert.That(((List<object>)root["y"]).Count, Is.EqualTo(0));
        }

        [TestCase("{")]
        [TestCase("{\"a\":}")]
        [TestCase("[1,]")]
        [TestCase("{\"a\":1}x")]
        [TestCase("\"unterminated")]
        [TestCase("tru")]
        public void RejectsMalformed(string json)
        {
            Assert.Throws<FormatException>(() => MiniJson.Parse(json));
        }
    }
}
