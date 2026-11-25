using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CorteDePlacasDesktop.Models;

namespace CorteDePlacasDesktop.ViewModels;

/// <summary>
/// Uma linha da lista de peças a cortar. Medidas em milímetros.
/// A peça é um retângulo (comprimento × largura) ou um formato desenhado no editor; nesse caso,
/// comprimento e largura são as medidas externas do desenho.
/// </summary>
public partial class PecaViewModel : ViewModelBase
{
    private readonly Action<PecaViewModel> _remover;
    private readonly Action<PecaViewModel> _desenhar;

    public PecaViewModel(Action<PecaViewModel> remover, Action<PecaViewModel> desenhar)
    {
        _remover = remover;
        _desenhar = desenhar;
    }

    /// <summary>Posição na lista, começando em 1.</summary>
    [ObservableProperty]
    public partial int Numero { get; set; }

    [ObservableProperty]
    public partial string Descricao { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormatoPrevia), nameof(TemPrevia))]
    public partial decimal? Comprimento { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormatoPrevia), nameof(TemPrevia))]
    public partial decimal? Largura { get; set; }

    [ObservableProperty]
    public partial decimal? Quantidade { get; set; } = 1;

    /// <summary>Formato desenhado, ou null quando a peça é um retângulo simples.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EhDesenhada), nameof(EhRetangular), nameof(FormatoPrevia), nameof(TemPrevia))]
    public partial FormatoChapa? Formato { get; set; }

    public bool EhDesenhada => Formato is not null;

    public bool EhRetangular => Formato is null;

    /// <summary>
    /// O que aparece na imagem da peça: o desenho, ou o retângulo das medidas digitadas.
    /// Null enquanto a peça não tem medidas.
    /// </summary>
    public FormatoChapa? FormatoPrevia =>
        Formato ?? (Comprimento > 0 && Largura > 0
            ? FormatoChapa.RetanguloSimples((double)Comprimento, (double)Largura)
            : null);

    public bool TemPrevia => FormatoPrevia is not null;

    /// <summary>Problema encontrado nesta peça, ou null se está tudo certo.</summary>
    [ObservableProperty]
    public partial string? Aviso { get; set; }

    /// <summary>Linha que o operador ainda não começou a preencher.</summary>
    public bool EstaVazia =>
        string.IsNullOrWhiteSpace(Descricao) && Comprimento is null && Largura is null && Formato is null;

    public bool EstaCompleta =>
        Comprimento > 0 && Largura > 0 && Quantidade >= 1;

    /// <summary>Área de uma peça em mm² (a área real, no caso de peça desenhada).</summary>
    public double AreaUnitaria => Formato?.Area ?? (double)((Comprimento ?? 0) * (Largura ?? 0));

    /// <summary>
    /// Recebe o formato desenhado no editor. Se ele for um retângulo simples, a peça volta a ser
    /// um retângulo comum (com as medidas editáveis).
    /// </summary>
    public void DefinirFormato(FormatoChapa formato)
    {
        Formato = formato.EhRetanguloSimples ? null : formato;
        Comprimento = (decimal)Math.Round(formato.Limites.Comprimento, 1);
        Largura = (decimal)Math.Round(formato.Limites.Largura, 1);
    }

    [RelayCommand]
    private void Desenhar() => _desenhar(this);

    [RelayCommand]
    private void Remover() => _remover(this);
}
