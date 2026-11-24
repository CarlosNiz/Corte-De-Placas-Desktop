using CommunityToolkit.Mvvm.ComponentModel;

namespace CorteDePlacasDesktop.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    /// <summary>
    /// Se a página tem dados que seriam perdidos ao sair dela.
    /// Quando true, o app pede confirmação antes de trocar de tela ou fechar.
    /// </summary>
    public virtual bool TemDadosNaoSalvos => false;
}
