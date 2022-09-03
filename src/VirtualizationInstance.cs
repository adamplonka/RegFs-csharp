using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.ProjectedFileSystem;

namespace RegFs;

[Flags]
public enum OptionalMethods
{
    None = 0,
    Notify = 0x1,
    QueryFileName = 0x2,
    CancelCommand = 0x4
}

internal abstract class VirtualizationInstance
{
    protected PRJ_NAMESPACE_VIRTUALIZATION_CONTEXT _instanceHandle;
    const string _instanceIdFile = @"\.regfsId";
    private string _rootPath;
    private PrjVirtualizingOptions _options;
    private PRJ_CALLBACKS _callbacks = new();
    private OptionalMethods _implementedOptionalMethods = OptionalMethods.None;

    public HRESULT Start(string rootPath, PrjVirtualizingOptions options)
    {
        _rootPath = rootPath;
        _options = options ?? new PrjVirtualizingOptions();

        // Ensure we have a virtualization root directory that is stamped with an instance ID using the
        // PrjMarkDirectoryAsPlaceholder API.
        var hr = EnsureVirtualizationRoot();
        if (hr.Failed)
        {
            return hr;
        }

        // Register the required C callbacks.
        _callbacks.StartDirectoryEnumerationCallback = StartDirEnum;
        _callbacks.EndDirectoryEnumerationCallback = EndDirEnum;
        _callbacks.GetDirectoryEnumerationCallback = GetDirEnum;
        _callbacks.GetPlaceholderInfoCallback = GetPlaceholderInfo;
        _callbacks.GetFileDataCallback = GetFileData;

        // Register the optional C callbacks.

        // Register Notify if the provider says it implemented it, unless the provider didn't create any
        // notification mappings.
        if (GetOptionalMethods().HasFlag(OptionalMethods.Notify) &&
            _options.NotificationMappings.Count > 0)
        {
            _callbacks.NotificationCallback = Notify;
        }

        // Register QueryFileName if the provider says it implemented it.
        if (GetOptionalMethods().HasFlag(OptionalMethods.QueryFileName))
        {
            _callbacks.QueryFileNameCallback = QueryFileName;
        }

        // Register CancelCommand if the provider says it implemented it.
        if (GetOptionalMethods().HasFlag(OptionalMethods.CancelCommand))
        {
            _callbacks.CancelCommandCallback = CancelCommand;
        }

        // Start the virtualization instance.
        hr = PInvoke.PrjStartVirtualizing(_rootPath, _callbacks, IntPtr.Zero, _options, out _instanceHandle);
        return hr;
    }

    public void Stop()
    {
        PInvoke.PrjStopVirtualizing(_instanceHandle);
    }

    protected HRESULT WritePlaceholderInfo(string relativePath, PRJ_PLACEHOLDER_INFO placeholderInfo)
    {
        return PInvoke.PrjWritePlaceholderInfo(_instanceHandle, relativePath, placeholderInfo, (uint)Marshal.SizeOf(placeholderInfo));
    }

    protected unsafe HRESULT WriteFileData(Guid streamId, IntPtr buffer, ulong byteOffset, uint length)
    {
        return PInvoke.PrjWriteFileData(_instanceHandle, streamId, buffer, byteOffset, length);
    }

    protected virtual HRESULT Notify(PRJ_CALLBACK_DATA callbackData,
        bool isDirectory,
        PRJ_NOTIFICATION notificationType,
        string destinationFileName,
        PRJ_NOTIFICATION_PARAMETERS notificationParameters)
    {
        // If the derived provider implements this callback they must call SetOptionalMethods(OptionalMethods::Notify)
        // to cause the callback to be registered when starting the virtualization instance.
        throw new NotImplementedException();
    }

    protected virtual HRESULT QueryFileName(PRJ_CALLBACK_DATA callbackData)
    {
        // If the derived provider implements this callback they must call SetOptionalMethods(OptionalMethods::QueryFileName)
        // to cause the callback to be registered when starting the virtualization instance.
        throw new NotImplementedException();
    }

    protected virtual void CancelCommand(PRJ_CALLBACK_DATA callbackData)
    {
        // If the derived provider implements this callback they must call SetOptionalMethods(OptionalMethods::CancelCommand)
        // to cause the callback to be registered when starting the virtualization instance.
        throw new NotImplementedException();
    }

    // Gets the set of optional methods the derived class has indicated that it has implemented.
    OptionalMethods GetOptionalMethods()
    {
        return _implementedOptionalMethods;
    }

    // Sets the set of optional methods the derived class wants to indicate that it has implemented.
    protected void SetOptionalMethods(OptionalMethods optionalMethodsToSet)
    {
        _implementedOptionalMethods |= optionalMethodsToSet;
    }

    /// <summary>
    /// Ensures that the directory _rootPath, which we want to use as the virtualization root, exists.
    /// 
    /// If the _rootPath directory does not yet exist, this routine:
    /// 1. Creates the _rootPath directory.
    /// 2. Generates a virtualization instance ID.
    /// 3. Stores the ID in a file in the directory to mark it as the virtualization root.
    /// 4. Marks the directory as the virtualization root, using the PrjMarkDirectoryAsPlaceholder API
    /// and the generated ID.
    /// 
    /// If the _rootPath directory already exists, this routine checks for the file that should contain
    /// the stored ID.  If it exists, we assume this is our virtualization root.
    /// 
    /// </summary>
    private HRESULT EnsureVirtualizationRoot()
    {
        Guid instanceId = default;

        // Try creating our virtualization root.
        if (File.Exists(_rootPath + _instanceIdFile))
        {
            // The virtualization root already exists. Check for the stored virtualization instance
            // ID.
            var fileBytes = File.ReadAllBytes(_rootPath + _instanceIdFile);
            instanceId = new Guid(fileBytes);
        }

        if (instanceId == default)
        {
            Directory.CreateDirectory(_rootPath);

            // We created a new directory.  Create a virtualization instance ID.
            instanceId = Guid.NewGuid();

            // Store the ID in the directory as a way for us to detect that this is our directory in
            // the future.
            File.WriteAllBytes(_rootPath + _instanceIdFile, instanceId.ToByteArray());

            // Mark the directory as the virtualization root.
            var hr = PInvoke.PrjMarkDirectoryAsPlaceholder(_rootPath, null, null, instanceId);
            if (hr.Failed)
            {
                // Let's do a best-effort attempt to clean up the directory.
                File.Delete(_rootPath + _instanceIdFile);
                Directory.Delete(_rootPath);

                return hr;
            }
        }

        return HRESULT.S_OK;
    }

    protected abstract HRESULT StartDirEnum(PRJ_CALLBACK_DATA callbackData, Guid enumerationId);

    protected abstract HRESULT EndDirEnum(PRJ_CALLBACK_DATA callbackData, Guid enumerationId);

    protected abstract HRESULT GetDirEnum(
       PRJ_CALLBACK_DATA callbackData,
       Guid enumerationId,
       string searchExpression,
       PRJ_DIR_ENTRY_BUFFER_HANDLE dirEntryBufferHandle
    );

    protected abstract HRESULT GetPlaceholderInfo(PRJ_CALLBACK_DATA CallbackData);

    protected abstract HRESULT GetFileData(PRJ_CALLBACK_DATA callbackData, ulong byteOffset, uint length);
}
