using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CorteDePlacasDesktop.Models;

namespace CorteDePlacasDesktop.ViewModels;

/// <summary>
/// Tela de novo plano de corte: o operador informa a chapa e as peças.
/// Medidas em milímetros.
/// </summary>
public partial class NovoPlanoViewModel : ViewModelBase
{
    private readonly MainViewModel _janela;
    private bool _sincronizandoMedidas;

    public NovoPlanoViewModel(MainViewModel janela)
    {
        _janela = janela;
        Pecas.CollectionChanged += OnPecasChanged;
        Pecas.Add(NovaPeca());
    }

    /// <summary>Pede confirmação antes de sair se alguma peça foi preenchida ou a chapa foi desenhada.</summary>
    public override bool TemDadosNaoSalvos => Pecas.Any(p => !p.EstaVazia) || EhFormatoPersonalizado;

    // ---- Chapa ----

    [ObservableProperty]
    public partial string Material { get; set; } = "";

    /// <summary>Formato da chapa: um retângulo ou um contorno desenhado, com retângulos e círculos que tiram ou somam área.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EhFormatoPersonalizado), nameof(DescricaoFormato), nameof(MedidasFormato))]
    public partial FormatoChapa Formato { get; set; } = FormatoChapa.RetanguloSimples(2750, 1850);

    public bool EhFormatoPersonalizado => !Formato.EhRetanguloSimples;

    public string DescricaoFormato
    {
        get
        {
            if (!EhFormatoPersonalizado)
                return "Retângulo";
            var cantos = $"{Formato.Contorno.Count} cantos";
            return Formato.Formas.Count switch
            {
                0 => $"Formato desenhado · {cantos}",
                1 => $"Formato desenhado · {cantos} · 1 forma",
                var n => $"Formato desenhado · {cantos} · {n} formas",
            };
        }
    }

    public string MedidasFormato =>
        $"{Formato.Limites.Comprimento:0.#} × {Formato.Limites.Largura:0.#} mm · {Formato.Area / 1_000_000:0.00} m²";

    /// <summary>Comprimento da chapa retangular (quando a chapa é desenhada, mostra a medida externa).</summary>
    [ObservableProperty]
    public partial decimal? ChapaComprimento { get; set; } = 2750;

    [ObservableProperty]
    public partial decimal? ChapaLargura { get; set; } = 1850;

    /// <summary>Usado pelo editor ao concluir o desenho.</summary>
    public void DefinirFormato(FormatoChapa formato)
    {
        _sincronizandoMedidas = true;
        Formato = formato;
        ChapaComprimento = (decimal)Math.Round(formato.Limites.Comprimento, 1);
        ChapaLargura = (decimal)Math.Round(formato.Limites.Largura, 1);
        _sincronizandoMedidas = false;
        Atualizar();
    }

    [RelayCommand]
    private void DesenharFormato() => _janela.Mostrar(new EditorChapaViewModel(_janela, this));

    [RelayCommand]
    private void UsarRetangulo()
    {
        var anterior = Formato;
        DefinirFormato(FormatoChapa.RetanguloSimples(anterior.Limites.Comprimento, anterior.Limites.Largura));
        _janela.MostrarToast($"A chapa voltou a ser um retângulo de {MedidasDoRetangulo()}.",
            textoAcao: "Desfazer", acao: () => DefinirFormato(anterior));
    }

    private string MedidasDoRetangulo() => $"{Formato.Limites.Comprimento:0.#} × {Formato.Limites.Largura:0.#} mm";

    /// <summary>Largura do corte da serra (kerf), perdida a cada corte.</summary>
    [ObservableProperty]
    public partial decimal? EspessuraSerra { get; set; } = 3;

    /// <summary>Margem descartada em cada borda da chapa.</summary>
    [ObservableProperty]
    public partial decimal? Refilo { get; set; } = 10;

    // ---- Peças ----

    public ObservableCollection<PecaViewModel> Pecas { get; } = new();

    // ---- Resumo e validação ----

    [ObservableProperty]
    public partial string Resumo { get; set; } = "";

    /// <summary>O que impede o cálculo, ou null quando está tudo pronto.</summary>
    [ObservableProperty]
    public partial string? MensagemValidacao { get; set; }

    /// <summary>Se a mensagem aponta um erro (true) ou só falta preencher algo (false).</summary>
    [ObservableProperty]
    public partial bool MensagemEhErro { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CalcularCommand))]
    public partial bool PodeCalcular { get; set; }

    private PecaViewModel NovaPeca() => new(RemoverPeca);

    [RelayCommand]
    private void AdicionarPeca()
    {
        Pecas.Add(NovaPeca());
        _janela.MostrarToast($"Peça {Pecas.Count} adicionada.", grupo: "peca-adicionada");
    }

    private void RemoverPeca(PecaViewModel peca)
    {
        var indice = Pecas.IndexOf(peca);
        if (indice < 0)
            return;

        Pecas.RemoveAt(indice);
        if (Pecas.Count == 0)
            Pecas.Add(NovaPeca());

        if (peca.EstaVazia)
        {
            _janela.MostrarToast("Linha vazia removida.");
            return;
        }

        var nome = string.IsNullOrWhiteSpace(peca.Descricao)
            ? $"Peça {indice + 1}"
            : $"Peça “{peca.Descricao.Trim()}”";
        _janela.MostrarToast($"{nome} removida.", textoAcao: "Desfazer",
            acao: () => RestaurarPeca(peca, indice, nome));
    }

    private void RestaurarPeca(PecaViewModel peca, int indice, string nome)
    {
        if (Pecas.Contains(peca))
            return;

        // Se só sobrou a linha vazia criada automaticamente, ela dá lugar à peça restaurada.
        if (Pecas.Count == 1 && Pecas[0].EstaVazia)
            Pecas.RemoveAt(0);

        Pecas.Insert(Math.Min(indice, Pecas.Count), peca);
        _janela.MostrarToast($"{nome} restaurada.");
    }

    // O cálculo do plano de corte será implementado na próxima etapa.
    [RelayCommand(CanExecute = nameof(PodeCalcular))]
    private void Calcular()
    {
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(ChapaComprimento) or nameof(ChapaLargura) && !_sincronizandoMedidas)
        {
            // Chapa retangular: as medidas digitadas definem o formato.
            if (!EhFormatoPersonalizado && ChapaComprimento > 0 && ChapaLargura > 0)
                Formato = FormatoChapa.RetanguloSimples((double)ChapaComprimento, (double)ChapaLargura);
            Atualizar();
        }
        else if (e.PropertyName is nameof(EspessuraSerra) or nameof(Refilo))
        {
            Atualizar();
        }
    }

    private void OnPecasChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (PecaViewModel p in e.OldItems)
                p.PropertyChanged -= OnPecaChanged;
        if (e.NewItems is not null)
            foreach (PecaViewModel p in e.NewItems)
                p.PropertyChanged += OnPecaChanged;

        for (var i = 0; i < Pecas.Count; i++)
            Pecas[i].Numero = i + 1;

        Atualizar();
    }

    private void OnPecaChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PecaViewModel.Aviso) or nameof(PecaViewModel.Numero))
            return;
        Atualizar();
    }

    /// <summary>Recalcula resumo, avisos das peças e se já dá para calcular.</summary>
    private void Atualizar()
    {
        var refilo = (double)(Refilo ?? 0);
        var limites = Formato.Limites;
        var utilComprimento = limites.Comprimento - 2 * refilo;
        var utilLargura = limites.Largura - 2 * refilo;

        string? erroChapa = null;
        if (!EhFormatoPersonalizado && (!(ChapaComprimento > 0) || !(ChapaLargura > 0)))
            erroChapa = "Informe o comprimento e a largura da chapa.";
        else if (EspessuraSerra is null)
            erroChapa = "Informe a espessura da serra.";
        else if (Refilo is null)
            erroChapa = "Informe o refilo.";
        else if (Formato.Validar() is { } erroFormato)
            erroChapa = $"Formato da chapa: {erroFormato}";
        else if (utilComprimento <= 0 || utilLargura <= 0)
            erroChapa = "O refilo é maior que a chapa.";

        var incompletas = 0;
        var naoCabem = 0;
        var totalPecas = 0;
        var areaTotal = 0m;

        foreach (var peca in Pecas)
        {
            peca.Aviso = null;
            if (peca.EstaVazia)
                continue;
            if (!peca.EstaCompleta)
            {
                incompletas++;
                continue;
            }

            var c = peca.Comprimento!.Value;
            var l = peca.Largura!.Value;
            var q = (int)peca.Quantidade!.Value;
            totalPecas += q;
            areaTotal += c * l * q;

            if (erroChapa is not null)
                continue;

            var cabe = Formato.Cabe((double)c, (double)l, refilo)
                       || (peca.PodeGirar && Formato.Cabe((double)l, (double)c, refilo));
            if (!cabe)
            {
                naoCabem++;
                var semGirar = peca.PodeGirar ? "" : " sem girar";
                peca.Aviso = EhFormatoPersonalizado
                    ? $"Não cabe em nenhuma parte da chapa{semGirar} (descontando o refilo)."
                    : $"Não cabe na área útil da chapa ({utilComprimento:0.#} × {utilLargura:0.#} mm){semGirar}.";
            }
        }

        Resumo = totalPecas == 0
            ? "Nenhuma peça informada"
            : $"{totalPecas} {(totalPecas == 1 ? "peça" : "peças")} · {areaTotal / 1_000_000m:0.00} m²";

        if (erroChapa is not null)
            (MensagemValidacao, MensagemEhErro) = (erroChapa, true);
        else if (naoCabem > 0)
            (MensagemValidacao, MensagemEhErro) = (naoCabem == 1 ? "1 peça não cabe na chapa." : $"{naoCabem} peças não cabem na chapa.", true);
        else if (incompletas > 0)
            (MensagemValidacao, MensagemEhErro) = (incompletas == 1 ? "1 peça está sem medida ou quantidade." : $"{incompletas} peças estão sem medida ou quantidade.", false);
        else if (totalPecas == 0)
            (MensagemValidacao, MensagemEhErro) = ("Adicione as peças que precisa cortar.", false);
        else
            (MensagemValidacao, MensagemEhErro) = (null, false);

        PodeCalcular = MensagemValidacao is null;
    }
}
