using System.Reflection;
using Microsoft.Data.Sqlite;

namespace Zara.Storage;

/// <inheritdoc cref="IMigrationRunner"/>
public sealed class MigrationRunner : IMigrationRunner
{
    private readonly IReadOnlyList<Migration> _migrations;

    public MigrationRunner()
    {
        _migrations = LoadEmbeddedMigrations(typeof(MigrationRunner).Assembly);
    }

    public void MigrateToLatest(SqliteConnection connection)
    {
        long current = GetUserVersion(connection);

        foreach (var migration in _migrations.Where(m => m.Version > current).OrderBy(m => m.Version))
        {
            using var transaction = connection.BeginTransaction();

            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = migration.Sql;
                cmd.ExecuteNonQuery();
            }

            // PRAGMA statements don't accept bound parameters; the version
            // number comes from our own embedded, trusted filenames — never
            // from external input — so string interpolation here is safe.
            using (var versionCmd = connection.CreateCommand())
            {
                versionCmd.Transaction = transaction;
                versionCmd.CommandText = $"PRAGMA user_version = {migration.Version};";
                versionCmd.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    private static long GetUserVersion(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>
    /// Discovers migrations from files embedded via the
    /// <c>Migrations\*.sql</c> item group in the csproj. Files must be named
    /// <c>NNN_description.sql</c>; NNN becomes the user_version this
    /// migration advances the database to. Non-matching embedded resources
    /// are ignored rather than treated as an error, so this assembly can gain
    /// other embedded resources later without breaking migration discovery.
    /// </summary>
    private static List<Migration> LoadEmbeddedMigrations(Assembly assembly)
    {
        var migrations = new List<Migration>();

        foreach (string resourceName in assembly.GetManifestResourceNames())
        {
            string[] parts = resourceName.Split('.');
            if (parts.Length < 2)
            {
                continue;
            }

            string extension = parts[^1];
            string nameWithoutExtension = parts[^2];

            if (!string.Equals(extension, "sql", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int underscoreIndex = nameWithoutExtension.IndexOf('_');
            if (underscoreIndex <= 0 || !int.TryParse(nameWithoutExtension[..underscoreIndex], out int version))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' vanished mid-load.");
            using var reader = new StreamReader(stream);

            migrations.Add(new Migration(version, $"{nameWithoutExtension}.{extension}", reader.ReadToEnd()));
        }

        var duplicate = migrations.GroupBy(m => m.Version).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Multiple embedded migrations claim version {duplicate.Key}: " +
                string.Join(", ", duplicate.Select(m => m.Name)));
        }

        return migrations;
    }

    private sealed record Migration(int Version, string Name, string Sql);
}
