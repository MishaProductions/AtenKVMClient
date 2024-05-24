using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Media.Animation;
using KVMClient.Utils;

namespace KVMClient
{
    public partial class MainWindow : Window
    {
        public static Preferences Preferences = new Preferences();
        public static SettingsObject SettingsObject = new SettingsObject();
        public MainWindow()
        {
            InitializeComponent();
            hosts.Tapped += Hosts_Tapped;
            mainFrame.Navigated += MainFrame_Navigated;
            mainFrame.CacheSize = 0;
            VersionLabel.Text = Program.TitleBranding;
            Loaded += MainWindow_Loaded;
            NavToWelcome();
        }

        private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            SettingsObject = Preferences.GetObj<SettingsObject>();

            hosts.Items.Clear();
            foreach (var setting in SettingsObject.Servers)
            {
                AddServer(setting);
            }
        }

        public static void SaveSettings()
        {
            Preferences.SetObj(SettingsObject);
        }

        private void MainFrame_Navigated(object sender, FluentAvalonia.UI.Navigation.NavigationEventArgs e)
        {
        }

        private void Hosts_Tapped(object? sender, Avalonia.Input.TappedEventArgs e)
        {
            var item = hosts.SelectedItem;
            if (item != null)
            {
                var itm = item as ListBoxItem;
                if (itm != null)
                {
                    var tag = itm.Tag as ServerDef;
                    if (tag != null)
                    {
                        NavToServer(tag);
                    }
                }
            }
        }

        private void NavToWelcome()
        {
            //BtnModifyHost.IsEnabled = false;
            //BtnRemoveHost.IsEnabled = false;
            mainFrame.BackStack.Clear();
            mainFrame.Navigate(typeof(WelcomeView), null, new SlideNavigationTransitionInfo());
        }

        private void NavToServer(ServerDef tag)
        {
            //BtnModifyHost.IsEnabled = true;
            //BtnRemoveHost.IsEnabled = true;
            mainFrame.BackStack.Clear();
            mainFrame.Navigate(typeof(ServerView), tag, new SlideNavigationTransitionInfo());
        }

        private void txtIP_TextChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e)
        {
        }

        private void AddServer(ServerDef def)
        {
            string cnt = string.IsNullOrEmpty(def.Name) ? def.IP : $"{def.Name} ({def.IP})";
            var item = new ListBoxItem()
            {
                Content = cnt,
                Tag = def,
                // Image = new BitmapImage(new Uri("pack://application:,,,/server.png")),
            };
            hosts.Items.Add(item);
        }

        private void Exit_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Close();
        }

        private async void AddHost_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var content = new HostAddDialogue();
            var dialog = new ContentDialog() { Title = "Add host", DefaultButton = ContentDialogButton.Primary, PrimaryButtonText = "Add", SecondaryButtonText = "Cancel", Content = content };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                if (string.IsNullOrEmpty(content.IP) || string.IsNullOrEmpty(content.Username) || string.IsNullOrEmpty(content.Password))
                {
                    await new ContentDialog() { Content = "Please enter username, password and IP/hostname", PrimaryButtonText = "OK" }.ShowAsync();
                    AddHost_Click(sender, e);
                    return;
                }

                var x = new ServerDef(content.IP, content.Username, content.Password, content.Name);
                AddServer(x);
                SettingsObject.Servers.Add(x);
                SaveSettings();
            }
        }

        private async void MenuModifyClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (hosts.SelectedItem != null)
            {
                var host = ((ListBoxItem)hosts.SelectedItem).Tag as ServerDef;
                if (host != null)
                {
                    var content = new HostAddDialogue() { IP = host.IP, Username = host.Username, Name = host.Name };
                    var dialog = new ContentDialog() { Title = "Modify host", DefaultButton = ContentDialogButton.Primary, PrimaryButtonText = "OK", SecondaryButtonText = "Cancel", Content = content };

                    if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                    {
                        if (string.IsNullOrEmpty(content.IP) || string.IsNullOrEmpty(content.Username))
                        {
                            await new ContentDialog() { Content = "Please enter username and IP/hostname", PrimaryButtonText = "OK" }.ShowAsync(this);
                            AddHost_Click(sender, e);
                            return;
                        }

                        host.IP = content.IP;
                        host.Name = content.Name;
                        host.Username = content.Username;
                        if (!string.IsNullOrEmpty(content.Password))
                        {
                            host.Password = content.Password;
                        }

                        SaveSettings();

                        hosts.Items.Clear();
                        foreach (var setting in SettingsObject.Servers)
                        {
                            AddServer(setting);
                        }
                    }
                }
            }
        }
        private void MenuRemoveClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (hosts.SelectedItem != null)
            {
                var host = ((ListBoxItem)hosts.SelectedItem).Tag as ServerDef;
                if (host != null)
                {
                    hosts.Items.Remove(hosts.SelectedItem);
                    SettingsObject.Servers.Remove(host);
                    SaveSettings();
                }
            }
        }
    }

    public class ServerDef
    {
        public string IP { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string Name { get; set; } = "";

        public ServerDef(string IP, string Username, string Password, string name = "")
        {
            this.IP = IP;
            this.Username = Username;
            this.Password = Password;
            this.Name = name;
        }
    }
}