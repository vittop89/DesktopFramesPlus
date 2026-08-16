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
        /// The entry point for a drop, as opposed to a paste.
        ///
        /// Anything already sitting in the destination is dropped from the batch first:
        /// dragging an item around inside the folder it came from is how a user rearranges
        /// icons, and it must not leave a duplicate behind. Explorer does nothing in that
        /// case too. A paste is different and keeps its own behaviour, because asking to
        /// paste into the current folder is a deliberate request for a second copy.
        /// </summary>
        public static int Drop(IEnumerable<string> sources, string targetFolder, bool move)
        {
            if (sources == null || string.IsNullOrEmpty(targetFolder)) return 0;

            List<string> incoming = new List<string>();
            foreach (string source in sources)
            {
                if (string.IsNullOrEmpty(source)) continue;
                if (SamePath(Path.GetDirectoryName(source), targetFolder)) continue;
                incoming.Add(source);
            }

            return Transfer(incoming, targetFolder, move);
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

                    bool originalRemoved = true;

                    if (isFolder)
                    {
                        if (move) originalRemoved = MoveDirectory(source, destination);
                        else BackupManager.CopyDirectory(source, destination);
                    }
                    else
                    {
                        if (move) originalRemoved = MoveFile(source, destination);
                        else File.Copy(source, destination, false);
                    }

                    // The item is at the destination either way, so it counts as transferred.
                    // Saying so plainly matters: the earlier wording read like nothing had
                    // happened, and a second attempt then produced a second copy.
                    if (!originalRemoved)
                    {
                        MessageBoxesManager.ShowOKOnlyMessageBoxForm(
                            $"'{Path.GetFileName(source)}' was copied to the destination, but the original could not be removed. Nothing was lost. Delete the original by hand if you still want it gone.",
                            "Moved, original left behind");
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
        /// Moves a folder. Returns false when the copy arrived but the original could not be
        /// removed, which is a different outcome from failing altogether.
        ///
        /// Directory.Move cannot cross volumes, so a move onto another drive — a cloud folder
        /// especially — falls back to copying and then deleting. The delete is the fragile
        /// half: a sync client or a search indexer can hold the folder open for a moment, and
        /// read-only files refuse to go. Reporting that as a plain failure was worse than the
        /// failure itself, because the copy had in fact arrived, and retrying produced a
        /// second one.
        /// </summary>
        private static bool MoveDirectory(string source, string destination)
        {
            try
            {
                Directory.Move(source, destination);
                return true;
            }
            catch (IOException) { }                  // usually "not the same volume"
            catch (UnauthorizedAccessException) { }

            BackupManager.CopyDirectory(source, destination);

            // Nothing is deleted until the copy has been checked. Losing the original because
            // a half-written copy looked convincing is the one outcome with no way back.
            if (!ArrivedIntact(source, destination))
                throw new IOException("the copy is incomplete, so the original was left untouched");

            return TryDeleteTree(source);
        }

        /// <summary>
        /// Moves a single file, with the same reasoning as the folder version: if the copy is
        /// there but the original will not go, that is worth saying rather than calling the
        /// whole thing a failure.
        /// </summary>
        private static bool MoveFile(string source, string destination)
        {
            try
            {
                File.Move(source, destination);
                return true;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            File.Copy(source, destination, false);
            if (new FileInfo(destination).Length != new FileInfo(source).Length)
                throw new IOException("the copy is incomplete, so the original was left untouched");

            try
            {
                File.SetAttributes(source, FileAttributes.Normal);
                File.Delete(source);
                return true;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.UI,
                    $"Copied '{source}' but could not remove the original: {ex.Message}");
                return false;
            }
        }

        /// <summary>Every file under source exists under destination with the same length.</summary>
        private static bool ArrivedIntact(string source, string destination)
        {
            try
            {
                foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                {
                    string relative = file.Substring(source.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string copied = Path.Combine(destination, relative);

                    if (!File.Exists(copied)) return false;
                    if (new FileInfo(copied).Length != new FileInfo(file).Length) return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                    $"Could not verify the copy of '{source}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Deletes a folder and everything under it, clearing the read-only flag that would
        /// otherwise stop it and giving a held handle a moment to be released. Returns false
        /// rather than throwing: by this point the copy is already safe at the destination.
        /// </summary>
        private static bool TryDeleteTree(string folder)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                        File.SetAttributes(file, FileAttributes.Normal);

                    Directory.Delete(folder, true);
                    return true;
                }
                catch (Exception ex)
                {
                    if (attempt == 2)
                    {
                        LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.UI,
                            $"Copied '{folder}' but could not remove the original: {ex.Message}");
                        return false;
                    }
                    System.Threading.Thread.Sleep(200);
                }
            }

            return false;
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

        /// <summary>
        /// Private clipboard format that marks a drag as having started inside a Portal.
        /// Explorer and every other application ignore it, so it is only ever present when
        /// the app itself is the source. That is what lets a drag between two Portals move
        /// by default while a file arriving from outside is still copied.
        /// </summary>
        public const string PortalDragFormat = "DesktopFramesPlus.PortalDrag";

        /// <summary>True when this drag was started by dragging an item out of a Portal.</summary>
        public static bool StartedInsideAPortal(IDataObject data)
        {
            try { return data != null && data.GetDataPresent(PortalDragFormat); }
            catch { return false; }
        }

        /// <summary>
        /// Whether a drop into a Portal should move rather than copy.
        ///
        /// An item dragged out of another Portal is being relocated, so it moves unless Ctrl
        /// asks for a copy. Anything arriving from outside the app is copied, because taking
        /// a file away from Explorer or from the desktop is not what a drop implies there;
        /// Shift still asks for a move. Both the cursor feedback and the drop itself read
        /// this, so what the pointer promises is what happens.
        /// </summary>
        public static bool DropShouldMove(IDataObject data, DragDropKeyStates keys)
        {
            return StartedInsideAPortal(data)
                ? (keys & DragDropKeyStates.ControlKey) != DragDropKeyStates.ControlKey
                : (keys & DragDropKeyStates.ShiftKey) == DragDropKeyStates.ShiftKey;
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
                data.SetData(PortalDragFormat, true);

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
