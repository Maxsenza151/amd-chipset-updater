using System;
using System.Collections.Generic;
using System.Management;
using System.Text.RegularExpressions;

namespace ChipsetUpdater
{
    sealed class HardwareInfo
    {
        public string CpuName;
        public string CpuVendor;
        public int CpuFamily;
        public int CpuModel;
        public string BoardManufacturer;
        public string BoardProduct;
        public bool IsNotebook;

        public bool IsSupported;
        public string UnsupportedReason;

        // What automatic detection found. DetectedChipset is null when the board name did not reveal it.
        public string DetectedSocket;
        public string DetectedChipset;

        // What is actually used: the detected values, or the user's manual choice.
        public string Socket;      // "AM4", "AM5", "sTR5", ... or "Notebook"
        public string Chipset;     // null = unknown (the socket's default AMD page is used)
        public bool IsManual;

        public string Note;        // extra hint shown in the UI

        /// <summary>True when the chipset is known or there is nothing to choose (notebooks).</summary>
        public bool ChipsetKnown
        {
            get { return Chipset != null || AmdPlatforms.ChipsetsFor(Socket).Length == 0; }
        }

        public string DriverPageUrl
        {
            get { return Socket == null ? null : AmdPlatforms.PageUrl(Socket, Chipset ?? AmdPlatforms.DefaultChipset(Socket)); }
        }

        public string FallbackPageUrl
        {
            get { return Socket == null ? null : AmdPlatforms.PageUrl(Socket, AmdPlatforms.DefaultChipset(Socket)); }
        }

        public string BoardText
        {
            get
            {
                string s = ((BoardManufacturer ?? "") + " " + (BoardProduct ?? "")).Trim();
                return s.Length == 0 ? "Unknown" : s;
            }
        }

        /// <summary>Uses the user's choice instead of the detected platform, if it is valid.</summary>
        public void ApplyManual(string socket, string chipset)
        {
            if (!IsSupported || !AmdPlatforms.IsValid(socket, chipset)) return;
            Socket = socket;
            Chipset = chipset;
            IsManual = true;
        }
    }

    /// <summary>AMD platforms and their chipset driver pages on amd.com.</summary>
    static class AmdPlatforms
    {
        const string BaseUrl = "https://www.amd.com/en/support/downloads/drivers.html/chipsets/";

        public const string Notebook = "Notebook";

        public static readonly string[] Sockets = { "AM5", "AM4", "sTR5", "sWRX8", "sTRX4", "TR4", Notebook };

        static readonly Dictionary<string, string[]> ChipsetsBySocket = new Dictionary<string, string[]>
        {
            { "AM5", new[] { "X870E", "X870", "B850", "B840", "X670E", "X670", "B650E", "B650", "A620" } },
            { "AM4", new[] { "X570", "B550", "A520", "X470", "B450", "X370", "B350", "A320" } },
            { "sTR5", new[] { "TRX50", "WRX90" } },
            { "sWRX8", new[] { "WRX80" } },
            { "sTRX4", new[] { "TRX40" } },
            { "TR4", new[] { "X399" } },
            { Notebook, new string[0] },
        };

        // Longer names first so "X870E" wins over "X870". Suffixes like "B550M", "X570S" still match.
        static readonly Regex ChipsetInName = new Regex(
            @"(?<![A-Z0-9])(X870E|X870|B850|B840|X670E|X670|B650E|B650|A620|X570|B550|A520|X470|B450|X370|B350|A320|TRX50|WRX90|WRX80|TRX40|X399)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string[] ChipsetsFor(string socket)
        {
            string[] list;
            return socket != null && ChipsetsBySocket.TryGetValue(socket, out list) ? list : new string[0];
        }

        public static bool IsValid(string socket, string chipset)
        {
            if (socket == null || !ChipsetsBySocket.ContainsKey(socket)) return false;
            return chipset == null || Array.IndexOf(ChipsetsFor(socket), chipset) >= 0;
        }

        public static string DefaultChipset(string socket)
        {
            switch (socket)
            {
                case "AM4": return "X570";
                case "sTR5": return "TRX50";
                case "sWRX8": return "WRX80";
                case "sTRX4": return "TRX40";
                case "TR4": return "X399";
                default: return "X870E"; // AM5 and notebooks: AMD ships one package for all platforms
            }
        }

        public static string PageUrl(string socket, string chipset)
        {
            string socketPath = socket == Notebook ? "am5" : socket.ToLowerInvariant();
            return BaseUrl + socketPath + "/" + chipset.ToLowerInvariant() + ".html";
        }

        /// <summary>Finds a chipset name inside a motherboard name, e.g. "ROG STRIX B550-F GAMING".</summary>
        public static string FindChipset(string boardName, out string socket)
        {
            socket = null;
            if (string.IsNullOrEmpty(boardName)) return null;
            Match m = ChipsetInName.Match(boardName);
            if (!m.Success) return null;
            string chipset = m.Groups[1].Value.ToUpperInvariant();
            foreach (KeyValuePair<string, string[]> kv in ChipsetsBySocket)
                if (Array.IndexOf(kv.Value, chipset) >= 0) socket = kv.Key;
            return chipset;
        }
    }

    static class HardwareDetector
    {
        // SMBIOS chassis types that mean a portable device.
        static readonly HashSet<int> PortableChassis = new HashSet<int> { 8, 9, 10, 11, 12, 14, 18, 21, 30, 31, 32 };

        static readonly Regex MobileCpuName = new Regex(
            @"\b\d{4}(U|H|HS|HX|HX3D)\b|RYZEN AI|RYZEN Z\d", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static HardwareInfo Detect()
        {
            var hw = new HardwareInfo();

            foreach (ManagementBaseObject o in Query("SELECT Name, Manufacturer, Caption FROM Win32_Processor"))
            {
                hw.CpuName = Clean(o["Name"]);
                hw.CpuVendor = Clean(o["Manufacturer"]);
                Match m = Regex.Match(Clean(o["Caption"]) ?? "", @"Family\s+(\d+)\s+Model\s+(\d+)", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    hw.CpuFamily = int.Parse(m.Groups[1].Value);
                    hw.CpuModel = int.Parse(m.Groups[2].Value);
                }
                break;
            }

            foreach (ManagementBaseObject o in Query("SELECT Manufacturer, Product FROM Win32_BaseBoard"))
            {
                hw.BoardManufacturer = Clean(o["Manufacturer"]);
                hw.BoardProduct = Clean(o["Product"]);
                break;
            }

            hw.IsNotebook = IsPortableChassis() || MobileCpuName.IsMatch(hw.CpuName ?? "");
            Classify(hw);
            hw.Socket = hw.DetectedSocket;
            hw.Chipset = hw.DetectedChipset;
            return hw;
        }

        static void Classify(HardwareInfo hw)
        {
            string name = (hw.CpuName ?? "").ToUpperInvariant();
            bool isAmd = hw.CpuVendor == "AuthenticAMD" || name.Contains("AMD");

            if (!isAmd)
            {
                hw.UnsupportedReason = "This PC does not have an AMD processor, so AMD chipset drivers do not apply.";
                return;
            }
            if (name.Contains("EPYC"))
            {
                hw.UnsupportedReason = "AMD EPYC server platforms are not supported. Use your server vendor's drivers.";
                return;
            }
            if (hw.CpuFamily > 0 && hw.CpuFamily < 0x17)
            {
                // FX, A-series, Phenom, older Athlon (AM3+, FM2+, ...): not covered by AMD Chipset Software.
                hw.UnsupportedReason = "This AMD processor predates Ryzen. AMD Chipset Software only supports "
                                     + "Ryzen and Threadripper platforms.";
                return;
            }

            hw.IsSupported = true;

            if (name.Contains("THREADRIPPER"))
            {
                ClassifyThreadripper(hw, name);
            }
            else if (hw.IsNotebook)
            {
                hw.DetectedSocket = AmdPlatforms.Notebook;
                hw.Note = "Notebook detected: AMD's universal chipset package is used. "
                        + "Your notebook maker may recommend its own drivers.";
                return;
            }
            else
            {
                hw.DetectedSocket = DesktopSocket(hw.CpuFamily, hw.CpuModel, name);
            }

            // Read the chipset from the motherboard name when it fits the detected socket.
            string boardSocket;
            string boardChipset = AmdPlatforms.FindChipset(hw.BoardProduct, out boardSocket);
            if (boardChipset != null && boardSocket == hw.DetectedSocket)
                hw.DetectedChipset = boardChipset;
        }

        static string DesktopSocket(int family, int model, string name)
        {
            if (family == 0x17) return "AM4";                       // Zen, Zen+, Zen 2
            if (family == 0x19) return model < 0x60 ? "AM4" : "AM5"; // Zen 3 (AM4) vs Zen 4 (AM5)
            if (family >= 0x1A) return "AM5";                       // Zen 5 and newer

            // Unknown family: guess from the model number (Ryzen 7000 and newer are AM5).
            Match m = Regex.Match(name, @"\b([1-9])\d{3}");
            if (m.Success && m.Groups[1].Value[0] >= '7') return "AM5";
            return "AM4";
        }

        /// <summary>Threadripper: the model number identifies both socket and chipset.</summary>
        static void ClassifyThreadripper(HardwareInfo hw, string name)
        {
            Match m = Regex.Match(name, @"\b(\d)\d{3}(WX|X)\b");
            char series = m.Success ? m.Groups[1].Value[0] : '7';
            bool wx = m.Success && m.Groups[2].Value == "WX";

            switch (series)
            {
                case '1':
                case '2': hw.DetectedSocket = "TR4"; hw.DetectedChipset = "X399"; break;
                case '3': hw.DetectedSocket = wx ? "sWRX8" : "sTRX4"; hw.DetectedChipset = wx ? "WRX80" : "TRX40"; break;
                case '5': hw.DetectedSocket = "sWRX8"; hw.DetectedChipset = "WRX80"; break;
                default: hw.DetectedSocket = "sTR5"; hw.DetectedChipset = wx ? "WRX90" : "TRX50"; break;
            }
        }

        static bool IsPortableChassis()
        {
            try
            {
                foreach (ManagementBaseObject o in Query("SELECT ChassisTypes FROM Win32_SystemEnclosure"))
                {
                    var types = o["ChassisTypes"] as ushort[];
                    if (types == null) continue;
                    foreach (ushort t in types)
                        if (PortableChassis.Contains(t)) return true;
                }
            }
            catch (ManagementException) { }
            return false;
        }

        static IEnumerable<ManagementBaseObject> Query(string wql)
        {
            using (var searcher = new ManagementObjectSearcher(wql))
            using (ManagementObjectCollection results = searcher.Get())
            {
                foreach (ManagementBaseObject o in results)
                    yield return o;
            }
        }

        static string Clean(object value)
        {
            return value == null ? null : Regex.Replace(value.ToString(), @"\s+", " ").Trim();
        }
    }
}
