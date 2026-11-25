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

        // Ao clicar nos campos de um canto ou forma no painel, ele fica destacado no desenho.
        ListaCantos.AddHandler(GotFocusEvent, (_, e) =>
        {
            if (e.Source is Control { DataContext: CantoViewModel canto } && DataContext is EditorChapaViewModel editor)
                editor.SelecionarCanto(canto);
        }, RoutingStrategies.Bubble);

        ListaFormas.AddHandler(GotFocusEvent, (_, e) =>
        {
            if (e.Source is Control { DataContext: FormaViewModel forma } && DataContext is EditorChapaViewModel editor)
                editor.SelecionarForma(forma);
        }, RoutingStrategies.Bubble);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Desenho.Focus();
    }
}
