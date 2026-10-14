using GW2MM.Core.Library;

namespace GW2MM.Windows.Interop;

/// <summary>Deletes to the Recycle Bin, as the original FileRecycle did.</summary>
internal sealed class RecycleBin : IFileRecycler
{
    public void Recycle(string path) =>
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
}
