using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CorteDePlacasDesktop.Impressao;
using CorteDePlacasDesktop.Models;

namespace CorteDePlacasDesktop.ViewModels;

/// <summary>Tela com o plano calculado: as chapas, onde vai cada peça e a ordem dos cortes.</summary>
public partial class ResultadoPlanoViewModel : ViewModelBase
{
    private readonly MainViewModel _janela;
    private readonly NovoPlanoViewModel _plano;

    public ResultadoPlanoViewModel(MainViewModel janela, NovoPlanoViewModel plano, ResultadoPlano resultado)
    {
        _janela = janela;
        _plano = plano;
        Resultado = resultado;
        Chapas = resultado.Chapas
            .Select(c => new ChapaResultadoViewModel(c, resultado, SelecionarChapa) { SelecionadoNoPasso = SelecionarPasso })
            .ToList();
        ChapaSelecionada = Chapas.FirstOrDefault();
    }

    public ResultadoPlano Resultado { get; }

    public IReadOnlyList<ChapaResultadoViewModel> Chapas { get; }

    /// <summary>Sair daqui sem salvar perde o plano montado (chapa e peças).</summary>
    public override bool TemDadosNaoSalvos => _plano.TemDadosNaoSalvos;

    public bool EhSerra => Resultado.Tipo == TipoCorte.Serra;

    public string Resumo
    {
        get
        {
            var chapas = Resultado.Chapas.Count == 1 ? "1 chapa" : $"{Resultado.Chapas.Count} chapas";
            var pecas = Resultado.TotalPecas == 1 ? "1 peça" : $"{Resultado.TotalPecas} peças";
            return $"{Resultado.Arrumacao} · corte de {Resultado.Espessura:0.#} mm · {chapas} · {pecas} · " +
                   $"aproveitamento médio {Resultado.AproveitamentoMedio:P0}";
        }
    }

    public string TituloDaLista => EhSerra ? "Ordem dos cortes" : "Ordem das peças";

    public string DicaDaLista => EhSerra
        ? "Clique num corte para ver onde ele fica no desenho."
        : "Clique numa peça para ver onde ela fica no desenho.";

    public bool TemPecasDeFora => Resultado.NaoCouberam.Count > 0;

    public string PecasDeFora => "Não couberam em nenhuma chapa: " + string.Join("; ",
        Resultado.NaoCouberam.Select(n =>
            $"{n.QuantidadeQueNaoCoube} × {n.Peca.Descricao} ({n.Peca.Comprimento:0.#} × {n.Peca.Largura:0.#} mm)"));

    public bool TemVariasChapas => Chapas.Count > 1;

    // ---- Aviso de várias chapas ----

    public string TituloVariasChapas => $"Este plano precisa de {Chapas.Count} chapas.";

    public string TextoVariasChapas => $" {Resultado.SepareAsChapas} Clique nas abas abaixo para ver o que cortar em cada uma.";

    public bool TemDicaDeEspessura => Resultado.DicaDaEspessura is not null;

    public string DicaDeEspessura => Resultado.DicaDaEspessura ?? "";

    // ---- Chapa e passo selecionados ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChapaAtual), nameof(Passos), nameof(TituloDaChapa))]
    public partial ChapaResultadoViewModel? ChapaSelecionada { get; set; }

    partial void OnChapaSelecionadaChanged(ChapaResultadoViewModel? oldValue, ChapaResultadoViewModel? newValue)
    {
        if (oldValue is not null)
            oldValue.Selecionada = false;
        if (newValue is not null)
            newValue.Selecionada = true;
        PassoSelecionado = null;
    }

    public ChapaDoPlano? ChapaAtual => ChapaSelecionada?.Chapa;

    public IReadOnlyList<PassoViewModel> Passos => ChapaSelecionada?.Passos ?? [];

    public string TituloDaChapa => ChapaSelecionada is { } c
        ? $"Chapa {c.Chapa.Numero} de {Chapas.Count} · {Pecas(c.Chapa.Pecas.Count)} · aproveitamento {c.Chapa.Aproveitamento:P0}"
        : "";

    public string Legenda => EhSerra
        ? "Números laranja: peças · círculos azuis: cortes, na ordem da lista · ↻ peça girada"
        : "Números laranja: peças · ↻ peça girada";

    internal static string Pecas(int n) => n == 1 ? "1 peça" : $"{n} peças";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CorteDestacado), nameof(PecaDestacada))]
    public partial PassoViewModel? PassoSelecionado { get; set; }

    partial void OnPassoSelecionadoChanged(PassoViewModel? oldValue, PassoViewModel? newValue)
    {
        if (oldValue is not null)
            oldValue.Selecionado = false;
        if (newValue is not null)
            newValue.Selecionado = true;
    }

    public CorteSerra? CorteDestacado => PassoSelecionado?.Corte;

    public PecaNaChapa? PecaDestacada => PassoSelecionado?.Peca;

    private void SelecionarChapa(ChapaResultadoViewModel chapa) => ChapaSelecionada = chapa;

    public void SelecionarPasso(PassoViewModel passo) =>
        PassoSelecionado = PassoSelecionado == passo ? null : passo;

    [RelayCommand]
    private void ChapaAnterior()
    {
        if (ChapaSelecionada is null)
            return;
        var i = Chapas.ToList().IndexOf(ChapaSelecionada);
        if (i > 0)
            ChapaSelecionada = Chapas[i - 1];
    }

    [RelayCommand]
    private void ProximaChapa()
    {
        if (ChapaSelecionada is null)
            return;
        var i = Chapas.ToList().IndexOf(ChapaSelecionada);
        if (i < Chapas.Count - 1)
            ChapaSelecionada = Chapas[i + 1];
    }

    // ---- Ações ----

    [RelayCommand]
    private void VoltarParaPlano() => _janela.Mostrar(_plano);

    /// <summary>Gera o PDF do plano (para imprimir) e abre no leitor de PDF do computador.</summary>
    [RelayCommand]
    private async Task ImprimirAsync()
    {
        try
        {
            var pasta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Planos de Corte");
            Directory.CreateDirectory(pasta);
            var arquivo = Path.Combine(pasta, $"Plano de corte {DateTime.Now:yyyy-MM-dd HH-mm-ss}.pdf");
            await Task.Run(() => PdfDoPlano.Gerar(Resultado, arquivo));

            Process.Start(new ProcessStartInfo(arquivo) { UseShellExecute = true });
            _janela.MostrarToast("PDF aberto: use Imprimir no leitor. Salvo em Documentos › Planos de Corte.");
        }
        catch (Exception ex)
        {
            _janela.MostrarToast($"Não foi possível gerar o PDF: {ex.Message}", TipoToast.Erro);
        }
    }
}

/// <summary>Uma aba de chapa no resultado.</summary>
public partial class ChapaResultadoViewModel : ViewModelBase
{
    private readonly Action<ChapaResultadoViewModel> _selecionar;

    public ChapaResultadoViewModel(ChapaDoPlano chapa, ResultadoPlano resultado, Action<ChapaResultadoViewModel> selecionar)
    {
        Chapa = chapa;
        _selecionar = selecionar;
        Passos = resultado.Tipo == TipoCorte.Serra
            ? chapa.Cortes.Select(c => new PassoViewModel(c.Ordem, c.Texto, c, null)).ToList()
            : chapa.Pecas.Select((p, i) => new PassoViewModel(i + 1,
                    $"Peça {p.Numero} · {p.Descricao} — {p.Medidas} mm{(p.Girada ? " (girada)" : "")}", null, p))
                .ToList();
        foreach (var passo in Passos)
            passo.AoSelecionar = p => SelecionadoNoPasso?.Invoke(p);
    }

    public ChapaDoPlano Chapa { get; }

    public string Titulo => $"Chapa {Chapa.Numero}";

    public string Detalhe => $"{Chapa.Aproveitamento:P0} · {ResultadoPlanoViewModel.Pecas(Chapa.Pecas.Count)}";

    public IReadOnlyList<PassoViewModel> Passos { get; }

    /// <summary>Repassa o clique num passo para a tela de resultado.</summary>
    internal Action<PassoViewModel>? SelecionadoNoPasso { get; set; }

    [ObservableProperty]
    public partial bool Selecionada { get; set; }

    [RelayCommand]
    private void Selecionar() => _selecionar(this);
}

/// <summary>Um passo da lista: um corte (serra) ou uma peça (CNC).</summary>
public partial class PassoViewModel : ViewModelBase
{
    public PassoViewModel(int ordem, string texto, CorteSerra? corte, PecaNaChapa? peca)
    {
        Ordem = ordem;
        Texto = texto;
        Corte = corte;
        Peca = peca;
    }

    public int Ordem { get; }

    public string Texto { get; }

    public CorteSerra? Corte { get; }

    public PecaNaChapa? Peca { get; }

    /// <summary>Aparar as bordas aparece diferente dos cortes das peças.</summary>
    public bool EhPreparo => Corte is { Tipo: not TipoDePasso.Corte };

    internal Action<PassoViewModel>? AoSelecionar { get; set; }

    [ObservableProperty]
    public partial bool Selecionado { get; set; }

    [RelayCommand]
    private void Selecionar() => AoSelecionar?.Invoke(this);
}
