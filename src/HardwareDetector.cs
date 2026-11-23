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

        public string Socket;          // "AM4", "AM5", "sTR5", ... or "Mobile"
        public string Chipset;         // "B550", ... (page actually used)
        public bool ChipsetFromBoard;  // chipset read from the motherboard name
        public string Note;            // extra hint shown in the UI

        public string DriverPageUrl
        {
            get { return Socket == null ? null : AmdPlatforms.PageUrl(Socket, Chipset); }
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

        public string PlatformText
        {
            get
            {
                if (!IsSupported) return "Not supported";
                if (Socket == "Mobile") return "Notebook / mobile APU (integrated chipset)";
                if (ChipsetFromBoard) return "Socket " + Socket + "  ·  " + Chipset + " chipset";
                return "Socket " + Socket + "  ·  chipset not identified (using " + Chipset + " page)";
            }
        }
    }

    /// <summary>Maps AMD platforms to their chipset driver pages on amd.com.</summary>
    static class AmdPlatforms
    {
        const string BaseUrl = "https://www.amd.com/en/support/downloads/drivers.html/chipsets/";

        static readonly Dictionary<string, string> ChipsetToSocket =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "X870E", "AM5" }, { "X870", "AM5" }, { "B850", "AM5" }, { "B840", "AM5" },
                { "X670E", "AM5" }, { "X670", "AM5" }, { "B650E", "AM5" }, { "B650", "AM5" }, { "A620", "AM5" },
                { "X570", "AM4" }, { "B550", "AM4" }, { "A520", "AM4" }, { "X470", "AM4" },
                { "B450", "AM4" }, { "X370", "AM4" }, { "B350", "AM4" }, { "A320", "AM4" },
                { "TRX50", "sTR5" }, { "WRX90", "sTR5" }, { "WRX80", "sWRX8" },
                { "TRX40", "sTRX4" }, { "X399", "TR4" },
            };

        // Longer names first so "X870E" wins over "X870". Suffixes like "B550M", "X570S" still match.
        static readonly Regex ChipsetInName = new Regex(
            @"(?<![A-Z0-9])(X870E|X870|B850|B840|X670E|X670|B650E|B650|A620|X570|B550|A520|X470|B450|X370|B350|A320|TRX50|WRX90|WRX80|TRX40|X399)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
            string socketPath = socket == "Mobile" ? "am5" : socket.ToLowerInvariant();
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
            socket = ChipsetToSocket[chipset];
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
                Match m = Regex.Match(Clean(o["Caption"]), @"Family\s+(\d+)\s+Model\s+(\d+)", RegexOptions.IgnoreCase);
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

            hw.IsSupported = true;

            if (name.Contains("THREADRIPPER"))
            {
                ClassifyThreadripper(hw, name);
            }
            else if (hw.IsNotebook)
            {
                hw.Socket = "Mobile";
                hw.Chipset = AmdPlatforms.DefaultChipset("Mobile");
                hw.Note = "Notebook detected: AMD's universal chipset package is used. "
                        + "Your notebook maker may recommend its own drivers.";
                return;
            }
            else
            {
                hw.Socket = DesktopSocket(hw.CpuFamily, hw.CpuModel, name);
                hw.Chipset = AmdPlatforms.DefaultChipset(hw.Socket);
            }

            // Refine the chipset from the motherboard name when it fits the detected socket.
            string boardSocket;
            string boardChipset = AmdPlatforms.FindChipset(hw.BoardProduct, out boardSocket);
            if (boardChipset != null && boardSocket == hw.Socket)
            {
                hw.Chipset = boardChipset;
                hw.ChipsetFromBoard = true;
            }
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

        static void ClassifyThreadripper(HardwareInfo hw, string name)
        {
            Match m = Regex.Match(name, @"\b(\d)\d{3}(WX|X)\b");
            char series = m.Success ? m.Groups[1].Value[0] : '7';
            bool wx = m.Success && m.Groups[2].Value == "WX";

            switch (series)
            {
                case '1':
                case '2': hw.Socket = "TR4"; hw.Chipset = "X399"; break;
                case '3': hw.Socket = wx ? "sWRX8" : "sTRX4"; hw.Chipset = wx ? "WRX80" : "TRX40"; break;
                case '5': hw.Socket = "sWRX8"; hw.Chipset = "WRX80"; break;
                default: hw.Socket = "sTR5"; hw.Chipset = wx ? "WRX90" : "TRX50"; break;
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
