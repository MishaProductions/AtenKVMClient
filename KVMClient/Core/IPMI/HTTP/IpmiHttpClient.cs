using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

namespace KVMClient.Core.IPMI.HTTP
{
    public class IpmiHttpClient : IIpmiInterfaceProvider
    {
        public readonly string Host;
        private static HttpClient? client;

        public string? SID;
        private string? CSREFToken;
        internal string Password = "";
        internal string Username = "";

        public IpmiHttpClient(string Host)
        {
            this.Host = Host;

            var handler = new HttpClientHandler
            {
                ClientCertificateOptions = ClientCertificateOption.Manual,
                ServerCertificateCustomValidationCallback =
                (httpRequestMessage, cert, cetChain, policyErrors) =>
                {
                    return true;
                }
            };

            client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                                     "AppleWebKit/537.36 (KHTML, like Gecko) " +
                                     "Chrome/99.0.4844.83 Safari/537.36");
            client.DefaultRequestHeaders.Add("Connection", "keep-alive");
            client.DefaultRequestHeaders.Add("Origin", "https://" + Host + "");
            client.DefaultRequestHeaders.Add("Pragma", "no-cache");

            client.DefaultRequestHeaders.Add("Referer", "https://" + Host + "/cgi/url_redirect.cgi?url_name=sys_smbios");

            client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "empty");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "cors");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");

            client.DefaultRequestHeaders.Add("X-Prototype-Version", "1.5.0");
            client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
        }
        private IpmiHttpClient() { Host = "localhost"; }

        public async Task<bool> Authenticate(string Usernam, string Password)
        {
            var values = new Dictionary<string, string>
  {
      { "name", Usernam },
      { "pwd", Password }
  };

            var content = new FormUrlEncodedContent(values);
            if (client == null) throw new Exception("client is null");

            var response = await client.PostAsync("https://" + Host + "/cgi/login.cgi", content);

            string cookie = "";
            foreach (var item in response.Headers)
            {
                if (item.Key == "Set-Cookie")
                {
                    foreach (var item2 in item.Value)
                    {
                        if (item2.Contains("path"))
                        {
                            cookie = item2;
                            break;
                        }
                    }
                }
            }

            if (cookie == "")
            {
                return false;
            }

            if (!cookie.StartsWith("SID="))
            {
                throw new Exception("Version error. unexpected cookie value");
            }

            var SID = cookie.Replace("SID=", "").Replace("; path=/ ; ;Secure; HttpOnly", "").Replace("; path=/", "");
            if (string.IsNullOrEmpty(SID))
            {
                throw new Exception("SID is null");
            }
            this.SID = SID;

            client.DefaultRequestHeaders.Add("Cookie", "SID=" + SID);


            var response2 = await client.GetAsync("https://" + Host + "/cgi/url_redirect.cgi?url_name=topmenu");
            var x = await response2.Content.ReadAsStringAsync();

            var proper = x.Replace("</html>", "");
            foreach (var item in proper.Split('\n'))
            {
                if (item.Contains("CSRF_TOKEN"))
                {
                    var proper2 = item.Substring(42);
                    proper2 = proper2.Replace("\");</script></body>", "");

                    var idx = proper2.IndexOf("\");/");
                    if (idx != -1)
                    {
                        proper2 = proper2.Substring(0, idx);
                    }

                    CSREFToken = proper2;
                }
            }

            if (CSREFToken != null)
            {
                client.DefaultRequestHeaders.Add("CSRF_TOKEN", CSREFToken);
                client.DefaultRequestHeaders.Add("Csrf-Token", CSREFToken); // newer hosts use this
            }
            this.Password = Password;
            Username = Usernam;
            return true;
        }
        public async Task<IpmiBoardInfoResult?> GetPlatformInfo()
        {
            //hack to allow no logon
            if (string.IsNullOrEmpty(SID))
            {
                throw new Exception("You are not logged in!");
            }

            var xml = await Query("Get_PlatformInfo.XML");
            XmlDocument xmlDoc = new();
            xmlDoc.LoadXml(xml);

            IpmiBoardInfoResult result = new IpmiBoardInfoResult();


            var ipmi = xmlDoc["IPMI"];

            if (ipmi != null)
            {
                var info = ipmi["PLATFORM_INFO"];
                if (info != null)
                {
                    int macAddrNum = int.Parse(info.GetAttribute("MB_MAC_NUM"));

                    if (!string.IsNullOrEmpty(info.GetAttribute("MB_MAC_ADDR")))
                    {
                        // legacy host (x9)
                        result.InterfaceMacs.Add(info.GetAttribute("MB_MAC_ADDR"));
                        for (int i = 1; i < macAddrNum; i++)
                        {
                            result.InterfaceMacs.Add(info.GetAttribute("MB_MAC_ADDR" + i));
                        }
                    }
                    else
                    {
                        // x11
                        for (int i = 1; i < macAddrNum + 1; i++)
                        {
                            result.InterfaceMacs.Add(info.GetAttribute("MB_MAC_ADDR" + i));
                        }
                    }

                    result.BiosVer = info.GetAttribute("BIOS_VERSION");
                    result.BiosDate = info.GetAttribute("BIOS_BUILD_DATE");
                }
                else
                {
                    throw new Exception("failed to query platform info structure");
                }
            }
            else
            {
                throw new Exception("failed to query platform info");
            }

            // get generic info
            xml = await Query("GENERIC_INFO.XML");
            xmlDoc = new();
            xmlDoc.LoadXml(xml);

            ipmi = xmlDoc["IPMI"];

            if (ipmi != null)
            {
                var info = ipmi["GENERIC_INFO"];
                if (info != null)
                {
                    var gen = info["GENERIC"];
                    var kern = info["KERNAL"];
                    if (gen != null)
                    {
                        result.BMCFWVer = gen.GetAttribute("IPMIFW_VERSION");
                        result.BMCMac = gen.GetAttribute("BMC_MAC");
                        result.BMCFWDate = gen.GetAttribute("IPMIFW_BLDTIME");

                        // bios version field is unused

                        return result;
                    }
                }
            }

            return result;
        }

        public async Task<IpmiInfo> GetGenericInfo()
        {
            //hack to allow no logon
            if (string.IsNullOrEmpty(SID))
            {
                throw new Exception("You are not logged in!");
            }

            var r = await Query("GENERIC_INFO.XML");
            XmlDocument xmlDoc = new();
            xmlDoc.LoadXml(r);

            var ipmi = xmlDoc["IPMI"];

            if (ipmi != null)
            {
                var info = ipmi["GENERIC_INFO"];
                if (info != null)
                {
                    var gen = info["GENERIC"];
                    var kern = info["KERNAL"];
                    if (gen != null)
                    {
                        var tag = gen.GetAttribute("IPMIFW_TAG");
                        var firmwareVersion = gen.GetAttribute("IPMIFW_VERSION");
                        var bmcMac = gen.GetAttribute("BMC_MAC");
                        var bmcbld = gen.GetAttribute("IPMIFW_BLDTIME");
                        var bios = gen.GetAttribute("BIOS_VERSION");
                        string kernel = "unknown";
                        if (kern != null)
                        {
                            kernel = kern.GetAttribute("VERSION");
                        }

                        return new IpmiInfo(tag, firmwareVersion, bmcMac) { BMCBldDate = bmcbld, BiosVersion = bios, KernelVersion = kernel };
                    }
                }
            }


            throw new Exception("parsing error");
        }

        public async Task<string> ResetBMC()
        {
            //hack to allow no logon
            if (string.IsNullOrEmpty(SID))
            {
                throw new Exception("You are not logged in!");
            }

            var r = await Query("", "", "/cgi/BMCReset.cgi");
            XmlDocument xmlDoc = new();
            xmlDoc.LoadXml(r);

            var ipmi = xmlDoc["IPMI"];

            if (ipmi != null)
            {
                var info = ipmi["BMC_RESET"];
                if (info != null)
                {
                    var state = info["STATE"];
                    if (state != null)
                    {
                        return state.GetAttribute("CODE");
                    }
                }
            }


            if (r.Contains("404"))
            {
                r = await Query("main_bmcreset", "", "/cgi/op.cgi");
                xmlDoc = new();
                xmlDoc.LoadXml(r);

                ipmi = xmlDoc["IPMI"];

                if (ipmi != null)
                {
                    var info = ipmi["BMC_RESET"];
                    if (info != null)
                    {
                        var state = info["STATE"];
                        if (state != null)
                        {
                            return state.GetAttribute("CODE");
                        }
                    }
                }
            }


            throw new Exception("parsing error");
        }

        public async Task<string> GetPowerState()
        {
            string r = "";

            try { r = await Query("GET_POWER_INFO.XML"); }
            catch
            {

            }

            if (r.StartsWith("<html>") || r == "")
            {
                r = await Query("POWER_INFO.XML");
            }
            if (r.StartsWith("<html>") || r == "")
            {
                throw new Exception("failed to get power info");
            }



            XmlDocument xmlDoc = new();
            xmlDoc.LoadXml(r);

            var ipmi = xmlDoc["IPMI"];

            if (ipmi != null)
            {
                var info = ipmi["POWER_INFO"];
                if (info != null)
                {
                    var gen = info["POWER"];
                    if (gen != null)
                    {
                        var tag = gen.GetAttribute("STATUS");

                        return tag;
                    }
                }
            }

            throw new Exception("parsing error");
        }

        public async Task ResetIKVM()
        {
            //hack to allow no logon
            if (string.IsNullOrEmpty(SID))
            {
                throw new Exception("You are not logged in!");
            }

            _ = await Query("", "", "/cgi/ResetIP.cgi");

            //this does not have any response
        }

        /// <summary>
        /// Power on
        /// </summary>
        /// <param name="state">1 = PowerON, 3 = Reset, 5 = PowerDown</param>
        /// <returns></returns>
        /// <exception cref="Exception">Unknown error</exception>
        public async Task SetPowerState(int state)
        {
            //todo: parse result
            var r = await Query("POWER_INFO.XML", "(1," + state + ")");
            if (r.StartsWith("<"))
            {
                //this is for newer firmwares
                r = await Query("GET_POWER_INFO.XML", "(1," + state + ")");
            }
        }

        public async Task<Stream?> CapturePreview()
        {
            if (client == null) throw new Exception("http client not initialized");

            // tell server to capture preview
            string r;
            try
            {
                r = await Query("sys_preview", "(0,0)", "/cgi/op.cgi");
                if (!r.StartsWith("<?xm") || r.Contains("html"))
                {
                    r = await Query("IKVM_PREVIEW.XML", "(0,0)", "/cgi/CapturePreview.cgi");
                    if (!r.StartsWith("<?xm"))
                    {
                        throw new Exception("failed to activate preview");
                    }
                }
            }
            catch
            {
                r = await Query("IKVM_PREVIEW.XML", "(0,0)", "/cgi/CapturePreview.cgi");
                if (!r.StartsWith("<?xm"))
                {
                    throw new Exception("failed to activate preview");
                }
            }

            // server is slow
            await Task.Delay(3000);

            // read preview
            string url = "http://" + Host + "/cgi/url_redirect.cgi?url_name=Snapshot&url_type=img&time_stamp=" + BuildTimeStamp();
            url = url.Replace(" ", "%20");

            var response = await client.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStreamAsync();
            }

            return null;
        }

        private bool isNewHost = false;
        private bool triedNewProtocol = false;

        private async Task<string> Query(string xml, string param = "(0,0)", string url = "/cgi/ipmi.cgi")
        {
            if (isNewHost)
                return await QueryNew(xml, param, url);
            else
                return await QueryOld(xml, param, url);
        }

        private async Task<string> QueryOld(string xml, string param = "(0,0)", string url = "/cgi/ipmi.cgi")
        {
            if (SID == null) throw new Exception("you must login!");

            var values = new Dictionary<string, string>
  {
                { xml, param },
                { "time_stamp",  BuildTimeStamp() },
                { "_", "" }
  };

            var content = new FormUrlEncodedContent(values);
            if (client == null) throw new Exception("client is null");


            var response = await client.PostAsync("https://" + Host + url, content);

            var s = await response.Content.ReadAsStringAsync();

            if (s.StartsWith("<html>"))
            {
                if (triedNewProtocol)
                {
                    throw new Exception("failed to fetch, session expired or bad request?");
                }
                else
                {
                    return await QueryNew(xml, param, url);
                }
            }

            return s;
        }
        private async Task<string> QueryNew(string xml, string param = "(0,0)", string url = "/cgi/ipmi.cgi")
        {
            if (SID == null) throw new Exception("you must login!");

            var values = new Dictionary<string, string>
  {
                { "op", xml },
                { "r",  param },
                { "_", "" }
  };

            var content = new FormUrlEncodedContent(values);
            if (client == null) throw new Exception("client is null");


            var response = await client.PostAsync("https://" + Host + url, content);

            var s = await response.Content.ReadAsStringAsync();
            triedNewProtocol = true;
            if (s.Contains("<html>"))
            {
                throw new Exception("failed to fetch, session expired or bad request?");
            }
            else
            {
                isNewHost = true;
            }

            return s;
        }

        public static string BuildTimeStamp()
        {
            var date = DateTime.Now;
            var utcDate = date.ToUniversalTime();

            var timeZoneName =
              date.IsDaylightSavingTime()
                ? TimeZoneInfo.Local.DaylightName
                : TimeZoneInfo.Local.StandardName;

            var timeStamp =
              utcDate.ToString("D")[..3] + " ";
            timeStamp +=
              string.Concat(utcDate.ToString("M").AsSpan(0, 3), " ");
            timeStamp +=
              utcDate.Day + " " + utcDate.Year + " ";

            var x = utcDate.ToString("HH:mm:ss");//.Replace(":", "");
            timeStamp += x + " GMT";
            timeStamp += date.ToString("zzz").Replace(":", "");
            timeStamp +=
              " (" +
              timeZoneName +
              ")";
            return timeStamp;
        }

        public async Task<FRUINFO> GetFRUInfo()
        {
            if (string.IsNullOrEmpty(SID))
            {
                throw new Exception("You are not logged in!");
            }

            var xml = await Query("FRU_INFO.XML");

            XmlSerializer serializer = new XmlSerializer(typeof(IPMI));
            using (StringReader reader = new StringReader(xml))
            {
                IPMI? test = (IPMI)serializer.Deserialize(reader);
                if (test != null && test.FRUINFO != null)
                {
                    return test.FRUINFO;
                }
            }
            return null;
        }

        public async Task<string> GetHwInfo()
        {
            if (string.IsNullOrEmpty(SID))
            {
                throw new Exception("You are not logged in!");
            }

            var xml = await Query("SMBIOS_INFO.XML");

            XmlDocument xmlDoc = new();
            xmlDoc.LoadXml(xml);

            var ipmi = xmlDoc["IPMI"];

            if (ipmi != null)
            {
                string response = "";
                foreach (XmlElement child in ipmi)
                {
                    int cpu = 1;
                    int dimm = 1;
                    int psu = 1;
                    if (child.Name == "BIOS")
                    {
                        response += $"BIOS Vendor: {child.GetAttribute("VENDOR")}, version: {child.GetAttribute("VER")}, release date: {child.GetAttribute("REL_DATE")}" + Environment.NewLine;
                    }
                    else if (child.Name == "SYSTEM")
                    {
                        response += $"System MANUFACTURER: {child.GetAttribute("MANUFACTURER")}, product name: {child.GetAttribute("PN")}, serial: {child.GetAttribute("0123456789")}" + Environment.NewLine;
                    }
                    else if (child.Name == "CPU")
                    {
                        response += $"CPU #: {cpu++}: {child.GetAttribute("VER")} {child.GetAttribute("SPEED")} cores: {child.GetAttribute("CORE")}" + Environment.NewLine;
                    }
                    else if (child.Name == "DIMM")
                    {
                        response += $"DIMM #: {dimm++}: {child.GetAttribute("SIZE")} {child.GetAttribute("MANUFACTURER")}" + Environment.NewLine;
                    }
                    else if (child.Name == "PowerSupply")
                    {
                        response += $"Power Supply #: {psu++}: status: {child.GetAttribute("STATUS")}, {child.GetAttribute("MAXPOWER")}" + Environment.NewLine;
                    }
                }

                return response;
            }

            throw new Exception("parsing error");

            return null;
        }

        public async Task<KvmSession?> GetKVMSession()
        {
            KvmSession result = new KvmSession();
            var response2 = await client.GetAsync("https://" + Host + "/cgi/url_redirect.cgi?url_name=ikvm&url_type=jwsk");
            var x = await response2.Content.ReadAsStringAsync();

            if (x.Contains("File Not Found"))
            {
                throw new Exception("Please switch to \"JAVA plug-in\" in BMC.");
            }

            XmlDocument xmlDoc = new();
            xmlDoc.LoadXml(x);

            int i = -1;

            foreach (XmlNode node in xmlDoc.GetElementsByTagName("argument"))
            {
                if (i >= 0)
                {
                    i++;
                }

                if (node.InnerText.Contains("http://") || node.InnerText.Contains("https://"))
                {

                }
                else if (node.InnerText == Host)
                {
                    i = 0;
                }

                if (i == 1)
                {
                    result.Username = node.InnerText;
                }
                else if (i == 2)
                {
                    result.Password = node.InnerText;
                }
            }


            return result;
        }
    }

    public class KvmSession
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }

    public class IpmiInfo
    {
        public string Tag { get; set; }
        public string FirmwareString { get; set; }
        public string BMCMac { get; set; }
        public string KernelVersion { get; set; }
        public string BiosVersion { get; set; }
        public string BMCBldDate { get; set; }

        public IpmiInfo(string tag, string firmware, string bmcMac)
        {
            Tag = tag;
            FirmwareString = firmware;
            BMCMac = bmcMac;
        }
    }
    [XmlRoot(ElementName = "IPMI")]
    public class IPMI
    {

        [XmlElement(ElementName = "PLATFORM_INFO", IsNullable = true)]
        public PLATFORMINFO? PLATFORMINFO { get; set; }
        [XmlElement(ElementName = "FRU_INFO", IsNullable = true)]
        public FRUINFO? FRUINFO { get; set; }
    }

    #region FRU info
    [XmlRoot(ElementName = "DEVICE")]
    public class DEVICE
    {

        [XmlAttribute(AttributeName = "ID")]
        public int ID { get; set; }
    }

    [XmlRoot(ElementName = "CHASSIS")]
    public class CHASSIS
    {

        [XmlAttribute(AttributeName = "TYPE")]
        public int TYPE { get; set; }

        [XmlAttribute(AttributeName = "PART_NUM")]
        public string PARTNUM { get; set; }

        [XmlAttribute(AttributeName = "SERIAL_NUM")]
        public string SERIALNUM { get; set; }
    }

    [XmlRoot(ElementName = "BOARD")]
    public class BOARD
    {

        [XmlAttribute(AttributeName = "LAN")]
        public int LAN { get; set; }

        [XmlAttribute(AttributeName = "MFG_DATE")]
        public string MFGDATE { get; set; }

        [XmlAttribute(AttributeName = "PROD_NAME")]
        public string PRODNAME { get; set; }

        [XmlAttribute(AttributeName = "MFC_NAME")]
        public string MFCNAME { get; set; }

        [XmlAttribute(AttributeName = "SERIAL_NUM")]
        public string SERIALNUM { get; set; }

        [XmlAttribute(AttributeName = "PART_NUM")]
        public string PARTNUM { get; set; }
    }

    [XmlRoot(ElementName = "PRODUCT")]
    public class PRODUCT
    {

        [XmlAttribute(AttributeName = "LAN")]
        public int LAN { get; set; }

        [XmlAttribute(AttributeName = "MFC_NAME")]
        public string MFCNAME { get; set; }

        [XmlAttribute(AttributeName = "PROD_NAME")]
        public string PRODNAME { get; set; }

        [XmlAttribute(AttributeName = "PART_NUM")]
        public string PARTNUM { get; set; }

        [XmlAttribute(AttributeName = "VERSION")]
        public string VERSION { get; set; }

        [XmlAttribute(AttributeName = "SERIAL_NUM")]
        public string SERIALNUM { get; set; }

        [XmlAttribute(AttributeName = "ASSET_TAG")]
        public string ASSETTAG { get; set; }
    }

    [XmlRoot(ElementName = "FRU_INFO")]
    public class FRUINFO
    {

        [XmlElement(ElementName = "DEVICE")]
        public DEVICE DEVICE { get; set; }

        [XmlElement(ElementName = "CHASSIS")]
        public CHASSIS CHASSIS { get; set; }

        [XmlElement(ElementName = "BOARD")]
        public BOARD BOARD { get; set; }

        [XmlElement(ElementName = "PRODUCT")]
        public PRODUCT PRODUCT { get; set; }

        [XmlAttribute(AttributeName = "RES")]
        public int RES { get; set; }
    }

    #endregion
    #region platform info
    [XmlRoot(ElementName = "HOST_AND_USER")]
    public class HOSTANDUSER
    {

        [XmlAttribute(AttributeName = "HOSTNAME")]
        public string HOSTNAME { get; set; }

        [XmlAttribute(AttributeName = "BMC_IP")]
        public string BMCIP { get; set; }

        [XmlAttribute(AttributeName = "SESS_USER_NAME")]
        public string SESSUSERNAME { get; set; }

        [XmlAttribute(AttributeName = "USER_ACCESS")]
        public int USERACCESS { get; set; }

        [XmlAttribute(AttributeName = "DHCP6C_DUID")]
        public string DHCP6CDUID { get; set; }
    }

    [XmlRoot(ElementName = "PLATFORM_INFO")]
    public class PLATFORMINFO
    {

        [XmlElement(ElementName = "HOST_AND_USER")]
        public HOSTANDUSER HOSTANDUSER { get; set; }

        [XmlAttribute(AttributeName = "MB_MAC_NUM")]
        public int MBMACNUM { get; set; }

        [XmlAttribute(AttributeName = "MB_MAC_ADDR")]
        public string MBMACADDR { get; set; }

        [XmlAttribute(AttributeName = "MB_MAC_ADDR1")]
        public string MBMACADDR1 { get; set; }

        [XmlAttribute(AttributeName = "MB_MAC_ADDR2")]
        public string MBMACADDR2 { get; set; }

        [XmlAttribute(AttributeName = "MB_MAC_ADDR3")]
        public string MBMACADDR3 { get; set; }

        [XmlAttribute(AttributeName = "BIOS_VERSION")]
        public string BIOSVERSION { get; set; }

        [XmlAttribute(AttributeName = "BIOS_VERSION_EXIST")]
        public int BiosVersionExists { get; set; }

        [XmlAttribute(AttributeName = "BIOS_BUILD_DATE")]
        public string BiosBuildDate { get; set; }

        [XmlAttribute(AttributeName = "BIOS_BUILD_DATE_EXIST")]
        public int BiosBuildDateExists { get; set; }
    }


    #endregion
}
