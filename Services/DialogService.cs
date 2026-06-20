using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Bucket.Services;

/// <summary>
/// Small helpers for the app's modal prompts. Each takes the calling window's
/// <see cref="XamlRoot"/> so dialogs anchor to the correct bucket window.
/// </summary>
public static class DialogService
{
    public static async Task<bool> ConfirmAsync(XamlRoot root, string title, string message,
        string primaryText, string closeText = "Cancel")
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = message,
            PrimaryButtonText = primaryText,
            CloseButtonText = closeText,
            DefaultButton = ContentDialogButton.Primary
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public static async Task<string?> PromptTextAsync(XamlRoot root, string title, string placeholder, string defaultText = "")
    {
        var box = new TextBox
        {
            PlaceholderText = placeholder,
            Text = defaultText,
            SelectionStart = defaultText.Length
        };
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = box,
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? box.Text : null;
    }

    public static async Task MessageAsync(XamlRoot root, string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Close
        };
        await dialog.ShowAsync();
    }

    /// <summary>
    /// Asks how to handle existing items at the destination. One decision is
    /// applied to all conflicts in the batch (Replace / Keep both / Skip / Cancel).
    /// </summary>
    public static async Task<ConflictAction> ResolveConflictAsync(XamlRoot root, int conflictCount)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = conflictCount == 1 ? "An item already exists" : $"{conflictCount} items already exist",
            Content = "Items with the same name are already in the destination folder. " +
                      "What would you like to do?",
            PrimaryButtonText = "Replace",
            SecondaryButtonText = "Keep both",
            CloseButtonText = "Skip",
            DefaultButton = ContentDialogButton.Primary
        };

        return await dialog.ShowAsync() switch
        {
            ContentDialogResult.Primary => ConflictAction.Overwrite,
            ContentDialogResult.Secondary => ConflictAction.KeepBoth,
            _ => ConflictAction.Skip
        };
    }
}
