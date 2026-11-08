// Dialogs not written yet. Each moves to its own Dialogs.<Name>.cs file when it's written.
namespace MediaPlayerSauce.UI
{
    public partial class MainWindow
    {
        public void ShowEffects(int tab) { ShowNotice("Effects and filters are coming soon."); }
        public void ShowMediaInfo() { ShowNotice("Media information is coming soon."); }
        public void ShowConvert() { ShowNotice("Converting is coming soon."); }
        public void ShowOnline(string source, string search) { ShowNotice("Watch online is coming soon."); }
    }
}
