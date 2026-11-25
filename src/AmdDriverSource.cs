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

    /// <summary>Reads AMD's chipset driver page and downloads the installer.</summary>
    static class AmdDriverSource
    {
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
                    string html = Http.GetString(url, "text/html,application/xhtml+xml,*/*;q=0.8", null);
                    DriverRelease r = Parse(html, url);
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
                    DownloadUrl = "https://" + m.Value.Substring(m.Value.IndexOf("://", StringComparison.Ordinal) + 3),
                    PageUrl = pageUrl,
                };
            }
            return best;
        }

        /// <summary>
        /// Downloads the installer into a new folder %TEMP%\AMDChipsetUpdater\&lt;random&gt; and returns its path
        /// (a fresh folder each time, so nothing can be planted next to it in advance).
        /// </summary>
        public static string Download(DriverRelease release, IProgress<DownloadProgress> progress)
        {
            string dir = Directory.CreateDirectory(Path.Combine(AppUpdater.DownloadRoot, Guid.NewGuid().ToString("N"))).FullName;
            string dest = Path.Combine(dir, release.FileName);
            try
            {
                Http.DownloadFile(release.DownloadUrl, dest, Referer, null, progress);
                if (!LooksLikeExe(dest))
                    throw new InvalidDataException("The downloaded file is not a Windows program.");
                return dest;
            }
            catch
            {
                try { Directory.Delete(dir, true); } catch (Exception) { }
                throw;
            }
        }

        static bool LooksLikeExe(string path)
        {
            using (FileStream fs = File.OpenRead(path))
                return fs.Length > 2 && fs.ReadByte() == 'M' && fs.ReadByte() == 'Z';
        }
    }

    static class VersionUtil
    {
        /// <summary>Parses "8.09.28.1124" or "v1.2.0" (leading zeros allowed) into a 4-part Version.</summary>
        public static Version Parse(string text)
        {
            if (text == null) return null;
            Match m = Regex.Match(text, @"\d+(\.\d+){1,3}");
            if (!m.Success) return null;
            string[] parts = m.Value.Split('.');
            var n = new int[4];
            for (int i = 0; i < parts.Length; i++)
                if (!int.TryParse(parts[i], out n[i])) return null;
            return new Version(n[0], n[1], n[2], n[3]);
        }

        /// <summary>"1.2.0.0" -> "1.2.0" (drops a trailing zero revision).</summary>
        public static string Short(Version v)
        {
            return v.Revision > 0 ? v.ToString(4) : v.ToString(3);
        }
    }
}
