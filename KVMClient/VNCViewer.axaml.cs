/*
    Note: most code in here is based off of https://github.com/kelleyk/noVNC but with
          fixes to allow "no signal", SSL, power control, users, etc to work properly.
*/
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using KVMClient.Core;
using KVMClient.Core.IPMI.UDP;
using KVMClient.Core.VNC;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace KVMClient
{
    public partial class VNCViewer : Window
    {
        private string Host { get; set; }
        private string Username { get; set; }
        private string Password { get; set; }
        private IpmiUdpClient IpmiClient { get; set; }


        private string? ServerName { get; set; }
        private WriteableBitmap Framebuffer;
        private TcpClient client = new TcpClient();
        private VncStream c = new VncStream();
        private Version? serverVersion;
        private int FramebufferWidth;
        private int FramebufferHeight;
        private Thread? threadMain;
        private double MaxUpdateRate = 15;
        private VNCPixelFormat? FramebufferPixelFormat;
        private bool Connected = false;
        private bool VideoOnly = false;
        private bool HasVideoSignal = false;
        private int numRects = 0;
        private int aten_len = -1;
        private int aten_type = -1;
        private int lines = 0;
        private int bytes = 0;
        private bool IsAST24000 = false;
        private Ast2100Decoder ast2100Decoder = new Ast2100Decoder();
        private long FrameStart = 0;
        private long FrameEnd = 0;
        private bool useSSL = false;
        private int FPS = 0;
        private RectInfo _cleanRect = new RectInfo(0, 0, -1, -1);
        private bool spamUpdateRequests = false;
        public VNCViewer()
        {
            Host = "";
            Username = "";
            Password = "";
            IpmiClient = new IpmiUdpClient();
            Framebuffer = new WriteableBitmap(new Avalonia.PixelSize(640, 480), new Avalonia.Vector(96.0, 96.0), PixelFormats.Rgba8888);
            DisplayImage.Source = Framebuffer;
        }
        public unsafe VNCViewer(ServerDef info, IpmiUdpClient client)
        {
            InitializeComponent();

            Host = info.IP;
            Username = info.Username;
            Password = info.Password;
            IpmiClient = client;

            // create framebuffer
            Framebuffer = new WriteableBitmap(new Avalonia.PixelSize(640, 480), new Avalonia.Vector(96.0, 96.0), PixelFormats.Rgba8888);
            DisplayImage.Source = Framebuffer;

            // handle events
            Loaded += VNCViewer_Loaded;
            Closed += VNCViewer_Closed;

            // keyboard
            var topLevel = TopLevel.GetTopLevel(this)!;
            topLevel.KeyDown += Image_KeyDown;
            topLevel.KeyUp += Image_KeyUp;
        }

        private async void VNCViewer_Loaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            await Connect();
        }
        private async Task Connect()
        {
            client = new TcpClient();
            client.ReceiveBufferSize = int.MaxValue;
            try
            {
                await client.ConnectAsync(Host, 5900);
            }
            catch (Exception e)
            {
                await new ContentDialog() { Content = "Connection to " + Host + ":5900 failed: " + e.Message, PrimaryButtonText = "OK" }.ShowAsync();
                return;
            }

            c.Stream = client.GetStream();
            c.isOpen = true;

            Title = "iKVM viewer - Connecting";

            int delay = 2;

            bool trySsl = false;
            try
            {
                while (client.Available == 0)
                {
                    await Task.Delay(1000);
                    delay--;

                    if (delay <= 0)
                    {
                        // await new ContentDialog() { Content = "Connection to " + Host + ":5900 failed: VNC Server did not send version. Try restarting IPMI. If that doesn't work, please report this issue!" + serverVersion, PrimaryButtonText = "OK" }.ShowAsync(this);
                        //Close();
                        //return;
                        trySsl = true;
                        break;
                    }
                }
            }
            catch
            {
                return;
            }

            if (trySsl)
            {
                try
                {
                    var ssl = new SslStream(client.GetStream());
                    c.Stream = ssl;

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
                        TargetHost = Host,

                        ClientCertificates = new System.Security.Cryptography.X509Certificates.X509CertificateCollection()
                        {
                            cert
                        },
                        RemoteCertificateValidationCallback = cb
                    }); ;

                    useSSL = true;
                    Title = "iKVM viewer (SSL) - Connecting";
                }
                catch (Exception ex)
                {
                    c.Close();
                    await new ContentDialog() { Content = "Connection to " + Host + ":5900 failed: SSL Authentication failed. " + ex.ToString(), PrimaryButtonText = "OK" }.ShowAsync(this);
                    Close();
                    return;
                }
            }

            Title = "iKVM viewer - negotiating version";

            if (!NegotiateVersion())
            {
                await new ContentDialog() { Content = "Connection to " + Host + ":5900 failed: as the following VNC version is not supported: " + serverVersion, PrimaryButtonText = "OK" }.ShowAsync(this);
                Close();
                return;
            }

            #region Authentication
            Title = "iKVM viewer - authenticating";

            if (!await DoAuthentication())
            {
                return;
            }

            Title = "iKVM viewer - reading framebuffer info";

            //1: share desktop, 0: take ownership
            this.c.SendByte((byte)(1));

            // setup initial framebuffer
            FramebufferHeight = this.c.ReceiveUInt16BE();
            FramebufferWidth = this.c.ReceiveUInt16BE();
            VncStream.SanityCheck(FramebufferWidth > 0 && FramebufferWidth < 0x8000);
            VncStream.SanityCheck(FramebufferHeight > 0 && FramebufferHeight < 0x8000);
            ResizeFB(FramebufferWidth, FramebufferHeight);

            try
            {
                FramebufferPixelFormat = VNCPixelFormat.Decode(this.c.Receive(VNCPixelFormat.Size), 0);
            }
            catch
            {
                await new ContentDialog() { Content = "Connection to " + Host + ":5900 failed: as the server has sent an invaild pixel format.", PrimaryButtonText = "OK" }.ShowAsync();
                return;
            }
            Title = "iKVM viewer - reading server info";
            ServerName = this.c.ReceiveString();

            var sessionID = this.c.Receive(8); // Unknown
            var VideoEnable = this.c.ReceiveByte(); // IKVMVideoEnable
            var val = this.c.ReceiveByte();
            VideoOnly = val == 0; // AKA Video Only (KbMsEnable)
            var KickUserEnable = this.c.ReceiveByte(); // IKVMKickEnable
            var VMEnable = this.c.ReceiveByte(); // VUSBEnable

            UpdateTitle();

            // if (this.aten)
            //{
            FramebufferPixelFormat.BitsPerPixel = 16;
            FramebufferPixelFormat.BitDepth = 15;
            FramebufferPixelFormat.RedMax = (1 << 5) - 1;
            FramebufferPixelFormat.GreenMax = (1 << 5) - 1;
            FramebufferPixelFormat.BlueMax = (1 << 5) - 1;
            FramebufferPixelFormat.RedShift = 10;
            FramebufferPixelFormat.GreenShift = 5;
            FramebufferPixelFormat.BlueShift = 0;
            //}

            var encodings = new VncEncoding[]
            {
                VncEncoding.Zlib,
                VncEncoding.Hextile,
                VncEncoding.CopyRect,
                VncEncoding.Raw,
                //VncEncoding.ATEN,
                VncEncoding.PseudoDesktopSize,
            };

            this.c.Send(new[] { (byte)2, (byte)0 });
            this.c.SendUInt16BE((ushort)encodings.Length);
            foreach (var encoding in encodings)
            {
                this.c.SendUInt32BE((uint)encoding);
            }

            // lblStatus.Text = "Connected to " + ServerName;

            Connected = true;
            SendUpdateRequests(GetCleanDirtyReset(), FramebufferWidth, FramebufferHeight);


            this.threadMain = new Thread(this.ThreadMain);
            this.threadMain.IsBackground = true;
            this.threadMain.Start();

            Thread videoSinger = new Thread(ThreadVideoSinger);
            videoSinger.Start();
            //prgLogin.Visible = false;
            #endregion
        }

        private void ThreadVideoSinger(object? obj)
        {
            while (true)
            {
                if (!c.isOpen)
                    break;
                if (!spamUpdateRequests)
                {
                    Thread.Sleep(1000);
                }
                else
                {
                    Console.WriteLine("ThreadVideoSinger");
                    SendUpdateRequests(GetCleanDirtyReset(), FramebufferWidth, FramebufferHeight);
                    Thread.Sleep(1000);
                }
            }
        }
        private bool NegotiateVersion()
        {
            this.serverVersion = this.c.ReceiveVersion();
            Console.WriteLine("VNC Version: " + serverVersion);
            if (serverVersion == new Version(55, 8))
            {
                IsAST24000 = true;
            }
            this.c.SendVersion(serverVersion);
            return true;
        }
        private async Task<bool> DoAuthentication()
        {
            int count;
            try
            {
                count = this.c.ReceiveByte();
                if (count == 0)
                {
                    string message = this.c.ReceiveString().Trim('\0');
                    await new ContentDialog() { Content = "Connection to " + Host + ":5900 failed: as the server has not provided any authentication methods. Error from the server: " + message, PrimaryButtonText = "OK" }.ShowAsync(this);
                    Close();
                    return false;
                }
            }
            catch (Exception e)
            {
                await new ContentDialog() { Content = "Auth begin fail: " + e.Message, PrimaryButtonText = "OK" }.ShowAsync(this);
                Close();
                return false;
            }

            var types = new List<AuthenticationMethod>();
            for (int i = 0; i < count; i++)
            {
                types.Add((AuthenticationMethod)this.c.ReceiveByte());
            }

            if (types.Contains(AuthenticationMethod.SuperMicro))
            {
                this.c.SendByte((byte)AuthenticationMethod.SuperMicro);

                DoInsydeAuth();

                try
                {
                    uint status = this.c.ReceiveUInt32BE();
                    if (status != 0)
                    {
                        string message = this.c.ReceiveString().Trim('\0');
                        await new ContentDialog() { Content = "Connection to " + Host + ":5900 failed: as the username or password is incorrect. Message from server: " + message, PrimaryButtonText = "OK" }.ShowAsync(this);
                        Close();
                        return false;
                    }
                }
                catch (Exception e)
                {
                    await new ContentDialog() { Content = "Cannot check if the username/pasword is correct as a error has occured. This is most likely due to them being incorrect." + e.Message, PrimaryButtonText = "OK" }.ShowAsync(this);
                    Close();
                    return false;
                }
            }
            else
            {
                await new ContentDialog() { Content = "Connection to " + Host + ":5900 failed: as the server has not provided any supported authentication methods. The only supported methods are Supermicro", PrimaryButtonText = "OK" }.ShowAsync();
                return false;
            }

            return true;
        }
        private void DoInsydeAuth()
        {
            int definedAuthLen = 24;
            var challenge = c.Receive(24);

            byte[] user = new byte[definedAuthLen];
            byte[] pw = new byte[definedAuthLen];

            for (int i = 0; i < definedAuthLen; i++)
            {
                if (i < Username.Length)
                {
                    user[i] = (byte)Username[i];
                }
                else
                {
                    user[i] = 0;
                }
            }

            for (int i = 0; i < definedAuthLen; i++)
            {
                if (i < Password.Length)
                {
                    pw[i] = (byte)Password[i];
                }
                else
                {
                    pw[i] = 0;
                }
            }

            c.Send(user);
            c.Send(pw);
            c.Flush();
        }
        private void ThreadMain()
        {
            try
            {
                while (true && c.isOpen)
                {
                    this.HandleResponse();
                }
            }
            catch (Exception ex)
            {
                client.Close();
                Debug.WriteLine(ex.ToString());
                Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await new ContentDialog() { Content = "A error has occured while recieving data:\n" + ex.ToString(), PrimaryButtonText = "OK" }.ShowAsync();
                });
            }
        }
        #region Message handling
        private void HandleResponse()
        {
            if (!c.isOpen)
                return;
            try
            {
                if (client.Available == 0)
                {
                    return;
                }
            }
            catch
            {
                return;
            }
            RFBMessageType command = (RFBMessageType)this.c.ReceiveByte();

            switch (command)
            {
                case RFBMessageType.FramebufferUpdate:
                    if (FrameStart == 0)
                    {
                        FrameStart = DateTime.Now.Ticks;
                    }
                    else
                    {
                        FrameEnd = DateTime.Now.Ticks;
                    }

                    if (HandleFramebufferUpdate())
                    {
                        SendFramebufferUpdateRequest(true);
                        //SendUpdateRequests(GetCleanDirtyReset(), FramebufferWidth, FramebufferHeight);
                    }

                    if (FrameStart != 0 && FrameEnd != 0)
                    {
                        var s = TimeSpan.FromTicks(FrameEnd) - TimeSpan.FromTicks(FrameStart);
                        FPS = (int)s.TotalSeconds;
                        UpdateTitle();
                        FrameStart = 0;
                        FrameEnd = 0;
                    }

                    break;
                case RFBMessageType.SetColorMapEntries:
                    c.ReceiveByte(); //padding

                    var firstColor = c.ReceiveUInt16BE();
                    var num_colors = c.ReceiveUInt16BE();

                    for (int i = 0; i < num_colors; i++)
                    {
                        var red = c.ReceiveUInt16BE() / 256;
                        var green = c.ReceiveUInt16BE() / 256;
                        var blue = c.ReceiveUInt16BE() / 256;

                        //todo
                    }

                    break;
                case RFBMessageType.ATENFrontGroundEvent:
                    c.Receive(20);
                    break;


                case RFBMessageType.ATENSessionMessage:
                    var count = c.ReceiveUInt32BE(); // u32
                    var tmp = c.Receive(4); // u32

                    int ctrl_code = 0;
                    for (var i = 0; i < 4; i++)
                        ctrl_code += tmp[i] * (int)Math.Pow(10, 3 - i);

                    var cMsg = c.Receive(256);

                    HandleIKVMMessage(ctrl_code, count, cMsg);

                    break;
                default:
                    Debug.WriteLine("Command not implemented: " + (int)command);
                    break;
            }
        }
        #endregion
        private void HandleIKVMMessage(int code, uint counter, byte[] cMsg)
        {
            var i = 0;
            var j = 0;
            var user = "";
            var index = 0;
            var ip = "0";
            var id = "";
            var disconnect = 0;
            var msg = "";

            while (cMsg[i] != 32 && i < 256 - j)
                id += (char)cMsg[i++];
            i++;
            j = i;
            while (cMsg[i] != 32 && i < 256 - j)
                user += (char)cMsg[i++];
            i++;
            j = i;
            while (cMsg[i] != 0 && i < 256 - j)
                ip += (char)cMsg[i++];
            if (ip.IndexOf(':') == -1)
            {
                string newIP = "";
                foreach (var item in ip.Split("."))
                {
                    var c = ("00" + item);
                    newIP += c.Substring(0, c.Length - 3);
                }
            }
            else
            {
                ip = ip.Substring(1);
            }

            switch (code)
            {
                //User join
                case 0:
                case 1:
                    Debug.WriteLine($"User {id} ({user}) with IP {ip} joined");
                    break;
                case 2:
                    Debug.WriteLine($"User {id} with IP {ip} left");
                    break;
                case 3:
                    Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        await new ContentDialog() { Content = "You have been disconnected because user logout happened!", PrimaryButtonText = "OK" }.ShowAsync(this);
                    });
                    Connected = false;
                    break;
                case 4:
                    Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        await new ContentDialog() { Content = "You have been disconnected because to many users are joined!", PrimaryButtonText = "OK" }.ShowAsync(this);
                    });
                    Connected = false;
                    break;
                case 5:
                    //unknown
                    break;
                case 6:
                    //unknown
                    break;
                case 7:
                    //unknown
                    break;
                case 8:
                    // MessageBox.Show("You have been disconnected because a BIOS update is in progress");
                    Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        await new ContentDialog() { Content = "You have been disconnected because a BIOS update is in progress", PrimaryButtonText = "OK" }.ShowAsync(this);
                    });
                    Connected = false;
                    break;
                case 9:
                    //  MessageBox.Show("");
                    Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        await new ContentDialog() { Content = "You have been disconnected because a firmware update is in progress", PrimaryButtonText = "OK" }.ShowAsync(this);
                    });
                    Connected = false;
                    break;
                default:
                    throw new NotImplementedException();
            }
        }
        private bool HandleFramebufferUpdate()
        {
            bool ret = true;


            // This is always read, which is not what noVNC does.
            c.ReceiveByte(); // padding
            numRects = c.ReceiveUInt16BE();
            bytes = 0;

            if (numRects != 1)
            {
                throw new Exception("rectange count should always be 1");
            }

            if (FrameStart != 0)
            {
                var duration = DateTime.Now.Ticks - FrameStart;
                FPS = (int)TimeSpan.FromTicks(duration).TotalSeconds;
                UpdateTitle();
                FrameStart = DateTime.Now.Ticks;
            }
            else
            {
                FrameStart = DateTime.Now.Ticks;
            }


            if (bytes == 0)
            {
                var r = this.c.ReceiveRectangle();
                VncEncoding encoding = (VncEncoding)this.c.ReceiveUInt32BE();
                int x = r.X, y = r.Y, w = r.Width, h = r.Height;

                if (r.X != 0 || r.Y != 0)
                {
                    throw new Exception("Unexpected start x,y,w,h values: " + r.ToString() + ". Typically, X and Y should be 0");
                }


                // HERMON uses 0x00 even when it is meant to be 0x59
                if (encoding == 0)
                {
                    encoding = VncEncoding.AtenHermon;
                }

                switch (encoding)
                {
                    case VncEncoding.AtenHermon:
                        ret = HandleHermonEncoding(h, w, x, y, 2);
                        break;
                    case VncEncoding.AtenAST2100:
                        HandleATENAst2100Encoding(h, w, x, y, 2);
                        break;
                    default:
                        throw new Exception("Unsupported encoding.");
                }
            }

            return ret;
        }
        #region Encoding
        private unsafe bool HandleHermonEncoding(int h, int w, int x, int y, int bpp)
        {
            // Common code start
            var txtmode = (int)this.c.ReceiveUInt32BE();
            aten_len = (int)this.c.ReceiveUInt32BE();

            if ((short)w <= 0 && (short)h <= 0)
            {
                Debug.WriteLine("[WARN] AMI screen is probably OFF");
                ResizeFB(640, 480);

                // Required to recieve further updates
                FramebufferWidth = -640;
                FramebufferHeight = -480;
                spamUpdateRequests = true;
                HasVideoSignal = false;

                UpdateTitle();

                aten_len = 0;

                return false;
            }
            else
            {
                spamUpdateRequests = false;
                if (!HasVideoSignal)
                {
                    HasVideoSignal = true;
                    UpdateTitle();
                }
            }



            if (h != Framebuffer.PixelSize.Height | w != Framebuffer.PixelSize.Width)
            {
                ResizeFB(w, h);
            }

            // common code end

            //if (aten_type == -1) // TODO: Is this needed?
            {
                aten_type = this.c.ReceiveByte();
                c.Receive(1);
                c.Receive(4); // number of subrects

                if (aten_len != this.c.ReceiveUInt32BE())
                    throw new Exception("ATEN RAW len mismatch");

                aten_len -= 10;
            }

            while (aten_len > 0)
            {
                switch (aten_type)
                {
                    case 0: //Subrects:
                        bytes = 6 + (16 * 16 * bpp); // at least a subrect
                        var a = c.ReceiveUInt16BE();
                        var b = c.ReceiveUInt16BE();
                        var yy = c.ReceiveByte();
                        var xx = c.ReceiveByte();

                        byte[] buffer2 = new byte[16 * 16 * 4];
                        ConvertColor(buffer2, c.Receive(bytes - 6), out int j1);

                        int proper_x = xx * 16;
                        int proper_y = yy * 16;

                        // draw rectangle & update screen
                        var bmpData2 = Framebuffer.Lock();
                        DrawImageToBuffer(buffer2, bmpData2, 16, 16, proper_x, proper_y);
                        bmpData2.Dispose();

                        Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            DisplayImage.Source = Framebuffer;
                            DisplayImage.InvalidateVisual();
                        });


                        aten_len -= bytes;
                        bytes = 0;
                        break;
                    case 1: //RAW
                        var olines = this.lines == 0 ? h : lines;

                        //begin

                        if (lines == 0)
                        {
                            lines = h;
                        }

                        bytes = w * bpp; // at least a line

                        var cur_y = y + (h - lines);

                        // while (bytes >= client.Available) { }
                        var floor = (int)Math.Floor((double)(client.Available / (w * bpp)));
                        var curr_height = (int)Math.Min(lines, floor);

                        byte[] buffer = new byte[curr_height * w * 4];
                        ConvertColor(buffer, this.c.Receive(curr_height * w * bpp), out int j0);

                        var yyy = h - lines;

                        Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            var bmpData = Framebuffer.Lock();
                            DrawImageToBuffer(buffer, bmpData, w, curr_height, 0, yyy);
                            bmpData.Dispose();
                            DisplayImage.Source = Framebuffer;
                            DisplayImage.InvalidateVisual();
                        }).Wait();



                        lines -= curr_height;

                        if (lines > 0)
                        {
                            bytes = w * bpp;
                        }
                        else
                        {
                            numRects--;
                            bytes = 0;
                        }

                        //end

                        aten_len -= (olines - lines) * w * bpp;

                        if (bytes > 0)
                        {
                            //return;
                            //  throw new Exception();
                        }

                        break;
                    default:
                        throw new Exception("Unknown ATEN_HERMON  type: " + aten_type);
                }
            }

            if (aten_len < 0)
            {
                Debug.WriteLine("aten_len dropped below zero");
            }
            if (aten_type == 0)
            {
                numRects--;
            }

            aten_len = -1;
            aten_type = -1;
            return true;
        }
        private unsafe bool HandleATENAst2100Encoding(int h, int w, int x, int y, int bpp)
        {
            // Common code start
            var txtmode = (int)this.c.ReceiveUInt32BE();
            aten_len = (int)this.c.ReceiveUInt32BE();
            var data = c.Receive(aten_len);

            if ((short)w <= 0 && (short)h <= 0)
            {
                Debug.WriteLine("[WARN] AMI screen is probably OFF");
                ResizeFB(640, 480);

                // Required to recieve further updates
                FramebufferWidth = -640;
                FramebufferHeight = -480;
                spamUpdateRequests = true;
                HasVideoSignal = false;

                UpdateTitle();

                aten_len = 0;

                return false;
            }
            else
            {
                spamUpdateRequests = false;
                if (!HasVideoSignal)
                {
                    HasVideoSignal = true;
                    UpdateTitle();
                }
            }



            if (h != Framebuffer.PixelSize.Height | w != Framebuffer.PixelSize.Width)
            {
                ResizeFB(w, h);
            }

            // common code end

            try
            {
                ast2100Decoder.Decode(data, w, h);
            }
            catch
            {

            }

            // the encoder already converts color for us, and this codec outputs the full display
            // buffer for some reason.


            var bmpData2 = Framebuffer.Lock();
            fixed(byte* ptr = ast2100Decoder.mOutBuffer)
            {
                Buffer.MemoryCopy(ptr, (void*)bmpData2.Address, bmpData2.RowBytes * bmpData2.Size.Height, ast2100Decoder.mOutBuffer.LongLength);
            }
            bmpData2.Dispose();

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                DisplayImage.Source = Framebuffer;
                DisplayImage.InvalidateVisual();
            });

            return true;
        }
        #endregion
        #region Requests
        public void SMCPowerAction(SMCPowerActionType type)
        {
            if (!Connected)
            {
                return;
            }
            Debug.WriteLine("Sending power action: " + type);
            var p = new byte[2];

            p[0] = 26; //26: poweroff
            p[1] = (byte)type;

            this.c.Send(p);
        }
        private void SendFramebufferUpdateRequest(bool incremental)
        {
            this.SendFramebufferUpdateRequest(incremental, 0, 0, (short)FramebufferWidth, (short)FramebufferHeight);
        }
        private void SendFramebufferUpdateRequest(bool incremental, int x, int y, short width, short height)
        {
            this.c.Send(GetFramebufferUpdateRequestBytes(incremental, x, y, width, height));
        }
        private byte[] GetFramebufferUpdateRequestBytes(bool incremental, int x, int y, short width, short height)
        {
            //Debug.WriteLine("Sending FramebufferUpdate");
            Console.WriteLine($"update region {x} {y} {width}x{height}");
            var p = new byte[10];

            if (width < 0 && height < 0)
            {
                incremental = false;
            }

            byte[] res = new byte[10];
            res[0] = 3;
            res[1] = incremental ? (byte)1 : (byte)0;
            Array.Copy(BitConverter.GetBytes((short)x), 0, res, 2, 2);
            Array.Copy(BitConverter.GetBytes((short)y), 0, res, 4, 2);
            Array.Copy(BitConverter.GetBytes((short)width), 0, res, 6, 2);
            Array.Copy(BitConverter.GetBytes((short)height), 0, res, 8, 2);
            return res;
        }
        public void SendATENPointerEvent(int x, int y, int pressedButtons)
        {
            if (!Connected)
            {
                return;
            }
            Debug.WriteLine("Sending SetPointer");
            var p = new byte[18];

            p[0] = (byte)5;
            p[1] = 0;
            p[2] = (byte)pressedButtons;

            p[3] = (byte)(x >> 8);
            p[4] = (byte)(x);

            p[5] = (byte)(y >> 8);
            p[6] = (byte)(y);
            //VncUtility.EncodeUInt16BE(p, 2, (ushort)x);
            //VncUtility.EncodeUInt16BE(p, 4, (ushort)y);
            this.c.Send(p);
        }
        public void SendATENKbdEvent(int keysym, bool down)
        {
            if (!Connected)
            {
                return;
            }
            Debug.WriteLine("Sending SetKeyboard");
            var p = new byte[18];

            p[0] = (byte)4;
            p[1] = 0;
            p[2] = down == true ? (byte)1 : (byte)0;

            p[3] = 0;
            p[4] = 0;

            p[5] = (byte)(keysym >> 24);
            p[6] = (byte)(keysym >> 16);
            p[7] = (byte)(keysym >> 8);
            p[8] = (byte)(keysym);

            this.c.Send(p);
        }
        #endregion
        #region FB Utils
        private void ConvertColor(byte[] OutArray, byte[] InArray, out int j)
        {
            j = 0;
            try
            {
                if (FramebufferPixelFormat == null)
                    throw new Exception();

                var bpp = 2;// FramebufferPixelFormat.BitsPerPixel;

                var redMult = (byte)(256 / FramebufferPixelFormat.RedMax);
                var greenMult = (byte)(256 / FramebufferPixelFormat.GreenMax);
                var blueMult = (byte)(256 / FramebufferPixelFormat.BlueMax);

                for (int i = 0; i < InArray.Length; i += bpp)
                {
                    int pix = 0;
                    for (int k = 0; k < bpp; k++)
                    {
                        if (FramebufferPixelFormat.IsLittleEndian)
                        {
                            //default
                            pix = (InArray[i + k] << (k * 8)) | pix;
                        }
                        else
                        {
                            pix = (pix << 8) | InArray[i + k];
                        }
                    }

                    var blue = (byte)(((pix >> FramebufferPixelFormat.BlueShift) & FramebufferPixelFormat.BlueMax) * blueMult);
                    var red = (byte)(((pix >> FramebufferPixelFormat.RedShift) & FramebufferPixelFormat.RedMax) * redMult);
                    var green = (byte)(((pix >> FramebufferPixelFormat.GreenShift) & FramebufferPixelFormat.GreenMax) * greenMult);


                    //Red (actually blue)
                    OutArray[j] = red;
                    //Green
                    OutArray[j + 1] = green;
                    //Blue (actually red)
                    OutArray[j + 2] = blue;
                    //Alpha
                    OutArray[j + 3] = 255;

                    j += 4;
                }
            }
            catch (Exception ex)
            {

                // MessageBox.Show(ex.ToString());
            }
        }
        private void ResizeFB(int w, int h)
        {
            if (FramebufferWidth != w || FramebufferHeight != h)
            {
                Debug.WriteLine("framebuffer resize: " + w + "," + h);

                FramebufferHeight = h;
                FramebufferWidth = w;
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    this.Framebuffer = new WriteableBitmap(new Avalonia.PixelSize(w, h), new Avalonia.Vector(96.0, 96.0), PixelFormats.Rgba8888);
                    DisplayImage.Source = Framebuffer;

                    // force redraw of entire screen as creating bitmap does not preserve pixels
                    this._cleanRect = new RectInfo(0, 0, -1, -1);
                });
            }
        }
        private unsafe void DrawImageToBuffer(byte[] sourceBytes, ILockedFramebuffer destination, int sourceWidth, int sourceHeight, int destinationX, int destinationY)
        {
            // TODO: misha: not sure why this occurs!
            if (destinationX + sourceWidth > Framebuffer.Size.Width)
            {
                //Debug.WriteLine($"INVAILD draw to {destinationX},{destinationY}");
                return;
            }

            // Calculate the number of bytes per pixel (4 for ARGB)
            int bytesPerPixel = 4;

            // Calculate the byte width of one row in the source image
            int sourceRowByteWidth = sourceWidth * 4;

            // Calculate the byte width of one row in the destination buffer
            int destinationRowByteWidth = destination.RowBytes;
            byte* ptr = (byte*)destination.Address;

            // Iterate over each row of the source image
            for (int y = 0; y < sourceHeight; y++)
            {
                // Calculate the starting byte index for the current row in the source image
                int sourceRowIndex = y * sourceRowByteWidth;

                // Calculate the starting byte index for the current row in the destination buffer
                int destinationRowIndex = (destinationY + y) * destinationRowByteWidth + (destinationX * bytesPerPixel);

                // Copy the current row from the source image to the destination buffer

                if (destinationY + y < Framebuffer.Size.Height)
                {
                    for (int i = 0; i < sourceRowByteWidth; i++)
                    {
                        ptr[destinationRowIndex + i] = sourceBytes[sourceRowIndex + i];
                    }
                }

            }
        }
        private CleanDirty GetCleanDirtyReset()
        {
            BoxInfo vp = new BoxInfo(0, 0, FramebufferWidth, FramebufferHeight);
            RectInfo cr = _cleanRect;

            BoxInfo cleanBox = new BoxInfo(cr.x1, cr.y1, cr.x2 - cr.x1 + 1, cr.y2 - cr.y1 + 1);
            List<BoxInfo> DirtyBoxes = new List<BoxInfo>();

            if (cr.x1 >= cr.x2 || cr.y1 >= cr.y2)
            {
                DirtyBoxes.Add(new BoxInfo(vp.x, vp.y, vp.w, vp.h));
            }
            else
            {
                var vx2 = vp.x + vp.w - 1;
                var vy2 = vp.y + vp.h - 1;

                if (vp.x < cr.x1)
                {
                    DirtyBoxes.Add(new BoxInfo(vp.x, vp.y, cr.x1 - vp.x, vp.h));
                }
                if (vx2 > cr.x2)
                {
                    DirtyBoxes.Add(new BoxInfo(cr.x2 + 1, vp.y, vx2 - cr.x2, vp.h));
                }
                if (vp.y < cr.y1)
                {
                    DirtyBoxes.Add(new BoxInfo(cr.x1, vp.y, cr.x2 - cr.x1 + 1, cr.y1 - vp.y));
                }
                if (vy2 > cr.y2)
                {
                    DirtyBoxes.Add(new BoxInfo(cr.x1, cr.y2 + 1, cr.x2 - cr.x1 + 1, vy2 - cr.y2));
                }
            }

            _cleanRect = new RectInfo(vp.x, vp.y, vp.x + vp.w - 1, vp.y + vp.h - 1);
            return new CleanDirty() { Clean = cleanBox, Dirty = DirtyBoxes };
        }

        private void SendUpdateRequests(CleanDirty info, int width, int height)
        {
            BoxInfo cb = info.Clean;
            int dirtyLen = info.Dirty.Count;

            List<byte> toSend = new List<byte>();

            if (dirtyLen != 0)
            {
                var db = info.Dirty[dirtyLen - 1];
                int w = db.w == 0 ? width : db.w;
                int h = db.h == 0 ? height : db.h;
                toSend.AddRange(GetFramebufferUpdateRequestBytes(false, db.x, db.y, (short)w, (short)h));
            }
            else if (cb.w > 0 && cb.h > 0)
            {
                int w = cb.w == 0 ? width : cb.w;
                int h = cb.h == 0 ? height : cb.h;
                toSend.AddRange(GetFramebufferUpdateRequestBytes(true, cb.x, cb.y, (short)w, (short)h));
            }

            c.Send(toSend.ToArray());
        }
        #endregion
        #region UI
        private int ConvertKey(PhysicalKey physicalKey)
        {
            if (PhysicalKey.A <= physicalKey && PhysicalKey.Z >= physicalKey)
            {
                return 0x04 + ((int)physicalKey - (int)PhysicalKey.A);
            }
            if (PhysicalKey.Digit1 <= physicalKey && PhysicalKey.Digit9 >= physicalKey)
            {
                return 0x1e + ((int)physicalKey - (int)PhysicalKey.Digit1);
            }
            if (PhysicalKey.F1 <= physicalKey && PhysicalKey.F12 >= physicalKey)
            {
                return 0x3a + ((int)physicalKey - (int)PhysicalKey.F1);
            }
            switch (physicalKey)
            {
                case PhysicalKey.None:
                    break;
                case PhysicalKey.Backquote:
                    return 0x35;
                case PhysicalKey.Backslash:
                    return 0x31;
                case PhysicalKey.BracketLeft:
                    return 0x2f;
                case PhysicalKey.BracketRight:
                    return 0x30;
                case PhysicalKey.Comma:
                    return 0x36;
                case PhysicalKey.Digit0:
                    return 0x27;
                case PhysicalKey.Digit1:
                    break;
                case PhysicalKey.Digit2:
                    break;
                case PhysicalKey.Digit3:
                    break;
                case PhysicalKey.Digit4:
                    break;
                case PhysicalKey.Digit5:
                    break;
                case PhysicalKey.Digit6:
                    break;
                case PhysicalKey.Digit7:
                    break;
                case PhysicalKey.Digit8:
                    break;
                case PhysicalKey.Digit9:
                    break;
                case PhysicalKey.Equal:
                    return 0x2e;
                case PhysicalKey.IntlBackslash:
                    break;
                case PhysicalKey.IntlRo:
                    break;
                case PhysicalKey.IntlYen:
                    break;


                case PhysicalKey.Minus:
                    return 0x2d;
                case PhysicalKey.Period:
                    return 0x37;
                case PhysicalKey.Quote:
                    break;
                case PhysicalKey.Semicolon:
                    return 0x33;
                case PhysicalKey.Slash:
                    return 0x38;
                case PhysicalKey.AltLeft:
                case PhysicalKey.AltRight:
                    return 0xe2;
                case PhysicalKey.Backspace:
                    return 0x2a;
                case PhysicalKey.CapsLock:
                    return 0x39;
                case PhysicalKey.ContextMenu:
                    break;
                case PhysicalKey.ControlLeft:
                case PhysicalKey.ControlRight:
                    return 0xe0;
                case PhysicalKey.Enter:
                    return 0x28;
                case PhysicalKey.MetaLeft:
                case PhysicalKey.MetaRight:
                    return 0xe3;
                case PhysicalKey.ShiftLeft:
                case PhysicalKey.ShiftRight:
                    return 0xe1;
                case PhysicalKey.Space:
                    return 0x2c;
                case PhysicalKey.Tab:
                    return 0x2b;
                case PhysicalKey.Delete:
                    return 0x4c;
                case PhysicalKey.End:
                    return 0x4d;
                case PhysicalKey.Home:
                    return 0x4a;
                case PhysicalKey.Insert:
                    return 0x49;
                case PhysicalKey.PageDown:
                    return 0x4e;
                case PhysicalKey.PageUp:
                    return 0x4b;
                case PhysicalKey.ArrowDown:
                    return 0x51;
                case PhysicalKey.ArrowLeft:
                    return 0x50;
                case PhysicalKey.ArrowRight:
                    return 0x4f;
                case PhysicalKey.ArrowUp:
                    return 0x52;
                case PhysicalKey.NumLock:
                    return 0; //todo
                case PhysicalKey.NumPad0:
                    break;
                case PhysicalKey.NumPad1:
                    break;
                case PhysicalKey.NumPad2:
                    break;
                case PhysicalKey.NumPad3:
                    break;
                case PhysicalKey.NumPad4:
                    break;
                case PhysicalKey.NumPad5:
                    break;
                case PhysicalKey.NumPad6:
                    break;
                case PhysicalKey.NumPad7:
                    break;
                case PhysicalKey.NumPad8:
                    break;
                case PhysicalKey.NumPad9:
                    break;
                case PhysicalKey.NumPadAdd:
                    break;
                case PhysicalKey.NumPadClear:
                    break;
                case PhysicalKey.NumPadComma:
                    break;
                case PhysicalKey.NumPadDecimal:
                    break;
                case PhysicalKey.NumPadDivide:
                    break;
                case PhysicalKey.NumPadEnter:
                    break;
                case PhysicalKey.NumPadEqual:
                    break;
                case PhysicalKey.NumPadMultiply:
                    break;
                case PhysicalKey.NumPadParenLeft:
                    break;
                case PhysicalKey.NumPadParenRight:
                    break;
                case PhysicalKey.NumPadSubtract:
                    break;
                case PhysicalKey.Escape:
                    return 0x29;
                case PhysicalKey.PrintScreen:
                    return 0x46;
                case PhysicalKey.ScrollLock:
                    return 0x47;
                case PhysicalKey.Pause:
                    return 0x48;
                default:
                    break;
            }
            return 0;
        }
        private void Image_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
        {
            SendATENKbdEvent(ConvertKey(e.PhysicalKey), true);
        }
        private void Image_KeyUp(object? sender, Avalonia.Input.KeyEventArgs e)
        {
            SendATENKbdEvent(ConvertKey(e.PhysicalKey), false);
        }
        private void Exit_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (c != null)
            {
                c.Close();
            }
            Close();
        }
        private void VNCViewer_Closed(object? sender, EventArgs e)
        {
            if (c != null)
            {
                c.Close();
            }
            Close();
        }
        private void Image_Tapped(object? sender, Avalonia.Input.TappedEventArgs e)
        {
            e.Pointer.Capture(DisplayImage);
        }
        private void ImmediateShutdown_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            SMCPowerAction(SMCPowerActionType.PowerOff);
        }
        private void PowerCycle_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            SMCPowerAction(SMCPowerActionType.PowerReset);
        }
        private void Poweron_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            SMCPowerAction(SMCPowerActionType.PowerOn);
        }
        private void ACPIShutdown_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            SMCPowerAction(SMCPowerActionType.SoftShutdown);
        }
        private void UpdateTitle()
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                string title = useSSL ? "iKVM Viewer (SSL) - " : "iKVM Viewer - ";

                if (VideoOnly)
                {
                    title += "- Video only - ";
                }

                if (HasVideoSignal)
                {
                    title += $"{FramebufferWidth}x{FramebufferHeight} ";
                    title += "(FPS: " + FPS + ")";
                }
                else
                {
                    title += "No signal";
                }
                Title = title;
            });
        }
        #endregion

        //private void pictureBox1_MouseMove(object sender, MouseEventArgs e)
        //{
        //   // var cursorPostion = DisplayBuffer.PointToClient(Cursor.Position);
        //    //SendPointerEvent(cursorPostion.X, cursorPostion.Y, 0);
        //}
    }

    public enum RFBMessageType : byte
    {
        FramebufferUpdate = 0,
        SetColorMapEntries = 1,
        Bell = 2,
        ReceiveClipboardData = 3,
        ATENSessionMessage = 57,
        ATENFrontGroundEvent = 4,
        DesktopSize = 200,
    }

    public enum SMCPowerActionType : byte
    {
        PowerOn = 1,
        PowerOff = 0,
        SoftShutdown = 3,
        PowerReset = 2,
    }
}
