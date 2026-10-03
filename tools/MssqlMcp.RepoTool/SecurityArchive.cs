using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;

namespace MssqlMcp.RepoTool;

internal static class SecurityArchive
{
    internal static void VerifySha256(string archive, string expected, string description)
    {
        using var stream = File.OpenRead(archive);
        if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new SecurityPolicyException($"SHA-256 verification failed for {description}");
    }

    internal static void ExtractExecutable(Stream tar, string memberName, string target)
    {
        using var reader = new TarReader(tar, leaveOpen: true);
        var found = false;
        while (reader.GetNextEntry() is { } entry)
        {
            if (entry.Name != memberName) continue;
            if (found || entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.DataStream is null)
                throw new SecurityPolicyException("Invalid executable archive member");
            using var output = File.Create(target);
            entry.DataStream.CopyTo(output);
            found = true;
        }
        if (!found) throw new SecurityPolicyException("Missing executable archive member");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    internal static string ContainedPath(string root, string name)
    {
        // Tar paths are POSIX paths even on Windows. Reject drive/UNC interpretations too.
        if (string.IsNullOrEmpty(name) || name.Contains('\\') || name.Contains(':') || name.StartsWith('/'))
            throw new SecurityPolicyException("Archive path escapes its destination");
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(root, name)));
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (target != prefix.TrimEnd(Path.DirectorySeparatorChar)
            && !target.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new SecurityPolicyException("Archive path escapes its destination");
        return target;
    }

    internal static void ExtractSource(Stream tar, string destination)
    {
        destination = Path.GetFullPath(destination);
        if (Directory.Exists(destination) && ((File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0
            || Directory.EnumerateFileSystemEntries(destination).Any()))
            throw new SecurityPolicyException("Source extraction requires an empty non-link destination");
        Directory.CreateDirectory(destination);
        var regular = new HashSet<string>(StringComparer.Ordinal);
        var links = new Dictionary<string, (string Target, string Name, bool Symbolic)>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        using var reader = new TarReader(tar, leaveOpen: true);
        while (reader.GetNextEntry() is { } entry)
        {
            if (entry.EntryType is TarEntryType.GlobalExtendedAttributes or TarEntryType.ExtendedAttributes) continue;
            var path = ContainedPath(destination, entry.Name);
            if (!paths.Add(path)) throw new SecurityPolicyException("Duplicate source archive member");
            switch (entry.EntryType)
            {
                case TarEntryType.Directory:
                    Directory.CreateDirectory(path);
                    break;
                case TarEntryType.RegularFile:
                case TarEntryType.V7RegularFile:
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                        entry.DataStream?.CopyTo(output);
                    if (!OperatingSystem.IsWindows())
                        File.SetUnixFileMode(path, entry.Mode & (UnixFileMode)0x1ed); // data-filter permissions (0755).
                    regular.Add(path);
                    break;
                case TarEntryType.SymbolicLink:
                case TarEntryType.HardLink:
                    var symbolic = entry.EntryType == TarEntryType.SymbolicLink;
                    var relative = symbolic ? Path.GetRelativePath(destination, Path.GetDirectoryName(path)!) + "/" + entry.LinkName : entry.LinkName;
                    if (Path.IsPathRooted(entry.LinkName)) throw new SecurityPolicyException("Archive link escapes its destination");
                    links.Add(path, (ContainedPath(destination, relative), entry.LinkName, symbolic));
                    break;
                default:
                    throw new SecurityPolicyException("Non-data source archive member");
            }
        }
        // No writes may traverse a link, regardless of entry order. Defer link creation
        // until every regular member is materialized and all link targets are checked.
        foreach (var path in paths)
        {
            var ancestor = Path.GetDirectoryName(path);
            while (ancestor is not null && ancestor != destination)
            {
                if (links.ContainsKey(ancestor)) throw new SecurityPolicyException("Archive member traverses a link");
                ancestor = Path.GetDirectoryName(ancestor);
            }
        }
        foreach (var (path, link) in links)
        {
            ResolveLinkTarget(destination, link.Symbolic ? Path.GetDirectoryName(path)! : destination,
                link.Name, links, new HashSet<string>(StringComparer.Ordinal) { path });
            if (!link.Symbolic && !regular.Contains(link.Target))
                throw new SecurityPolicyException("Archive hard link does not refer to a regular member");
        }
        foreach (var (path, link) in links)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (link.Symbolic)
            {
                if (Directory.Exists(link.Target)) Directory.CreateSymbolicLink(path, link.Name);
                else File.CreateSymbolicLink(path, link.Name);
            }
        }
        if (links.Values.Any(link => !link.Symbolic))
        {
            // TarEntry.ExtractToFile cannot create hard links; the framework directory
            // extractor can. Supply only validated link metadata, never archive paths.
            using var hardLinks = new MemoryStream();
            using (var writer = new TarWriter(hardLinks, leaveOpen: true))
                foreach (var (path, link) in links)
                    if (!link.Symbolic)
                        writer.WriteEntry(new PaxTarEntry(TarEntryType.HardLink, Path.GetRelativePath(destination, path).Replace('\\', '/'))
                        {
                            LinkName = Path.GetRelativePath(destination, link.Target).Replace('\\', '/')
                        });
            hardLinks.Position = 0;
            TarFile.ExtractToDirectory(hardLinks, destination, overwriteFiles: false);
        }
    }

    private static string ResolveLinkTarget(string root, string start, string name,
        Dictionary<string, (string Target, string Name, bool Symbolic)> links, HashSet<string> seen)
    {
        var current = start;
        foreach (var component in name.Split('/'))
        {
            if (component is "" or ".") continue;
            current = ContainedPath(root, Path.GetRelativePath(root, current) + "/" + component);
            if (links.TryGetValue(current, out var link))
            {
                if (!seen.Add(current)) throw new SecurityPolicyException("Cyclic archive link");
                current = ResolveLinkTarget(root, link.Symbolic ? Path.GetDirectoryName(current)! : root,
                    link.Name, links, seen);
            }
        }
        return current;
    }

    internal static Stream OpenGzip(string path) => new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
}
