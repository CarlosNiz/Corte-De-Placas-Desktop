using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CorteDePlacasDesktop.ViewModels;

/// <summary>
/// Uma linha da lista de peças a cortar. Medidas em milímetros.
/// </summary>
public partial class PecaViewModel : ViewModelBase
{
    private readonly Action<PecaViewModel> _remover;

    public PecaViewModel(Action<PecaViewModel> remover)
    {
        _remover = remover;
    }

    /// <summary>Posição na lista, começando em 1.</summary>
    [ObservableProperty]
    public partial int Numero { get; set; }

    [ObservableProperty]
    public partial string Descricao { get; set; } = "";

    [ObservableProperty]
    public partial decimal? Comprimento { get; set; }

    [ObservableProperty]
    public partial decimal? Largura { get; set; }

    [ObservableProperty]
    public partial decimal? Quantidade { get; set; } = 1;

    /// <summary>Se a peça pode ser girada 90° na chapa. Desmarcar quando o veio precisa ser respeitado.</summary>
    [ObservableProperty]
    public partial bool PodeGirar { get; set; } = true;

    /// <summary>Problema encontrado nesta peça, ou null se está tudo certo.</summary>
    [ObservableProperty]
    public partial string? Aviso { get; set; }

    /// <summary>Linha que o operador ainda não começou a preencher.</summary>
    public bool EstaVazia =>
        string.IsNullOrWhiteSpace(Descricao) && Comprimento is null && Largura is null;

    public bool EstaCompleta =>
        Comprimento > 0 && Largura > 0 && Quantidade >= 1;

    [RelayCommand]
    private void Remover() => _remover(this);
}
