using Microsoft.Data.Sqlite;
using System;
using System.Globalization;

namespace Nikse.SubtitleEdit.Logic.JavData;

/// <summary>
/// One film's translation guidebook - who speaks to whom and at what speech level.
/// ★<see cref="Text"/> is deliberately unparsed. Measured over the 42 guidebooks on disk, only
///   five use an arrow notation and the other 37 are prose; a parser would work on one in eight
///   and quietly mangle the rest. Prose is a perfectly good prompt.
/// </summary>
public sealed class JavGuidebookEntry
{
    public string Code { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;

    /// <summary>Set when a human fixed this. ★A machine pass must never overwrite it.</summary>
    public bool Pinned { get; init; }

    /// <summary>pinned | prescan | stored | user | se</summary>
    public string Source { get; init; } = string.Empty;

    public string Updated { get; init; } = string.Empty;
    public string UpdatedBy { get; init; } = string.Empty;

    public bool IsEmpty => Text.Length == 0;
}

/// <summary>
/// Fork addition. Reads and writes the per-film guidebooks in the shared register catalogue.
///
/// ★Why this matters: the speech-level pass has to be told who is speaking to whom, and today the
///   user types that in by hand into a field that is stored globally and leaks into the next film.
///   The translator already derived exactly this text for every film it processed. Reading it is
///   the difference between a blank box and a filled one.
///
/// ★Writing back is the other half: when the user corrects the note here, the next translation run
///   picks it up, because <see cref="Save"/> pins the row and the translator's prescan is forbidden
///   from overwriting a pin.
/// </summary>
public static class JavGuidebook
{
    internal const string TableName = "guidebook";

    /// <summary>
    /// ★Kept identical to the translator's <c>guidebook.py</c> cap. The text is injected into every
    ///   batch's system prompt, so an unbounded note is a token leak on every request.
    /// </summary>
    public const int MaxTextLength = 4000;

    /// <summary>Marks rows this editor wrote, so the two sides stay tellable apart in the data.</summary>
    public const string WrittenBy = "nakatashi";

    /// <summary>
    /// The guidebook for <paramref name="code"/>, or null when there is none - which is the normal
    /// case for a film the translator has not processed.
    /// </summary>
    public static JavGuidebookEntry? Load(string? code)
    {
        var key = JavDataPaths.NormalizeCode(code);
        if (key.Length == 0)
        {
            return null;
        }

        using var connection = JavDb.OpenRead(JavDataPaths.RegisterDb);
        if (connection == null || !JavDb.HasTable(connection, TableName))
        {
            return null;
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "select text, pinned, source, updated, updated_by from " + TableName + " where code=$code limit 1";
            command.Parameters.AddWithValue("$code", key);

            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            var text = JavDb.GetText(reader, 0).Trim();
            if (text.Length == 0)
            {
                return null;
            }

            return new JavGuidebookEntry
            {
                Code = key,
                Text = text,
                Pinned = JavDb.GetBool(reader, 1),
                Source = JavDb.GetText(reader, 2),
                Updated = JavDb.GetText(reader, 3),
                UpdatedBy = JavDb.GetText(reader, 4),
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Stores the user's guidebook for <paramref name="code"/> and pins it. Returns false when the
    /// catalogue is unreachable, writing is switched off, or the table has not been migrated yet.
    ///
    /// ★Always pins. This is only ever called from an explicit save, and an unpinned row would be
    ///   replaced by the next prescan - which is precisely the loss this feature exists to prevent.
    /// </summary>
    public static bool Save(string? code, string? text)
    {
        var key = JavDataPaths.NormalizeCode(code);
        var body = (text ?? string.Empty).Trim();
        if (key.Length == 0 || body.Length == 0 || !JavDataPaths.CanWriteRegisterDb)
        {
            return false;
        }

        if (body.Length > MaxTextLength)
        {
            body = body[..MaxTextLength];
        }

        using var connection = JavDb.OpenWrite(JavDataPaths.RegisterDb);
        if (connection == null)
        {
            return false;
        }

        try
        {
            EnsureTable(connection);

            using var command = connection.CreateCommand();
            command.CommandText =
                "insert into " + TableName + "(code, text, pinned, source, updated, updated_by) " +
                "values($code, $text, 1, 'user', $updated, $by) " +
                "on conflict(code) do update set " +
                "text=excluded.text, pinned=1, source='user', updated=excluded.updated, updated_by=excluded.updated_by";
            command.Parameters.AddWithValue("$code", key);
            command.Parameters.AddWithValue("$text", body);
            command.Parameters.AddWithValue("$updated", Timestamp());
            command.Parameters.AddWithValue("$by", WrittenBy);
            command.ExecuteNonQuery();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// ★Same shape the translator's migration creates, so whichever side runs first wins and the
    ///   other finds the table already there. <c>if not exists</c> makes that a non-event.
    /// </summary>
    internal static void EnsureTable(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "create table if not exists " + TableName + "(" +
            "  code       text primary key," +
            "  text       text not null," +
            "  pinned     integer not null default 0," +
            "  source     text not null default 'prescan'," +
            "  updated    text not null," +
            "  updated_by text not null default ''" +
            ")";
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// ★Local time in the translator's exact format ("%Y-%m-%d %H:%M:%S"). The two programs write
    ///   into the same column, and a row whose stamp does not sort with its neighbours is worse
    ///   than useless when the point is to see which side last touched a film.
    /// </summary>
    private static string Timestamp()
        => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
