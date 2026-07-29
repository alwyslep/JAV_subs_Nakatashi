using Nikse.SubtitleEdit.Logic.Config;
using System;
using System.IO;
using System.Text;

namespace Nikse.SubtitleEdit.Logic.JavData;

/// <summary>
/// Fork addition. Resolves the three SQLite catalogues shared with the sibling translator project
/// and normalises the release code they are keyed by.
///
/// ★Nothing here touches the database. It answers "where would it be" and "does it exist", so the
///   callers can decide without a try/catch around a connection.
/// </summary>
public static class JavDataPaths
{
    /// <summary>Two-layer glossary - global plus per-series - written by the translator's term pass.</summary>
    public static string TermsDb => Resolve(Se.Settings.JavData.TermsDbPath, SeJavData.TermsDbFileName);

    /// <summary>Speech-level metrics, and the per-film guidebooks after they are migrated in.</summary>
    public static string RegisterDb => Resolve(Se.Settings.JavData.RegisterDbPath, SeJavData.RegisterDbFileName);

    /// <summary>Release-code catalogue; fills in what a video's own tags leave out.</summary>
    public static string CatalogDb => Resolve(Se.Settings.JavData.CatalogDbPath, SeJavData.CatalogDbFileName);

    public static bool HasTermsDb => Exists(TermsDb);
    public static bool HasRegisterDb => Exists(RegisterDb);
    public static bool HasCatalogDb => Exists(CatalogDb);

    /// <summary>Whether writing back is both configured and possible.</summary>
    public static bool CanWriteRegisterDb => Se.Settings.JavData.AllowWrite && HasRegisterDb;

    public static bool CanWriteTermsDb => Se.Settings.JavData.AllowWrite && HasTermsDb;

    /// <summary>
    /// An explicit per-file path wins; otherwise the file sits in the configured folder, and if
    /// that is blank too, in the translator's own default. ★An explicit path is used exactly as
    /// given even when it does not exist - silently substituting the default would hide a typo.
    /// </summary>
    private static string Resolve(string configuredPath, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return configuredPath.Trim();
        }

        var folder = Se.Settings.JavData.DataFolder;
        if (string.IsNullOrWhiteSpace(folder))
        {
            folder = SeJavData.DefaultDataFolder;
        }

        try
        {
            return Path.Combine(folder.Trim(), fileName);
        }
        catch (ArgumentException)
        {
            // Invalid characters in a hand-edited setting - treat as "not configured".
            return string.Empty;
        }
    }

    private static bool Exists(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            return File.Exists(path);
        }
        catch
        {
            // An unmounted drive or a denied path is simply "no".
            return false;
        }
    }

    /// <summary>
    /// Normalises a release code into the key the shared catalogues use.
    ///
    /// ★This must stay byte-identical to the translator's <c>guidebook.py::_key()</c>:
    ///   non <c>[A-Za-z0-9._-]</c> becomes <c>_</c>, leading dots are stripped, and the result is
    ///   capped at 80 characters. The two programs look each other's rows up by this string, so a
    ///   difference does not raise an error - it silently finds nothing, which is far worse.
    ///   Verified against the 42 guidebooks on disk: every key matched the catalogue.
    /// </summary>
    public static string NormalizeCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return string.Empty;
        }

        var trimmed = code.Trim();
        var sb = new StringBuilder(trimmed.Length);
        foreach (var c in trimmed)
        {
            var keep = c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-';
            sb.Append(keep ? c : '_');
        }

        var key = sb.ToString().TrimStart('.');
        return key.Length > 80 ? key[..80] : key;
    }

    /// <summary>
    /// The series prefix the glossary's series layer is keyed by - "ABF" out of "ABF-108".
    /// ★Returns an empty string rather than the whole code when there is no separator, because
    ///   the global layer is addressed by an empty series and must not be hit by accident.
    /// </summary>
    public static string SeriesPrefix(string? code)
    {
        var key = NormalizeCode(code);
        var cut = key.IndexOf('-');
        return cut > 0 ? key[..cut] : string.Empty;
    }
}
