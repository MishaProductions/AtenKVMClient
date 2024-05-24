using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace KVMClient.Core.VNC.VirtualStorage
{
    public class VirtualStorageManager
    {
        private string host = "", entry_user = "", entry_key = "";
        private bool useSSL = false;
        public byte[] deviceStatus = Array.Empty<byte>();

        // constants

        public const byte VM_DEV_NO_DISK = 255;
        public const byte VM_DEV_IMA_MOUNTED = 0;
        public const byte VM_DEV_ISO_MOUNTED = 3;
        public const byte VM_DEV_WEB_ISO_MOUNTED = 4;

        public VirtualStorageManager()
        {

        }

        public void SetAuthInfo(string host, string entry_user, string entry_key, bool useSSL)
        {
            this.host = host;
            this.entry_user = entry_user;
            this.entry_key = entry_key;
            this.useSSL = useSSL;
        }

        public string DeviceInfo
        {
            get
            {
                if (deviceStatus.Length == 0)
                {
                    return "N/A";
                }

                string response = "";

                for (int i = 1; i < deviceStatus.Length; i++)
                {
                    var b = deviceStatus[i];
                    response += $"Device {i}: ";
                    if (b == VM_DEV_NO_DISK)
                    {
                        response += "No disk emulation set.";
                    }
                    else if (b == VM_DEV_IMA_MOUNTED)
                    {
                        response += "IMG/IMA mounted.";
                    }
                    else if (b == VM_DEV_ISO_MOUNTED || b == VM_DEV_WEB_ISO_MOUNTED)
                    {
                        response += "ISO mounted.";
                    }
                    else
                    {
                        response += "Unknown";
                    }

                    response += Environment.NewLine;
                }

                return response;
            }
        }

        private async Task<VncStream> OpenStream()
        {
            TcpClient client = new TcpClient();
            await client.ConnectAsync(host, 623); // TODO: Don't hardcode port
            client.ReceiveBufferSize = int.MaxValue;
            VncStream stream = new VncStream();
            stream.Stream = client.GetStream();

            if (useSSL)
            {
                try
                {
                    var ssl = new SslStream(client.GetStream());
                    stream.Stream = ssl;

                    var cert = X509Certificate2.CreateFromPem(IpmiBoardInfo.IPMICertificate, IpmiBoardInfo.IPMIPrivateKey);

                    // https://github.com/dotnet/runtime/issues/66283
                    if (OperatingSystem.IsWindows())
                    {
                        X509Certificate2 ephemeral = cert;

                        var c = cert.Export(X509ContentType.Pfx);
                        cert = new X509Certificate2(c);
                        File.WriteAllBytes("ipmi.pfx", c);
                        ephemeral.Dispose();
                    }

                    var cb = new RemoteCertificateValidationCallback(bool (object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors) =>
                    {
                        return true;
                    });

                    await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions()
                    {
                        TargetHost = host,

                        ClientCertificates = new System.Security.Cryptography.X509Certificates.X509CertificateCollection()
                        {
                            cert
                        },
                        RemoteCertificateValidationCallback = cb
                    }); ;

                    useSSL = true;
                }
                catch (Exception ex)
                {
                    stream.Close();
                }
            }

            stream.isOpen = true;
            return stream;
        }
        public async Task GetDeviceInfo()
        {
            var stream = await OpenStream();

            byte[] packet = [0, 0, 0, 8, 0, 0, 0, 0];
            stream.Send(packet);
            var data_buffer = stream.Receive(8);
            var length = data_buffer[4] + (data_buffer[5] << 8) + (data_buffer[6] << 16) + (data_buffer[7] << 24);

            deviceStatus = stream.Receive(length);
        }

        public async Task Mount(int deviceIndex, string path, bool isISO)
        {

        }
        public async Task Unmount(int deviceIndex)
        {

        }
    }
}
