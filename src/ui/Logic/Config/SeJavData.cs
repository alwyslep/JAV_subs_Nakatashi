namespace Nikse.SubtitleEdit.Logic.Config;

/// <summary>
/// Fork addition. Where the catalogues this editor shares with the sibling translator live.
///
/// ★Why these are settings and not constants: the two programs run from different folders, so
///   "next to the executable" - the portable rule this fork otherwise follows - cannot be a
///   shared location by construction. A fixed default that either side can override is the
///   compromise: it keeps working untouched on this machine, and a move (a different drive, a
///   USB stick) is one edit on each side rather than a rebuild.
///
/// ★Blank means "use <see cref="DefaultDataFolder"/>", not "disabled". Absent files are simply
///   skipped at read time - see <c>JavDataPaths</c> - so a machine without the drive mounted
///   loses the feature and nothing else.
/// </summary>
public class SeJavData
{
    /// <summary>
    /// Folder holding the shared catalogues. Blank uses <see cref="DefaultDataFolder"/>.
    /// The three paths below override this one file at a time when set.
    /// </summary>
    public string DataFolder { get; set; }

    /// <summary>Two-layer glossary (global + series). Source of name spellings.</summary>
    public string TermsDbPath { get; set; }

    /// <summary>Speech-level metrics, and - after the migration - the per-film guidebooks.</summary>
    public string RegisterDbPath { get; set; }

    /// <summary>Catalogue keyed by release code; fills in what a video's own tags omit.</summary>
    public string CatalogDbPath { get; set; }

    /// <summary>
    /// Whether the editor may write back what the user corrects.
    /// ★On by default. Writing here is never a background action - it happens only when the user
    ///   has edited a note or a spelling and saved it, so every write is already an explicit
    ///   choice. Defaulting this off would mean the accumulation this fork exists to enable is
    ///   silently missing until someone finds a checkbox. The flag remains as an escape hatch.
    /// </summary>
    public bool AllowWrite { get; set; }

    /// <summary>
    /// ★The translator's current hard-coded home (<c>termdb.DB_PATH</c>, <c>regdb.DB_PATH</c>,
    ///   <c>meta.QUEUE_DB</c>). Keeping it as the default means this fork reads the existing
    ///   catalogues with no configuration at all on the machine they were built on.
    /// </summary>
    public const string DefaultDataFolder = @"D:\";

    public const string TermsDbFileName = "jav-terms.sqlite";
    public const string RegisterDbFileName = "jav-register.sqlite";
    public const string CatalogDbFileName = "queue.sqlite";

    public SeJavData()
    {
        DataFolder = string.Empty;
        TermsDbPath = string.Empty;
        RegisterDbPath = string.Empty;
        CatalogDbPath = string.Empty;
        AllowWrite = true;
    }
}
