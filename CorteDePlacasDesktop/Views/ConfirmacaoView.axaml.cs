using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CorteDePlacasDesktop.Views;

public partial class ConfirmacaoView : UserControl
{
    public ConfirmacaoView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        BotaoCancelar.Focus();
    }
}
