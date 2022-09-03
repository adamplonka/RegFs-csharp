using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using static RegFs.Macros;

/*++

Copyright (c) Microsoft Corporation

Abstract:

Helper class to encapsulate registry operations.

--*/

namespace RegFs;

/// <summary>
/// Represents a single entry in the registry, capturing its name and size.
/// </summary>
struct RegEntry
{
    public string Name;
    public long Size;
}

/// <summary>
/// Stores RegEntry items for the entries within a registry key, separated into lists of subkeys
/// and values.
/// </summary>
struct RegEntries
{
    public List<RegEntry> SubKeys;
    public List<RegEntry> Values;

    public RegEntries()
    {
        SubKeys = new();
        Values = new();
    }
};

class RegOps
{
    /// <summary>
    /// Maps string names of the predefined registry keys (HKEY_CLASSES_ROOT, HKEY_CURRENT_USER, etc.)
    /// to their RegistryKey values.
    /// </summary>
    private readonly Dictionary<string, RegistryKey> _regRootKeyMap = new()
    {
        ["HKEY_CLASSES_ROOT"] = Registry.ClassesRoot,
        ["HKEY_CURRENT_USER"] = Registry.CurrentUser,
        ["HKEY_LOCAL_MACHINE"] = Registry.LocalMachine,
        ["HKEY_USERS"] = Registry.Users,
        ["HKEY_CURRENT_CONFIG"] = Registry.CurrentConfig
    };

    /// <summary>
    /// Returns a RegEntries struct populated with the subkeys and values in the registry key whose
    /// path is specified.
    /// </summary>
    public HRESULT EnumerateKey(string path, RegEntries entries)
    {
        var hr = HRESULT.S_OK;
        if (PathUtils.IsVirtualizationRoot(path))
        {
            // The path is the "root" of the registry, so return the names of the predefined keys
            // in _regRootKeyMap.
            foreach (var kvp in _regRootKeyMap)
            {
                var entry = new RegEntry { Name = kvp.Key };
                entries.SubKeys.Add(entry);
            }
        }
        else
        {
            // The path is somewhere below the root, so try opening the key.
            hr = OpenKeyByPath(path, out var subKey);

            // If the path corresponds to a registry key, enumerate it.
            if (subKey != null)
            {
                hr = EnumerateKey(subKey, entries);
                subKey.Close();
            }
        }

        return hr;
    }

    /// <summary>
    /// Reads a value from the registry.
    /// </summary>
    public bool ReadValue(string path, IntPtr data, uint len)
    {
        var lastPos = path.LastIndexOf('\\');
        if (lastPos == -1)
        {
            // There are no '\' characters in the path. The only paths with no '\' are the predefined
            // keys, so this can't be a value.
            return false;
        }

        // Split the path into <key>\<value>
        var keyPath = path[..lastPos];
        var valName = path[(lastPos + 1)..];

        // Open the key path to get a RegistryKey handle to it.
        OpenKeyByPath(keyPath, out RegistryKey subkey);
        GetValue(subkey, valName, data, (int)len);
        return true;
    }

    /// <summary>
    /// Returns true if the given path corresponds to a key that exists in the registry.
    /// </summary>
    public bool DoesKeyExist(string path)
    {
        OpenKeyByPath(path, out var subkey);
        if (subkey == null)
        {
            Console.WriteLine($"{CurrentName()}: key [{path}] doesn't exist");
            return false;
        }

        subkey.Close();
        return true;
    }

    public HRESULT GetValue(RegistryKey key, string valueName, IntPtr data, int dataSize)
    {
        var type = 0;
        return PInvoke.RegQueryValueEx(key.Handle, valueName, null, ref type, data, ref dataSize);
    }

    /// <summary>
    /// Returns true if the given path corresponds to a value that exists in the registry, and tells
    /// you how big it is.
    /// </summary>
    public bool DoesValueExist(string path, out int valSize)
    {
        valSize = 0;
        var pos = path.LastIndexOf('\\');
        if (pos == -1)
        {
            // There are no '\' characters in the path. The only paths with no '\' are the predefined
            // keys, so this can't be a value.
            return false;
        }
        else
        {
            OpenKeyByPath(path[..pos], out var subkey);
            if (subkey == null)
            {
                Console.WriteLine($"{CurrentName()}: value [{path[..pos]}] doesn't exist");
                return false;
            }

            var valPathStr = path[(pos + 1)..];
            if (valPathStr == "(default)")
            {
                valPathStr = null;
            }
            var exists = ValueExists(subkey, valPathStr, out valSize);
            subkey.Close();

            return exists;
        }
    }

    /// <summary>
    /// Gets the RegistryKey for a registry key given the path, if it exists.
    /// </summary>
    private HRESULT OpenKeyByPath(string path, out RegistryKey hKey)
    {
        var hr = HRESULT.S_OK;
        var pos = path.IndexOf('\\');
        if (pos == -1)
        {
            // There are no '\' characters in the path. The only paths with no '\' are the predefined
            // keys, so try to find the correct RegistryKey in the predefined keys map.
            if (!_regRootKeyMap.TryGetValue(path, out hKey))
            {
                Console.WriteLine($"{CurrentName()}: root key [{path}] doesn't exist");
                hr = HRESULT.E_FILENOTFOUND;
            }
        }
        else
        {
            // The first component of the path should be a predefined key, so get its RegistryKey value and
            // try opening the rest of the key relative to it.
            var rootKeyStr = path[..pos];
            var rootKey = _regRootKeyMap[rootKeyStr];
            hKey = rootKey.OpenSubKey(path[(pos + 1)..]);

            return HRESULT.S_OK;
        }

        return hr;
    }

    private long GetValueSize(RegistryKey key, string valueName)
    {
        return ValueExists(key, valueName, out var size) ? size : 0;
    }

    private bool ValueExists(RegistryKey key, string valueName, out int dataSize)
    {
        dataSize = 0;
        var type = 0;
        var hr = PInvoke.RegQueryValueEx(key.Handle, valueName, null, ref type, IntPtr.Zero, ref dataSize);
        return hr == HRESULT.S_OK;
    }

    /// <summary>
    /// Returns a RegEntries struct populated with the subkeys and values in the specified registry key.
    /// </summary>
    HRESULT EnumerateKey(RegistryKey hKey, RegEntries entries)
    {
        // If there are subkeys, enumerate them until RegEnumKeyEx fails.
        if (hKey.SubKeyCount > 0)
        {
            var subKeys = hKey.GetSubKeyNames();
            Console.WriteLine($"{CurrentName()}: Subkeys:\n{string.Join("\n", subKeys)}");
            entries.SubKeys.AddRange(
                subKeys.Select(subKey => new RegEntry { Name = subKey }));
        }

        if (ValueExists(hKey, null, out var defaultValueSize))
        {
            entries.Values.Add(new RegEntry { Name = "(default)", Size = defaultValueSize });
        }

        // If there are values, enumerate them until RegEnumValue fails.
        if (hKey.ValueCount > 0)
        {
            var valueNames = hKey.GetValueNames();
            Console.WriteLine($"{CurrentName()}: Values:\n{string.Join('\n', valueNames)}");
            entries.Values.AddRange(
                valueNames.Select(name => new RegEntry { Name = name, Size = GetValueSize(hKey, name) }));
        }

        return HRESULT.S_OK;
    }
}