using Windows.Win32;
using Windows.Win32.Storage.ProjectedFileSystem;

namespace RegFs;

/// <summary>
/// A comparison routine for std::sort that wraps PrjFileNameCompare() so that we can sort our DirInfo
/// the same way the file system would.
/// This holds the information the RegFS provider will return for a single directory entry.
///
/// Note that RegFS does not supply any timestamps.  This is because the only timestamp the registry
/// maintains is the last write time for a key.  It does not maintain creation, last-access, or change
/// times for keys, and it does not maintain any timestamps at all for values.  When RegFS calls
/// PrjFillDirEntryBuffer(), ProjFS sees that the timestamp values are 0 and uses the current time
/// instead.
/// </summary>
struct DirEntry
{
    public string FileName;
    public bool IsDirectory;
    public long FileSize;
};

class DirInfoComparer : IComparer<DirEntry>
{
    public static readonly DirInfoComparer Default = new();
    public int Compare(DirEntry x, DirEntry y) => PInvoke.PrjFileNameCompare(x.FileName, y.FileName);
}

/// <summary>
/// RegFS uses a DirInfo object to hold directory entries.  When RegFS receives enumeration callbacks
/// it populates the DirInfo with a vector of DirEntry structs, one for each key and value in the
/// registry key being enumerated.
///
/// Refer to RegfsProvider::StartDirEnum, RegfsProvider::GetDirEnum, and RegfsProvider::EndDirEnum
/// to see how this class is used.
/// </summary>
class DirInfo
{
    const int MAX_PATH = 260;
    int _currIndex;
    bool _entriesFilled;
    readonly List<DirEntry> _entries = new();

    public void Reset()
    {
        _currIndex = 0;
        _entriesFilled = false;
        _entries.Clear();
    }

    public bool EntriesFilled => _entriesFilled;

    public bool CurrentIsValid => _currIndex < _entries.Count;

    public PRJ_FILE_BASIC_INFO CurrentBasicInfo() => new()
    {
        IsDirectory = _entries[_currIndex].IsDirectory,
        FileSize = _entries[_currIndex].FileSize
    };

    public string CurrentFileName => _entries[_currIndex].FileName;

    public bool MoveNext()
    {
        _currIndex++;
        return CurrentIsValid;
    }

    public void FillDirEntry(string dirName)
    {
        FillItemEntry(dirName, 0, true);
    }

    public void FillFileEntry(string fileName, long fileSize)
    {
        FillItemEntry(fileName, fileSize, false);
    }

    public void FillItemEntry(string fileName, long fileSize, bool isDirectory)
    {
        if (fileName.Length > MAX_PATH || PathUtils.ContainsInvalidFileNameChar(fileName))
        {
            return;
        }

        DirEntry entry = new()
        {
            FileName = fileName,
            IsDirectory = isDirectory,
            FileSize = fileSize
        };

        _entries.Add(entry);
    }

    public void SortEntriesAndMarkFilled()
    {
        _entriesFilled = true;
        _entries.Sort(DirInfoComparer.Default);
    }
}