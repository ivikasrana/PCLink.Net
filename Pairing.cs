using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXing;
using ZXing.QrCode;

namespace PCLink.Net;

public static class Pairing
{
    public static string SetupLink(string user, string password, int dav, int http, int ftp) =>
        $"pclink://setup?user={Uri.EscapeDataString(user)}&pass={Uri.EscapeDataString(password)}&dav={dav}&http={http}&ftp={ftp}";

    public static BitmapSource RenderQr(string text, int scale = 8)
    {
        var hints = new Dictionary<EncodeHintType, object> { [EncodeHintType.MARGIN] = 2 };
        var matrix = new QRCodeWriter().encode(text, BarcodeFormat.QR_CODE, 0, 0, hints);
        var width = matrix.Width * scale;
        var height = matrix.Height * scale;
        var pixels = new byte[width * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                pixels[y * width + x] = matrix[x / scale, y / scale] ? (byte)0 : (byte)255;
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray8, null, pixels, width);
        image.Freeze();
        return image;
    }

    public static async Task<PhoneInfo?> WaitForPhoneAsync(string user, string password, int httpPort, IProgress<string> log, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            log.Report("Looking for the phone");
            foreach (var phone in await Discovery.FindAsync(1200))
            {
                var port = phone.Http > 0 ? phone.Http : httpPort;
                try
                {
                    using var client = new PhoneClient(phone.Host, port, user, password);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(2500);
                    return await client.InfoAsync(timeout.Token) with { Host = phone.Host };
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                }
            }
            await Task.Delay(1000, ct);
        }
        return null;
    }
}
