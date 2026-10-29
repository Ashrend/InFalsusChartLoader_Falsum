using System;

namespace InFalsusChartLoader_falsum.Tests
{
    internal static class Program
    {
        private static int _failed;

        private static void Check(string name, bool ok, string detail = null)
        {
            if (ok)
            {
                Console.WriteLine($"PASS {name}");
                return;
            }

            _failed++;
            Console.WriteLine($"FAIL {name}" + (detail == null ? "" : $": {detail}"));
        }

        private static void Main()
        {
            Check("folder id", PackManifest.TryGetFolderId("[Pack]test1", out string id, out _) && id == "test1");
            Check("folder id empty", !PackManifest.TryGetFolderId("[Pack]", out _, out _));
            Check("folder id absent", !PackManifest.TryGetFolderId("test1", out _, out _));

            const string full = "{\"name\":{\"en\":\"E\",\"ja\":\"J\",\"ko\":\"K\",\"zh-Hant\":\"T\",\"zh-Hans\":\"S\"}}";
            bool parsed = PackManifest.TryParse(full, out PackManifest manifest, out string reason);
            Check("full names parse", parsed, reason);
            Check("full names en", parsed && manifest.Names[0] == "E");
            Check("full names ja", parsed && manifest.Names[1] == "J");
            Check("full names ko", parsed && manifest.Names[2] == "K");
            Check("full names zh-Hant", parsed && manifest.Names[3] == "T");
            Check("full names zh-Hans", parsed && manifest.Names[4] == "S");

            parsed = PackManifest.TryParse("{\"name\":{\"zh-Hant\":\"T\"}}", out manifest, out reason);
            Check("single language parse", parsed, reason);
            Check("single language fallback", parsed && AllNames(manifest, "T"));

            parsed = PackManifest.TryParse("{\"name\":{\"ja\":\"J\",\"zh-Hans\":\"S\"}}", out manifest, out reason);
            Check("fallback preferred", parsed && manifest.Names[0] == "J" && manifest.Names[4] == "S", reason);

            parsed = PackManifest.TryParse("{\"name\":{}}", out _, out reason);
            Check("empty name rejected", !parsed && !string.IsNullOrWhiteSpace(reason));

            parsed = PackManifest.TryParse("{\"name\":\"not an object\"}", out _, out reason);
            Check("string name rejected", !parsed && !string.IsNullOrWhiteSpace(reason));

            parsed = PackManifest.TryParse("{\"name\":{\"en\":\"E\"},\"future\":1}", out manifest, out reason);
            Check("unknown fields ignored", parsed && manifest.Names[0] == "E", reason);

            parsed = PackManifest.TryParse("not json", out _, out reason);
            Check("invalid json rejected", !parsed && !string.IsNullOrWhiteSpace(reason));

            parsed = PackManifest.TryParse("{\"name\":{\"en\":\"E\"}}", out manifest, out reason);
            Check("default category is story", parsed && manifest.Category == PackCategory.Story, reason);

            parsed = PackManifest.TryParse("{\"name\":{\"en\":\"E\"},\"pack\":\"song\"}", out manifest, out reason);
            Check("pack category song", parsed && manifest.Category == PackCategory.Song, reason);

            parsed = PackManifest.TryParse("{\"name\":{\"en\":\"E\"},\"category\":\"song\"}", out manifest, out reason);
            Check("category alias song", parsed && manifest.Category == PackCategory.Song, reason);

            parsed = PackManifest.TryParse("{\"name\":{\"en\":\"E\"},\"pack\":\"story\"}", out manifest, out reason);
            Check("pack category story", parsed && manifest.Category == PackCategory.Story, reason);

            parsed = PackManifest.TryParse("{\"name\":{\"en\":\"E\"},\"pack\":\"unknown\"}", out _, out reason);
            Check("unknown category rejected", !parsed && !string.IsNullOrWhiteSpace(reason));

            Environment.ExitCode = _failed == 0 ? 0 : 1;
        }

        private static bool AllNames(PackManifest manifest, string expected)
        {
            for (int i = 0; i < PackManifest.Languages; i++)
                if (manifest.Names[i] != expected) return false;
            return true;
        }
    }
}
