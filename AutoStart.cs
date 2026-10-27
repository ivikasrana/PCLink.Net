using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace PCLink.Net;

public static class AutoStart
{
    private const string Name = "PCLink.Net";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static string? Set(bool enabled)
    {
        RemoveRunKey();
        if (!enabled)
        {
            ProcessRunner.Run("schtasks.exe", $"/Delete /TN \"{Name}\" /F");
            return null;
        }
        var file = Path.Combine(Path.GetTempPath(), "PCLink.Net.task.xml");
        try
        {
            File.WriteAllText(file, TaskXml(), Encoding.Unicode);
            var result = ProcessRunner.Run("schtasks.exe", $"/Create /TN \"{Name}\" /XML \"{file}\" /F");
            return result.Exit == 0 ? null : result.Output.Trim();
        }
        finally
        {
            try { File.Delete(file); } catch { }
        }
    }

    private static void RemoveRunKey()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        key?.DeleteValue(Name, false);
    }

    private static string TaskXml()
    {
        var user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
        var exe = SecurityElement.Escape(Environment.ProcessPath!);
        var dir = SecurityElement.Escape(AppContext.BaseDirectory);
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Starts PCLink.Net in the tray after sign-in.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{user}</UserId>
                  <Delay>PT30S</Delay>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>LeastPrivilege</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <StartWhenAvailable>true</StartWhenAvailable>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
                <RestartOnFailure>
                  <Interval>PT1M</Interval>
                  <Count>3</Count>
                </RestartOnFailure>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{exe}</Command>
                  <Arguments>--tray</Arguments>
                  <WorkingDirectory>{dir}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
    }
}
