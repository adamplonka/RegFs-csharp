/*++

Copyright (c) Microsoft Corporation.  All rights reserved.

Abstract:

Utility class for file system path operations.

--*/

namespace RegFs;

class PathUtils
{
    private static readonly char[] invalidFileNameChars = Path.GetInvalidFileNameChars();

    /// <summary>
    /// Returns true if the given path is for the virtualization root.  The path must be expressed
    /// relative to the virtualization root
    /// </summary>
    public static bool IsVirtualizationRoot(string filePathName)
    {
        return string.IsNullOrEmpty(filePathName) || filePathName == "\\";
    }

    /// <summary>
    /// Returns true if the file name contains any invalid character.
    /// </summary>
    public static bool ContainsInvalidFileNameChar(string fileName)
    {
        return fileName.Split(invalidFileNameChars, 2).Length == 2;
    }
};
