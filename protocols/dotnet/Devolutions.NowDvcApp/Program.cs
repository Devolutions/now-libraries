namespace Devolutions.NowDvcApp
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new ConnectionDlg());
        }
    }
}