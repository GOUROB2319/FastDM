namespace FastDM.Tests
{
    // Importing a list of links: 50 lines must give at most 50 downloads, never hundreds.
    static class BatchImportTests
    {
        const string Id1 = "dQw4w9WgXcQ";
        const string Id2 = "yWNPxS0sXnE";

        static BatchRow One(string line) => BatchImport.Parse(line).Single();

        public static void Run()
        {
            Reading();
            YouTube();
            OtherSites();
            Duplicates();
            Safety();
            Quality();
        }

        static void Reading()
        {
            T.Section("Batch import: reading the text");

            T.Check("empty / blank text -> no rows", BatchImport.Parse("").Count == 0 && BatchImport.Parse("  \r\n \n").Count == 0 && BatchImport.Parse(null).Count == 0);

            var rows = BatchImport.Parse("\uFEFF# my list\r\n// another comment\r\n; and another\r\n\r\nhttps://a.test/x.zip\r\n  https://a.test/y.zip  \r\n");
            T.Check("BOM, CRLF, blank and comment lines are skipped", rows.Count == 2 && rows[0].Text == "https://a.test/x.zip" && rows[1].Text == "https://a.test/y.zip");
            T.Check("line numbers stay the real ones", rows[0].Line == 5 && rows[1].Line == 6);

            T.Check("a line without a link is flagged, not selected", One("hello world").Kind == BatchKind.Invalid && !One("hello world").Selected);
            T.Check("javascript: line is not a link", One("javascript:alert(1)").Kind == BatchKind.Invalid);
            T.Check("file: link is not accepted", One("file:///c:/windows/system32/calc.exe").Kind == BatchKind.Invalid);
            T.Check("very long junk line is shortened", One(new string('x', 500)).Text.Length <= 201);

            var titled = One("My favourite song - https://a.test/song.mp3 (mirror)");
            T.Check("link is found inside a line, trailing ) removed", titled.Text == "https://a.test/song.mp3" && titled.Kind == BatchKind.File);

            var two = One("https://a.test/one.zip https://a.test/two.zip");
            T.Check("two links on one line: first used, user told", two.Text == "https://a.test/one.zip" && two.Note.Contains("first link"));

            T.Check("fragment (#...) is dropped", One("https://a.test/x.zip#section").Text == "https://a.test/x.zip");
        }

        static void YouTube()
        {
            T.Section("Batch import: YouTube links");

            var watch = One("https://www.youtube.com/watch?v=" + Id1 + "&list=RDdQw4w9WgXcQ&index=3&start_radio=1");
            T.Check("watch link with playlist parts -> one video, clean link", watch.Kind == BatchKind.Video && watch.Selected && watch.Text == "https://www.youtube.com/watch?v=" + Id1);
            T.Check("the playlist part is reported", watch.Note.Contains("playlist part ignored"));

            foreach (var url in new[]
            {
                "https://youtu.be/" + Id1,
                "https://youtu.be/" + Id1 + "?si=abc&t=30",
                "https://m.youtube.com/watch?v=" + Id1,
                "https://www.youtube.com/shorts/" + Id1,
                "https://www.youtube.com/live/" + Id1,
                "https://www.youtube.com/embed/" + Id1,
                "https://music.youtube.com/watch?v=" + Id1,
            })
            {
                var r = One(url);
                T.Check("one video: " + url, r.Kind == BatchKind.Video && r.Selected && r.Text == "https://www.youtube.com/watch?v=" + Id1);
            }

            T.Check("playlist page is not selected", One("https://www.youtube.com/playlist?list=PLabc123").Kind == BatchKind.Playlist && !One("https://www.youtube.com/playlist?list=PLabc123").Selected);
            T.Check("watch link with list but no video id -> playlist", One("https://www.youtube.com/watch?list=PLabc123").Kind == BatchKind.Playlist);
            foreach (var url in new[] { "https://www.youtube.com/@somechannel", "https://www.youtube.com/channel/UCabc", "https://www.youtube.com/c/Name", "https://www.youtube.com/user/Name", "https://www.youtube.com/" })
                T.Check("channel or page is not selected: " + url, One(url).Kind == BatchKind.Playlist && !One(url).Selected);

            T.Check("bad video id is not accepted as a video", One("https://www.youtube.com/watch?v=short").Kind == BatchKind.Playlist);
        }

        static void OtherSites()
        {
            T.Section("Batch import: other sites");

            var reel = One("https://www.facebook.com/reel/1234567890123456/");
            T.Check("Facebook reel (ends with /) is a video, not a folder", reel.Kind == BatchKind.Video && reel.Selected);
            T.Check("Instagram reel (ends with /) is a video", One("https://www.instagram.com/reel/Cxyz123abc/").Kind == BatchKind.Video);
            T.Check("TikTok video is a video", One("https://www.tiktok.com/@user/video/7300000000000000000").Kind == BatchKind.Video);
            T.Check("home page of a video site is not a video", One("https://www.facebook.com/").Kind == BatchKind.Invalid && One("https://www.instagram.com").Kind == BatchKind.Invalid);

            T.Check("link ending with / on another site is a folder", One("https://files.example.com/movies/").Kind == BatchKind.Folder && One("https://files.example.com/movies/").Selected);
            T.Check("ftp folder is a folder", One("ftp://ftp.example.com/pub/").Kind == BatchKind.Folder);
            T.Check("sftp folder is a folder", One("sftp://host.example.com/data/").Kind == BatchKind.Folder);
            T.Check("link to a file is a file", One("https://files.example.com/a.zip").Kind == BatchKind.File);
        }

        static void Duplicates()
        {
            T.Section("Batch import: duplicates");

            var rows = BatchImport.Parse("https://a.test/x.zip\nhttps://a.test/x.zip\nHTTPS://A.TEST/x.zip\nhttps://a.test/x.zip/\nhttps://a.test/other.zip");
            T.Check("same link repeated -> only the first is selected", rows[0].Selected && rows.Skip(1).Take(3).All(r => r.Duplicate && !r.Selected) && rows[4].Selected);
            T.Check("duplicates are still listed with a reason", rows[1].Note.Contains("earlier line"));

            var yt = BatchImport.Parse("https://youtu.be/" + Id1 + "\nhttps://www.youtube.com/watch?v=" + Id1 + "&list=PLxyz\nhttps://www.youtube.com/shorts/" + Id1 + "\nhttps://youtu.be/" + Id2);
            T.Check("same YouTube video written 3 ways -> 1 selected", yt.Count(r => r.Selected) == 2 && yt[0].Selected && yt[3].Selected && yt[1].Duplicate && yt[2].Duplicate);

            var listed = BatchImport.Parse("https://youtu.be/" + Id1 + "\nhttps://a.test/f.zip", new[] { "https://www.youtube.com/watch?v=" + Id1 + "&feature=share" });
            T.Check("already in the download list -> not selected, says so", listed[0].AlreadyListed && !listed[0].Selected && listed[0].Note.Contains("already in your download list") && listed[1].Selected);
        }

        static void Safety()
        {
            T.Section("Batch import: 50 links must stay 50");

            var lines = Enumerable.Range(0, 50).Select(i =>
                "https://www.youtube.com/watch?v=" + (Id1.Substring(0, 8) + i.ToString("000")) + "&list=RD" + Id1 + "&index=" + i + "&start_radio=1");
            var rows = BatchImport.Parse(string.Join("\n", lines));
            T.Check("50 radio-mix links -> exactly 50 rows", rows.Count == 50);
            T.Check("all 50 are single videos and selected", rows.All(r => r.Kind == BatchKind.Video && r.Selected));
            T.Check("none of them points at a playlist", rows.All(r => !r.Text.Contains("list=")));

            var mixed = BatchImport.Parse("https://youtu.be/" + Id1 + "\nhttps://www.youtube.com/playlist?list=PLabc\nnonsense\nhttps://files.example.com/a/\nhttps://www.facebook.com/reel/111/");
            T.Check("summary counts what was found and what was left out",
                BatchImport.Summarize(mixed) == "4 links: 2 videos, 1 folder.  Not selected: 1 playlist or channel, 1 line that is not a link");

            T.Check("password in a link is hidden in the preview", BatchImport.DisplayUrl("ftp://user:secret@host.example.com/a/") == "ftp://user:***@host.example.com/a/");
            T.Check("link without a password is unchanged", BatchImport.DisplayUrl("https://a.test/x?y=a@b") == "https://a.test/x?y=a@b");
        }

        static void Quality()
        {
            T.Section("Batch import: quality choices");

            T.Check("9 presets", BatchQuality.Presets.Length == 9);
            T.Check("best available", BatchQuality.Presets[0].Format == "bv*+ba/b" && !BatchQuality.Presets[0].AudioOnly);

            var p1080 = BatchQuality.Presets.Single(q => q.MaxHeight == 1080);
            T.Check("up to 1080p keeps a fallback to best", p1080.Format == "bv*[height<=1080]+ba/b[height<=1080]/bv*+ba/b");

            var m4a = BatchQuality.Presets.Single(q => q.AudioOnly && q.AudioFormat == "m4a");
            var mp3 = BatchQuality.Presets.Single(q => q.AudioOnly && q.AudioFormat == "mp3");
            T.Check("audio only uses the audio format", m4a.Format == "ba/b" && mp3.Format == "ba/b");
            T.Check("every preset has a label", BatchQuality.Presets.All(q => q.Label.Length > 0));
        }
    }
}
