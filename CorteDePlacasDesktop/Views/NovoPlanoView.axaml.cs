using System;
using System.Collections.Specialized;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CorteDePlacasDesktop.ViewModels;

namespace CorteDePlacasDesktop.Views;

public partial class NovoPlanoView : UserControl
{
    private NovoPlanoViewModel? _viewModel;

    public NovoPlanoView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
            _viewModel.Pecas.CollectionChanged -= OnPecasChanged;

        _viewModel = DataContext as NovoPlanoViewModel;

        if (_viewModel is not null)
            _viewModel.Pecas.CollectionChanged += OnPecasChanged;
    }

    /// <summary>
    /// Ao adicionar uma peça, leva o cursor para a descrição da nova linha
    /// para o operador continuar digitando.
    /// </summary>
    private void OnPecasChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add)
            return;

        var indice = e.NewStartingIndex;
        Dispatcher.UIThread.Post(() =>
        {
            var linha = ListaPecas.ContainerFromIndex(indice);
            if (linha is null)
                return;
            linha.BringIntoView();
            linha.GetVisualDescendants().OfType<TextBox>().FirstOrDefault()?.Focus();
        }, DispatcherPriority.Loaded);
    }
}
