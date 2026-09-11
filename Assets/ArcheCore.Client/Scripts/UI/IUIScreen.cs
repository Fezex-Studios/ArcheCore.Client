namespace ArcheCore.Client.UI.Interfaces
{
    // Replaces IUIScreen. Used by every show/hide-able panel in both the
    // login flow and the world HUD, so a single manager pattern works
    // for both scenes instead of two different idioms.
    public interface IUIPanel
    {
        bool IsVisible { get; }
        void Show();
        void Hide();
    }
}