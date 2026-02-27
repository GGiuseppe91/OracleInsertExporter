using Microsoft.Extensions.Configuration;
using Oracle.ManagedDataAccess.Client;
using System.Data;
using System.Globalization;
using System.Text;

/// <summary>
/// Entry point and core logic for exporting Oracle table data as SQL INSERT statements.
/// </summary>
internal static class Program
{
    // Immutable record to hold a column's name and Oracle data type (e.g. VARCHAR2, DATE, NUMBER).
    private sealed record ColumnInfo(string Name, string DataType);

    /// <summary>
    /// Holds all runtime configuration values read from appsettings.json, environment variables,
    /// or command-line arguments.
    /// </summary>
    private sealed class ExportConfig
    {
        /// <summary>
        /// Gets or sets the ConnectionString
        /// </summary>
        public string ConnectionString { get; set; } = "";

        /// <summary>
        /// Gets or sets the OutputDir
        /// </summary>
        public string OutputDir { get; set; } = "export_sql";

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

    /// <summary>
    /// The Main
    /// </summary>
    /// <param name="args">The args<see cref="string[]"/></param>
    /// <returns>The <see cref="int"/></returns>
    public static int Main(string[] args)
    {
        // Il logger viene creato prima di tutto il resto.
        // Usiamo "export_sql" come cartella di default nel caso cfg non sia ancora disponibile.
        Logger? logger = null;

        try
        {
            var cfg = LoadConfig(args);

            if (string.IsNullOrWhiteSpace(cfg.ConnectionString))
            {
                //logger?.LogError("ConnectionString mancante.");
                throw new InvalidOperationException("ConnectionString mancante.");
            }
            if (cfg.Tables.Count == 0)
            {
                //logger?.LogError("Nessuna tabella configurata.");
                throw new InvalidOperationException("Nessuna tabella configurata.");
            }
                
            Directory.CreateDirectory(cfg.OutputDir);

            // Ora che conosciamo OutputDir, creiamo il logger.
            logger = new Logger(cfg.OutputDir);

            using var conn = new OracleConnection(cfg.ConnectionString);
            conn.Open();

            var currentSchema = GetCurrentSchema(conn);
            logger.Log($"Connesso a Oracle. Schema corrente: {currentSchema}");
            logger.Log($"Output: {Path.GetFullPath(cfg.OutputDir)}");

            if (cfg.OneFilePerTable)
            {
                foreach (var table in cfg.Tables)
                    ExportSingleTableToOwnFile(conn, cfg, table, currentSchema, logger);
            }
            else
            {
                ExportAllTablesToOneFile(conn, cfg, currentSchema, logger);
            }

            logger.Log("Esportazione completata.");
            return 0;
        }
        catch (Exception ex)
        {
            // Se il logger è già disponibile, usa lui; altrimenti scrivi solo su stderr.
            if (logger is not null)
                logger.LogError(ex.ToString());
            else
                //logger?.LogError("ERRORE: " + ex.ToString());
                Console.Error.WriteLine("ERRORE: " + ex);
            return 1;
        }
        finally
        {
            logger?.Dispose(); // Garantisce la chiusura del file anche in caso di eccezione.
        }
    }

    /// <summary>
    /// The LoadConfig
    /// </summary>
    /// <param name="args">The args<see cref="string[]"/></param>
    /// <returns>The <see cref="ExportConfig"/></returns>
    private static ExportConfig LoadConfig(string[] args)
    {
        // Configuration priority (lowest to highest):
        //   1) appsettings.json  — required base file
        //   2) Environment variables prefixed with OIE_  (e.g. OIE_Oracle__ConnectionString)
        //   3) CLI arguments parsed manually below (highest priority, for quick overrides)
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddEnvironmentVariables(prefix: "OIE_");

        var config = builder.Build();

        // All Oracle-specific settings live under the "Oracle" section in appsettings.json.
        var oracle = config.GetSection("Oracle");

        // Populate the config object with values from the file/env sources.
        var cfg = new ExportConfig
        {
            ConnectionString = oracle["ConnectionString"] ?? "",
            OutputDir = oracle["OutputDir"] ?? "export_sql",
            QuoteIdentifiers = TryGetBool(oracle["QuoteIdentifiers"], false),
            OneFilePerTable = TryGetBool(oracle["OneFilePerTable"], true),
            CommitEveryRowsComment = TryGetInt(oracle["CommitEveryRowsComment"], 500),
            Tables = ReadStringList(oracle.GetSection("Tables")),
            WhereByTable = ReadStringDictionary(oracle.GetSection("WhereByTable")),
            OrderByByTable = ReadStringDictionary(oracle.GetSection("OrderByByTable")),
        };

        // Apply any CLI overrides that were passed in the "--Key=Value" or "--Key Value" format.
        // Example: dotnet run -- --Oracle:ConnectionString="User Id=...;Password=...;..."
        var overrides = ParseArgs(args);

        // Each override is applied only if the value is actually present and non-empty.
        if (overrides.TryGetValue("Oracle:ConnectionString", out var cs) && !string.IsNullOrWhiteSpace(cs))
            cfg.ConnectionString = cs;

        if (overrides.TryGetValue("Oracle:OutputDir", out var od) && !string.IsNullOrWhiteSpace(od))
            cfg.OutputDir = od;

        if (overrides.TryGetValue("Oracle:OneFilePerTable", out var oft) && bool.TryParse(oft, out var b1))
            cfg.OneFilePerTable = b1;

        if (overrides.TryGetValue("Oracle:QuoteIdentifiers", out var qi) && bool.TryParse(qi, out var b2))
            cfg.QuoteIdentifiers = b2;

        return cfg;
    }

    /// <summary>
    /// The ReadStringList
    /// </summary>
    /// <param name="section">The section<see cref="IConfigurationSection"/></param>
    /// <returns>The <see cref="List{string}"/></returns>
    private static List<string> ReadStringList(IConfigurationSection section)
    {
        // Reads a JSON array like: "Tables": ["ORDERS", "CUSTOMERS"]
        // Blank entries are excluded to guard against accidental empty strings in config.
        return section.GetChildren()
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList()!;
    }

    /// <summary>
    /// The ReadStringDictionary
    /// </summary>
    /// <param name="section">The section<see cref="IConfigurationSection"/></param>
    /// <returns>The <see cref="Dictionary{string, string}"/></returns>
    private static Dictionary<string, string> ReadStringDictionary(IConfigurationSection section)
    {
        // Reads a JSON object like: "WhereByTable": { "ORDERS": "WHERE STATUS = 'OPEN'" }
        // The resulting dictionary is case-insensitive so table name lookup is resilient to casing.
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in section.GetChildren())
        {
            // Skip any keys whose value is empty or whitespace-only.
            if (!string.IsNullOrWhiteSpace(child.Value))
                dict[child.Key] = child.Value!;
        }
        return dict;
    }

    /// <summary>
    /// The ParseArgs
    /// </summary>
    /// <param name="args">The args<see cref="string[]"/></param>
    /// <returns>The <see cref="Dictionary{string, string}"/></returns>
    private static Dictionary<string, string> ParseArgs(string[] args)
    {
        // Parses CLI arguments of the form:
        //   --Key=Value       (inline equals sign)
        //   --Key Value       (space-separated next token)
        //   --Flag            (boolean flag; defaults to "true")
        // Non "--" tokens are silently skipped.
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];

            // Only process tokens that start with "--".
            if (!a.StartsWith("--", StringComparison.Ordinal))
                continue;

            // Strip the leading "--" to get the raw key[=value] string.
            a = a.Substring(2);

            string key;
            string value;

            var eq = a.IndexOf('=');
            if (eq >= 0)
            {
                // "--Key=Value" form: split at the first '='.
                key = a.Substring(0, eq);
                value = a.Substring(eq + 1);
            }
            else
            {
                key = a;

                // "--Key Value" form: peek at the next token if it doesn't start with "--".
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    value = args[i + 1];
                    i++; // Consume the next token so it isn't re-processed.
                }
                else
                {
                    // Standalone flag with no value — treat as a boolean "true".
                    value = "true";
                }
            }

            // Trim surrounding whitespace and optional surrounding quotes from the value.
            dict[key] = value.Trim().Trim('"');
        }

        return dict;
    }

    /// <summary>
    /// The TryGetBool
    /// </summary>
    /// <param name="s">The s<see cref="string?"/></param>
    /// <param name="fallback">The fallback<see cref="bool"/></param>
    /// <returns>The <see cref="bool"/></returns>
    // Parses a string as a boolean, returning the fallback value if parsing fails or input is null.
    private static bool TryGetBool(string? s, bool fallback)
        => bool.TryParse(s, out var b) ? b : fallback;

    /// <summary>
    /// The TryGetInt
    /// </summary>
    /// <param name="s">The s<see cref="string?"/></param>
    /// <param name="fallback">The fallback<see cref="int"/></param>
    /// <returns>The <see cref="int"/></returns>
    // Parses a string as an integer using the invariant culture, returning the fallback on failure.
    private static int TryGetInt(string? s, int fallback)
        => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : fallback;

    /// <summary>
    /// The GetCurrentSchema
    /// </summary>
    /// <param name="conn">The conn<see cref="OracleConnection"/></param>
    /// <returns>The <see cref="string"/></returns>
    private static string GetCurrentSchema(OracleConnection conn)
    {
        // SYS_CONTEXT('USERENV','CURRENT_SCHEMA') returns the schema that unqualified
        // object references resolve to for this session — typically the logged-in user's schema.
        using var cmd = new OracleCommand("SELECT SYS_CONTEXT('USERENV','CURRENT_SCHEMA') FROM dual", conn);
        var v = cmd.ExecuteScalar();
        return Convert.ToString(v, CultureInfo.InvariantCulture) ?? "UNKNOWN";
    }

    /// <summary>
    /// The ExportAllTablesToOneFile
    /// </summary>
    /// <param name="conn">The conn<see cref="OracleConnection"/></param>
    /// <param name="cfg">The cfg<see cref="ExportConfig"/></param>
    /// <param name="currentSchema">The currentSchema<see cref="string"/></param>
    private static void ExportAllTablesToOneFile(OracleConnection conn, ExportConfig cfg, string currentSchema, Logger logger)
    {
        var filePath = Path.Combine(cfg.OutputDir, $"export_all_tables_{DateTime.Now:yyyyMMdd_HHmmss}.sql");
        using var sw = CreateWriter(filePath);

        // Write the file-level header comment with generation timestamp.
        WriteFileHeader(sw, "TUTTE LE TABELLE");

        foreach (var table in cfg.Tables)
        {
            // Visually separate each table's block with a banner comment for readability.
            sw.WriteLine();
            sw.WriteLine(new string('-', 80));
            sw.WriteLine($"-- TABELLA: {table}");
            sw.WriteLine(new string('-', 80));

            // Delegate the actual data reading and INSERT generation to the shared writer method.
            ExportTableIntoWriter(conn, cfg, table, sw, currentSchema, logger);
        }

        sw.Flush(); // Ensure all buffered data is written to disk before closing.
        logger.Log($"Creato: {filePath}");
    }

    /// <summary>
    /// The ExportSingleTableToOwnFile
    /// </summary>
    /// <param name="conn">The conn<see cref="OracleConnection"/></param>
    /// <param name="cfg">The cfg<see cref="ExportConfig"/></param>
    /// <param name="tableName">The tableName<see cref="string"/></param>
    /// <param name="currentSchema">The currentSchema<see cref="string"/></param>
    private static void ExportSingleTableToOwnFile(OracleConnection conn, ExportConfig cfg, string tableName, string currentSchema, Logger logger)
    {
        // Derive a safe file name from the table name, replacing characters that are
        // invalid in file names (including '.' used in SCHEMA.TABLE notation).
        var filePath = Path.Combine(cfg.OutputDir, $"{currentSchema}.{SanitizeFileName(tableName)}_{DateTime.Now:yyyyMMdd_HHmmss}.sql");
        using var sw = CreateWriter(filePath);

        WriteFileHeader(sw, tableName);
        ExportTableIntoWriter(conn, cfg, tableName, sw, currentSchema, logger);

        sw.Flush(); // Flush the buffer to guarantee the file is complete on disk.
        logger.Log($"Creato: {filePath}");
    }

    /// <summary>
    /// The CreateWriter
    /// </summary>
    /// <param name="filePath">The filePath<see cref="string"/></param>
    /// <returns>The <see cref="StreamWriter"/></returns>
    private static StreamWriter CreateWriter(string filePath)
    {
        // Create (or overwrite) the file and wrap it in a StreamWriter.
        // UTF-8 without BOM is used so the files are compatible with most SQL tools and Unix pipelines.
        var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        return new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>
    /// The WriteFileHeader
    /// </summary>
    /// <param name="sw">The sw<see cref="StreamWriter"/></param>
    /// <param name="title">The title<see cref="string"/></param>
    private static void WriteFileHeader(StreamWriter sw, string title)
    {
        // Write a standard SQL comment block at the top of every output file so readers
        // know what the file contains, when it was generated, and any important caveats.
        sw.WriteLine($"-- Export INSERT per: {title}");
        sw.WriteLine($"-- Generato: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        //sw.WriteLine("-- NOTE:");
        //sw.WriteLine("--  - INSERT senza gestione duplicati (come richiesto).");
        //sw.WriteLine("--  - Se vuoi, aggiungi manualmente TRUNCATE/DELETE prima degli INSERT.");
        sw.WriteLine();
    }

    /// <summary>
    /// The ExportTableIntoWriter
    /// </summary>
    /// <param name="conn">The conn<see cref="OracleConnection"/></param>
    /// <param name="cfg">The cfg<see cref="ExportConfig"/></param>
    /// <param name="tableName">The tableName<see cref="string"/></param>
    /// <param name="sw">The sw<see cref="StreamWriter"/></param>
    /// <param name="currentSchema">The currentSchema<see cref="string"/></param>
    private static void ExportTableIntoWriter(OracleConnection conn, ExportConfig cfg, string tableName, StreamWriter sw, string currentSchema, Logger logger)
    {
        // Retrieve the ordered column metadata for this table from the Oracle data dictionary.
        var cols = GetColumns(conn, tableName, currentSchema, logger);
        if (cols.Count == 0)
        {
            logger.LogError($"Nessuna colonna trovata per {tableName}. Controlla nome tabella/schema.");
            throw new InvalidOperationException($"Nessuna colonna trovata per {tableName}. Controlla nome tabella/schema.");
        }
        // Look up any optional WHERE / ORDER BY clauses configured for this specific table.
        // If not configured, the suffix is an empty string (i.e. no filtering or ordering).
        var where = cfg.WhereByTable.TryGetValue(tableName, out var w) && !string.IsNullOrWhiteSpace(w) ? " " + w : "";
        var orderBy = cfg.OrderByByTable.TryGetValue(tableName, out var o) && !string.IsNullOrWhiteSpace(o) ? " " + o : "";

        // Build the SELECT statement dynamically using the column names from the data dictionary.
        var selectCols = string.Join(", ", cols.Select(c => QuoteIdent(c.Name, cfg.QuoteIdentifiers)));
        var sql = $"SELECT {selectCols} FROM {tableName}{where}{orderBy}";

        // SequentialAccess is a performance hint: it tells the ODP.NET driver we will read
        // columns left-to-right without random access, enabling streaming for large LOB columns.
        using var cmd = new OracleCommand(sql, conn) { BindByName = true };
        using var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess);

        int rowCount = 0;

        while (reader.Read())
        {
            rowCount++;

            // Convert each column value to its Oracle SQL literal representation.
            var valuesSql = new string[cols.Count];
            for (int i = 0; i < cols.Count; i++)
            {
                // Read the raw value; treat DB nulls uniformly as null for literal conversion.
                object? val = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
                valuesSql[i] = ToOracleLiteral(val, cols[i].DataType);
            }

            // Build the INSERT statement column list (recomputed per row in case quoting matters,
            // though in practice it is the same for every row of the same table).
            var insertCols = string.Join(", ", cols.Select(c => QuoteIdent(c.Name, cfg.QuoteIdentifiers)));

            // Write the INSERT statement directly to the stream using multiple Write calls
            // to avoid building a large intermediate string for every row.
            sw.Write("INSERT INTO ");
            sw.Write(tableName);
            sw.Write(" (");
            sw.Write(insertCols);
            sw.Write(") VALUES (");
            sw.Write(string.Join(", ", valuesSql));
            sw.WriteLine(");");

            // Emit a periodic commit marker comment so operators know where safe checkpoints are.
            // The marker is commented out by default; uncommenting it enables batch commits.
            if (cfg.CommitEveryRowsComment > 0 && rowCount % cfg.CommitEveryRowsComment == 0)
                sw.WriteLine("-- COMMIT;");
        }

        // Final commit marker at the end of the table's INSERT block.
        sw.WriteLine();
        sw.WriteLine("-- COMMIT;");

        logger.Log($"{tableName}: righe esportate = {rowCount}");
    }

    /// <summary>
    /// The GetColumns
    /// </summary>
    /// <param name="conn">The conn<see cref="OracleConnection"/></param>
    /// <param name="tableName">The tableName<see cref="string"/></param>
    /// <param name="currentSchema">The currentSchema<see cref="string"/></param>
    /// <returns>The <see cref="List{ColumnInfo}"/></returns>
    private static List<ColumnInfo> GetColumns(OracleConnection conn, string tableName, string currentSchema, Logger logger)
    {
        string owner;
        string tab;

        // Support both "TABLENAME" and "SCHEMA.TABLENAME" input formats.
        var parts = tableName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 2)
        {
            // Explicit schema prefix provided — use it directly.
            owner = parts[0].ToUpperInvariant();
            tab = parts[1].ToUpperInvariant();
        }
        else if (parts.Length == 1)
        {
            // No schema prefix — fall back to the session's current schema.
            owner = currentSchema.ToUpperInvariant();
            tab = parts[0].ToUpperInvariant();
        }
        else
        {
            // More than one dot is ambiguous and unsupported.
            logger.LogError($"Nome tabella non valido: '{tableName}'. Usa 'TABELLA' o 'SCHEMA.TABELLA'.");
            throw new ArgumentException($"Nome tabella non valido: '{tableName}'. Usa 'TABELLA' o 'SCHEMA.TABELLA'.");
        }

        // Query ALL_TAB_COLUMNS (accessible to the current user) for column metadata.
        // ORDER BY COLUMN_ID ensures the SELECT and INSERT column lists are always in the same order.
        const string sql = @"
SELECT COLUMN_NAME, DATA_TYPE
FROM ALL_TAB_COLUMNS
WHERE OWNER = :OWNER AND TABLE_NAME = :TAB
ORDER BY COLUMN_ID";

        using var cmd = new OracleCommand(sql, conn);
        // Use bind parameters to avoid SQL injection and improve cursor reuse in Oracle's shared pool.
        cmd.Parameters.Add(":OWNER", OracleDbType.Varchar2, owner, ParameterDirection.Input);
        cmd.Parameters.Add(":TAB", OracleDbType.Varchar2, tab, ParameterDirection.Input);

        using var r = cmd.ExecuteReader();
        var cols = new List<ColumnInfo>();
        while (r.Read())
            cols.Add(new ColumnInfo(r.GetString(0), r.GetString(1)));

        return cols;
    }

    /// <summary>
    /// The QuoteIdent
    /// </summary>
    /// <param name="ident">The ident<see cref="string"/></param>
    /// <param name="quote">The quote<see cref="bool"/></param>
    /// <returns>The <see cref="string"/></returns>
    private static string QuoteIdent(string ident, bool quote)
        // If quoting is disabled, return the identifier as-is (Oracle normalises to uppercase).
        // If quoting is enabled, wrap in double quotes and escape any embedded double quotes
        // by doubling them, per the SQL standard.
        => !quote ? ident : "\"" + ident.Replace("\"", "\"\"") + "\"";

    /// <summary>
    /// The ToOracleLiteral
    /// </summary>
    /// <param name="value">The value<see cref="object?"/></param>
    /// <param name="oracleDataType">The oracleDataType<see cref="string"/></param>
    /// <returns>The <see cref="string"/></returns>
    private static string ToOracleLiteral(object? value, string oracleDataType)
    {
        // NULL values (both C# null and ADO.NET DBNull) map directly to the SQL NULL keyword.
        if (value is null || value == DBNull.Value)
            return "NULL";

        switch (value)
        {
            // Escape single quotes by doubling them — the standard SQL string escape.
            case string s:
                return "'" + s.Replace("'", "''") + "'";
            case char c:
                return "'" + c.ToString().Replace("'", "''") + "'";

            // Oracle has no native BOOLEAN type; map to 1/0 integers instead.
            case bool b:
                return b ? "1" : "0";

            case DateTime dt:
                // Oracle's DATE type stores date + time (up to seconds).
                if (oracleDataType.Equals("DATE", StringComparison.OrdinalIgnoreCase))
                {
                    var sdt = dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    return $"TO_DATE('{sdt}','YYYY-MM-DD HH24:MI:SS')";
                }
                // TIMESTAMP types preserve sub-second precision (up to 7 fractional digits).
                if (oracleDataType.StartsWith("TIMESTAMP", StringComparison.OrdinalIgnoreCase))
                {
                    var sdt = dt.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
                    return $"TO_TIMESTAMP('{sdt}','YYYY-MM-DD HH24:MI:SS.FF7')";
                }
                // Fallback for any other date-like type — use second-level precision.
                {
                    var sdt = dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    return $"TO_DATE('{sdt}','YYYY-MM-DD HH24:MI:SS')";
                }

            // Numeric types: use InvariantCulture to ensure "." as the decimal separator,
            // regardless of the OS locale, which is required for valid Oracle SQL literals.
            case decimal dec:
                return dec.ToString(CultureInfo.InvariantCulture);
            case double d:
                // "R" (round-trip) format guarantees no precision is lost during serialization.
                return d.ToString("R", CultureInfo.InvariantCulture);
            case float f:
                return f.ToString("R", CultureInfo.InvariantCulture);

            // All integer types can be rendered as plain numeric literals with no special handling.
            case sbyte or byte or short or ushort or int or uint or long or ulong:
                return Convert.ToString(value, CultureInfo.InvariantCulture)!;

            // Binary data (RAW / BLOB columns) is encoded as a hex string and wrapped in
            // Oracle's HEXTORAW() function so it is interpreted as binary, not text.
            case byte[] bytes:
                var hex = BitConverter.ToString(bytes).Replace("-", "");
                return $"HEXTORAW('{hex}')";

            // Catch-all: convert the value to a string and quote it.
            // Covers types like OracleDecimal, OracleString, etc. returned by some ODP.NET drivers.
            default:
                var str = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                return "'" + str.Replace("'", "''") + "'";
        }
    }

    /// <summary>
    /// The SanitizeFileName
    /// </summary>
    /// <param name="s">The s<see cref="string"/></param>
    /// <returns>The <see cref="string"/></returns>
    private static string SanitizeFileName(string s)
    {
        // Replace every character that is illegal in a file name (e.g. /, \, :, *, ?, ", <, >, |)
        // with an underscore to produce a safe, portable file name.
        foreach (var ch in Path.GetInvalidFileNameChars())
            s = s.Replace(ch, '_');

        // Also convert dots to underscores so that "SCHEMA.TABLE" becomes "SCHEMA_TABLE.sql"
        // rather than "SCHEMA.TABLE.sql", which would imply a double extension.
        return s.Replace('.', '_');
    }
}