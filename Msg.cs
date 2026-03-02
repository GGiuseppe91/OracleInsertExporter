using System.Globalization;

/// <summary>
/// Provides localised log/error messages based on the OS UI culture.
/// Supported languages: Italian (it), English (everything else).
/// The language is resolved once at class initialisation and stays fixed for the process lifetime.
/// </summary>
static class Msg
{
    // Detect the OS UI language once; fall back to English for any non-Italian locale.
    private static readonly bool IsItalian =
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            .Equals("it", StringComparison.OrdinalIgnoreCase);

    // ── Connection / startup ─────────────────────────────────────────────
    public static string ConnectedSchema(string schema) => IsItalian
        ? $"Connesso a Oracle. Schema corrente: {schema}"
        : $"Connected to Oracle. Current schema: {schema}";

    public static string OutputDir(string path) => IsItalian
        ? $"Output: {path}"
        : $"Output directory: {path}";

    public static string ExportCompleted() => IsItalian
        ? "Esportazione completata."
        : "Export completed.";

    // ── Errors ───────────────────────────────────────────────────────────
    public static string MissingConnectionString() => IsItalian
        ? "ConnectionString mancante."
        : "ConnectionString is missing.";

    public static string NoTablesConfigured() => IsItalian
        ? "Nessuna tabella configurata."
        : "No tables configured.";

    public static string NoColumnsFound(string tableName) => IsItalian
        ? $"Nessuna colonna trovata per {tableName}. Controlla nome tabella/schema."
        : $"No columns found for {tableName}. Check the table name and schema.";

    public static string InvalidTableName(string tableName) => IsItalian
        ? $"Nome tabella non valido: '{tableName}'. Usa 'TABELLA' o 'SCHEMA.TABELLA'."
        : $"Invalid table name: '{tableName}'. Use 'TABLE' or 'SCHEMA.TABLE'.";

    // ── Progress ─────────────────────────────────────────────────────────
    public static string FileCreated(string filePath) => IsItalian
        ? $"Creato: {filePath}"
        : $"Created: {filePath}";

    public static string RowsExported(string tableName, int rowCount) => IsItalian
        ? $"{tableName}: righe esportate = {rowCount}"
        : $"{tableName}: exported rows = {rowCount}";
}