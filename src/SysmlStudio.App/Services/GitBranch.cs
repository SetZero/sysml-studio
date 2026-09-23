namespace SysmlStudio.App.Services;

/// <summary>
/// The branch the model folder's repository has checked out, read straight
/// from .git/HEAD. The status bar shows it; nothing else needs git.
/// </summary>
public static class GitBranch
{
    public static string? Of(string folder)
    {
        for (var dir = new DirectoryInfo(folder); dir is not null; dir = dir.Parent)
        {
            var git = System.IO.Path.Combine(dir.FullName, ".git");
            var head = Directory.Exists(git) ? System.IO.Path.Combine(git, "HEAD") : ReadWorktreeHead(git);
            if (head is null || !File.Exists(head))
                continue;

            var text = File.ReadAllText(head).Trim();
            const string prefix = "ref: refs/heads/";
            return text.StartsWith(prefix, StringComparison.Ordinal) ? text[prefix.Length..] : text[..Math.Min(8, text.Length)];
        }

        return null;
    }

    /// <summary>A worktree's .git is a file naming the real git directory.</summary>
    private static string? ReadWorktreeHead(string gitFile)
    {
        if (!File.Exists(gitFile))
            return null;

        var line = File.ReadAllText(gitFile).Trim();
        const string prefix = "gitdir: ";
        return line.StartsWith(prefix, StringComparison.Ordinal)
            ? System.IO.Path.Combine(line[prefix.Length..], "HEAD")
            : null;
    }
}
