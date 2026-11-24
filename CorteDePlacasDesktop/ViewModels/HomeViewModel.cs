using CommunityToolkit.Mvvm.Input;

namespace CorteDePlacasDesktop.ViewModels;

/// <summary>
/// Tela inicial do aplicativo.
/// </summary>
public partial class HomeViewModel : ViewModelBase
{
    private readonly MainViewModel _janela;

    public HomeViewModel(MainViewModel janela)
    {
        _janela = janela;
    }

    [RelayCommand]
    private void NovoPlano() => _janela.AbrirNovoPlano();

    // A tela de abrir plano será criada em uma próxima etapa.
    [RelayCommand]
    private void AbrirPlano()
    {
    }
}
