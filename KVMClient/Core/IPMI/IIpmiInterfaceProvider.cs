using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace KVMClient.Core.IPMI
{
    public interface IIpmiInterfaceProvider
    {
        /// <summary>
        /// Preforms authentication
        /// </summary>
        /// <param name="username"></param>
        /// <param name="password"></param>
        /// <returns>True if success, false if failure</returns>
        Task<bool> Authenticate(string host, string username, string password);

        Task<IpmiBoardInfoResult?> GetPlatformInfo();
        Task<Stream?> CapturePreview();
    }

    public class IpmiBoardInfoResult
    {
        public string BoardModel { get; set; } = "Unknown";
        public string BMCMac { get; set; } = "Unknown";
        public List<string> InterfaceMacs { get; set; } = new List<string>();
        public string BMCFWVer { get; set; } = "";
        public string BMCFWDate { get; set; } = "";
        public string BiosDate { get; set; } = "";
        public string BiosVer { get; set; } = "";
        public bool HasBiosInfo { get; set; } = false;
    }
}
