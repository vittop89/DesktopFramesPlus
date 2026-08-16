using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Windows;

namespace Desktop_Frames
{
    /// <summary>
    /// Moves and copies real files in and out of Portal frames.
    ///
    /// A Portal mirrors a folder on disk, so everything here is an ordinary file
    /// operation: nothing needs to be written back to frames.json, and the
    /// FileSystemWatcher already running on the portal refreshes the view on its own.
    ///
    /// The same routines serve both the Paste command and the drop handler, so a
    /// file that arrives by dragging and a file that arrives by pasting are treated
    /// identically.
    /// </summary>
    internal static class PortalFileTransfer
    {
        /// <summary>DROPEFFECT_MOVE, the flag Explorer sets on the clipboard for a cut.</summary>
        private const int DropEffectMove = 2;

        /// <summary>True when the Windows clipboard currently holds at least one file or folder.</summary>
        public static bool ClipboardHasFiles()
        {
            try
            {
                return Clipboard.ContainsFileDropList() && Clipboard.GetFileDropList().Count > 0;
            }
            catch
            {
                // Another process can hold the clipboard open; treat that as "nothing to paste"
                // rather than letting the menu throw while it is being built.
                return false;
            }
        }

        /// <summary>
        /// Drops whatever the clipboard holds into targetFolder and returns how many items
        /// arrived. A cut moves and a copy copies, decided by the marker Explorer leaves on
        /// the clipboard, so pasting behaves the same whether the item was cut here or in
        /// any other Windows application.
        /// </summary>
        public static int PasteInto(string targetFolder)
        {
            List<string> sources = new List<string>();
            bool move;

            try
            {
                if (!ClipboardHasFiles()) return 0;
                foreach (string source in Clipboard.GetFileDropList())
                    if (!string.IsNullOrEmpty(source)) sources.Add(source);
                move = ClipboardHoldsACut();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Could not read the clipboard: {ex.Message}");
                return 0;
            }

            int transferred = Transfer(sources, targetFolder, move);

            // A cut is spent once it has been pasted, exactly as in Explorer. Leaving it in
            // place would let a second paste move files that are no longer where the
            // clipboard says they are.
            if (move && transferred > 0)
            {
                try { Clipboard.Clear(); } catch { }
            }

            return transferred;
        }

        /// <summary>
        /// True when the clipboard was filled by a cut rather than a copy. Explorer records
        /// this in a "Preferred DropEffect" stream, and Desktop Frames + writes the same
        /// marker from its own Cut command.
        /// </summary>
        private static bool ClipboardHoldsACut()
        {
            try
            {
                IDataObject data = Clipboard.GetDataObject();
                if (data == null || !data.GetDataPresent("Preferred DropEffect")) return false;

                if (data.GetData("Preferred DropEffect") is MemoryStream stream)
                {
                    byte[] flag = new byte[4];
                    stream.Position = 0;
                    if (stream.Read(flag, 0, 4) == 4)
                        return (BitConverter.ToInt32(flag, 0) & DropEffectMove) == DropEffectMove;
                }
            }
            catch { }

            return false;
        }

        /// <summary>
        /// Copies or moves every source into targetFolder, skipping the ones that cannot go
        /// there, and returns how many succeeded. Failures are reported per item so one bad
        /// file does not abandon the rest of the batch.
        /// </summary>
        public static int Transfer(IEnumerable<string> sources, string targetFolder, bool move)
        {
            if (sources == null || string.IsNullOrEmpty(targetFolder) || !Directory.Exists(targetFolder))
                return 0;

            int transferred = 0;

            foreach (string source in sources)
            {
                if (string.IsNullOrEmpty(source)) continue;

                try
                {
                    bool isFolder = Directory.Exists(source);
                    if (!isFolder && !File.Exists(source)) continue;

                    // Moving an item into the folder it already lives in would only produce a
                    // pointless "name (1)" duplicate, so it is quietly skipped. A copy is left
                    // alone: duplicating inside one folder is a legitimate thing to ask for.
                    string currentParent = Path.GetDirectoryName(source);
                    if (move && SamePath(currentParent, targetFolder)) continue;

                    // A folder cannot be dropped inside itself or inside one of its own
                    // children; the operation would either fail halfway or recurse forever.
                    if (isFolder && (SamePath(source, targetFolder) || IsInside(targetFolder, source)))
                    {
                        MessageBoxesManager.ShowOKOnlyMessageBoxForm(
                            $"'{Path.GetFileName(source)}' cannot be placed inside itself.", "Error");
                        continue;
                    }

                    string destination = FreeDestinationFor(source, targetFolder);

                    if (isFolder)
                    {
                        if (move) MoveDirectory(source, destination);
                        else BackupManager.CopyDirectory(source, destination);
                    }
                    else
                    {
                        if (move) File.Move(source, destination);
                        else File.Copy(source, destination, false);
                    }

                    transferred++;
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                        $"Could not transfer '{source}' to '{targetFolder}': {ex.Message}");
                    MessageBoxesManager.ShowOKOnlyMessageBoxForm(
                        $"Failed to transfer {Path.GetFileName(source)}: {ex.Message}", "Error");
                }
            }

            return transferred;
        }

        /// <summary>
        /// Directory.Move cannot cross volumes, so a move that spans two drives falls back to
        /// copying and then deleting the original. The delete only runs once the copy is done.
        /// </summary>
        private static void MoveDirectory(string source, string destination)
        {
            try
            {
                Directory.Move(source, destination);
            }
            catch (IOException)
            {
                BackupManager.CopyDirectory(source, destination);
                Directory.Delete(source, true);
            }
        }

        /// <summary>
        /// Returns a path inside targetFolder that nothing occupies yet, appending " (1)",
        /// " (2)" and so on. This matches the naming the existing drop handler already uses.
        /// </summary>
        public static string FreeDestinationFor(string source, string targetFolder)
        {
            string name = Path.GetFileName(source);
            string candidate = Path.Combine(targetFolder, name);

            string bareName = Path.GetFileNameWithoutExtension(source);
            string extension = Directory.Exists(source) ? string.Empty : Path.GetExtension(source);

            int counter = 1;
            while (File.Exists(candidate) || Directory.Exists(candidate))
                candidate = Path.Combine(targetFolder, $"{bareName} ({counter++}){extension}");

            return candidate;
        }

        /// <summary>Starts an ordinary Windows file drag, the kind Explorer accepts.</summary>
        public static void BeginDrag(DependencyObject source, string path)
        {
            if (source == null || string.IsNullOrEmpty(path)) return;
            if (!File.Exists(path) && !Directory.Exists(path)) return;

            try
            {
                StringCollection paths = new StringCollection { path };
                DataObject data = new DataObject();
                data.SetFileDropList(paths);

                // Both effects are offered so the drop target decides: dropping on another
                // portal moves, dropping on Explorer follows the usual Windows rules.
                DragDrop.DoDragDrop(source, data, DragDropEffects.Copy | DragDropEffects.Move);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Could not start dragging '{path}': {ex.Message}");
            }
        }

        private static bool SamePath(string first, string second)
        {
            if (string.IsNullOrEmpty(first) || string.IsNullOrEmpty(second)) return false;
            return string.Equals(Normalize(first), Normalize(second), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True when candidate sits somewhere under root.</summary>
        private static bool IsInside(string candidate, string root)
        {
            if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(root)) return false;
            string rootWithSeparator = Normalize(root) + Path.DirectorySeparatorChar;
            return Normalize(candidate).StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string path)
        {
            try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch { return path; }
        }
    }
}
