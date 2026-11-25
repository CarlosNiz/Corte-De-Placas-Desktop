using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CorteDePlacasDesktop.Models;

namespace CorteDePlacasDesktop.ViewModels;

/// <summary>Área com defeito (nó, mancha, furo...) que não pode ser usada.</summary>
public partial class DefeitoViewModel : ViewModelBase
{
    private readonly Action<DefeitoViewModel> _remover;

    public DefeitoViewModel(Retangulo area, Action<DefeitoViewModel> remover)
    {
        X = (decimal)Math.Round(area.X, 1);
        Y = (decimal)Math.Round(area.Y, 1);
        Comprimento = (decimal)Math.Round(area.Comprimento, 1);
        Largura = (decimal)Math.Round(area.Largura, 1);
        _remover = remover;
    }

    [ObservableProperty]
    public partial int Numero { get; set; }

    [ObservableProperty]
    public partial decimal? X { get; set; }

    [ObservableProperty]
    public partial decimal? Y { get; set; }

    [ObservableProperty]
    public partial decimal? Comprimento { get; set; }

    [ObservableProperty]
    public partial decimal? Largura { get; set; }

    public bool EstaCompleto => X is not null && Y is not null && Comprimento is not null && Largura is not null;

    public Retangulo Area => new((double)(X ?? 0), (double)(Y ?? 0), (double)(Comprimento ?? 0), (double)(Largura ?? 0));

    [RelayCommand]
    private void Remover() => _remover(this);
}
