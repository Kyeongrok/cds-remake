using Velopack;

namespace cds_helper;

internal class Starter
{
    [STAThread]
    private static void Main(string[] args)
    {
        VelopackApp.Build().Run();
        _ = new App().Run();
    }
}