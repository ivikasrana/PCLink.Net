# PCLink.Net

[![license](https://img.shields.io/badge/license-MIT%20%2B%20Commercial-blue.svg)](#license)

Windows desktop companion for [PCLink](https://github.com/ivikasrana/PCLinkKotlin), the Android
app that shares your phone's storage with your PC over the local network. No cable, no cloud.

PCLink.Net pairs with the phone, maps it as a drive in Windows Explorer, transfers and syncs
files, sends text to the phone and shows the phone's notifications on the PC.

## Features

- **Pairing**: show a QR code for the phone to scan, or find the phone on the network
  (UDP discovery on port `1198`).
- **WebDAV drive**: map the phone to a drive letter, reconnect it automatically and disconnect
  it from the app.
- **Shortcuts**: open the phone over FTP in Explorer or open its web page in the browser.
- **Send files**: drop files or folders to upload them to a phone folder, with resumable
  chunked uploads and cancel support.
- **APK install**: drop `.apk` files to install them on the phone.
- **Folder sync**: two-way sync between a PC folder and a phone folder.
- **Text to phone**: send typed text or the clipboard to the phone.
- **Notification mirror**: show phone notifications on the PC.
- **Windows fixes**: check the WebClient service and apply or restore the registry settings
  needed for WebDAV (Basic login over HTTP and the file size limit).
- **Diagnostics**: check the WebDAV, HTTP and FTP connections to the phone.
- **Tray**: start with Windows in the tray, light and dark themes.
- The password is kept in Windows Credential Manager.

## Requirements

- Windows 10 or 11 (x64).
- The PCLink app running on an Android phone on the same network.

## Getting started

1. Start the server in the PCLink app on the phone.
2. Open PCLink.Net and choose **Show pairing code**, then on the phone tap
   **Scan code from PC**. Or use **Find phone** and enter the details shown on the phone.
3. Choose a drive letter and **Map WebDAV drive**.

If the drive fails to map, open the **Windows** tab and choose **Apply fixes**
(requires administrator rights).

Settings are stored in `%LOCALAPPDATA%\PCLink.Net`.

## Building

Requires the .NET 10 SDK on Windows.

```powershell
dotnet build
dotnet run
```

Publish a self contained single file executable:

```powershell
dotnet publish -c Release
```

## License

PCLink.Net is dual licensed:

- **[MIT License](LICENSE)**: free for personal, open source and commercial use, with
  attribution and no warranty.
- **[Commercial License](COMMERCIAL-LICENSE.md)**: for use without attribution, with
  warranty, support or custom terms.

Contributions are accepted under the MIT License.
