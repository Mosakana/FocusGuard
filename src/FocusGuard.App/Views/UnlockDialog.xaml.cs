using System.Windows;
using System.Windows.Input;

namespace FocusGuard.App.Views;

public partial class UnlockDialog : Window
{
    public UnlockDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Disable paste on the password input
        DataObject.AddPastingHandler(PasswordInput, OnPaste);

        // Also block Ctrl+V via preview
        PasswordInput.PreviewKeyDown += OnPreviewKeyDown;

        PasswordInput.Focus();
    }

    private void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        e.CancelCommand(); // Block all paste attempts
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Block Ctrl+V paste shortcut
        if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            e.Handled = true;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    // Allow dragging the borderless window
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        DragMove();
    }
}
