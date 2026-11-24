using CommunityToolkit.Mvvm.ComponentModel;

namespace CorteDePlacasDesktop.ViewModels;

/// <summary>
/// Janela principal: guarda a página exibida no momento e faz a navegação entre telas.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    public MainViewModel()
    {
        CurrentPage = new HomeViewModel(this);
    }

    [ObservableProperty]
    public partial ViewModelBase CurrentPage { get; set; }

    public void AbrirInicio() => CurrentPage = new HomeViewModel(this);

    public void AbrirNovoPlano() => CurrentPage = new NovoPlanoViewModel(this);
}
