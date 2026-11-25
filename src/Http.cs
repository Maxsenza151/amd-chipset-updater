using System;
using System.IO;
using System.Net;

namespace ChipsetUpdater
{
    struct DownloadProgress
    {
        public long Received;
        public long Total; // -1 when unknown
    }

    static class Http
    {
        public const string BrowserUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0 Safari/537.36";

        public static HttpWebRequest CreateRequest(string url, string userAgent)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = userAgent ?? BrowserUserAgent;
            req.Headers[HttpRequestHeader.AcceptLanguage] = "en-US,en;q=0.9";
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            req.AllowAutoRedirect = true;
            req.Timeout = 30000;
            req.ReadWriteTimeout = 60000;
            return req;
        }

        public static string GetString(string url, string accept, string userAgent)
        {
            HttpWebRequest req = CreateRequest(url, userAgent);
            req.Accept = accept;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var reader = new StreamReader(resp.GetResponseStream()))
                return reader.ReadToEnd();
        }

        /// <summary>
        /// Downloads a binary file to <paramref name="dest"/> (via a .part file).
        /// Fails if the server answers with a web page instead of a file.
        /// </summary>
        public static void DownloadFile(string url, string dest, string referer, string userAgent,
                                        IProgress<DownloadProgress> progress)
        {
            string part = dest + ".part";
            try
            {
                DownloadTo(url, part, referer, userAgent, progress);
            }
            catch
            {
                try { File.Delete(part); } catch (Exception) { }
                throw;
            }

            if (File.Exists(dest)) File.Delete(dest);
            File.Move(part, dest);
        }

        static void DownloadTo(string url, string part, string referer, string userAgent,
                               IProgress<DownloadProgress> progress)
        {
            HttpWebRequest req = CreateRequest(url, userAgent);
            req.Accept = "*/*";
            if (referer != null) req.Referer = referer;

            using (var resp = (HttpWebResponse)req.GetResponse())
            {
                if (resp.ContentType != null && resp.ContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The server returned a web page instead of the file. Try again later.");

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
        }
    }
}
