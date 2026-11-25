using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
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

    /// <summary>Formato da chapa: um retângulo ou um contorno desenhado, com retângulos e círculos que tiram ou somam área.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EhFormatoPersonalizado), nameof(DescricaoFormato), nameof(MedidasFormato))]
    public partial FormatoChapa Formato { get; set; } = FormatoChapa.RetanguloSimples(1000, 1000);

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
    public partial decimal? ChapaComprimento { get; set; } = 1000;

    [ObservableProperty]
    public partial decimal? ChapaLargura { get; set; } = 1000;

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
    private void DesenharFormato() =>
        _janela.Mostrar(new EditorFormatoViewModel(_janela, new AlvoDoEditor(
            Titulo: "Formato da chapa",
            Nome: "chapa",
            PermiteVariasPartes: true,
            FormatoInicial: Formato,
            PaginaDeVolta: this,
            AoConcluir: DefinirFormato)));

    /// <summary>Abre o editor para desenhar o formato de uma peça.</summary>
    private void DesenharPeca(PecaViewModel peca)
    {
        // Começa do desenho atual; se a peça ainda é retângulo, das medidas dela (ou de um tamanho padrão).
        var inicial = peca.Formato
                      ?? (peca.Comprimento > 0 && peca.Largura > 0
                          ? FormatoChapa.RetanguloSimples((double)peca.Comprimento, (double)peca.Largura)
                          : FormatoChapa.RetanguloSimples(600, 400));
        var titulo = string.IsNullOrWhiteSpace(peca.Descricao)
            ? $"Formato da peça {peca.Numero}"
            : $"Formato da peça {peca.Numero} · {peca.Descricao.Trim()}";

        _janela.Mostrar(new EditorFormatoViewModel(_janela, new AlvoDoEditor(
            Titulo: titulo,
            Nome: "peça",
            PermiteVariasPartes: false,
            FormatoInicial: inicial,
            PaginaDeVolta: this,
            AoConcluir: peca.DefinirFormato)));
    }

    [RelayCommand]
    private void UsarRetangulo()
    {
        var anterior = Formato;
        DefinirFormato(FormatoChapa.RetanguloSimples(anterior.Limites.Comprimento, anterior.Limites.Largura));
        _janela.MostrarToast($"A chapa voltou a ser um retângulo de {MedidasDoRetangulo()}.",
            textoAcao: "Desfazer", acao: () => DefinirFormato(anterior));
    }

    private string MedidasDoRetangulo() => $"{Formato.Limites.Comprimento:0.#} × {Formato.Limites.Largura:0.#} mm";

    /// <summary>Largura do corte (da serra ou da fresa), perdida a cada corte.</summary>
    [ObservableProperty]
    public partial decimal? EspessuraSerra { get; set; } = 3;

    // ---- Tipo de corte ----

    /// <summary>Como a máquina corta: serra (de ponta a ponta) ou CNC (qualquer posição).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EhSerra), nameof(EhCnc), nameof(RotuloEspessura), nameof(DicaEspessura),
        nameof(DescricaoTipoCorte))]
    public partial TipoCorte TipoCorte { get; set; } = TipoCorte.Serra;

    public bool EhSerra => TipoCorte == TipoCorte.Serra;

    public bool EhCnc => TipoCorte == TipoCorte.Cnc;

    public string RotuloEspessura => EhSerra ? "Espessura da serra" : "Diâmetro da fresa";

    public string DicaEspessura => EhSerra
        ? "Material perdido em cada corte (a largura do disco da serra)."
        : "Material perdido em volta de cada peça (o diâmetro da fresa da CNC).";

    public string DescricaoTipoCorte => EhSerra
        ? "Cortes retos de ponta a ponta."
        : "Peças em qualquer posição da chapa.";

    [RelayCommand]
    private void UsarSerra() => TipoCorte = TipoCorte.Serra;

    [RelayCommand]
    private void UsarCnc() => TipoCorte = TipoCorte.Cnc;

    /// <summary>Quanto aparar de cada borda da chapa (para deixá-la reta e limpa). 0 = não aparar.</summary>
    [ObservableProperty]
    public partial decimal? Refilo { get; set; } = 0;

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

    /// <summary>Enquanto o cálculo roda (o botão mostra "Calculando…").</summary>
    [ObservableProperty]
    public partial bool Calculando { get; set; }

    private PecaViewModel NovaPeca() => new(RemoverPeca, DesenharPeca);

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

    /// <summary>Calcula o plano (fora da tela, para não travar) e abre o resultado.</summary>
    [RelayCommand(CanExecute = nameof(PodeCalcular))]
    private async Task CalcularAsync()
    {
        var pecas = Pecas
            .Where(p => !p.EstaVazia && p.EstaCompleta)
            .Select(p => new PecaPedida(
                p.Numero,
                string.IsNullOrWhiteSpace(p.Descricao) ? $"Peça {p.Numero}" : p.Descricao.Trim(),
                (double)p.Comprimento!.Value,
                (double)p.Largura!.Value,
                (int)p.Quantidade!.Value,
                p.Formato))
            .ToList();
        var chapa = Formato;
        var refilo = (double)(Refilo ?? 0);
        var espessura = (double)(EspessuraSerra ?? 0);
        var tipo = TipoCorte;

        Calculando = true;
        try
        {
            var resultado = await Task.Run(() => CalculadoraDePlano.Calcular(chapa, refilo, espessura, tipo, pecas));
            _janela.Mostrar(new ResultadoPlanoViewModel(_janela, this, resultado));
            var chapas = resultado.Chapas.Count == 1 ? "1 chapa" : $"{resultado.Chapas.Count} chapas";
            if (resultado.NaoCouberam.Count == 0)
                _janela.MostrarToast($"Plano calculado: {chapas}.");
            else
                _janela.MostrarToast($"Plano calculado ({chapas}), mas algumas peças não couberam.", TipoToast.Erro);
        }
        finally
        {
            Calculando = false;
        }
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
            erroChapa = "Informe quanto aparar das bordas (0 para não aparar).";
        else if (Formato.Validar() is { } erroFormato)
            erroChapa = $"Formato da chapa: {erroFormato}";
        else if (utilComprimento <= 0 || utilLargura <= 0)
            erroChapa = "A medida para aparar as bordas é maior que a chapa.";

        var incompletas = 0;
        var naoCabem = 0;
        var totalPecas = 0;
        var areaTotal = 0.0;

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

            var c = (double)peca.Comprimento!.Value;
            var l = (double)peca.Largura!.Value;
            var q = (int)peca.Quantidade!.Value;
            totalPecas += q;
            areaTotal += peca.AreaUnitaria * q;

            if (erroChapa is not null)
                continue;

            // A peça pode ser girada 90°. Peça desenhada: por enquanto a checagem usa o
            // retângulo que a envolve (pode recusar encaixes muito justos).
            var cabe = Formato.Cabe(c, l, refilo) || Formato.Cabe(l, c, refilo);
            if (!cabe)
            {
                naoCabem++;
                peca.Aviso = EhFormatoPersonalizado
                    ? "Não cabe em nenhuma parte da chapa, nem girando (descontando as bordas aparadas)."
                    : $"Não cabe na área útil da chapa ({utilComprimento:0.#} × {utilLargura:0.#} mm), nem girando.";
            }
        }

        Resumo = totalPecas == 0
            ? "Nenhuma peça informada"
            : $"{totalPecas} {(totalPecas == 1 ? "peça" : "peças")} · {areaTotal / 1_000_000:0.00} m²";

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
