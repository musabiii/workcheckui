using CommunityToolkit.Mvvm.ComponentModel;
using WorkCheck.Services;

namespace WorkCheck.ViewModels;

public partial class AmbientOption(AmbientSound sound, string title) : ObservableObject
{
    public AmbientSound Sound { get; } = sound;
    public string Title { get; } = title;

    [ObservableProperty] private bool _isSelected;
}

public record AmbientGroup(string Title, IReadOnlyList<AmbientOption> Options);
