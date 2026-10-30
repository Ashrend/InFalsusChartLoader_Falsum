using System;
using System.IO;
using System.IO.Compression;
using System.Text;

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
            // Some hosts deny child processes writes below the user temp folder, and the falsum
            // reader extracts its audio and cover there. Keep that cache in the test output
            // instead; the production cache path is unchanged. Test-only.
            string temp = Path.Combine(AppContext.BaseDirectory, ".test-temp");
            Directory.CreateDirectory(temp);
            Environment.SetEnvironmentVariable("TMP", temp);
            Environment.SetEnvironmentVariable("TEMP", temp);

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

            OverlayTests();
            JacketTests();
            RoutingTests();

            Environment.ExitCode = _failed == 0 ? 0 : 1;
        }

        private static void OverlayTests()
        {
            string folder = TempFolder();
            try
            {
                File.WriteAllBytes(Path.Combine(folder, "j.png"), new byte[] { 1 });
                File.WriteAllBytes(Path.Combine(folder, "b.png"), new byte[] { 1 });
                string json = "{\"name\":\"New\",\"composer\":\"New Artist\",\"preview_seconds\":[5,30],"
                    + "\"illust\":[\"A\",null,\"B\",\"C\"],\"jacket\":\"j.png\","
                    + "\"background\":[null,null,\"b.png\",null],\"charter\":[\"a\",\"b\",\"c\",\"d\"],"
                    + "\"lv\":[1,2,3,4],\"id\":\"ignored\",\"song\":\"ignored.wav\","
                    + "\"chart\":\"ignored.spc\",\"future\":1}";
                File.WriteAllText(Path.Combine(folder, "if"), json);

                bool parsed = IfOverlay.TryRead(folder, out IfOverlay overlay, out string why);
                Check("overlay parses", parsed, why);
                Check("overlay any", overlay != null && overlay.Any);
                Check("overlay name", overlay?.Name == "New");
                Check("overlay preview", overlay != null && overlay.HasPreview && overlay.PreviewStart == 5f
                      && overlay.PreviewEnd == 30f);
                Check("overlay illust null slot", overlay?.Illustrators?[1] == string.Empty);
                Check("overlay jacket all four", overlay != null
                      && overlay.Jackets[0] == Path.Combine(folder, "j.png")
                      && overlay.Jackets[3] == Path.Combine(folder, "j.png"));
                Check("overlay background null slot", overlay != null && overlay.Backgrounds[0] == null
                      && overlay.Backgrounds[2] == Path.Combine(folder, "b.png"));
                Check("overlay charter four", overlay?.Charters?[3] == "d");

                // Apply(): display fields overwrite a falsum-shaped ChartInfo, per-difficulty fields only fill gaps.
                var info = new ChartInfo
                {
                    Name = "Old",
                    Composer = "Old Artist",
                    PreviewStart = 1f,
                    PreviewEnd = 2f,
                    Illustrators = new[] { "old", "old", "old", "old" },
                    JacketPaths = new[] { "old", "old", "old", "old" },
                    BackgroundPaths = new[] { "old", "old", "old", "old" },
                    Charters = new[] { "", "", "", "D" },
                    Level = new int?[] { null, null, null, 15 },
                    NoChart = new bool[ChartInfo.Difficulties],
                };
                // Guarded: a parser regression should fail these checks, not abort the suite with an NRE.
                if (overlay != null) overlay.Apply(info);
                Check("overlay apply name", info.Name == "New");
                Check("overlay apply composer", info.Composer == "New Artist");
                Check("overlay apply preview", info.PreviewStart == 5f && info.PreviewEnd == 30f);
                Check("overlay apply illustrators", info.Illustrators?[0] == "A"
                      && info.Illustrators?[1] == string.Empty && info.Illustrators?[3] == "C");
                Check("overlay apply jackets", info.JacketPaths?[0] == Path.Combine(folder, "j.png")
                      && info.JacketPaths?[3] == Path.Combine(folder, "j.png"));
                Check("overlay apply backgrounds", info.BackgroundPaths?[0] == null
                      && info.BackgroundPaths?[2] == Path.Combine(folder, "b.png"));
                Check("overlay apply charter gap", info.Charters[0] == "a");
                Check("overlay apply charter keeps falsum", info.Charters[3] == "D");
                Check("overlay apply level gap", info.Level[0] == 1);
                Check("overlay apply level keeps falsum", info.Level[3] == 15);

                var blocked = new ChartInfo
                {
                    NoChart = new bool[ChartInfo.Difficulties],
                    Charters = new[] { "", "", "", "" },
                    Level = new int?[ChartInfo.Difficulties],
                };
                blocked.NoChart[2] = true;
                if (overlay != null) overlay.Apply(blocked);
                Check("overlay skips no-chart charter", blocked.Charters[2] == "");
                Check("overlay skips no-chart level", blocked.Level[2] == null);

                var fresh = new ChartInfo
                {
                    NoChart = new bool[ChartInfo.Difficulties],
                    Charters = null,
                    Level = null,
                };
                if (overlay != null) overlay.Apply(fresh);
                Check("overlay allocates charters", fresh.Charters != null
                      && fresh.Charters.Length == ChartInfo.Difficulties && fresh.Charters[0] == "a");
                Check("overlay allocates levels", fresh.Level != null
                      && fresh.Level.Length == ChartInfo.Difficulties && fresh.Level[0] == 1);

                // A single charter value belongs to the last difficulty only.
                File.WriteAllText(Path.Combine(folder, "if"), "{\"charter\":\"solo\"}");
                parsed = IfOverlay.TryRead(folder, out overlay, out why);
                Check("overlay single charter parses", parsed, why);
                Check("overlay single charter writes index 3", overlay?.Charters?[3] == "solo");
                Check("overlay single charter leaves nulls", overlay != null && overlay.Charters[0] == null
                      && overlay.Charters[1] == null && overlay.Charters[2] == null);

                File.WriteAllText(Path.Combine(folder, "if"), "{\"jacket\":\"missing.png\"}");
                parsed = IfOverlay.TryRead(folder, out overlay, out why);
                // The non-empty reason below is compiled only in Debug, so run this suite in Debug.
                Check("overlay missing jacket rejected", !parsed && !string.IsNullOrWhiteSpace(why));

                File.WriteAllText(Path.Combine(folder, "if"), "{\"lv\":[1.5,2,3,4]}");
                parsed = IfOverlay.TryRead(folder, out overlay, out why);
                Check("overlay fractional level rejected", !parsed);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        private static void JacketTests()
        {
            Check("null jackets rejected", !new ChartInfo().HasJackets());
            Check("one missing jacket rejected", !new ChartInfo
            {
                JacketPaths = new[] { "a.png", null, "c.png", "d.png" },
            }.HasJackets());
            Check("four jackets accepted", new ChartInfo
            {
                JacketPaths = new[] { "a.png", "b.png", "c.png", "d.png" },
            }.HasJackets());
        }

        private static void RoutingTests()
        {
            string a = TempFolder(), b = TempFolder(), c = TempFolder(), d = TempFolder(), e = TempFolder();
            string f = TempFolder();
            string g = TempFolder();

            try
            {
                // 1. falsum-only, with a cover
                WriteFalsum(a, "songA", cover: true, title: "Title A", artist: "Artist A", rating: 15, designer: "D");
                ChartInfoReader.TryRead(a, out ChartInfo ia, out string ra);
                Check("falsum only", ia != null, ra);
                Check("falsum title", ia?.Name == "Title A");
                Check("falsum preview default", ia != null && ia.PreviewStart == 0f && ia.PreviewEnd == 60f);

                // 2. falsum-only, no cover -> skip
                WriteFalsum(b, "songB", cover: false, title: "B", artist: "B", rating: null, designer: null);
                // The non-empty reason below is compiled only in Debug, so run this suite in Debug.
                Check("no cover skipped", !ChartInfoReader.TryRead(b, out _, out string rb)
                      && !string.IsNullOrEmpty(rb));

                // 3. falsum + an if that gives display keys only
                WriteFalsum(c, "songC", cover: true, title: "Old", artist: "Old", rating: null, designer: null);
                File.WriteAllBytes(Path.Combine(c, "j.png"), new byte[] { 1 });
                File.WriteAllText(Path.Combine(c, "if"),
                    "{\"name\":\"New\",\"composer\":\"New Artist\",\"preview_seconds\":[5,30],"
                    + "\"jacket\":\"j.png\",\"charter\":[\"a\",\"b\",\"c\",\"d\"],\"lv\":[1,2,3,4],"
                    + "\"id\":\"other\",\"song\":\"x.wav\",\"chart\":\"y.spc\"}");
                Check("overlay routing", ChartInfoReader.TryRead(c, out ChartInfo ic, out string rc), rc);
                Check("overlay name wins", ic?.Name == "New");
                Check("overlay id ignored", ic?.BaseName == "songC");
                Check("falsum charts kept", ic != null && ic.ChartBytes[3] != null && !ic.NoChart[3]);
                Check("overlay lv fills", ic?.Level?[3] == 4);
                Check("overlay charter fills", ic?.Charters?[3] == "d");

                // 4. the if's jacket points at a file that is not there -> skip
                WriteFalsum(d, "songD", cover: true, title: "D", artist: "D", rating: null, designer: null);
                File.WriteAllText(Path.Combine(d, "if"), "{\"jacket\":\"missing.png\"}");
                // The non-empty reason below is compiled only in Debug, so run this suite in Debug.
                Check("bad overlay skipped", !ChartInfoReader.TryRead(d, out _, out string rd)
                      && !string.IsNullOrEmpty(rd));

                // 5. broken falsum + a full if -> fall back to the strict if
                File.WriteAllText(Path.Combine(e, "bad.falsum"), "not a zip");
                WriteFullIf(e, "ifSong");
                Check("broken falsum falls back", ChartInfoReader.TryRead(e, out ChartInfo ie, out string re), re);
                Check("fallback uses if id", ie?.BaseName == "ifSong");
                Check("fallback uses if charts", ie != null && ie.ChartBytes == null && ie.ChartPaths[3] != null);

                // 6. falsum with no cover + an if that supplies an existing jacket -> the overlay
                // gates the song in, but the charts still come from the falsum.
                WriteFalsum(f, "songF", cover: false, title: "F", artist: "F", rating: null, designer: null);
                File.WriteAllBytes(Path.Combine(f, "j.png"), new byte[] { 1 });
                File.WriteAllText(Path.Combine(f, "if"), "{\"jacket\":\"j.png\"}");
                bool readF = ChartInfoReader.TryRead(f, out ChartInfo infoF, out string rf);
                Check("overlay jacket rescues no cover", readF && infoF != null
                      && infoF.JacketPaths?[0] == Path.Combine(f, "j.png")
                      && infoF.ChartBytes?[3] != null && infoF.NoChart?[3] == false, rf);

                // 7. an if-only folder: the if is the whole chart description, with no falsum.
                WriteFullIf(g, "ifOnly");
                Check("if only", ChartInfoReader.TryRead(g, out ChartInfo ig, out string rg), rg);
                Check("if only id", ig?.BaseName == "ifOnly");
                Check("if only chart", ig?.ChartPaths?[3] != null);
                Check("if only no compiled charts", ig != null && ig.ChartBytes == null);
                Check("if only no falsum", !FalsumReader.HasPackage(g));
            }
            finally
            {
                Directory.Delete(a, true);
                Directory.Delete(b, true);
                Directory.Delete(c, true);
                Directory.Delete(d, true);
                Directory.Delete(e, true);
                Directory.Delete(f, true);
                Directory.Delete(g, true);
            }
        }

        private static void WriteFalsum(string folder, string songId, bool cover, string title, string artist,
                                        int? rating, string designer)
        {
            var json = new StringBuilder();
            json.Append("{\"songId\":\"").Append(songId).Append("\",")
                .Append("\"title\":\"").Append(title).Append("\",")
                .Append("\"artist\":\"").Append(artist).Append("\",")
                .Append("\"audio\":{\"file\":\"a.wav\",\"durationMs\":60000},");
            if (cover) json.Append("\"cover\":{\"file\":\"c.png\"},");
            json.Append("\"difficulties\":[{\"index\":0,\"available\":false},"
                        + "{\"index\":1,\"available\":false},{\"index\":2,\"available\":false},"
                        + "{\"index\":3,\"available\":true,\"chart\":\"chart.txt\"");
            if (rating.HasValue) json.Append(",\"rating\":").Append(rating.Value);
            if (designer != null) json.Append(",\"chartDesigner\":\"").Append(designer).Append('"');
            json.Append("}]}");

            using ZipArchive archive = ZipFile.Open(Path.Combine(folder, songId + ".falsum"), ZipArchiveMode.Create);
            WriteEntry(archive, "song.json", json.ToString());
            WriteEntry(archive, "a.wav", "audio");
            if (cover) WriteEntry(archive, "c.png", "png");
            WriteEntry(archive, "chart.txt", "chart(120,4)\ntap(0,1,0)\n");
        }

        private static void WriteFullIf(string folder, string id)
        {
            File.WriteAllBytes(Path.Combine(folder, "song.wav"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(folder, "jacket.png"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(folder, "chart.spc"), new byte[] { 1 });
            File.WriteAllText(Path.Combine(folder, "if"),
                "{\"id\":\"" + id + "\",\"name\":\"Full\",\"composer\":\"Composer\","
                + "\"song\":\"song.wav\",\"jacket\":\"jacket.png\",\"preview_seconds\":[1,2],"
                + "\"chart\":[null,null,null,\"chart.spc\"],"
                + "\"charter\":[null,null,null,\"X\"],"
                + "\"lv\":[null,null,null,10]}");
        }

        private static void WriteEntry(ZipArchive archive, string name, string text)
        {
            using Stream stream = archive.CreateEntry(name).Open();
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(text);
        }

        private static string TempFolder()
        {
            string folder = Path.Combine(Path.GetTempPath(), "ifcl-tests", Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(folder);
                return folder;
            }
            catch (UnauthorizedAccessException)
            {
                // Some hosts deny writes below the user temp folder; keep the test self-contained.
                folder = Path.Combine(AppContext.BaseDirectory, ".test-temp", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(folder);
                return folder;
            }
        }

        private static bool AllNames(PackManifest manifest, string expected)
        {
            for (int i = 0; i < PackManifest.Languages; i++)
                if (manifest.Names[i] != expected) return false;
            return true;
        }
    }
}
