using CommunityToolkit.Mvvm.ComponentModel;

namespace CorteDePlacasDesktop.ViewModels;

/// <summary>
/// Janela principal: guarda a página exibida no momento.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial ViewModelBase CurrentPage { get; set; } = new HomeViewModel();
}
