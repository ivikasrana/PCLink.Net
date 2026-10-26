using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace PCLink.Net;

public static class Discovery
{
    private const int Port = 1198;

    private const int MaxSweepHosts = 510;

    private static IEnumerable<IPAddress> Targets()
    {
        yield return IPAddress.Broadcast;
        var sweep = new List<IPAddress>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask is null) continue;
                var ip = ToUInt(unicast.Address);
                var mask = ToUInt(unicast.IPv4Mask);
                var network = ip & mask;
                var broadcast = network | ~mask;
                yield return FromUInt(broadcast);
                if (broadcast - network - 1 > MaxSweepHosts) continue;
                for (var host = network + 1; host < broadcast; host++)
                    if (host != ip) sweep.Add(FromUInt(host));
            }
        }
        foreach (var host in sweep) yield return host;
    }

    private static uint ToUInt(IPAddress address) => System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes());

    private static IPAddress FromUInt(uint value)
    {
        var bytes = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return new IPAddress(bytes);
    }

    public static async Task<List<PhoneInfo>> FindAsync(int milliseconds = 1500)
    {
        var found = new Dictionary<string, PhoneInfo>();
        using var client = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        client.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
        var payload = Encoding.ASCII.GetBytes("PCLINK?");
        foreach (var target in Targets().Distinct())
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
