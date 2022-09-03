using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.ProjectedFileSystem;
using static RegFs.Macros;

namespace RegFs;

class RegfsProvider : VirtualizationInstance
{
    // An enumeration session starts when StartDirEnum is invoked and ends when EndDirEnum is invoked.
    // This tracks the active enumeration sessions.
    readonly Dictionary<Guid, DirInfo> _activeEnumSessions = new();
    readonly RegOps _regOps = new();

    // If this flag is set to true, RegFS will block the following namespace-altering operations
    // that take place under virtualization root:
    // 1) file or directory deletion
    // 2) file or directory rename
    //
    // New file or folder create cannot be easily blocked due to limitations in ProjFS.
    bool _readonlyNamespace = true;

    // If this flag is set to true, RegFS will block file content modifications for placeholder files.
    // bool _readOnlyFileContent = true;

    public RegfsProvider()
    {
        // Record that this class implements the optional Notify callback.
        SetOptionalMethods(OptionalMethods.Notify);
    }

    /// <summary>
    /// ProjFS invokes this callback to request metadata information for a file or a directory.
    ///
    /// If the file or directory exists in the provider's namespace, the provider calls
    /// WritePlaceholderInfo() to give ProjFS the info for the requested name.
    ///
    /// The metadata information ProjFS supports includes:
    ///
    ///    Mandatory:
    ///        FileBasicInfo.IsDirectory - the requested name is a file or directory.
    ///
    ///    Mandatory for files:
    ///        FileBasicInfo.FileSize - file size in bytes.
    ///
    ///    Optional:
    ///        VersionInfo - A 256 bytes ID which can be used to distinguish different versions of file content
    ///                      for one file name.
    ///        FileBasicInfo.CreationTime/LastAccessTime/LastWriteTime/ChangeTime - timestamps of the file.
    ///        FileBasicInfo.FileAttributes - File Attributes.
    ///
    ///    Optional and less commonly used:
    ///        EaInformation - Extended attribute (EA) information.
    ///        SecurityInformation - Security descriptor information.
    ///        StreamsInformation - Alternate data stream information.
    ///
    /// See also PRJ_PLACEHOLDER_INFORMATION structure in projectedfslib.h for more details.
    ///
    /// If the file or directory doesn't exist in the provider's namespace, this callback returns
    /// HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND).
    ///
    /// If the provider is unable to process the request (e.g. due to network error) or it wants to block
    /// the request, this callback returns an appropriate HRESULT error code.
    ///
    /// Below is a list of example commands that demonstrate how GetPlaceholderInfo is called.
    ///
    /// Assuming z:\reg doesn't exist, run 'regfs.exe z:\reg' to create the root.
    /// Now start another command line window, 'cd z:\reg' then run below commands in sequence.
    ///
    /// 1) cd HKEY_LOCAL_MACHINE
    ///   The first time you cd into a folder that exists in provider's namespace, GetPlaceholderInfo is
    ///   called with CallbackData.FilePathName = "HKEY_LOCAL_MACHINE".  This callback will cause an
    ///   on-disk placeholder file called "HKEY_LOCAL_MACHINE" to be created under z:\reg.
    ///
    /// 2) cd .. & cd HKEY_LOCAL_MACHINE
    ///   The second and subsequent time you cd into a folder that exists in provider's namespace, this
    ///   callback will not be called because the on-disk placeholder for HKEY_LOCAL_MACHINE already exists.
    ///
    /// 3) mkdir newfolder
    ///   If _readonlyNamespace is true, GetPlaceholderInfo returns ERROR_ACCESS_DENIED, so the mkdir command
    ///   reports "Access is denied" and the placeholder is not created.  If _readonlyNamespace is false,
    ///   GetPlaceholderInfo returns ERROR_FILE_NOT_FOUND so the command succeeds and newfolder is created.
    ///
    /// 4) cd SOFTWARE\Microsoft\.NETFramework
    ///   The first time you cd into a deep path, GetPlaceholderInfo is called repeatedly with the
    ///   following CallbackData.FilePathName values:
    ///   1) "HKEY_LOCAL_MACHINE\SOFTWARE"
    ///   2) "HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft"
    ///   3) "HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\.NETFramework"
    /// </summary>
    protected override HRESULT GetPlaceholderInfo(PRJ_CALLBACK_DATA CallbackData)
    {
        Console.WriteLine($"\n----> {CurrentName()}: Path [{CallbackData.FilePathName}] triggered by [{CallbackData.TriggeringProcessImageFileName}]");

        bool isKey;
        int valSize = 0;
        RegOps _regOps = new();

        // Find out whether the specified path exists in the registry, and whether it is a key or a value.
        try
        {
            if (_regOps.DoesKeyExist(CallbackData.FilePathName))
            {
                isKey = true;
            }
            else if (_regOps.DoesValueExist(CallbackData.FilePathName, out valSize))
            {
                isKey = false;
            }
            else
            {
                Console.WriteLine($"<---- {CurrentName()}: return 0x{HRESULT.E_FILENOTFOUND.Value:x8}");
                return HRESULT.E_FILENOTFOUND;
            }
        }
        catch (SecurityException)
        {
            Console.WriteLine($"<---- {CurrentName()}: return 0x{HRESULT.E_ACCESSDENIED.Value:x8}");
            return HRESULT.E_ACCESSDENIED;
        }

        // Format the PRJ_PLACEHOLDER_INFO structure.  For registry keys we create directories on disk,
        // for values we create files.
        var placeholderInfo = new PRJ_PLACEHOLDER_INFO
        {
            FileBasicInfo =
            {
                IsDirectory = isKey,
                FileSize = valSize
            }
        };

        // Create the on-disk placeholder.
        var hr = WritePlaceholderInfo(CallbackData.FilePathName, placeholderInfo);
        Console.WriteLine($"<---- {CurrentName()}: return 0x{hr.Value:x8}");
        return hr;
    }

    /// <summary>
    /// ProjFS invokes this callback to tell the provider that a directory enumeration is starting.
    /// A user-mode tool usually uses FindFirstFile/FindNextFile APIs to enumerate a directory.Those
    /// APIs send QueryDirectory requests to the file system.  If the enumeration is for a placeholder
    /// folder, ProjFS intercepts and blocks those requests.  Then ProjFS invokes the registered directory
    /// enumeration callbacks (StartDirEnum, GetDirEnum, EndDirEnum) to get a list of names in provider's
    /// namespace, merges those names with the names physically on disk under that folder, then unblocks
    /// the enumeration requests and returns the merged list to the caller.
    /// </summary>
    protected override HRESULT StartDirEnum(PRJ_CALLBACK_DATA CallbackData, Guid EnumerationId)
    {
        Console.WriteLine($"\n----> {CurrentName()}: Path [{CallbackData.FilePathName}] triggerred by [{CallbackData.TriggeringProcessImageFileName}]");

        // For each dir enum session, ProjFS sends:
        //      one StartEnumCallback
        //      one or more GetEnumCallbacks
        //      one EndEnumCallback
        // These callbacks will use the same value for EnumerationId for the same session.
        // Here we map the EnumerationId to a new DirInfo object.
        _activeEnumSessions[EnumerationId] = new DirInfo();
        Console.WriteLine($"<---- {CurrentName()}: return 0x{HRESULT.S_OK.Value:x8}");
        return HRESULT.S_OK;
    }

    /// <summary>
    /// ProjFS invokes this callback to tell the provider that a directory enumeration is over.  This
    /// gives the provider the opportunity to release any resources it set up for the enumeration.
    /// </summary>
    protected override HRESULT EndDirEnum(PRJ_CALLBACK_DATA CallbackData, Guid EnumerationId)
    {
        Console.WriteLine($"\n----> {CurrentName()}");

        // Get rid of the DirInfo object we created in StartDirEnum.
        _activeEnumSessions.Remove(EnumerationId);
        Console.WriteLine($"<---- {CurrentName()}: return 0x{HRESULT.S_OK.Value:x8}");

        return HRESULT.S_OK;
    }

    /// <summary>
    /// ProjFS invokes this callback to request a list of files and directories under the given directory.
    /// 
    /// To handle this callback, RegFS calls DirInfo->FillFileEntry/FillDirEntry for each matching file
    /// or directory.
    /// 
    /// If the SearchExpression argument specifies something that doesn't exist in provider's namespace,
    /// or if the directory being enumerated is empty, the provider just returns S_OK without storing
    /// anything in DirEntryBufferHandle.  ProjFS will return the correct error code to the caller.
    /// 
    /// Below is a list of example commands that will invoke GetDirectoryEntries callbacks.
    /// These assume you've cd'd into the virtualization root folder.
    /// 
    /// Command                  CallbackData.FilePathName    SearchExpression
    /// ------------------------------------------------------------------------
    /// dir                      ""(root folder)               *
    /// dir foo*                 ""(root folder)               foo*
    /// dir H + TAB              ""(root folder)               H*
    /// dir HKEY_LOCAL_MACHINE   ""(root folder)               HKEY_LOCAL_MACHINE
    /// dir HKEY_LOCAL_MACHIN?   ""(root folder)               HKEY_LOCAL_MACHIN>
    /// 
    /// In the last example, the ">" character is the special wildcard value DOS_QM.  ProjFS handles this
    /// and other special file system wildcard values in its PrjFileNameMatch and PrjDoesNameContainWildCards
    /// APIs.
    /// </summary>
    protected override HRESULT GetDirEnum(
        PRJ_CALLBACK_DATA CallbackData,
        Guid EnumerationId,
        string SearchExpression,
        PRJ_DIR_ENTRY_BUFFER_HANDLE DirEntryBufferHandle
    )
    {
        Console.WriteLine($"\n----> {CurrentName()}: Path [{CallbackData.FilePathName}] SearchExpression [{SearchExpression}]");
        var hr = HRESULT.S_OK;

        // Get the correct enumeration session from our map.
        // Get out our DirInfo helper object, which manages the context for this enumeration.
        if (!_activeEnumSessions.TryGetValue(EnumerationId, out var dirInfo))
        {
            // We were asked for an enumeration we don't know about.
            hr = HRESULT.E_INVALIDARG;

            Console.WriteLine($"<---- {CurrentName()}: Unknown enumeration ID");
            return hr;
        }

        // If the enumeration is restarting, reset our bookkeeping information.
        if (CallbackData.Flags.HasFlag(PRJ_CALLBACK_DATA_FLAGS.PRJ_CB_DATA_FLAG_ENUM_RESTART_SCAN))
        {
            dirInfo.Reset();
        }

        if (!dirInfo.EntriesFilled)
        {
            // The DirInfo associated with the current session hasn't been initialized yet.  This method
            // will enumerate the subkeys and values in the registry key corresponding to CallbackData.FilePathName.
            // For each one that matches SearchExpression it will create an entry to return to ProjFS
            // and store it in the DirInfo object.
            hr = PopulateDirInfoForPath(CallbackData.FilePathName, dirInfo, SearchExpression);
            if (hr.Failed)
            {
                Console.WriteLine($"<---- {CurrentName()}: Failed to populate dirInfo: 0x{hr.Value:x8}");
                return hr;
            }

            // This will ensure the entries in the DirInfo are sorted the way the file system expects.
            dirInfo.SortEntriesAndMarkFilled();
        }

        // Return our directory entries to ProjFS.
        while (dirInfo.CurrentIsValid)
        {
            // ProjFS allocates a fixed size buffer then invokes this callback.  The callback needs to
            // call PrjFillDirEntryBuffer to fill as many entries as possible until the buffer is full.
            if (PInvoke.PrjFillDirEntryBuffer(dirInfo.CurrentFileName, dirInfo.CurrentBasicInfo(), DirEntryBufferHandle) != HRESULT.S_OK)
            {
                break;
            }

            // Only move the current entry cursor after the entry was successfully filled, so that we
            // can start from the correct index in the next GetDirEnum callback for this enumeration
            // session.
            dirInfo.MoveNext();
        }

        Console.WriteLine($"<---- {CurrentName()}: return 0x{hr.Value:x8}");
        return hr;
    }

    /// <summary>
    /// Populates a DirInfo object with directory and file entires that represent the registry keys and
    /// values that are under a given key.
    /// </summary>
    HRESULT PopulateDirInfoForPath(string relativePath, DirInfo dirInfo, string searchExpression)
    {
        RegEntries entries = new();

        // Get a list of the registry keys and values under the given key.
        var hr = _regOps.EnumerateKey(relativePath, entries);
        if (hr.Failed)
        {
            Console.WriteLine($"{CurrentName()}: Could not enumerate key: 0x{hr.Value:x8}");
            return hr;
        }

        // Store each registry key that matches searchExpression as a directory entry.
        foreach (var subKey in entries.SubKeys)
        {
            if (PInvoke.PrjFileNameMatch(subKey.Name, searchExpression))
            {
                dirInfo.FillDirEntry(subKey.Name);
            }
        }

        // Store each registry value that matches searchExpression as a file entry.
        foreach (var val in entries.Values)
        {
            if (PInvoke.PrjFileNameMatch(val.Name, searchExpression))
            {
                dirInfo.FillFileEntry(val.Name, val.Size);
            }
        }

        return hr;
    }

    /// <summary>
    /// ProjFS invokes this callback to request the contents of a file.
    /// 
    /// To handle this callback, the provider issues one or more calls to WriteFileData() to give
    /// ProjFS the file content. ProjFS will convert the on-disk placeholder into a hydrated placeholder,
    /// populated with the file contents.  Afterward, subsequent file reads will no longer invoke the
    /// GetFileStream callback.
    /// 
    /// If multiple threads read the same placeholder file simultaneously, ProjFS ensures that the provider
    /// receives only one GetFileStream callback.
    /// 
    /// If the provider is unable to process the request, it return an appropriate error code.  The caller
    /// who issued the read will receive an error, and the next file read for the same file will invoke
    /// GetFileStream again.
    /// 
    /// Below is a list of example commands that will invoke GetFileStream callbacks.
    /// Assume there's a file named 'testfile' in provider's namespace:
    /// 
    /// type testfile
    /// echo 123>>testfile
    /// echo 123>testfile
    /// </summary>
    protected unsafe override HRESULT GetFileData(PRJ_CALLBACK_DATA callbackData, ulong byteOffset, uint length)
    {
        Console.WriteLine($"\n----> {CurrentName()}: Path [{callbackData.FilePathName}] triggered by [{callbackData.TriggeringProcessImageFileName}]");
        // We're going to need alignment information that is stored in the instance to service this
        // callback.
        var hr = PInvoke.PrjGetVirtualizationInstanceInfo(_instanceHandle, out PRJ_VIRTUALIZATION_INSTANCE_INFO instanceInfo);
        if (hr.Failed)
        {
            Console.WriteLine($"<---- {CurrentName()}: PrjGetVirtualizationInstanceInfo: 0x{hr.Value:x8}");
            return hr;
        }

        // Allocate a buffer that adheres to the machine's memory alignment.  We have to do this in case
        // the caller who caused this callback to be invoked is performing non-cached I/O.  For more
        // details, see the topic "Providing File Data" in the ProjFS documentation.
        var writeBuffer = PInvoke.PrjAllocateAlignedBuffer(_instanceHandle, length);
        if (writeBuffer == IntPtr.Zero)
        {
            Console.WriteLine($"<---- {CurrentName()}: Could not allocate write buffer.");
            return HRESULT.E_OUTOFMEMORY;
        }

        // Read the data out of the registry.
        if (!_regOps.ReadValue(callbackData.FilePathName, writeBuffer, length))
        {
            hr = HRESULT.E_FILENOTFOUND;
            PInvoke.PrjFreeAlignedBuffer(writeBuffer);
            Console.WriteLine($"<---- {CurrentName()}: Failed to read from registry.");
            return hr;
        }

        // Call ProjFS to write the data we read from the registry into the on-disk placeholder.
        hr = WriteFileData(callbackData.DataStreamId, writeBuffer, byteOffset, length);
        if (hr.Failed)
        {
            // If this callback returns an error, ProjFS will return this error code to the thread that
            // issued the file read, and the target file will remain an empty placeholder.
            Console.WriteLine($"{CurrentName()}: failed to write file for [{callbackData.FilePathName}]: 0x{hr.Value:x8}");
        }

        // Free the memory-aligned buffer we allocated.
        PInvoke.PrjFreeAlignedBuffer(writeBuffer);
        Console.WriteLine($"<---- {CurrentName()}: return 0x{hr.Value:x8}");
        return hr;
    }

    /// <summary>
    /// ProjFS invokes this callback to deliver notifications of file system operations.
    /// 
    /// The provider can specify which notifications it wishes to receive by filling out an array of
    /// PRJ_NOTIFICATION_MAPPING structures that it feeds to PrjStartVirtualizing in the PRJ_STARTVIRTUALIZING_OPTIONS
    /// structure.
    /// 
    /// For the following notifications the provider can return a failure code.  This will prevent the
    /// associated file system operation from taking place.
    /// 
    /// PRJ_NOTIFICATION_FILE_OPENED
    /// PRJ_NOTIFICATION_PRE_DELETE
    /// PRJ_NOTIFICATION_PRE_RENAME
    /// PRJ_NOTIFICATION_PRE_SET_HARDLINK
    /// PRJ_NOTIFICATION_FILE_PRE_CONVERT_TO_FULL
    /// 
    /// All other notifications are informational only.
    /// 
    /// See also the PRJ_NOTIFICATION_TYPE enum for more details about the notification types.
    /// </summary>
    protected override HRESULT Notify(
        PRJ_CALLBACK_DATA CallbackData,
        bool IsDirectory,
        PRJ_NOTIFICATION NotificationType,
        string DestinationFileName,
        PRJ_NOTIFICATION_PARAMETERS NotificationParameters)
    {
        //if (CallbackData.InstanceContext != IntPtr.Zero)
        //{
        //    var del = Marshal.GetDelegateForFunctionPointer<PInvoke.GetManagedObjectDelegate>(CallbackData.InstanceContext);
        //}
        var hr = HRESULT.S_OK;
        Console.WriteLine($"\n----> {CurrentName()}: Path [{CallbackData.FilePathName}] triggered by [{CallbackData.TriggeringProcessImageFileName}]");
        Console.WriteLine($"----  Notification: {NotificationType}");

        switch (NotificationType)
        {
            case PRJ_NOTIFICATION.PRJ_NOTIFICATION_FILE_OPENED:
                break;

            case PRJ_NOTIFICATION.PRJ_NOTIFICATION_FILE_HANDLE_CLOSED_FILE_MODIFIED:
            case PRJ_NOTIFICATION.PRJ_NOTIFICATION_FILE_OVERWRITTEN:

                Console.WriteLine($" ----- [{CallbackData.FilePathName}] was modified");
                break;

            case PRJ_NOTIFICATION.PRJ_NOTIFICATION_NEW_FILE_CREATED:

                Console.WriteLine($" ----- [{CallbackData.FilePathName}] was created");
                break;

            case PRJ_NOTIFICATION.PRJ_NOTIFICATION_FILE_RENAMED:

                Console.WriteLine($" ----- [{CallbackData.FilePathName}] -> [{DestinationFileName}]");
                break;

            case PRJ_NOTIFICATION.PRJ_NOTIFICATION_FILE_HANDLE_CLOSED_FILE_DELETED:

                Console.WriteLine($" ----- [{CallbackData.FilePathName}] was deleted");
                break;

            case PRJ_NOTIFICATION.PRJ_NOTIFICATION_PRE_RENAME:

                if (_readonlyNamespace)
                {
                    // Block file renames.
                    hr = HRESULT.E_ACCESSDENIED;
                    Console.WriteLine($" ----- rename request for [{CallbackData.FilePathName}] was rejected");
                }
                else
                {
                    Console.WriteLine($" ----- rename request for [{CallbackData.FilePathName}]");
                }
                break;

            case PRJ_NOTIFICATION.PRJ_NOTIFICATION_PRE_DELETE:

                if (_readonlyNamespace)
                {
                    // Block file deletion.  We must return a particular NTSTATUS to ensure the file system
                    // properly recognizes that this is a deny-delete.
                    hr = new HRESULT(NTSTATUS.STATUS_CANNOT_DELETE);
                    Console.WriteLine($" ----- delete request for [{CallbackData.FilePathName}] was rejected");
                }
                else
                {
                    Console.WriteLine($" ----- delete request for [{CallbackData.FilePathName}]");
                }
                break;

            case PRJ_NOTIFICATION.PRJ_NOTIFICATION_FILE_PRE_CONVERT_TO_FULL:
                break;

            default:
                Console.WriteLine($"{CurrentName()}: Unexpected notification");
                break;
        }

        Console.WriteLine($"<---- {CurrentName()}: return 0x{hr.Value:x8}");
        return hr;
    }
}
