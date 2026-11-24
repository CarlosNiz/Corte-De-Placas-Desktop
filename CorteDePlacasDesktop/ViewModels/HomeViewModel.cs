using CommunityToolkit.Mvvm.Input;

namespace CorteDePlacasDesktop.ViewModels;

/// <summary>
/// Tela inicial do aplicativo.
/// </summary>
public partial class HomeViewModel : ViewModelBase
{
    // As telas de destino serão criadas nas próximas etapas.
    [RelayCommand]
    private void NovoPlano()
    {
    }

    [RelayCommand]
    private void AbrirPlano()
    {
    }
}
