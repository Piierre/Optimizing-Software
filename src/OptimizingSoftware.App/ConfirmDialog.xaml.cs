using System.Windows;
using System.Windows.Input;

namespace OptimizingSoftware.App;

/// <summary>
/// Confirmation maison : la première ligne de la question sert de titre, le reste de détail.
/// Annuler (ou Échap) répond non ; seul le bouton bleu répond oui.
/// </summary>
public partial class ConfirmDialog : Window
{
    private ConfirmDialog(string question, string okText, string? cancelText)
    {
        InitializeComponent();
        var split = question.IndexOf("\n\n", StringComparison.Ordinal);
        Heading.Text = split < 0 ? question : question[..split];
        Body.Text = split < 0 ? "" : question[(split + 2)..].Trim();
        if (Body.Text.Length == 0) BodyScroll.Visibility = Visibility.Collapsed;
        OkButton.Content = okText;
        if (cancelText is null) CancelButton.Visibility = Visibility.Collapsed;
        else CancelButton.Content = cancelText;
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        Loaded += (_, _) => (cancelText is null ? OkButton : CancelButton).Focus();
    }

    /// <summary>Pose une question oui/non. Vrai seulement si l'utilisateur clique sur le bouton principal.</summary>
    public static bool Ask(Window? owner, string question, string okText = "Continuer") =>
        Show(owner, question, okText, "Annuler");

    /// <summary>Affiche une information avec un seul bouton.</summary>
    public static void Inform(Window? owner, string message) => Show(owner, message, "OK", null);

    private static bool Show(Window? owner, string text, string okText, string? cancelText)
    {
        var dialog = new ConfirmDialog(text, okText, cancelText);
        if (owner is { IsLoaded: true }) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return dialog.ShowDialog() == true;
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
