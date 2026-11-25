using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CorteDePlacasDesktop.Models;

namespace CorteDePlacasDesktop.ViewModels;

/// <summary>Um canto do contorno da chapa, editável pela tabela ou arrastando no desenho.</summary>
public partial class CantoViewModel : ViewModelBase
{
    private readonly Action<CantoViewModel> _remover;

    public CantoViewModel(Ponto ponto, Action<CantoViewModel> remover)
    {
        X = (decimal)Math.Round(ponto.X, 1);
        Y = (decimal)Math.Round(ponto.Y, 1);
        _remover = remover;
    }

    [ObservableProperty]
    public partial int Numero { get; set; }

    [ObservableProperty]
    public partial decimal? X { get; set; }

    [ObservableProperty]
    public partial decimal? Y { get; set; }

    [ObservableProperty]
    public partial bool Selecionado { get; set; }

    public bool EstaCompleto => X is not null && Y is not null;

    public Ponto Ponto => new((double)(X ?? 0), (double)(Y ?? 0));

    public void MoverPara(Ponto ponto)
    {
        X = (decimal)Math.Round(ponto.X, 1);
        Y = (decimal)Math.Round(ponto.Y, 1);
    }

    [RelayCommand]
    private void Remover() => _remover(this);
}
