using System.Text.Json;
using JapaneseLearningAssistant.Core.Models;
using Microsoft.Data.Sqlite;

namespace JapaneseLearningAssistant.Core.Services;

public sealed class HistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _databasePath;
    private readonly string _historyFilePath;
    private readonly string _connectionString;

    public HistoryStore(string appDataDirectory)
    {
        Directory.CreateDirectory(appDataDirectory);
        _databasePath = Path.Combine(appDataDirectory, "history.db");
        _historyFilePath = Path.Combine(appDataDirectory, "history.json");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath
        }.ToString();

        EnsureDatabase();
        MigrateLegacyJsonIfNeeded();
    }

    public async Task SaveAsync(HistoryEntry entry, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(entry.Id))
        {
            entry.Id = Guid.NewGuid().ToString("N");
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        await UpsertHistoryEntryAsync(connection, transaction, entry, cancellationToken);
        await ReplaceSentenceItemsAsync(connection, transaction, entry, cancellationToken);
        await TrimHistoryAsync(connection, transaction, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<List<HistoryEntry>> LoadAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, created_at, original_text, summary, result_json, provider, model, audio_file_path
            FROM analysis_history
            ORDER BY created_at DESC
            LIMIT 100;
            """;

        var entries = new List<HistoryEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            try
            {
                var resultJson = reader.GetString(4);
                var result = JsonSerializer.Deserialize<JapaneseAnalysisResult>(resultJson, JsonOptions) ?? new JapaneseAnalysisResult();
                entries.Add(new HistoryEntry
                {
                    Id = reader.GetString(0),
                    CreatedAt = DateTimeOffset.Parse(reader.GetString(1)),
                    OriginalText = reader.GetString(2),
                    AnalysisProvider = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    AnalysisModel = reader.IsDBNull(6) ? "" : reader.GetString(6),
                    AnalysisResult = result,
                    AudioFilePath = reader.IsDBNull(7) ? null : reader.GetString(7)
                });
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"Failed to load a history row from SQLite: {ex.Message}");
            }
        }

        return entries;
    }

    private void EnsureDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        ConfigureConnection(connection);

        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS analysis_history (
              id TEXT PRIMARY KEY,
              created_at TEXT NOT NULL,
              original_text TEXT NOT NULL,
              summary TEXT,
              result_json TEXT NOT NULL,
              provider TEXT,
              model TEXT,
              audio_file_path TEXT
            );

            CREATE TABLE IF NOT EXISTS sentence_items (
              id TEXT PRIMARY KEY,
              history_id TEXT NOT NULL REFERENCES analysis_history(id) ON DELETE CASCADE,
              sentence_index INTEGER NOT NULL,
              text TEXT NOT NULL,
              is_favorite INTEGER NOT NULL DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS idx_analysis_history_created_at
              ON analysis_history(created_at DESC);

            CREATE INDEX IF NOT EXISTS idx_sentence_items_history_id
              ON sentence_items(history_id, sentence_index);
            """;
        command.ExecuteNonQuery();
    }

    private void MigrateLegacyJsonIfNeeded()
    {
        if (!File.Exists(_historyFilePath) || HasAnyHistoryRows())
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_historyFilePath);
            var entries = JsonSerializer.Deserialize<List<HistoryEntry>>(json, JsonOptions) ?? [];
            if (entries.Count == 0)
            {
                return;
            }

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            ConfigureConnection(connection);
            using var transaction = connection.BeginTransaction();
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Id))
                {
                    entry.Id = Guid.NewGuid().ToString("N");
                }

                UpsertHistoryEntry(connection, transaction, entry);
                ReplaceSentenceItems(connection, transaction, entry);
            }

            transaction.Commit();
            AppLogger.Info($"Migrated legacy JSON history to SQLite. Count={entries.Count}, Database={_databasePath}");
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to migrate legacy JSON history to SQLite.");
        }
    }

    private bool HasAnyHistoryRows()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        ConfigureConnection(connection);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM analysis_history LIMIT 1);";
        return Convert.ToInt32(command.ExecuteScalar()) == 1;
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        ConfigureConnection(connection);
        return connection;
    }

    private static void ConfigureConnection(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;
            PRAGMA journal_mode = WAL;
            """;
        command.ExecuteNonQuery();
    }

    private static async Task UpsertHistoryEntryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        HistoryEntry entry,
        CancellationToken cancellationToken)
    {
        await using var command = CreateUpsertHistoryCommand(connection, transaction, entry);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void UpsertHistoryEntry(SqliteConnection connection, SqliteTransaction transaction, HistoryEntry entry)
    {
        using var command = CreateUpsertHistoryCommand(connection, transaction, entry);
        command.ExecuteNonQuery();
    }

    private static SqliteCommand CreateUpsertHistoryCommand(
        SqliteConnection connection,
        SqliteTransaction transaction,
        HistoryEntry entry)
    {
        var resultJson = JsonSerializer.Serialize(entry.AnalysisResult, JsonOptions);
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO analysis_history (
              id, created_at, original_text, summary, result_json, provider, model, audio_file_path
            )
            VALUES (
              $id, $created_at, $original_text, $summary, $result_json, $provider, $model, $audio_file_path
            )
            ON CONFLICT(id) DO UPDATE SET
              created_at = excluded.created_at,
              original_text = excluded.original_text,
              summary = excluded.summary,
              result_json = excluded.result_json,
              provider = excluded.provider,
              model = excluded.model,
              audio_file_path = excluded.audio_file_path;
            """;
        command.Parameters.AddWithValue("$id", entry.Id);
        command.Parameters.AddWithValue("$created_at", entry.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$original_text", entry.OriginalText);
        command.Parameters.AddWithValue("$summary", entry.AnalysisResult.SummaryZh);
        command.Parameters.AddWithValue("$result_json", resultJson);
        command.Parameters.AddWithValue("$provider", entry.AnalysisProvider);
        command.Parameters.AddWithValue("$model", entry.AnalysisModel);
        command.Parameters.AddWithValue("$audio_file_path", (object?)entry.AudioFilePath ?? DBNull.Value);
        return command;
    }

    private static async Task ReplaceSentenceItemsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        HistoryEntry entry,
        CancellationToken cancellationToken)
    {
        await using var deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = transaction;
        deleteCommand.CommandText = "DELETE FROM sentence_items WHERE history_id = $history_id;";
        deleteCommand.Parameters.AddWithValue("$history_id", entry.Id);
        await deleteCommand.ExecuteNonQueryAsync(cancellationToken);

        var sentences = SentenceSplitter.Split(GetSentenceSourceText(entry.AnalysisResult));
        for (var i = 0; i < sentences.Count; i++)
        {
            await using var insertCommand = CreateInsertSentenceCommand(connection, transaction, entry.Id, i, sentences[i]);
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static void ReplaceSentenceItems(SqliteConnection connection, SqliteTransaction transaction, HistoryEntry entry)
    {
        using var deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = transaction;
        deleteCommand.CommandText = "DELETE FROM sentence_items WHERE history_id = $history_id;";
        deleteCommand.Parameters.AddWithValue("$history_id", entry.Id);
        deleteCommand.ExecuteNonQuery();

        var sentences = SentenceSplitter.Split(GetSentenceSourceText(entry.AnalysisResult));
        for (var i = 0; i < sentences.Count; i++)
        {
            using var insertCommand = CreateInsertSentenceCommand(connection, transaction, entry.Id, i, sentences[i]);
            insertCommand.ExecuteNonQuery();
        }
    }

    private static SqliteCommand CreateInsertSentenceCommand(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string historyId,
        int sentenceIndex,
        string text)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO sentence_items (id, history_id, sentence_index, text, is_favorite)
            VALUES ($id, $history_id, $sentence_index, $text, 0);
            """;
        command.Parameters.AddWithValue("$id", $"{historyId}-{sentenceIndex:D4}");
        command.Parameters.AddWithValue("$history_id", historyId);
        command.Parameters.AddWithValue("$sentence_index", sentenceIndex);
        command.Parameters.AddWithValue("$text", text);
        return command;
    }

    private static async Task TrimHistoryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = transaction;
        deleteCommand.CommandText = """
            DELETE FROM analysis_history
            WHERE id NOT IN (
              SELECT id FROM analysis_history
              ORDER BY created_at DESC
              LIMIT 100
            );
            """;
        await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string GetSentenceSourceText(JapaneseAnalysisResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.ReadingOptimizedJapanese))
        {
            return result.ReadingOptimizedJapanese;
        }

        if (!string.IsNullOrWhiteSpace(result.NaturalJapanese))
        {
            return result.NaturalJapanese;
        }

        if (!string.IsNullOrWhiteSpace(result.CorrectedJapanese))
        {
            return result.CorrectedJapanese;
        }

        return result.TranslatedJapanese;
    }
}

public sealed class HistoryEntry
{
    public string Id { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public string OriginalText { get; set; } = "";
    public string AnalysisProvider { get; set; } = "";
    public string AnalysisModel { get; set; } = "";
    public JapaneseAnalysisResult AnalysisResult { get; set; } = new();
    public string? AudioFilePath { get; set; }
}
