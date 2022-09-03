using Microsoft.Win32.SafeHandles;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.ProjectedFileSystem;

namespace RegFs;

internal class Program
{
    private static unsafe int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("> regfs.exe <Virtualization Root Path>");

            return -1;
        }

        // args[0] should be the path to the virtualization root.
        var rootPath = args[0];
        // Specify the notifications that we want ProjFS to send to us.  Everywhere under the virtualization
        // root we want ProjFS to tell us when files have been opened, when they're about to be renamed,
        // and when they're about to be deleted.
        var notificationMappings = new PrjNotificationMapping[]
        {
                new() {
                    NotificationRoot = string.Empty,
                    NotificationBitMask = PRJ_NOTIFY_TYPES.PRJ_NOTIFY_FILE_OPENED |
                                        PRJ_NOTIFY_TYPES.PRJ_NOTIFY_PRE_RENAME |
                                        PRJ_NOTIFY_TYPES.PRJ_NOTIFY_PRE_DELETE
                }
        };

        // Store the notification mapping we set up into a start options structure.  We leave all the
        // other options at their defaults.
        var opts = new PrjVirtualizingOptions
        {
            NotificationMappings = notificationMappings
        };

        // Start the provider using the options we set up.
        var provider = new RegfsProvider();
        var hr = provider.Start(rootPath, opts);
        if (hr.Failed)
        {
            Console.WriteLine($"Failed to start virtualization instance: 0x{hr.Value:x8}");
            return -1;
        }

        Console.WriteLine($"RegFS is running at virtualization root [{rootPath}]");
        Console.WriteLine("Press Enter to stop the provider...");
        Console.ReadLine();

        provider.Stop();

        return 0;
    }
}
