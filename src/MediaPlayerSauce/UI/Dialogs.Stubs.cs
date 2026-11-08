// Dialogs not written yet. Each moves to its own Dialogs.<Name>.cs file when it's written.
namespace MediaPlayerSauce.UI
{
    public partial class MainWindow
    {
        public void ShowOpenDisc() { ShowNotice("Playing discs is coming soon."); }
        public void ShowOnline(string source, string search) { ShowNotice("Watch online is coming soon."); }
        public void ShowPreferences() { ShowNotice("Preferences are coming soon."); }
        public void ShowOpenWith() { ShowNotice("Open with is coming soon."); }
        public void ShowShortcuts() { ShowNotice("The list of shortcuts is coming soon."); }
        public void ShowAbout() { ShowNotice("Media Player (Sauce Edition)"); }
    }
}
