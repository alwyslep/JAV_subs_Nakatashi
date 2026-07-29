using Microsoft.Data.Sqlite;
using System;

namespace Nikse.SubtitleEdit.Logic.JavData;

/// <summary>
/// Fork addition. Opens the shared catalogues.
///
/// ★These files belong to another program that may be running right now. Three rules follow, and
///   every store in this folder keeps them:
///   ①read-only unless the user is saving something, ②WAL so a reader never blocks the writer,
///   ③short statements - no transaction is held open across UI work.
///
/// ★Fail-soft by construction: a missing drive, a locked file or a table that has not been
///   migrated yet all mean "no data", never an exception the caller has to think about.
/// </summary>
internal static class JavDb
{
    /// <summary>How long to wait for the translator to finish a write before giving up.</summary>
    private const int BusyTimeoutSeconds = 5;

    internal static SqliteConnection? OpenRead(string path)
        => Open(path, SqliteOpenMode.ReadOnly);

    internal static SqliteConnection? OpenWrite(string path)
        => Open(path, SqliteOpenMode.ReadWrite);

    private static SqliteConnection? Open(string path, SqliteOpenMode mode)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        SqliteConnection? connection = null;
        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = mode,

                // ★Private, not Shared. Shared cache serialises readers against each other inside
                //   this process for no gain here, and interacts badly with WAL.
                Cache = SqliteCacheMode.Private,
                DefaultTimeout = BusyTimeoutSeconds,
            }.ToString();

            connection = new SqliteConnection(connectionString);
            connection.Open();
            return connection;
        }
        catch
        {
            connection?.Dispose();
            return null;
        }
    }

    /// <summary>
    /// True when <paramref name="table"/> exists. ★Called before every read: the guidebook table
    /// only appears once the translator side has run its migration, and until then this fork must
    /// behave exactly as it did before rather than reporting an error the user cannot act on.
    /// </summary>
    internal static bool HasTable(SqliteConnection connection, string table)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "select 1 from sqlite_master where type='table' and name=$name limit 1";
            command.Parameters.AddWithValue("$name", table);
            return command.ExecuteScalar() != null;
        }
        catch
        {
            return false;
        }
    }

    internal static string GetText(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);

    internal static bool GetBool(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return false;
        }

        try
        {
            return reader.GetInt64(ordinal) != 0;
        }
        catch (InvalidCastException)
        {
            // SQLite columns are loosely typed; a text "1" is possible from a hand edit.
            return string.Equals(reader.GetString(ordinal), "1", StringComparison.Ordinal);
        }
    }
}
