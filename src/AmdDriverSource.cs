using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;

namespace ChipsetUpdater
{
    sealed class DriverRelease
    {
        public Version Version;
        public string VersionText;
        public string FileName;
        public string DownloadUrl;
        public string PageUrl;
    }

    struct DownloadProgress
    {
        public long Received;
        public long Total; // -1 when unknown
    }

    /// <summary>Reads AMD's chipset driver page and downloads the installer.</summary>
    static class AmdDriverSource
    {
        const string UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0 Safari/537.36";

        // drivers.amd.com refuses downloads that do not come from amd.com.
        const string Referer = "https://www.amd.com/";

        const string GenericPage = "https://www.amd.com/en/support/downloads/drivers.html/chipsets/am5/x870e.html";

        static readonly Regex ChipsetLink = new Regex(
            @"https?://drivers\.amd\.com/drivers/(?:[\w\-./]*/)?(amd_chipset_software_(\d+(?:\.\d+){1,3})\.exe)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static DriverRelease GetLatest(HardwareInfo hw)
        {
            var urls = new List<string>();
            foreach (string u in new[] { hw.DriverPageUrl, hw.FallbackPageUrl, GenericPage })
                if (u != null && !urls.Contains(u)) urls.Add(u);

            Exception last = null;
            foreach (string url in urls)
            {
                try
                {
                    DriverRelease r = Parse(FetchString(url), url);
                    if (r != null) return r;
                    last = new InvalidDataException(
                        "No chipset driver link was found on the AMD page (the website layout may have changed).");
                }
                catch (WebException ex) { last = ex; }
            }
            throw last;
        }

        static DriverRelease Parse(string html, string pageUrl)
        {
            html = html.Replace("\\/", "/"); // links inside embedded JSON
            DriverRelease best = null;
            foreach (Match m in ChipsetLink.Matches(html))
            {
                Version v = VersionUtil.Parse(m.Groups[2].Value);
                if (v == null || (best != null && v <= best.Version)) continue;
                best = new DriverRelease
                {
                    Version = v,
                    VersionText = m.Groups[2].Value,
                    FileName = m.Groups[1].Value,
                    DownloadUrl = m.Value,
                    PageUrl = pageUrl,
                };
            }
            return best;
        }

        static string FetchString(string url)
        {
            HttpWebRequest req = CreateRequest(url);
            req.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var reader = new StreamReader(resp.GetResponseStream()))
                return reader.ReadToEnd();
        }

        /// <summary>Downloads the installer to %TEMP%\AMDChipsetUpdater and returns its path.</summary>
        public static string Download(DriverRelease release, IProgress<DownloadProgress> progress)
        {
            string dir = Path.Combine(Path.GetTempPath(), "AMDChipsetUpdater");
            Directory.CreateDirectory(dir);
            string dest = Path.Combine(dir, release.FileName);
            string part = dest + ".part";

            HttpWebRequest req = CreateRequest(release.DownloadUrl);
            req.Referer = Referer;
            req.Accept = "*/*";
            req.ReadWriteTimeout = 60000;

            using (var resp = (HttpWebResponse)req.GetResponse())
            {
                if (resp.ContentType != null && resp.ContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        "AMD's server returned a web page instead of the installer. Try again later, "
                        + "or download the driver manually from the AMD page.");

                long total = resp.ContentLength;
                using (Stream input = resp.GetResponseStream())
                using (FileStream output = File.Create(part))
                {
                    var buffer = new byte[81920];
                    long received = 0, lastReport = 0;
                    int n;
                    while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        output.Write(buffer, 0, n);
                        received += n;
                        if (progress != null && (received - lastReport >= 262144 || received == total))
                        {
                            lastReport = received;
                            progress.Report(new DownloadProgress { Received = received, Total = total });
                        }
                    }
                    if (total > 0 && received != total)
                        throw new IOException("The download was interrupted. Please try again.");
                }
            }

            if (!LooksLikeExe(part))
            {
                File.Delete(part);
                throw new InvalidDataException("The downloaded file is not a Windows program.");
            }
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(part, dest);
            return dest;
        }

        static bool LooksLikeExe(string path)
        {
            using (FileStream fs = File.OpenRead(path))
                return fs.Length > 2 && fs.ReadByte() == 'M' && fs.ReadByte() == 'Z';
        }

        static HttpWebRequest CreateRequest(string url)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = UserAgent;
            req.Headers[HttpRequestHeader.AcceptLanguage] = "en-US,en;q=0.9";
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            req.AllowAutoRedirect = true;
            req.Timeout = 30000;
            return req;
        }
    }

    static class VersionUtil
    {
        /// <summary>Parses "8.09.28.1124" (leading zeros allowed) into a Version.</summary>
        public static Version Parse(string text)
        {
            if (text == null) return null;
            Match m = Regex.Match(text, @"\d+(\.\d+){1,3}");
            if (!m.Success) return null;
            string[] parts = m.Value.Split('.');
            var n = new int[4];
            for (int i = 0; i < parts.Length; i++)
                if (!int.TryParse(parts[i], out n[i])) return null;
            switch (parts.Length)
            {
                case 2: return new Version(n[0], n[1]);
                case 3: return new Version(n[0], n[1], n[2]);
                default: return new Version(n[0], n[1], n[2], n[3]);
            }
        }
    }
}
