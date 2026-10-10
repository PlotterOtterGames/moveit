using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace MoveIt.Games.Tests
{
    /// <summary>The C# code rules agree with the server and the box's Java (server/test/vectors/codes.json).</summary>
    public class GiftCodeTests
    {
        static string VectorsFile()
        {
            foreach (string start in new[] { TestContext.CurrentContext.TestDirectory, Directory.GetCurrentDirectory() })
            {
                for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                {
                    string path = Path.Combine(dir.FullName, "server", "test", "vectors", "codes.json");
                    if (File.Exists(path)) return path;
                }
            }
            return null;
        }

        [Test]
        public void MatchesTheServerVectors()
        {
            string path = VectorsFile();
            if (path == null) Assert.Ignore("server/test/vectors/codes.json not found (project copied without the repo?)");
            string json = File.ReadAllText(path);
            StringAssert.Contains($"\"alphabet\": \"{GiftCode.Alphabet}\"", json);
            var valid = Regex.Matches(json, "\"input\": \"([^\"]*)\",\\s*\"code\": \"([^\"]*)\"");
            Assert.Greater(valid.Count, 10);
            foreach (Match m in valid) Assert.AreEqual(m.Groups[2].Value, GiftCode.Normalize(m.Groups[1].Value), m.Groups[1].Value);
            string invalidBlock = Regex.Match(json, "\"invalid\": \\[(.*?)\\]", RegexOptions.Singleline).Groups[1].Value;
            var invalid = Regex.Matches(invalidBlock, "\"([^\"]*)\"");
            Assert.Greater(invalid.Count, 5);
            foreach (Match m in invalid) Assert.IsNull(GiftCode.Normalize(m.Groups[1].Value), m.Groups[1].Value);
        }

        [Test]
        public void CatchesEverySingleCharacterTypo()
        {
            string body = "ABCDEFGHJKLMNPQ";
            string code = body + GiftCode.CheckChar(body);
            Assert.IsTrue(GiftCode.IsValid(code));
            for (int i = 0; i < GiftCode.Length; i++)
            {
                foreach (char c in GiftCode.Alphabet)
                {
                    if (c == code[i]) continue;
                    Assert.IsFalse(GiftCode.IsValid(code.Substring(0, i) + c + code.Substring(i + 1)), $"{i}:{c}");
                }
            }
        }

        [Test]
        public void FormatsPartialCodesWhileTyping()
        {
            Assert.AreEqual("", GiftCode.Format(""));
            Assert.AreEqual("ABCD", GiftCode.Format("ABCD"));
            Assert.AreEqual("ABCD-E", GiftCode.Format("ABCDE"));
            Assert.AreEqual("ABCD-EFGH-JKLM-NPQR", GiftCode.Format("ABCDEFGHJKLMNPQR"));
        }
    }
}
