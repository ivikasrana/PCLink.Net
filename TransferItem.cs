using System.ComponentModel;
using System.Windows.Media;

namespace PCLink.Net;

public class TransferItem : INotifyPropertyChanged
{
    private double progress;
    private string status = "Waiting";

    public string Name { get; init; } = "";

    public double Progress
    {
        get => progress;
        set { progress = value; Changed(nameof(Progress)); }
    }

    public string Status
    {
        get => status;
        set { status = value; Changed(nameof(Status)); }
    }

    public bool Finished => status is not ("Waiting" or "Sending");

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public record DiagLine(string Mark, Brush Brush, string Text);
