using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace PCLink.Net;

public static class Discovery
{
    private const int Port = 1198;

    private static IEnumerable<IPAddress> BroadcastAddresses()
    {
        yield return IPAddress.Broadcast;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask is null) continue;
                var ip = unicast.Address.GetAddressBytes();
                var mask = unicast.IPv4Mask.GetAddressBytes();
                var broadcast = new byte[4];
                for (var i = 0; i < 4; i++) broadcast[i] = (byte)(ip[i] | ~mask[i]);
                yield return new IPAddress(broadcast);
            }
        }
    }

    public static async Task<List<PhoneInfo>> FindAsync(int milliseconds = 1500)
    {
        var found = new Dictionary<string, PhoneInfo>();
        using var client = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        client.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
        var payload = Encoding.ASCII.GetBytes("PCLINK?");
        foreach (var target in BroadcastAddresses().Distinct())
        {
            try { await client.SendAsync(payload, payload.Length, new IPEndPoint(target, Port)); }
            catch (SocketException) { }
        }
        using var cts = new CancellationTokenSource(milliseconds);
        try
        {
            while (true)
            {
                UdpReceiveResult result;
                try { result = await client.ReceiveAsync(cts.Token); }
                catch (SocketException) when (!cts.IsCancellationRequested) { continue; }
                if (Parse(result) is { } phone) found[phone.Id] = phone;
            }
        }
        catch (OperationCanceledException)
        {
        }
        return found.Values.ToList();
    }

    private static PhoneInfo? Parse(UdpReceiveResult result)
    {
        try
        {
            using var document = JsonDocument.Parse(result.Buffer);
            var e = document.RootElement;
            return new PhoneInfo(e.GetProperty("id").GetString() ?? "", e.GetProperty("name").GetString() ?? "", result.RemoteEndPoint.Address.ToString(),
                e.GetProperty("http").GetInt32(), e.GetProperty("dav").GetInt32(), e.GetProperty("ftp").GetInt32(), e.GetProperty("user").GetString() ?? "");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
