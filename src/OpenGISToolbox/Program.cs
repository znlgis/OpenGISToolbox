using Avalonia;
using System;

namespace OpenGISToolbox;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Ensure the bundled MaxRev GDAL runtime is used and not a system install
        // (e.g. OSGeo4W). A system-wide GDAL_DRIVER_PATH/GDAL_DATA/PROJ_LIB would point
        // GDAL at version-incompatible plugins/data and crash initialization. Run this
        // before any third-party/GDAL API is touched.
        Environment.SetEnvironmentVariable("GDAL_DRIVER_PATH", null);
        Environment.SetEnvironmentVariable("GDAL_DATA", null);
        Environment.SetEnvironmentVariable("PROJ_LIB", null);
        Environment.SetEnvironmentVariable("PROJ_DATA", null);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
