using Avalonia.Controls;
using CorteDePlacasDesktop.ViewModels;

namespace CorteDePlacasDesktop.Views;

public partial class MainWindow : Window
{
    private bool _fechamentoConfirmado;

    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Ao fechar a janela com um plano preenchido, pergunta antes para não perder as peças.
    /// </summary>
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        if (_fechamentoConfirmado
            || e.Cancel
            || e.CloseReason == WindowCloseReason.OSShutdown
            || DataContext is not MainViewModel vm
            || !vm.CurrentPage.TemDadosNaoSalvos)
        {
            return;
        }

        e.Cancel = true;
        if (vm.Dialogo is not null)
            return;

        var fechar = await vm.ConfirmarAsync(
            "Fechar o aplicativo?",
            "As peças que você informou serão descartadas.",
            "Fechar e descartar");
        if (fechar)
        {
            _fechamentoConfirmado = true;
            Close();
        }
    }
}
