using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CorteDePlacasDesktop.Models;

namespace CorteDePlacasDesktop.ViewModels;

public enum ModoEditor
{
    /// <summary>Arrastar cantos, criar cantos nas bordas, selecionar.</summary>
    Ajustar,

    /// <summary>Desenhar um contorno novo, clicando canto a canto.</summary>
    Desenhar,

    /// <summary>Arrastar um retângulo para marcar uma área com defeito.</summary>
    Defeito,
}

/// <summary>
/// Tela onde o operador desenha o formato da chapa com o mouse: contorno e defeitos.
/// </summary>
public partial class EditorChapaViewModel : ViewModelBase
{
    private readonly MainViewModel _janela;
    private readonly NovoPlanoViewModel _plano;
    private List<Ponto>? _contornoAntesDoDesenho;
    private bool _alterado;
    private bool _carregando;

    public EditorChapaViewModel(MainViewModel janela, NovoPlanoViewModel plano)
    {
        _janela = janela;
        _plano = plano;

        Cantos.CollectionChanged += OnColecaoChanged;
        Defeitos.CollectionChanged += OnColecaoChanged;

        _carregando = true;
        var formato = plano.Formato;
        foreach (var p in formato.Contorno)
            Cantos.Add(NovoCanto(p));
        foreach (var d in formato.Defeitos)
            Defeitos.Add(NovoDefeito(d));
        RetanguloComprimento = (decimal)formato.Limites.Comprimento;
        RetanguloLargura = (decimal)formato.Limites.Largura;
        _carregando = false;

        Atualizar();
    }

    /// <summary>Avisa o desenho que algo mudou e ele precisa ser redesenhado.</summary>
    public event Action? Alterado;

    public ObservableCollection<CantoViewModel> Cantos { get; } = new();

    public ObservableCollection<DefeitoViewModel> Defeitos { get; } = new();

    /// <summary>Formato atual, ou null enquanto algum campo está vazio ou o contorno não foi fechado.</summary>
    public FormatoChapa? Formato { get; private set; }

    public override bool TemDadosNaoSalvos => _alterado || _plano.TemDadosNaoSalvos;

    // ---- Modo ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmAjuste), nameof(EmDesenho), nameof(EmDefeito), nameof(Ajuda))]
    public partial ModoEditor Modo { get; set; } = ModoEditor.Ajustar;

    public bool EmAjuste => Modo == ModoEditor.Ajustar;

    public bool EmDesenho => Modo == ModoEditor.Desenhar;

    public bool EmDefeito => Modo == ModoEditor.Defeito;

    public string Ajuda => Modo switch
    {
        ModoEditor.Desenhar =>
            "Clique para marcar cada canto da chapa. Para fechar, clique no primeiro canto. Esc cancela.",
        ModoEditor.Defeito =>
            "Clique e arraste sobre a chapa para marcar a área com defeito.",
        _ =>
            "Arraste os cantos para mudar o formato. Clique numa borda para criar um canto novo.",
    };

    [RelayCommand]
    private void ModoAjustar()
    {
        if (EmDesenho)
            CancelarDesenho();
        Modo = ModoEditor.Ajustar;
    }

    [RelayCommand]
    private void DesenharContorno()
    {
        if (EmDesenho)
            return;
        _contornoAntesDoDesenho = Cantos.Select(c => c.Ponto).ToList();
        SelecionarCanto(null);
        Modo = ModoEditor.Desenhar;
        Cantos.Clear();
    }

    [RelayCommand]
    private void MarcarDefeito()
    {
        if (EmDesenho)
            CancelarDesenho();
        SelecionarCanto(null);
        Modo = ModoEditor.Defeito;
    }

    // ---- Seleção ----

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoverCantoSelecionadoCommand))]
    public partial CantoViewModel? CantoSelecionado { get; set; }

    partial void OnCantoSelecionadoChanged(CantoViewModel? oldValue, CantoViewModel? newValue)
    {
        if (oldValue is not null)
            oldValue.Selecionado = false;
        if (newValue is not null)
            newValue.Selecionado = true;
        Alterado?.Invoke();
    }

    public void SelecionarCanto(CantoViewModel? canto) => CantoSelecionado = canto;

    // ---- Ações vindas do desenho ----

    public void MoverCanto(CantoViewModel canto, Ponto ponto) => canto.MoverPara(ponto);

    /// <summary>Cria um canto novo na borda que liga o canto <paramref name="indiceBorda"/> ao seguinte.</summary>
    public CantoViewModel InserirCanto(int indiceBorda, Ponto ponto)
    {
        var canto = NovoCanto(ponto);
        Cantos.Insert(indiceBorda + 1, canto);
        SelecionarCanto(canto);
        return canto;
    }

    public void AdicionarCantoDesenho(Ponto ponto)
    {
        if (EmDesenho)
            Cantos.Add(NovoCanto(ponto));
    }

    public bool PodeFecharDesenho => EmDesenho && Cantos.Count >= 3;

    public void FecharDesenho()
    {
        if (!PodeFecharDesenho)
            return;
        _contornoAntesDoDesenho = null;
        Modo = ModoEditor.Ajustar;
        Atualizar();
        _janela.MostrarToast($"Contorno com {Cantos.Count} cantos desenhado.");
    }

    public void DesfazerUltimoCantoDesenho()
    {
        if (EmDesenho && Cantos.Count > 0)
            Cantos.RemoveAt(Cantos.Count - 1);
    }

    public void CancelarDesenho()
    {
        if (!EmDesenho)
            return;
        Cantos.Clear();
        foreach (var p in _contornoAntesDoDesenho ?? [])
            Cantos.Add(NovoCanto(p));
        _contornoAntesDoDesenho = null;
        Modo = ModoEditor.Ajustar;
        Atualizar();
    }

    public void AdicionarDefeito(Retangulo area)
    {
        var defeito = NovoDefeito(area);
        Defeitos.Add(defeito);
        Modo = ModoEditor.Ajustar;
        _janela.MostrarToast($"Defeito {defeito.Numero} marcado.");
    }

    // ---- Remoções ----

    [RelayCommand(CanExecute = nameof(TemCantoSelecionado))]
    private void RemoverCantoSelecionado()
    {
        if (CantoSelecionado is not null)
            RemoverCanto(CantoSelecionado);
    }

    private bool TemCantoSelecionado() => CantoSelecionado is not null;

    private void RemoverCanto(CantoViewModel canto)
    {
        if (!EmDesenho && Cantos.Count <= 3)
        {
            _janela.MostrarToast("A chapa precisa de pelo menos 3 cantos.", TipoToast.Erro);
            return;
        }

        var indice = Cantos.IndexOf(canto);
        if (indice < 0)
            return;
        if (canto == CantoSelecionado)
            SelecionarCanto(null);
        Cantos.RemoveAt(indice);

        _janela.MostrarToast($"Canto {indice + 1} removido.", textoAcao: "Desfazer", acao: () =>
        {
            if (!Cantos.Contains(canto))
                Cantos.Insert(Math.Min(indice, Cantos.Count), canto);
        });
    }

    private void RemoverDefeito(DefeitoViewModel defeito)
    {
        var indice = Defeitos.IndexOf(defeito);
        if (indice < 0)
            return;
        Defeitos.RemoveAt(indice);

        _janela.MostrarToast($"Defeito {indice + 1} removido.", textoAcao: "Desfazer", acao: () =>
        {
            if (!Defeitos.Contains(defeito))
                Defeitos.Insert(Math.Min(indice, Defeitos.Count), defeito);
        });
    }

    // ---- Retângulo simples ----

    [ObservableProperty]
    public partial decimal? RetanguloComprimento { get; set; }

    [ObservableProperty]
    public partial decimal? RetanguloLargura { get; set; }

    [RelayCommand]
    private void AplicarRetangulo()
    {
        if (!(RetanguloComprimento > 0) || !(RetanguloLargura > 0))
        {
            _janela.MostrarToast("Informe o comprimento e a largura do retângulo.", TipoToast.Erro);
            return;
        }

        if (EmDesenho)
            CancelarDesenho();

        var anterior = Cantos.Select(c => c.Ponto).ToList();
        SubstituirContorno(FormatoChapa.RetanguloSimples((double)RetanguloComprimento, (double)RetanguloLargura).Contorno);
        Modo = ModoEditor.Ajustar;

        _janela.MostrarToast($"Contorno trocado por um retângulo de {RetanguloComprimento:0.#} × {RetanguloLargura:0.#} mm.",
            textoAcao: "Desfazer", acao: () => SubstituirContorno(anterior));
    }

    private void SubstituirContorno(IEnumerable<Ponto> pontos)
    {
        SelecionarCanto(null);
        Cantos.Clear();
        foreach (var p in pontos)
            Cantos.Add(NovoCanto(p));
    }

    // ---- Resumo e validação ----

    [ObservableProperty]
    public partial string Resumo { get; set; } = "";

    /// <summary>O que impede concluir, ou null quando o formato está pronto.</summary>
    [ObservableProperty]
    public partial string? Erro { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConcluirCommand))]
    public partial bool PodeConcluir { get; set; }

    [RelayCommand(CanExecute = nameof(PodeConcluir))]
    private void Concluir()
    {
        if (Formato is null)
            return;
        _plano.DefinirFormato(Formato.Normalizado());
        _janela.Mostrar(_plano);
        _janela.MostrarToast("Formato da chapa atualizado.");
    }

    [RelayCommand]
    private async Task CancelarAsync()
    {
        if (_alterado)
        {
            var descartar = await _janela.ConfirmarAsync(
                "Descartar o desenho?",
                "O formato da chapa vai voltar a ser como estava antes.",
                "Descartar desenho");
            if (!descartar)
                return;
        }
        _janela.Mostrar(_plano);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(Modo))
            Atualizar();
    }

    private CantoViewModel NovoCanto(Ponto p)
    {
        var canto = new CantoViewModel(p, RemoverCanto);
        canto.PropertyChanged += OnItemChanged;
        return canto;
    }

    private DefeitoViewModel NovoDefeito(Retangulo r)
    {
        var defeito = new DefeitoViewModel(r, RemoverDefeito);
        defeito.PropertyChanged += OnItemChanged;
        return defeito;
    }

    private void OnColecaoChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        for (var i = 0; i < Cantos.Count; i++)
            Cantos[i].Numero = i + 1;
        for (var i = 0; i < Defeitos.Count; i++)
            Defeitos[i].Numero = i + 1;
        if (!_carregando)
            _alterado = true;
        Atualizar();
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CantoViewModel.Numero) or nameof(CantoViewModel.Selecionado))
            return;
        if (!_carregando)
            _alterado = true;
        Atualizar();
    }

    private void Atualizar()
    {
        var completos = Cantos.All(c => c.EstaCompleto) && Defeitos.All(d => d.EstaCompleto);
        Formato = completos && !EmDesenho
            ? new FormatoChapa(Cantos.Select(c => c.Ponto), Defeitos.Select(d => d.Area))
            : null;

        if (EmDesenho)
            Erro = Cantos.Count < 3
                ? "Marque pelo menos 3 cantos e feche o contorno."
                : "Clique no primeiro canto para fechar o contorno.";
        else if (!completos)
            Erro = "Preencha as medidas de todos os cantos e defeitos.";
        else
            Erro = Formato!.Validar();

        var partes = new List<string>
        {
            Cantos.Count == 1 ? "1 canto" : $"{Cantos.Count} cantos",
            Defeitos.Count switch { 0 => "sem defeitos", 1 => "1 defeito", var n => $"{n} defeitos" },
        };
        if (Formato is not null && Erro is null)
        {
            partes.Add($"{Formato.Limites.Comprimento:0.#} × {Formato.Limites.Largura:0.#} mm");
            partes.Add($"{Formato.Area / 1_000_000:0.00} m²");
        }
        Resumo = string.Join(" · ", partes);

        PodeConcluir = Erro is null;
        Alterado?.Invoke();
    }
}
