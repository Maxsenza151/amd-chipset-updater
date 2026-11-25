using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace ChipsetUpdater
{
    sealed class AppRelease
    {
        public Version Version;
        public string VersionText;
        public string PageUrl;     // GitHub release page (changelog)
        public string AssetName;   // installer or portable zip, matching how this copy was distributed
        public string AssetUrl;
        public string Sha256;      // published by GitHub for each release asset
    }

    /// <summary>Updates the app itself from the GitHub releases of the project.</summary>
    static class AppUpdater
    {
        public const string Repo = "Maxsenza151/amd-chipset-updater";
        const string LatestReleaseApi = "https://api.github.com/repos/" + Repo + "/releases/latest";
        const string ExeName = "AMDChipsetUpdater.exe";

        // Written by the Inno Setup installer (AppId + "_is1"); per-user install.
        const string InstallerKey =
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{8F3C2A51-6D2E-4B7A-9C41-2E5B7A1D9F30}_is1";

        public static Version CurrentVersion
        {
            get { return VersionUtil.Parse(typeof(AppUpdater).Assembly.GetName().Version.ToString()); }
        }

        static string UserAgent
        {
            get { return "AMDChipsetUpdater/" + VersionUtil.Short(CurrentVersion); }
        }

        /// <summary>True when this exe is the one installed by the installer (not a portable copy).</summary>
        public static bool IsInstalledCopy(string exePath)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(InstallerKey))
                {
                    string location = k == null ? null : k.GetValue("InstallLocation") as string;
                    if (string.IsNullOrEmpty(location)) return false;
                    return string.Equals(
                        Path.GetFullPath(location).TrimEnd('\\'),
                        Path.GetDirectoryName(Path.GetFullPath(exePath)).TrimEnd('\\'),
                        StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception) { return false; }
        }

        /// <summary>Reads the latest GitHub release. <paramref name="installed"/> picks the asset type.</summary>
        public static AppRelease GetLatest(bool installed)
        {
            string json = Http.GetString(LatestReleaseApi, "application/vnd.github+json", UserAgent);
            var root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (root == null) return null;

            Version version = VersionUtil.Parse(Str(root, "tag_name"));
            if (version == null) return null;

            var release = new AppRelease
            {
                Version = version,
                VersionText = VersionUtil.Short(version),
                PageUrl = Str(root, "html_url") ?? "https://github.com/" + Repo + "/releases/latest",
            };

            var wanted = new Regex(installed ? @"^AMDChipsetUpdater-Setup-.*\.exe$" : @"^AMDChipsetUpdater-Portable-.*\.zip$",
                                   RegexOptions.IgnoreCase);
            object assetsValue;
            var assets = root.TryGetValue("assets", out assetsValue) ? assetsValue as object[] : null;
            if (assets != null)
            {
                foreach (object o in assets)
                {
                    var asset = o as Dictionary<string, object>;
                    string name = asset == null ? null : Str(asset, "name");
                    if (name == null || !wanted.IsMatch(name)) continue;

                    release.AssetName = name;
                    release.AssetUrl = Str(asset, "browser_download_url");
                    string digest = Str(asset, "digest");
                    if (digest != null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                        release.Sha256 = digest.Substring(7).ToLowerInvariant();
                    break;
                }
            }
            return release;
        }

        /// <summary>
        /// Downloads and verifies the update, and for portable copies swaps the exe in place.
        /// Returns what to start once this app has closed.
        /// </summary>
        public static ProcessStartInfo Prepare(AppRelease release, string exePath, bool installed,
                                               IProgress<DownloadProgress> progress)
        {
            if (release.AssetUrl == null)
                throw new InvalidDataException("This release has no " + (installed ? "installer" : "portable zip") + " to download.");
            if (release.Sha256 == null)
                throw new InvalidDataException("GitHub did not publish a checksum for the file, so it cannot be verified.");

            string dir = Path.Combine(Path.GetTempPath(), "AMDChipsetUpdater");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, Path.GetFileName(release.AssetName));
            Http.DownloadFile(release.AssetUrl, file, null, UserAgent, progress);

            if (!string.Equals(Sha256Of(file), release.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(file);
                throw new InvalidDataException("The downloaded file does not match GitHub's checksum, so it was deleted.");
            }

            if (installed)
            {
                // The installer updates the files and starts the app again (/RELAUNCH=1, see setup.iss).
                return new ProcessStartInfo(file, "/SILENT /SUPPRESSMSGBOXES /NORESTART /RELAUNCH=1") { UseShellExecute = true };
            }

            ReplacePortableExe(file, exePath);
            File.Delete(file);
            return new ProcessStartInfo(exePath) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exePath) };
        }

        /// <summary>A running exe cannot be overwritten but can be renamed: rename it to .old, put the new one in place.</summary>
        static void ReplacePortableExe(string zipPath, string exePath)
        {
            string newExe = exePath + ".new";
            string oldExe = exePath + ".old";

            using (ZipArchive zip = ZipFile.OpenRead(zipPath))
            {
                ZipArchiveEntry entry = null;
                foreach (ZipArchiveEntry e in zip.Entries)
                    if (string.Equals(e.Name, ExeName, StringComparison.OrdinalIgnoreCase)) { entry = e; break; }
                if (entry == null) throw new InvalidDataException("The portable zip does not contain " + ExeName + ".");
                entry.ExtractToFile(newExe, true);
            }

            if (File.Exists(oldExe)) File.Delete(oldExe);
            File.Move(exePath, oldExe);
            try
            {
                File.Move(newExe, exePath);
            }
            catch
            {
                File.Move(oldExe, exePath);
                throw;
            }
        }

        /// <summary>Removes the leftovers of a portable update (the old exe may still be closing).</summary>
        public static void CleanupAfterUpdate(string exePath)
        {
            string[] leftovers = { exePath + ".old", exePath + ".new" };
            if (!File.Exists(leftovers[0]) && !File.Exists(leftovers[1])) return;

            Task.Run(() =>
            {
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    bool done = true;
                    foreach (string f in leftovers)
                    {
                        try { if (File.Exists(f)) File.Delete(f); }
                        catch (Exception) { done = false; }
                    }
                    if (done) return;
                    Thread.Sleep(500);
                }
            });
        }

        static string Sha256Of(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream fs = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) ? v as string : null;
        }
    }
}
