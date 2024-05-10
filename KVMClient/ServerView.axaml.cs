using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;
using KVMClient.Core.IPMI.HTTP;
using KVMClient.Core.IPMI.UDP;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace KVMClient;

public partial class ServerView : UserControl
{
    private IpmiHttpClient hclient;
    private IpmiUdpClient iclient;
    private string IP = "";
    private string Username = "";
    private string Password = "";
    public ServerView()
    {
        InitializeComponent();
        AddHandler(Frame.NavigatingFromEvent, OnNavigatingFrom, RoutingStrategies.Direct);
        AddHandler(Frame.NavigatedFromEvent, OnNavigatedFrom, RoutingStrategies.Direct);
        AddHandler(Frame.NavigatedToEvent, OnNavigatedTo, RoutingStrategies.Direct);
    }

    private void OnNavigatedTo(object? sender, NavigationEventArgs e)
    {
        DoLoading((ServerDef)e.Parameter);
    }

    private void OnNavigatedFrom(object? sender, NavigationEventArgs e)
    {

    }

    private void OnNavigatingFrom(object? sender, NavigatingCancelEventArgs e)
    {

    }

    private void UpdateLoading(bool done, string message = "")
    {
        groupLoading.IsVisible = !done;
        tabContent.IsVisible = done;

        txtLoadingStatus.Text = message;
    }
    private async void DoLoading(ServerDef parameter)
    {
        IP = parameter.IP;
        Username = parameter.Username;
        Password = parameter.Password;

        try
        {
            hclient = new IpmiHttpClient(parameter.IP);
            iclient = new IpmiUdpClient();

            UpdateLoading(false, "Connecting to IPMI port");
            await LoadIpmi();

            UpdateLoading(false, "Connecting");

            // If IPMI protocol works but http doesnt then dont show error dialogue
            httpLoadSuccessful = await hclient.Authenticate(parameter.Username, parameter.Password);
            if (!httpLoadSuccessful && !ipmiLoadSuccessful)
            {
                await new ContentDialog() { Content = "Error while loading content: Authentication failed. Check username and password. AMI (typically used in X8 motherboards) firmware is not supported.", PrimaryButtonText = "OK", Title = "Authentication failed" }.ShowAsync();
            }
            else
            {
                txtTop.Text = IP;
                UpdateLoading(false, "Gathering data");
                await InitData();
            }
        }
        catch (Exception ex)
        {
            progressRing.IsVisible = false;
            await new ContentDialog() { Content = "Error while loading content: " + ex.ToString(), PrimaryButtonText = "OK", Title = "Failed to connect to " + parameter.IP + ". Press OK To attempt to load IPMI protocol. Functionality will be limited." }.ShowAsync();
        }
    }

    private async Task InitData()
    {

        //load mac addresses
        if (httpLoadSuccessful)
        {
            try
            {
                UpdateLoading(false, "Loading platform info");
                var datas = await hclient.GetPlatformInfo();

                if (datas != null)
                {
                    groupBiosVer.IsVisible = false;

                    int i = 1;
                    foreach (var data in datas.InterfaceMacs)
                    {
                        var w = new WrapPanel();
                        w.Orientation = Avalonia.Layout.Orientation.Vertical;

                        var txt = new TextBlock();
                        txt.Text = "LAN " + (i++) + " MAC:";
                        txt.Foreground = Brushes.Gray;

                        var sel = new SelectableTextBlock();
                        sel.Text = data;
                        sel.FontSize = 19;

                        w.Children.Add(txt);
                        w.Children.Add(sel);

                        MainWrapPanel.Children.Add(w);
                    }

                    lblBiosVer.Text = datas.BiosVer;
                    lblBiosDate.Text = "(" + datas.BiosDate + ")";
                    if (!string.IsNullOrWhiteSpace(datas.BiosVer))
                    {
                        groupBiosVer.IsVisible = true;
                    }

                    lblBmcFwVer.Text = datas.BMCFWVer[..2] + "." + datas.BMCFWVer[2..];
                    lblBmcFwDate.Text = datas.BMCFWDate;
                    lblBmcMac.Text = datas.BMCMac;
                }
            }
            catch
            {

            }

            //Load power
            UpdateLoading(false, "Loading power state");
            await LoadPowerInfo();

            UpdateLoading(true);

            //Load preview
            await LoadPreview();
        }
        else
        {
            UpdateLoading(true);
        }
    }
    private bool ipmiLoadSuccessful = false;
    private bool httpLoadSuccessful = false;
    private async Task LoadIpmi()
    {
        try
        {
            var result = await iclient.Start(IP, Username, Password);
            if (result)
            {
                ipmiLoadSuccessful = true;
                lblBoardInfo.Text = await iclient.GetBoardInfo();
            }
            else
            {
                await new ContentDialog() { Content = "A error has occured while communicating with IPMI port (623). Some functionality will be limited, such as board model, power control, and some other features. Probably a bug in this software or wrong creds.\n", PrimaryButtonText = "OK", Title = "IPMI Connection failed" }.ShowAsync();
            }
        }
        catch (Exception ex)
        {
            ipmiLoadSuccessful = false;
            await new ContentDialog() { Content = "A error has occured while connecting to IPMI port (623). Some functionality will be limited, such as board model, power control, and some other features. If the port is accessible, please restart the BMC as soon as possible.\n" + ex.ToString(), PrimaryButtonText = "OK", Title = "IPMI Connection failed" }.ShowAsync();
        }
    }
    #region VNC
    private async Task LoadPreview()
    {
        prgPreview.IsVisible = true;
        VNCPreview.Source = new WriteableBitmap(new PixelSize(640, 480), new Vector(96, 96));

        try
        {
            var stream = await hclient.CapturePreview();

            if (stream == null)
            {
                VNCPreview.Source = new Bitmap(AssetLoader.Open(new Uri("avares://KVMClient/Assets/Images/previewloadfail.png")));
            }
            else
            {
                VNCPreview.Source = new Bitmap(stream);
            }
        }
        catch
        {
            VNCPreview.Source = new Bitmap(AssetLoader.Open(new Uri("avares://KVMClient/Assets/Images/previewloadfail.png")));
        }

        prgPreview.IsVisible = false;
    }
    private async void Preview_Tapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        KvmSession? sess = null;
        try
        {
            sess = await hclient.GetKVMSession();
            if (sess == null)
            {
                await new ContentDialog() { Content = "Failed to open KVM session\n", PrimaryButtonText = "OK", Title = "Connection failed" }.ShowAsync();
                return;
            }
        }
        catch (Exception ex)
        {
            if (sess == null)
            {
                await new ContentDialog() { Content = "Failed to open KVM session:\n" + ex.ToString(), PrimaryButtonText = "OK", Title = "Connection failed" }.ShowAsync();
                return;
            }
        }

        new VNCViewer(new ServerDef(IP, sess.Username, sess.Password), iclient).Show();
    }

    private async void btnRefreshPreview_Click(object? sender, RoutedEventArgs e)
    {
        await LoadPreview();
    }

    #endregion
    #region Power
    private async Task LoadPowerInfo()
    {
        try
        {
            var powers = await hclient.GetPowerState();
            if (powers == "ON")
            {
                btnPoweron.IsEnabled = false;

                btnPoweroff.IsEnabled = true;
                btnReset.IsEnabled = true;
            }
            else
            {
                btnPoweron.IsEnabled = true;

                btnPoweroff.IsEnabled = false;
                btnReset.IsEnabled = false;
            }
        }
        catch { }
    }

    ///// <summary>
    ///// 1 = PowerON, 3 = Reset, 5 = PowerDown
    ///// </summary>
    ///// <param name="state"></param>
    //private async Task SetPowerState(int state)
    //{
    //    txtPower.Text = "L O A D I N G . . .";
    //    try
    //    {
    //        await client.SetPowerState(state);

    //        var powers = await client.GetPowerState();
    //        txtPower.Text = powers;
    //    }
    //    catch { txtPower.Text = "Unknown"; }
    //}
    //private async void btnReboot_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    //{
    //    await SetPowerState(3);
    //}

    //private async void btnPowerOn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    //{
    //    await SetPowerState(1);
    //}

    //private async void btnShutDown_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    //{
    //    await SetPowerState(5);
    //}
    //private async void btnPowerRefresh_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    //{
    //    await LoadPowerInfo();
    //}
    #endregion

    private async void btnResetIPMI_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            btnIPMIReset.IsEnabled = false;
            prgIPMIReset.IsVisible = true;

            var status = await hclient.ResetBMC();
            if (status != "OK")
            {
                ShowDialog("Error", "IPMI reset failed. Status code is " + status);
                btnIPMIReset.IsEnabled = true;
                prgIPMIReset.IsVisible = false;
                return;
            }

            ShowDialog("Success", "IPMI reset was successful. Please wait 1-2 minutes before trying to login again");
        }
        catch (Exception ex)
        {
            ShowDialog("Error", "IPMI reset failed. Error:\n" + ex.Message);
        }
    }

    private async void btnResetIKVM_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            btniKVMReset.IsEnabled = false;
            prgiKVMReset.IsVisible = true;

            await hclient.ResetIKVM();
        }
        catch (Exception ex)
        {
            ShowDialog("Error", "IPMI reset failed. Error:\n" + ex.Message);
        }

        btniKVMReset.IsEnabled = true;
        prgiKVMReset.IsVisible = false;
    }
    private async void ShowDialog(string title, string content)
    {
        await new ContentDialog() { Title = title, Content = content, PrimaryButtonText = "OK" }.ShowAsync();
    }

    private bool loadedHWInfo = false;
    private async void TabControl_SelectionChanged(object? sender, Avalonia.Controls.SelectionChangedEventArgs e)
    {
        if (tabContent != null && tabContent.SelectedItem == hwInfoTab)
        {
            if (!loadedHWInfo)
            {
                loadedHWInfo = true;
                string result = "";
                try
                {
                    var fru = await hclient.GetFRUInfo();
                    if (fru != null)
                    {
                        result += "FRU Info:" + Environment.NewLine;
                        result += $"Chasis type: {fru.CHASSIS.TYPE}, part number: {fru.CHASSIS.PARTNUM}, serial: {fru.CHASSIS.SERIALNUM}" + Environment.NewLine;
                        result += $"Board manufacture date: {fru.BOARD.MFGDATE}, name: {fru.BOARD.PRODNAME}, serial: {fru.BOARD.SERIALNUM}" + Environment.NewLine;
                        result += $"Product name: {fru.PRODUCT.PRODNAME}, serial: {fru.PRODUCT.SERIALNUM}" + Environment.NewLine;
                    }
                    else
                    {
                        result += "Failed to load FRU info" + Environment.NewLine;
                    }
                }
                catch
                {
                    result += "Failed to load FRU info" + Environment.NewLine;
                    loadedHWInfo = false;
                }

                try
                {
                    var hw = await hclient.GetHwInfo();
                    if (hw != null)
                    {
                        result += Environment.NewLine + "Hardware Info:" + Environment.NewLine;
                        result += hw;
                    }
                }
                catch
                {

                }


                txtHwInfo.Text = result;
            }
        }
    }
}