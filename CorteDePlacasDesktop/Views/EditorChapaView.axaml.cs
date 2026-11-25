using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CorteDePlacasDesktop.ViewModels;

namespace CorteDePlacasDesktop.Views;

public partial class EditorChapaView : UserControl
{
    public EditorChapaView()
    {
        InitializeComponent();

        // Ao clicar nos campos de um canto na tabela, o canto fica destacado no desenho.
        ListaCantos.AddHandler(GotFocusEvent, (_, e) =>
        {
            if (e.Source is Control { DataContext: CantoViewModel canto } && DataContext is EditorChapaViewModel editor)
                editor.SelecionarCanto(canto);
        }, RoutingStrategies.Bubble);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Desenho.Focus();
    }
}
