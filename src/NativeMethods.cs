using Microsoft.Win32.SafeHandles;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Foundation.Metadata;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.ProjectedFileSystem;

namespace RegFs
{
    public static class Macros
    {
        public static string CurrentName([CallerMemberName] string caller = "") => caller;
    }
}

namespace Windows.Win32
{
    namespace Foundation
    {
        internal partial struct HRESULT
        {
            public static readonly HRESULT E_FILENOTFOUND = unchecked((HRESULT)0x80070002);
        }
    }

    internal static partial class PInvoke
    {
        /// <summary>Configures a ProjFS virtualization instance and starts it, making it available to service I/O and invoke callbacks on the provider.</summary>
        /// <param name="virtualizationRootPath">
        /// <para>Pointer to a null-terminated unicode string specifying the full path to the virtualization root directory. The provider must have called <a href="https://docs.microsoft.com/windows/desktop/api/projectedfslib/nf-projectedfslib-prjmarkdirectoryasplaceholder">PrjMarkDirectoryAsPlaceholder</a> passing the specified path as the rootPathName parameter and NULL as the targetPathName parameter before calling this routine. This only needs to be done once to designate the path as the virtualization root directory</para>
        /// <para><see href="https://docs.microsoft.com/windows/win32/api//projectedfslib/nf-projectedfslib-prjstartvirtualizing#parameters">Read more on docs.microsoft.com</see>.</para>
        /// </param>
        /// <param name="callbacks">Pointer to a <a href="https://docs.microsoft.com/windows/desktop/api/projectedfslib/ns-projectedfslib-prj_callbacks">PRJ_CALLBACKS</a> structure that has been initialized with PrjCommandCallbacksInit and filled in with pointers to the provider's callback functions.</param>
        /// <param name="instanceContext">Pointer to context information defined by the provider for each instance. This parameter is optional and can be NULL. If it is specified, ProjFS will return it in the InstanceContext member of <a href="https://docs.microsoft.com/windows/desktop/api/projectedfslib/ns-projectedfslib-prj_callback_data">PRJ_CALLBACK_DATA</a> when invoking provider callback routines.</param>
        /// <param name="options">An optional pointer to a  <a href="https://docs.microsoft.com/windows/desktop/api/projectedfslib/ns-projectedfslib-prj_startvirtualizing_options">PRJ_STARTVIRTUALIZING_OPTIONS</a>.</param>
        /// <param name="namespaceVirtualizationContext">
        /// <para>On success returns an opaque handle to the ProjFS virtualization instance. The provider passes this value when calling functions that require a PRJ_NAMESPACE_VIRTUALIZATION_CONTEXT as input.</para>
        /// <para><see href="https://docs.microsoft.com/windows/win32/api//projectedfslib/nf-projectedfslib-prjstartvirtualizing#parameters">Read more on docs.microsoft.com</see>.</para>
        /// </param>
        /// <returns>The error, HRESULT_FROM_WIN32(ERROR_REPARSE_TAG_MISMATCH), indicates that virtualizationRootPath has not been configured as a virtualization root.</returns>
        /// <remarks>
        /// <para><see href="https://docs.microsoft.com/windows/win32/api//projectedfslib/nf-projectedfslib-prjstartvirtualizing">Learn more about this API from docs.microsoft.com</see>.</para>
        /// </remarks>
        [DllImport("PROJECTEDFSLIB", ExactSpelling = true, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [SupportedOSPlatform("windows10.0.17763")]
        internal static extern HRESULT PrjStartVirtualizing([MarshalAs(UnmanagedType.LPWStr)] string virtualizationRootPath, PRJ_CALLBACKS callbacks, IntPtr instanceContext, PRJ_STARTVIRTUALIZING_OPTIONS options, out PRJ_NAMESPACE_VIRTUALIZATION_CONTEXT namespaceVirtualizationContext);

        [DllImport("ADVAPI32", CharSet = CharSet.Unicode, BestFitMapping = false)]
        [ResourceExposure(ResourceScope.None)]
        public static extern HRESULT RegQueryValueEx(SafeRegistryHandle hKey, string lpValueName,
            int[] lpReserved, ref int lpType, IntPtr lpData,
            ref int lpcbData);

        /// <summary>Allocates a buffer that meets the memory alignment requirements of the virtualization instance's storage device.</summary>
        /// <param name="namespaceVirtualizationContext">Opaque handle for the virtualization instance.</param>
        /// <param name="size">The size of the buffer required, in bytes.</param>
        /// <returns>Returns NULL if the buffer could not be allocated.</returns>
        /// <remarks>
        /// <para><see href="https://docs.microsoft.com/windows/win32/api//projectedfslib/nf-projectedfslib-prjallocatealignedbuffer">Learn more about this API from docs.microsoft.com</see>.</para>
        /// </remarks>
        [DllImport("PROJECTEDFSLIB", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [SupportedOSPlatform("windows10.0.17763")]
        internal static extern IntPtr PrjAllocateAlignedBuffer(PRJ_NAMESPACE_VIRTUALIZATION_CONTEXT namespaceVirtualizationContext, nuint size);

        /// <summary>Frees an allocated buffer.</summary>
        /// <param name="buffer">The buffer to free.</param>
        /// <returns>If this function succeeds, it returns <b xmlns:loc="http://microsoft.com/wdcml/l10n">S_OK</b>. Otherwise, it returns an <b xmlns:loc="http://microsoft.com/wdcml/l10n">HRESULT</b> error code.</returns>
        /// <remarks>
        /// <para><see href="https://docs.microsoft.com/windows/win32/api//projectedfslib/nf-projectedfslib-prjfreealignedbuffer">Learn more about this API from docs.microsoft.com</see>.</para>
        /// </remarks>
        [DllImport("PROJECTEDFSLIB", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [SupportedOSPlatform("windows10.0.17763")]
        internal static extern void PrjFreeAlignedBuffer(IntPtr buffer);

        /// <summary>TBD.</summary>
        /// <param name="namespaceVirtualizationContext">
        /// <para>Opaque handle for the virtualization instance.</para>
        /// <para>If the provider is servicing a <a href="https://docs.microsoft.com/windows/desktop/api/projectedfslib/nc-projectedfslib-prj_get_file_data_cb">PRJ_GET_FILE_DATA_CB</a> callback, this must be the value from the VirtualizationInstanceHandle member of the callbackData passed to the provider in the callback.</para>
        /// <para><see href="https://docs.microsoft.com/windows/win32/api//projectedfslib/nf-projectedfslib-prjwritefiledata#parameters">Read more on docs.microsoft.com</see>.</para>
        /// </param>
        /// <param name="dataStreamId">
        /// <para>Identifier for the data stream to write to.</para>
        /// <para>If the provider is servicing a <a href="https://docs.microsoft.com/windows/desktop/api/projectedfslib/nc-projectedfslib-prj_get_file_data_cb">PRJ_GET_FILE_DATA_CB</a> callback, this must be the value from the DataStreamId member of the callbackData passed to the provider in the callback.</para>
        /// <para><see href="https://docs.microsoft.com/windows/win32/api//projectedfslib/nf-projectedfslib-prjwritefiledata#parameters">Read more on docs.microsoft.com</see>.</para>
        /// </param>
        /// <param name="buffer">Pointer to a buffer containing the data to write. The buffer must be at least as large as the value of the length parameter in bytes. The provider should use <a href="https://docs.microsoft.com/windows/desktop/api/projectedfslib/nf-projectedfslib-prjallocatealignedbuffer">PrjAllocateAlignedBuffer</a> to ensure that the buffer meets the storage device's alignment requirements.</param>
        /// <param name="byteOffset">Byte offset from the beginning of the file at which to write the data.</param>
        /// <param name="length">The number of bytes to write to the file.</param>
        /// <returns>HRESULT_FROM_WIN32(ERROR_OFFSET_ALIGNMENT_VIOLATION) indicates that the user's handle was opened for unbuffered I/O and byteOffset is not aligned to the sector size of the storage device.</returns>
        /// <remarks>
        /// <para><see href="https://docs.microsoft.com/windows/win32/api//projectedfslib/nf-projectedfslib-prjwritefiledata">Learn more about this API from docs.microsoft.com</see>.</para>
        /// </remarks>
        [DllImport("PROJECTEDFSLIB", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [SupportedOSPlatform("windows10.0.17763")]
        internal static extern HRESULT PrjWriteFileData(PRJ_NAMESPACE_VIRTUALIZATION_CONTEXT namespaceVirtualizationContext, Guid dataStreamId, IntPtr buffer, ulong byteOffset, uint length);

        internal unsafe static HRESULT PrjStartVirtualizing(string virtualizationRootPath, PRJ_CALLBACKS callbacks, IntPtr instanceContext, PrjVirtualizingOptions options, out PRJ_NAMESPACE_VIRTUALIZATION_CONTEXT namespaceVirtualizationContext)
        {
            var mappingsArray = options.NotificationMappings.Select(mapping => mapping.ToStruct()).ToArray();
            fixed (void* mappingsArrayP = mappingsArray)
            {
                var localOptions = new PRJ_STARTVIRTUALIZING_OPTIONS
                {
                    Flags = options.Flags,
                    PoolThreadCount = options.PoolThreadCount,
                    ConcurrentThreadCount = options.ConcurrentThreadCount,
                    NotificationMappings = (IntPtr)mappingsArrayP,
                    NotificationMappingsCount = (uint)options.NotificationMappings.Count
                };
                try
                {
                    return PInvoke.PrjStartVirtualizing(virtualizationRootPath, callbacks, instanceContext, localOptions, out namespaceVirtualizationContext);
                }
                finally
                {
                    foreach (var mapping in mappingsArray)
                    {
                        Marshal.FreeHGlobal(mapping.NotificationRoot);
                    }
                }
            }
        }
    }

    namespace Storage.ProjectedFileSystem
    {
        /// <summary>Options to provide when starting a virtualization instance.</summary>
        /// <remarks>
        /// <para><see href="https://docs.microsoft.com/windows/win32/api//projectedfslib/ns-projectedfslib-prj_startvirtualizing_options">Learn more about this API from docs.microsoft.com</see>.</para>
        /// </remarks>
        internal struct PRJ_STARTVIRTUALIZING_OPTIONS
        {
            /// <summary>A flag for starting virtualization.</summary>
            public PRJ_STARTVIRTUALIZING_FLAGS Flags;
            /// <summary>The number of threads the provider wants to create to service callbacks.</summary>
            public uint PoolThreadCount;
            /// <summary>The maximum number of threads the provider wants to run concurrently to process callbacks.</summary>
            public uint ConcurrentThreadCount;
            /// <summary>An array of zero or more notifiction mappings. See the Remarks section of PRJ_NOTIFICATION MAPPING for more details.</summary>
            public IntPtr NotificationMappings;
            /// <summary>The number of notification mappings provided in NotificationMappings.</summary>
            public uint NotificationMappingsCount;
        }

        internal class PrjVirtualizingOptions
        {
            /// <summary>A flag for starting virtualization.</summary>
            public PRJ_STARTVIRTUALIZING_FLAGS Flags;
            /// <summary>The number of threads the provider wants to create to service callbacks.</summary>
            public uint PoolThreadCount;
            /// <summary>The maximum number of threads the provider wants to run concurrently to process callbacks.</summary>
            public uint ConcurrentThreadCount;
            /// <summary>An array of zero or more notifiction mappings. See the Remarks section of PRJ_NOTIFICATION MAPPING for more details.</summary>
            public IList<PrjNotificationMapping> NotificationMappings = new List<PrjNotificationMapping>();
        }

        /// <summary>Describes a notification mapping, which is a pairing between a directory (referred to as a &quot;notification root&quot;) and a set of notifications, expressed as a bit mask.</summary>
        /// <remarks>
        /// <para>PRJ_NOTIFICATION_MAPPING describes a "notification mapping", which is a pairing between a directory (referred to as a "notification root") and a set of notifications, expressed as a bit mask, which ProjFS should send for that directory and its descendants. A notification mapping can also be established for a single file.</para>
        /// <para>The provider puts an array of zero or more PRJ_NOTIFICATION_MAPPING structures in the NotificationMappings member of the options parameter of PrjStartVirtualizing to configure notifications for the virtualization root.</para>
        /// <para>If the provider does not specify any notification mappings, ProjFS will default to sending the notifications PRJ_NOTIFICATION_FILE_OPENED, PRJ_NOTIFICATION_NEW_FILE_CREATED, and PRJ_NOTIFICATION_FILE_OVERWRITTEN for all files and directories in the virtualization instance.</para>
        /// <para>The directory or file is specified relative to the virtualization root, with an empty string representing the virtualization root itself.</para>
        /// <para>If the provider specifies multiple notification mappings, and some are descendants of others, the mappings must be specified in descending depth. Notification mappings at deeper levels override higher-level ones for their descendants.</para>
        /// <para><see href="https://docs.microsoft.com/windows/win32/api//projectedfslib/ns-projectedfslib-prj_notification_mapping#">Read more on docs.microsoft.com</see>.</para>
        /// </remarks>
        internal struct PRJ_NOTIFICATION_MAPPING
        {
            /// <summary>A bit mask representing a set of notifications.</summary>
            public PRJ_NOTIFY_TYPES NotificationBitMask;
            /// <summary>The directory that the notification mapping is paired to.</summary>
            public IntPtr NotificationRoot;
        }

        internal class PrjNotificationMapping
        {
            public PRJ_NOTIFY_TYPES NotificationBitMask;
            public string NotificationRoot;

            internal PRJ_NOTIFICATION_MAPPING ToStruct()
            {
                return new PRJ_NOTIFICATION_MAPPING
                {
                    NotificationBitMask = NotificationBitMask,
                    NotificationRoot = Marshal.StringToHGlobalUni(NotificationRoot)
                };
            }
        }

        /// <summary>Defines the standard information passed to a provider for every operation callback.</summary>
        /// <remarks>
        /// <para><see href="https://docs.microsoft.com/windows/win32/api//projectedfslib/ns-projectedfslib-prj_callback_data">Learn more about this API from docs.microsoft.com</see>.</para>
        /// </remarks>
        internal struct PRJ_CALLBACK_DATA
        {
            /// <summary>Size in bytes of this structure. The provider must not attempt to access any field of this structure that is located beyond this value.</summary>
            public uint Size;
            /// <summary>Callback-specific flags.</summary>
            public PRJ_CALLBACK_DATA_FLAGS Flags;
            /// <summary>Opaque handle to the virtualization instance that is sending the callback.</summary>
            public PRJ_NAMESPACE_VIRTUALIZATION_CONTEXT NamespaceVirtualizationContext;
            /// <summary>
            /// <para>A value that uniquely identifies a particular invocation of a callback. The provider uses this value:</para>
            /// <para></para>
            /// <para>This doc was truncated.</para>
            /// <para><see href="https://docs.microsoft.com/windows/win32/api//projectedfslib/ns-projectedfslib-prj_callback_data#members">Read more on docs.microsoft.com</see>.</para>
            /// </summary>
            public int CommandId;
            /// <summary>A value that uniquely identifies the file handle for the callback.</summary>
            public Guid FileId;
            /// <summary>A value that uniquely identifies an open data stream for the callback.</summary>
            public Guid DataStreamId;
            /// <summary>The path to the target file. This is a null-terminated string of Unicode characters. This path is always specified relative to the virtualization root.</summary>
            [MarshalAs(UnmanagedType.LPWStr)]
            public string FilePathName;
            /// <summary>Version information if the target of the callback is a placeholder or partial file.</summary>
            public PRJ_PLACEHOLDER_VERSION_INFO VersionInfo;
            /// <summary>
            /// <para>The process identifier for the process that triggered this callback. If this information is not available, this will be 0. Callbacks that supply this information include: <a href="https://docs.microsoft.com/windows/desktop/api/projectedfslib/nc-projectedfslib-prj_get_placeholder_info_cb">PRJ_GET_PLACEHOLDER_INFO_CB</a>, <a href="https://docs.microsoft.com/windows/desktop/api/projectedfslib/nc-projectedfslib-prj_get_file_data_cb">PRJ_GET_FILE_DATA_CB</a>, and <a href="https://docs.microsoft.com/windows/desktop/api/projectedfslib/nc-projectedfslib-prj_notification_cb">PRJ_NOTIFICATION_CB</a>.</para>
            /// <para><see href="https://docs.microsoft.com/windows/win32/api//projectedfslib/ns-projectedfslib-prj_callback_data#members">Read more on docs.microsoft.com</see>.</para>
            /// </summary>
            public uint TriggeringProcessId;
            /// <summary>A null-terminated Unicode string specifying the image file name corresponding to TriggeringProcessId. If TriggeringProcessId is 0 this will be NULL.</summary>
            [MarshalAs(UnmanagedType.LPWStr)]
            public string TriggeringProcessImageFileName;
            /// <summary>
            /// <para>A pointer to context information defined by the provider. The provider passes this context in the instanceContext parameter of <a href="https://docs.microsoft.com/windows/desktop/api/projectedfslib/nf-projectedfslib-prjstartvirtualizing">PrjStartVirtualizing</a>.</para>
            /// <para>If the provider did not specify such a context, this value will be NULL.</para>
            /// <para><see href="https://docs.microsoft.com/windows/win32/api//projectedfslib/ns-projectedfslib-prj_callback_data#members">Read more on docs.microsoft.com</see>.</para>
            /// </summary>
            public IntPtr InstanceContext;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate HRESULT PRJ_START_DIRECTORY_ENUMERATION_CB(PRJ_CALLBACK_DATA callbackData, Guid enumerationId);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate HRESULT PRJ_END_DIRECTORY_ENUMERATION_CB(PRJ_CALLBACK_DATA callbackData, Guid enumerationId);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate HRESULT PRJ_GET_DIRECTORY_ENUMERATION_CB(PRJ_CALLBACK_DATA callbackData, Guid enumerationId, [MarshalAs(UnmanagedType.LPWStr)] string searchExpression, PRJ_DIR_ENTRY_BUFFER_HANDLE dirEntryBufferHandle);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate HRESULT PRJ_GET_PLACEHOLDER_INFO_CB(PRJ_CALLBACK_DATA callbackData);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate HRESULT PRJ_GET_FILE_DATA_CB(PRJ_CALLBACK_DATA callbackData, ulong byteOffset, uint length);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate HRESULT PRJ_QUERY_FILE_NAME_CB(PRJ_CALLBACK_DATA callbackData);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate HRESULT PRJ_NOTIFICATION_CB(PRJ_CALLBACK_DATA callbackData, bool isDirectory, PRJ_NOTIFICATION notification, [MarshalAs(UnmanagedType.LPWStr)] string destinationFileName, PRJ_NOTIFICATION_PARAMETERS operationParameters);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate void PRJ_CANCEL_COMMAND_CB(PRJ_CALLBACK_DATA callbackData);
    }
}
