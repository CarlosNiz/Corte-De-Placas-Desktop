using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CorteDePlacasDesktop.ViewModels;

/// <summary>
/// Janela principal: navegação entre telas, diálogo de confirmação e avisos (toasts).
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    /// <summary>Só o aviso mais recente aparece (ele fica no centro da barra superior).</summary>
    private const int MaximoDeToasts = 1;

    public MainViewModel()
    {
        CurrentPage = new HomeViewModel(this);
    }

    // ---- Navegação ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstaNoInicio))]
    public partial ViewModelBase CurrentPage { get; set; }

    /// <summary>Usado para esconder o botão "Início" da barra quando já estamos nele.</summary>
    public bool EstaNoInicio => CurrentPage is HomeViewModel;

    [RelayCommand]
    public async Task AbrirInicioAsync()
    {
        if (CurrentPage.TemDadosNaoSalvos)
        {
            var sair = await ConfirmarAsync(
                "Sair do plano de corte?",
                "O plano que você está montando (chapa e peças) será descartado.",
                "Sair e descartar");
            if (!sair)
                return;

            CurrentPage = new HomeViewModel(this);
            MostrarToast("Plano descartado.");
            return;
        }

        CurrentPage = new HomeViewModel(this);
    }

    public void AbrirNovoPlano() => CurrentPage = new NovoPlanoViewModel(this);

    /// <summary>Mostra uma página que já existe (ex.: voltar do editor da chapa para o plano).</summary>
    public void Mostrar(ViewModelBase pagina) => CurrentPage = pagina;

    partial void OnCurrentPageChanged(ViewModelBase value)
    {
        // Os avisos falam da tela anterior (e um "Desfazer" agiria sobre ela): ao trocar de tela, saem.
        Toasts.Clear();
    }

    // ---- Diálogo de confirmação ----

    /// <summary>Diálogo aberto no momento, ou null.</summary>
    [ObservableProperty]
    public partial ConfirmacaoViewModel? Dialogo { get; set; }

    /// <summary>Mostra o diálogo e espera a resposta. Retorna true se o operador confirmou.</summary>
    public async Task<bool> ConfirmarAsync(string titulo, string mensagem, string textoConfirmar,
        string textoCancelar = "Continuar editando")
    {
        var dialogo = new ConfirmacaoViewModel(titulo, mensagem, textoConfirmar, textoCancelar);
        Dialogo = dialogo;
        try
        {
            return await dialogo.Resposta;
        }
        finally
        {
            Dialogo = null;
        }
    }

    // ---- Toasts ----

    /// <summary>Avisos na tela, o mais recente por último (embaixo).</summary>
    public ObservableCollection<ToastViewModel> Toasts { get; } = new();

    /// <summary>
    /// Mostra um aviso rápido. Com <paramref name="textoAcao"/> e <paramref name="acao"/>,
    /// o aviso ganha um botão (ex.: "Desfazer") e fica mais tempo na tela.
    /// Avisos do mesmo <paramref name="grupo"/> se substituem em vez de empilhar.
    /// </summary>
    public void MostrarToast(string mensagem, TipoToast tipo = TipoToast.Sucesso,
        string? textoAcao = null, Action? acao = null, string? grupo = null)
    {
        if (grupo is not null)
            foreach (var anterior in Toasts.Where(t => t.Grupo == grupo).ToList())
                Toasts.Remove(anterior);

        var toast = new ToastViewModel(mensagem, tipo, textoAcao, acao, grupo, FecharToast);
        Toasts.Add(toast);
        while (Toasts.Count > MaximoDeToasts)
            Toasts.RemoveAt(0);

        var duracao = acao is null ? TimeSpan.FromSeconds(4) : TimeSpan.FromSeconds(8);
        _ = FecharDepoisAsync(toast, duracao);
    }

    private async Task FecharDepoisAsync(ToastViewModel toast, TimeSpan duracao)
    {
        await Task.Delay(duracao);
        FecharToast(toast);
    }

    private void FecharToast(ToastViewModel toast) => Toasts.Remove(toast);
}
