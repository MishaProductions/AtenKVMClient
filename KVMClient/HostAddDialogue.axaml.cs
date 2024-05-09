using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace KVMClient;

public partial class HostAddDialogue : UserControl
{
    public string IP
    {
        get
        {
            return txtAddHostIp.Text;
        }
        set
        {
            txtAddHostIp.Text = value;
        }
    }
    public string Username
    {
        get
        {
            return txtAddHostUser.Text;
        }
        set
        {
            txtAddHostUser.Text = value;
        }
    }
    public string Password
    {
        get
        {
            return txtAddHostPw.Text;
        }
        set
        {
            txtAddHostPw.Text = value;
        }
    }
    public string Name
    {
        get
        {
            return txtAddHostName.Text;
        }
        set
        {
            txtAddHostName.Text = value;
        }
    }
    public HostAddDialogue()
    {
        InitializeComponent();
    }
}