namespace Kairo.ViewModels;

public class NodeViewModel : ViewModelBase
{
    public int Id { get; }
    public string Name { get; }
    public string Host { get; }
    public string PortRangeDisplay { get; }
    public string Description { get; }
    public string DisplayLabel => $"{Name} ({Host})";

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public NodeViewModel(int id, string name, string host, string portRangeDisplay, string description)
    {
        Id = id;
        Name = name;
        Host = host;
        PortRangeDisplay = portRangeDisplay;
        Description = description;
    }
}
