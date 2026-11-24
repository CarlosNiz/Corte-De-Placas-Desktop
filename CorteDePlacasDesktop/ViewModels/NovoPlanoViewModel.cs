using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CorteDePlacasDesktop.ViewModels;

/// <summary>
/// Tela de novo plano de corte: o operador informa a chapa e as peças.
/// Medidas em milímetros.
/// </summary>
public partial class NovoPlanoViewModel : ViewModelBase
{
    private readonly MainViewModel _janela;

    public NovoPlanoViewModel(MainViewModel janela)
    {
        _janela = janela;
        Pecas.CollectionChanged += OnPecasChanged;
        AdicionarPeca();
    }

    // ---- Chapa ----

    [ObservableProperty]
    public partial string Material { get; set; } = "";

    [ObservableProperty]
    public partial decimal? ChapaComprimento { get; set; } = 2750;

    [ObservableProperty]
    public partial decimal? ChapaLargura { get; set; } = 1850;

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
    public partial string AreaUtil { get; set; } = "";

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

    [RelayCommand]
    private void AdicionarPeca() => Pecas.Add(new PecaViewModel(RemoverPeca));

    private void RemoverPeca(PecaViewModel peca)
    {
        Pecas.Remove(peca);
        if (Pecas.Count == 0)
            AdicionarPeca();
    }

    [RelayCommand]
    private void Voltar() => _janela.AbrirInicio();

    // O cálculo do plano de corte será implementado na próxima etapa.
    [RelayCommand(CanExecute = nameof(PodeCalcular))]
    private void Calcular()
    {
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(ChapaComprimento) or nameof(ChapaLargura)
            or nameof(EspessuraSerra) or nameof(Refilo))
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

    /// <summary>Recalcula área útil, resumo, avisos das peças e se já dá para calcular.</summary>
    private void Atualizar()
    {
        var refilo = Refilo ?? 0;
        var utilComprimento = (ChapaComprimento ?? 0) - 2 * refilo;
        var utilLargura = (ChapaLargura ?? 0) - 2 * refilo;

        string? erroChapa = null;
        if (!(ChapaComprimento > 0) || !(ChapaLargura > 0))
            erroChapa = "Informe o comprimento e a largura da chapa.";
        else if (EspessuraSerra is null)
            erroChapa = "Informe a espessura da serra.";
        else if (Refilo is null)
            erroChapa = "Informe o refilo.";
        else if (utilComprimento <= 0 || utilLargura <= 0)
            erroChapa = "O refilo é maior que a chapa.";

        AreaUtil = erroChapa is null ? $"{utilComprimento:0.#} × {utilLargura:0.#} mm" : "—";

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

            var cabe = (c <= utilComprimento && l <= utilLargura)
                       || (peca.PodeGirar && l <= utilComprimento && c <= utilLargura);
            if (erroChapa is null && !cabe)
            {
                naoCabem++;
                peca.Aviso = peca.PodeGirar
                    ? $"Não cabe na área útil da chapa ({AreaUtil})."
                    : $"Não cabe na área útil da chapa ({AreaUtil}) sem girar.";
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
