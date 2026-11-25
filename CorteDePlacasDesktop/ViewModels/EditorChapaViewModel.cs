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
    /// <summary>Arrastar cantos e formas, criar cantos nas bordas, selecionar.</summary>
    Ajustar,

    /// <summary>Desenhar um contorno novo, clicando canto a canto.</summary>
    Desenhar,

    /// <summary>Arrastar para criar um retângulo que tira ou soma área.</summary>
    Retangulo,

    /// <summary>Clicar no centro e arrastar para criar um círculo que tira ou soma área.</summary>
    Circulo,
}

/// <summary>
/// Tela onde o operador desenha a placa com o mouse: o contorno (cantos) e retângulos
/// e círculos que tiram ou somam área.
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
        Formas.CollectionChanged += OnColecaoChanged;

        _carregando = true;
        var formato = plano.Formato;
        foreach (var p in formato.Contorno)
            Cantos.Add(NovoCanto(p));
        foreach (var f in formato.Formas)
            Formas.Add(NovaForma(f));
        RetanguloComprimento = (decimal)formato.Limites.Comprimento;
        RetanguloLargura = (decimal)formato.Limites.Largura;
        _carregando = false;

        Atualizar();
    }

    /// <summary>Avisa o desenho que algo mudou e ele precisa ser redesenhado.</summary>
    public event Action? Alterado;

    public ObservableCollection<CantoViewModel> Cantos { get; } = new();

    public ObservableCollection<FormaViewModel> Formas { get; } = new();

    /// <summary>Formato atual, ou null enquanto algum campo está vazio ou o contorno não foi fechado.</summary>
    public FormatoChapa? Formato { get; private set; }

    public override bool TemDadosNaoSalvos => _alterado || _plano.TemDadosNaoSalvos;

    // ---- Modo ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmAjuste), nameof(EmDesenho), nameof(EmRetangulo), nameof(EmCirculo),
        nameof(CriandoForma), nameof(Ajuda))]
    public partial ModoEditor Modo { get; set; } = ModoEditor.Ajustar;

    public bool EmAjuste => Modo == ModoEditor.Ajustar;

    public bool EmDesenho => Modo == ModoEditor.Desenhar;

    public bool EmRetangulo => Modo == ModoEditor.Retangulo;

    public bool EmCirculo => Modo == ModoEditor.Circulo;

    public bool CriandoForma => EmRetangulo || EmCirculo;

    /// <summary>Se as formas novas tiram ou somam área.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NovaFormaRemove), nameof(NovaFormaAdiciona), nameof(Ajuda))]
    public partial OperacaoForma OperacaoNovaForma { get; set; } = OperacaoForma.Remover;

    public bool NovaFormaRemove => OperacaoNovaForma == OperacaoForma.Remover;

    public bool NovaFormaAdiciona => OperacaoNovaForma == OperacaoForma.Adicionar;

    public string Ajuda
    {
        get
        {
            var efeito = NovaFormaRemove ? "tirar da placa" : "somar à placa";
            return Modo switch
            {
                ModoEditor.Desenhar =>
                    "Clique para marcar cada canto da chapa. Para fechar, clique no primeiro canto. Esc cancela.",
                ModoEditor.Retangulo =>
                    $"Clique e arraste para desenhar um retângulo que vai {efeito}.",
                ModoEditor.Circulo =>
                    $"Clique no centro e arraste para o lado para desenhar um círculo que vai {efeito}.",
                _ =>
                    "Arraste cantos e formas para mudar a placa. Clique numa borda para criar um canto novo.",
            };
        }
    }

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
        LimparSelecao();
        Modo = ModoEditor.Desenhar;
        Cantos.Clear();
    }

    [RelayCommand]
    private void CriarRetangulo() => EntrarEmModoForma(ModoEditor.Retangulo);

    [RelayCommand]
    private void CriarCirculo() => EntrarEmModoForma(ModoEditor.Circulo);

    [RelayCommand]
    private void NovasFormasTiram() => OperacaoNovaForma = OperacaoForma.Remover;

    [RelayCommand]
    private void NovasFormasSomam() => OperacaoNovaForma = OperacaoForma.Adicionar;

    private void EntrarEmModoForma(ModoEditor modo)
    {
        if (EmDesenho)
            CancelarDesenho();
        LimparSelecao();
        Modo = modo;
    }

    // ---- Seleção ----

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoverSelecionadoCommand))]
    public partial CantoViewModel? CantoSelecionado { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoverSelecionadoCommand))]
    public partial FormaViewModel? FormaSelecionada { get; set; }

    partial void OnCantoSelecionadoChanged(CantoViewModel? oldValue, CantoViewModel? newValue)
    {
        if (oldValue is not null)
            oldValue.Selecionado = false;
        if (newValue is not null)
        {
            newValue.Selecionado = true;
            FormaSelecionada = null;
        }
        Alterado?.Invoke();
    }

    partial void OnFormaSelecionadaChanged(FormaViewModel? oldValue, FormaViewModel? newValue)
    {
        if (oldValue is not null)
            oldValue.Selecionada = false;
        if (newValue is not null)
        {
            newValue.Selecionada = true;
            CantoSelecionado = null;
        }
        Alterado?.Invoke();
    }

    public void SelecionarCanto(CantoViewModel? canto) => CantoSelecionado = canto;

    public void SelecionarForma(FormaViewModel? forma) => FormaSelecionada = forma;

    public void LimparSelecao()
    {
        CantoSelecionado = null;
        FormaSelecionada = null;
    }

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

    /// <summary>Cria a forma desenhada com o mouse (retângulo ou círculo, conforme o modo).</summary>
    public void AdicionarForma(FormaChapa forma)
    {
        var nova = NovaForma(forma);
        Formas.Add(nova);
        Modo = ModoEditor.Ajustar;
        SelecionarForma(nova);
        var efeito = forma.Operacao == OperacaoForma.Remover ? "tira da placa" : "soma à placa";
        _janela.MostrarToast($"{nova.Titulo} criado ({efeito}).");
    }

    public void MoverForma(FormaViewModel forma, FormaChapa novaPosicao) => forma.Definir(novaPosicao);

    // ---- Remoções ----

    [RelayCommand(CanExecute = nameof(TemSelecao))]
    private void RemoverSelecionado()
    {
        if (FormaSelecionada is not null)
            RemoverForma(FormaSelecionada);
        else if (CantoSelecionado is not null)
            RemoverCanto(CantoSelecionado);
    }

    private bool TemSelecao() => CantoSelecionado is not null || FormaSelecionada is not null;

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

    private void RemoverForma(FormaViewModel forma)
    {
        var indice = Formas.IndexOf(forma);
        if (indice < 0)
            return;
        var titulo = forma.Titulo;
        if (forma == FormaSelecionada)
            SelecionarForma(null);
        Formas.RemoveAt(indice);

        _janela.MostrarToast($"{titulo} removido.", textoAcao: "Desfazer", acao: () =>
        {
            if (!Formas.Contains(forma))
                Formas.Insert(Math.Min(indice, Formas.Count), forma);
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
        if (e.PropertyName is nameof(Modo) or nameof(OperacaoNovaForma))
            Atualizar();
    }

    private CantoViewModel NovoCanto(Ponto p)
    {
        var canto = new CantoViewModel(p, RemoverCanto);
        canto.PropertyChanged += OnItemChanged;
        return canto;
    }

    private FormaViewModel NovaForma(FormaChapa f)
    {
        var forma = new FormaViewModel(f, RemoverForma);
        forma.PropertyChanged += OnItemChanged;
        return forma;
    }

    private void OnColecaoChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        for (var i = 0; i < Cantos.Count; i++)
            Cantos[i].Numero = i + 1;
        for (var i = 0; i < Formas.Count; i++)
            Formas[i].Numero = i + 1;
        if (!_carregando)
            _alterado = true;
        Atualizar();
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CantoViewModel.Numero) or nameof(CantoViewModel.Selecionado)
            or nameof(FormaViewModel.Selecionada) or nameof(FormaViewModel.Titulo))
        {
            return;
        }
        if (!_carregando)
            _alterado = true;
        Atualizar();
    }

    private void Atualizar()
    {
        var completos = Cantos.All(c => c.EstaCompleto) && Formas.All(f => f.EstaCompleta);
        Formato = completos && !EmDesenho
            ? new FormatoChapa(Cantos.Select(c => c.Ponto), Formas.Select(f => f.Forma))
            : null;

        if (EmDesenho)
            Erro = Cantos.Count < 3
                ? "Marque pelo menos 3 cantos e feche o contorno."
                : "Clique no primeiro canto para fechar o contorno.";
        else if (!completos)
            Erro = "Preencha as medidas de todos os cantos e formas.";
        else
            Erro = Formato!.Validar();

        var partes = new List<string>
        {
            Cantos.Count == 1 ? "1 canto" : $"{Cantos.Count} cantos",
            Formas.Count switch { 0 => "sem formas", 1 => "1 forma", var n => $"{n} formas" },
        };
        if (Formato is not null && Erro is null)
        {
            partes.Add($"{Formato.Limites.Comprimento:0.#} × {Formato.Limites.Largura:0.#} mm");
            partes.Add($"{Formato.Area / 1_000_000:0.00} m²");
            if (Formato.Partes > 1)
                partes.Add($"{Formato.Partes} partes separadas");
        }
        Resumo = string.Join(" · ", partes);

        PodeConcluir = Erro is null;
        Alterado?.Invoke();
    }
}
