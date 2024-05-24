using Avalonia.Controls;
using KVMClient.Core.VNC.VirtualStorage;
using static KVMClient.Core.VNC.VirtualStorage.VirtualStorageManager;
using System;
using Avalonia.Platform.Storage;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace KVMClient
{
    public partial class VirtualMediaWindow : Window
    {
        public bool AllowClose { get; set; } = false;
        private VNCViewer? Viewer;
        private string[] files = new string[4];
        public string DeviceInfo
        {
            get
            {
                if (txtMountInfo.Text == null)
                    return "";
                return txtMountInfo.Text;
            }
            set
            {
                txtMountInfo.Text = value;
            }
        }

        public VirtualMediaWindow()
        {
            InitializeComponent();
        }
        public VirtualMediaWindow(VNCViewer viewer)
        {
            InitializeComponent();
            this.Viewer = viewer;
        }
        private void Window_Closing(object? sender, Avalonia.Controls.WindowClosingEventArgs e)
        {
            e.Cancel = !AllowClose;
            Hide();
        }

        private void TabControl_SelectionChanged(object? sender, Avalonia.Controls.SelectionChangedEventArgs e)
        {
            if (TabControl != null)
                SetIndex(TabControl.SelectedIndex);
        }

        private async void ChooseFile_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var FileTypeFilter = new List<FilePickerFileType>() { new FilePickerFileType("img") };
            string title = "";
            if (cmbDeviceType.SelectedIndex == 1)
            {
                title = "Open ISO";
                FileTypeFilter = new List<FilePickerFileType>() { new FilePickerFileType("ISO images") { Patterns = ["*.iso"] } };

            }
            else if (cmbDeviceType.SelectedIndex == 2)
            {
                FileTypeFilter = new List<FilePickerFileType>() { new FilePickerFileType("HD image/floppy images") { Patterns = ["*.img", "*.ima"] } };
            }

            if (cmbDeviceType.SelectedIndex != 0)
            {
                // Get top level from the current control. Alternatively, you can use Window reference instead.
                var topLevel = TopLevel.GetTopLevel(this);

                if (topLevel != null)
                {
                    // Start async operation to open the dialog.
                    var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                    {
                        Title = title,
                        AllowMultiple = false,
                        FileTypeFilter = FileTypeFilter
                    });

                    if (files.Count >= 1)
                    {
                        var f = files[0];
                        var name = f.TryGetLocalPath();
                        if (name != null)
                        {
                            txtFileName.Text = Path.GetFileName(name);
                            this.files[TabControl.SelectedIndex] = name;
                        }
                    }
                }
            }
        }

        private async void Mount_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (Viewer != null && Viewer.VirtualStorageManager.deviceStatus.Length != 0)
            {
                if (cmbDeviceType.SelectedIndex != 0)
                {
                    await Viewer.VirtualStorageManager.Mount(TabControl.SelectedIndex, files[TabControl.SelectedIndex], cmbDeviceType.SelectedIndex == 1 ? true : false);
                }
            }
        }

        private async void Unmount_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (Viewer != null && Viewer.VirtualStorageManager.deviceStatus.Length != 0)
            {
                await Viewer.VirtualStorageManager.Unmount(TabControl.SelectedIndex);
            }
        }

        private async void Window_Opened(object? sender, System.EventArgs e)
        {
            if (Viewer != null)
            {
                await Viewer.VirtualStorageManager.GetDeviceInfo();
                txtMountInfo.Text = Viewer.VirtualStorageManager.DeviceInfo;


                SetIndex(0);
            }
        }

        private void SetIndex(int i)
        {
            if (Viewer != null && Viewer.VirtualStorageManager.deviceStatus.Length != 0)
            {
                switch (Viewer.VirtualStorageManager.deviceStatus[i + 1])
                {
                    case VM_DEV_NO_DISK:
                        cmbDeviceType.SelectedIndex = 0;

                        btnMount.IsEnabled = true;
                        btnUnmount.IsEnabled = false;

                        btnChooseFile.IsEnabled = true;
                        cmbDeviceType.IsEnabled = true;
                        break;
                    case VM_DEV_WEB_ISO_MOUNTED:
                    case VM_DEV_ISO_MOUNTED:
                        cmbDeviceType.SelectedIndex = 2;
                        btnMount.IsEnabled = false;
                        btnUnmount.IsEnabled = true;

                        btnChooseFile.IsEnabled = false;
                        cmbDeviceType.IsEnabled = false;
                        break;
                    case VM_DEV_IMA_MOUNTED:
                        cmbDeviceType.SelectedIndex = 1;
                        btnMount.IsEnabled = false;
                        btnUnmount.IsEnabled = true;

                        btnChooseFile.IsEnabled = false;
                        cmbDeviceType.IsEnabled = false;
                        break;
                }

                if (files[i] != null)
                {
                    txtFileName.Text = Path.GetFileName(files[i]);
                }
                else
                {
                    txtFileName.Text = "";
                }
                btnChooseFile.IsEnabled = cmbDeviceType.SelectedIndex != 0;
            }
        }

        private void DeviceType_SelectionChanged(object? sender, Avalonia.Controls.SelectionChangedEventArgs e)
        {
            if (cmbDeviceType != null)
                btnChooseFile.IsEnabled = cmbDeviceType.SelectedIndex != 0;
        }
    }
}
