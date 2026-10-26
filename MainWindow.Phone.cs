using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace PCLink.Net;

public partial class MainWindow
{
    private void ShowConnection()
    {
        HostBox.Text = settings.Host;
        DavPortBox.Text = settings.DavPort.ToString();
        HttpPortBox.Text = settings.HttpPort.ToString();
        FtpPortBox.Text = settings.FtpPort.ToString();
    }

    private void PhoneMoved(PhoneInfo phone)
    {
        settings.Host = phone.Host;
        if (phone.Http > 0) settings.HttpPort = phone.Http;
        if (phone.Dav > 0) settings.DavPort = phone.Dav;
        if (phone.Ftp > 0) settings.FtpPort = phone.Ftp;
        settings.TrySave();
        ShowConnection();
        tray.Balloon("PCLink", $"Phone found at {phone.Host}");
    }

    private bool TryReadPorts(TextBlock status, out int dav, out int http, out int ftp)
    {
        http = ftp = 0;
        var ok = int.TryParse(DavPortBox.Text, out dav) && int.TryParse(HttpPortBox.Text, out http) && int.TryParse(FtpPortBox.Text, out ftp)
                 && Validation.IsPort(dav) && Validation.IsPort(http) && Validation.IsPort(ftp);
        if (!ok) Say(status, "Ports must be numbers from 0 to 65535.", false);
        return ok;
    }

    private bool ReadConnection()
    {
        if (!TryReadPorts(ConnectionStatus, out var dav, out var http, out var ftp)) return false;
        var host = HostBox.Text.Trim();
        if (host.Length > 0 && !Validation.IsHost(host))
        {
            Say(ConnectionStatus, "The phone IP address is not valid.", false);
            return false;
        }
        settings.Host = host;
        settings.DavPort = dav;
        settings.HttpPort = http;
        settings.FtpPort = ftp;
        settings.User = UserBox.Text.Trim();
        settings.Drive = DriveBox.SelectedItem as string ?? "";
        if (PasswordBox.Password.Length > 0)
        {
            AppState.Password = PasswordBox.Password;
            CredentialStore.Save(AppState.Password);
            PasswordBox.Clear();
        }
        settings.TrySave();
        return true;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (ReadConnection()) Say(ConnectionStatus, "Saved.", true);
    }

    private async void Pair_Click(object sender, RoutedEventArgs e)
    {
        if (pairCts is not null)
        {
            pairCts.Cancel();
            return;
        }
        if (!TryReadPorts(PairText, out var dav, out var http, out var ftp)) return;
        var user = OrDefault(UserBox.Text, DefaultUser);
        var password = PasswordBox.Password.Length > 0 ? PasswordBox.Password : AppState.Password;
        if (password.Length < 4)
        {
            Say(PairText, "Enter a password of at least 4 characters in the Password box first. The phone will use it.", false);
            return;
        }
        const string prompt = "On the phone open PCLink and tap Scan code from PC.";
        QrImage.Source = Pairing.RenderQr(Pairing.SetupLink(user, password, dav, http, ftp));
        QrImage.Visibility = Visibility.Visible;
        PairButton.Content = "Cancel pairing";
        Say(PairText, prompt, true);
        pairCts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var log = new Progress<string>(text => PairText.Text = $"{prompt} {text}");
        PhoneInfo? phone = null;
        try { phone = await Pairing.WaitForPhoneAsync(user, password, http, log, pairCts.Token); }
        catch (Exception) { }
        pairCts.Dispose();
        pairCts = null;
        PairButton.Content = "Show pairing code";
        QrImage.Visibility = Visibility.Collapsed;
        if (phone is null)
        {
            Say(PairText, "Pairing stopped.", false);
            return;
        }
        settings.Host = phone.Host;
        settings.HttpPort = phone.Http > 0 ? phone.Http : http;
        settings.DavPort = phone.Dav;
        settings.FtpPort = phone.Ftp;
        settings.User = user;
        settings.PhoneId = phone.Id;
        settings.TrySave();
        AppState.Password = password;
        CredentialStore.Save(password);
        PasswordBox.Clear();
        ShowConnection();
        UserBox.Text = user;
        Say(PairText, $"Paired with {phone.Name} at {phone.Host}.", true);
        Say(ConnectionStatus, "Saved.", true);
    }

    private async void Find_Click(object sender, RoutedEventArgs e)
    {
        FindButton.IsEnabled = false;
        Say(PairText, "Searching the network", true);
        var phones = await Discovery.FindAsync(2000);
        FindButton.IsEnabled = true;
        if (phones.Count == 0)
        {
            Say(PairText, "No phone answered. Start the server in the app and check both are on the same Wi-Fi.", false);
            return;
        }
        var match = phones.FirstOrDefault(p => p.Id == settings.PhoneId) ?? phones[0];
        HostBox.Text = match.Host;
        if (match.Dav > 0) DavPortBox.Text = match.Dav.ToString();
        if (match.Http > 0) HttpPortBox.Text = match.Http.ToString();
        if (match.Ftp > 0) FtpPortBox.Text = match.Ftp.ToString();
        settings.PhoneId = match.Id;
        Say(PairText, $"Found {match.Name} at {match.Host}. Press Save, and enter its password if none is saved.", true);
    }

    private async void Map_Click(object sender, RoutedEventArgs e)
    {
        if (!ReadConnection()) return;
        if (DriveBox.SelectedItem is not string drive)
        {
            Say(DriveStatus, "No free drive letter.", false);
            return;
        }
        if (!WebClientTweaks.Read().BasicOk)
        {
            Say(DriveStatus, "Apply the fixes on the Windows tab first.", false);
            return;
        }
        if (AppState.Password.Length == 0)
        {
            Say(DriveStatus, "Enter the password first.", false);
            return;
        }
        var button = (Button)sender;
        button.IsEnabled = false;
        var (host, port, user, password) = (settings.Host, settings.DavPort, settings.User, AppState.Password);
        var error = await Task.Run(() => DriveMapper.Map(drive, host, port, user, password));
        button.IsEnabled = true;
        Say(DriveStatus, error ?? $"Mapped {drive}", error is null);
    }

    private void Unmap_Click(object sender, RoutedEventArgs e)
    {
        if (DriveBox.SelectedItem is not string drive) return;
        if (drive == settings.Drive && ReconnectCheck.IsChecked == true)
        {
            ReconnectCheck.IsChecked = false;
            ReadOptions();
        }
        var error = DriveMapper.Unmap(drive);
        Say(DriveStatus, error ?? $"Disconnected {drive}", error is null);
    }

    private void Ftp_Click(object sender, RoutedEventArgs e)
    {
        if (!ReadConnection()) return;
        var user = Uri.EscapeDataString(settings.User);
        Process.Start(new ProcessStartInfo("explorer.exe", $"ftp://{user}@{settings.Host}:{settings.FtpPort}/") { UseShellExecute = true });
    }

    private void Web_Click(object sender, RoutedEventArgs e)
    {
        if (!ReadConnection()) return;
        Process.Start(new ProcessStartInfo($"http://{settings.Host}:{settings.HttpPort}/") { UseShellExecute = true });
    }
}
