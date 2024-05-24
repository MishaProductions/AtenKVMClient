using Avalonia.Controls;

namespace KVMClient
{
    public partial class UsersWindow : Window
    {
        public bool AllowClose { get; set; } = false;
        private VNCViewer? Viewer;
        public UsersWindow()
        {
            InitializeComponent();
        }
        public UsersWindow(VNCViewer viewer)
        {
            InitializeComponent();
            this.Viewer = viewer;
        }
        private void Window_Closing(object? sender, Avalonia.Controls.WindowClosingEventArgs e)
        {
            e.Cancel = !AllowClose;
            Hide();
        }
    }
}
