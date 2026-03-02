/// <summary>
/// Holds all runtime configuration values read from appsettings.json, environment variables,
/// or command-line arguments.
/// </summary>

class ExportConfig
{
    /// <summary>
    /// Gets or sets the ConnectionString
    /// </summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>
    /// Gets or sets the OutputDir
    /// </summary>
    public string OutputDir { get; set; } = $"export_sql_{DateTime.Now:yyyy-MM-dd HH:mm:ss}";

    /// <summary>
    /// Gets or sets a value indicating whether QuoteIdentifiers
    /// </summary>
    // When true, all column and table identifiers are wrapped in double quotes,
    // preserving case and allowing reserved words to be used as names.
    public bool QuoteIdentifiers { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether OneFilePerTable
    /// </summary>
    // When true, each table produces its own .sql file; otherwise all tables go into one file.
    public bool OneFilePerTable { get; set; } = true;

    /// <summary>
    /// Gets or sets the Tables
    /// </summary>
    // List of table names to export. Supports "TABLENAME" or "SCHEMA.TABLENAME" format.
    public List<string> Tables { get; set; } = new();

    /// <summary>
    /// Gets or sets the WhereByTable
    /// </summary>
    // Optional WHERE clause per table (e.g. "WHERE STATUS = 'ACTIVE'") to filter exported rows.
    // Keys are case-insensitive table names.
    public Dictionary<string, string> WhereByTable { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the OrderByByTable
    /// </summary>
    // Optional ORDER BY clause per table to control the row order in the output file.
    // Keys are case-insensitive table names.
    public Dictionary<string, string> OrderByByTable { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the CommitEveryRowsComment
    /// </summary>
    // A commented-out "-- COMMIT;" marker is written every N rows as a reminder/checkpoint.
    // Set to 0 to disable intermediate commit markers.
    public int CommitEveryRowsComment { get; set; } = 500;
}