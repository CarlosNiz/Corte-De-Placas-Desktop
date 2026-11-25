using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CorteDePlacasDesktop.Models;

namespace CorteDePlacasDesktop.ViewModels;

/// <summary>
/// Retângulo ou círculo colocado na placa, que tira ou soma área.
/// Retângulo: X e Y são o canto inferior esquerdo. Círculo: X e Y são o centro.
/// </summary>
public partial class FormaViewModel : ViewModelBase
{
    private readonly Action<FormaViewModel> _remover;

    /// <param name="nomeDoAlvo">"chapa" ou "peça", para os textos dos botões.</param>
    public FormaViewModel(FormaChapa forma, Action<FormaViewModel> remover, string nomeDoAlvo)
    {
        Tipo = forma.Tipo;
        _remover = remover;
        TextoTira = $"− Tira da {nomeDoAlvo}";
        TextoSoma = $"+ Soma à {nomeDoAlvo}";
        Definir(forma);
    }

    public TipoForma Tipo { get; }

    public string TextoTira { get; }

    public string TextoSoma { get; }

    public bool EhCirculo => Tipo == TipoForma.Circulo;

    public bool EhRetangulo => Tipo == TipoForma.Retangulo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo))]
    public partial int Numero { get; set; }

    public string Titulo => $"{(EhCirculo ? "Círculo" : "Retângulo")} {Numero}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Remove), nameof(Adiciona))]
    public partial OperacaoForma Operacao { get; set; }

    public bool Remove => Operacao == OperacaoForma.Remover;

    public bool Adiciona => Operacao == OperacaoForma.Adicionar;

    [ObservableProperty]
    public partial bool Selecionada { get; set; }

    [ObservableProperty]
    public partial decimal? X { get; set; }

    [ObservableProperty]
    public partial decimal? Y { get; set; }

    /// <summary>Só para retângulo.</summary>
    [ObservableProperty]
    public partial decimal? Comprimento { get; set; }

    /// <summary>Só para retângulo.</summary>
    [ObservableProperty]
    public partial decimal? Largura { get; set; }

    /// <summary>Só para círculo.</summary>
    [ObservableProperty]
    public partial decimal? Diametro { get; set; }

    public bool EstaCompleta => X is not null && Y is not null
                                && (EhCirculo ? Diametro is not null : Comprimento is not null && Largura is not null);

    public FormaChapa Forma => EhCirculo
        ? FormaChapa.Circular(Operacao, new Ponto(D(X), D(Y)), D(Diametro))
        : FormaChapa.Retangular(Operacao, new Retangulo(D(X), D(Y), D(Comprimento), D(Largura)));

    public void Definir(FormaChapa forma)
    {
        Operacao = forma.Operacao;
        if (EhCirculo)
        {
            X = Arredondar(forma.Centro.X);
            Y = Arredondar(forma.Centro.Y);
            Diametro = Arredondar(forma.Diametro);
        }
        else
        {
            X = Arredondar(forma.Limites.X);
            Y = Arredondar(forma.Limites.Y);
            Comprimento = Arredondar(forma.Limites.Comprimento);
            Largura = Arredondar(forma.Limites.Largura);
        }
    }

    [RelayCommand]
    private void TirarDaPlaca() => Operacao = OperacaoForma.Remover;

    [RelayCommand]
    private void SomarAPlaca() => Operacao = OperacaoForma.Adicionar;

    [RelayCommand]
    private void Remover() => _remover(this);

    private static double D(decimal? valor) => (double)(valor ?? 0);

    private static decimal Arredondar(double valor) => (decimal)Math.Round(valor, 1);
}
