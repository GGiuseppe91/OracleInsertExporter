# OracleInsertExporter

Exports Oracle tables as `INSERT INTO` SQL statements into `.sql` files.

---

## 0. Quick Start

```
1. Edit appsettings.json → set ConnectionString and Tables
2. dotnet run
3. Find the .sql files and the .log file in the export_sql\ folder
```

---

## 1. What It Does

- Reads configuration from `appsettings.json` (connection string, tables, optional filters)
- Connects to Oracle via the ODP.NET Managed Driver
- Exports all rows from the configured tables as `INSERT INTO <table> (...) VALUES (...);`
- Saves `.sql` files to a configurable output folder
- Logs every operation with a timestamp to a `.log` file
- Recommended mode: one `.sql` file per table (`OneFilePerTable = true`)

---

## 2. Requirements

| Component | Details |
|---|---|
| .NET SDK | Version 8 or higher |
| Oracle Driver | `Oracle.ManagedDataAccess.Core` — already included via NuGet |
| Oracle Access | Credentials with at least `SELECT` on `ALL_TAB_COLUMNS` and on the tables to export |

---

## 3. Configuration (`appsettings.json`)

### Parameter Reference

| Parameter | Type | Default | Description |
|---|---|---|---|
| `ConnectionString` | string | *(required)* | Oracle connection string in ODP.NET format |
| `OutputDir` | string | `export_sql` | Folder where `.sql` and `.log` files are saved |
| `OneFilePerTable` | bool | `true` | `true` = one file per table; `false` = single combined file |
| `QuoteIdentifiers` | bool | `false` | `true` = wraps column/table names in double quotes |
| `CommitEveryRowsComment` | int | `500` | How often to write a `-- COMMIT;` comment (0 = disabled) |
| `Tables` | array | *(required)* | List of tables to export. Format: `TABLENAME` or `SCHEMA.TABLENAME` |
| `WhereByTable` | object | *(empty)* | Optional `WHERE` clause per table to filter exported rows |
| `OrderByByTable` | object | *(empty)* | Optional `ORDER BY` clause per table |

### Full Example

```json
{
  "Oracle": {
    "ConnectionString": "User Id=USER;Password=PWD;Data Source=//HOST:1521/SERVICE;",
    "OutputDir": "export_sql",
    "OneFilePerTable": true,
    "QuoteIdentifiers": false,
    "CommitEveryRowsComment": 500,
    "Tables": [
      "ORDERS",
      "CUSTOMERS",
      "SCHEMA2.PRODUCTS"
    ],
    "WhereByTable": {
      "ORDERS": "WHERE STATUS = 'A'",
      "CUSTOMERS": "WHERE CREATED_DATE > DATE '2024-01-01'"
    },
    "OrderByByTable": {
      "ORDERS": "ORDER BY ID"
    }
  }
}
```

> ⚠️ **Security:** never commit `appsettings.json` with real credentials. Add it to `.gitignore` and use environment variables instead (see section 6A).

---

## 4. Running from Source

```bash
dotnet restore
dotnet run
```

### Expected Console Output

```
Log: C:\export_sql\export_20250227_143000.log
Connected to Oracle. Current schema: MYSCHEMA
Output: C:\export_sql
  ORDERS: exported rows = 1523
  CUSTOMERS: exported rows = 47
  SCHEMA2.PRODUCTS: exported rows = 8901
Export completed.
```

### Generated Files

- One `.sql` file per table, e.g. `export_sql\ORDERS_20250227_143001.sql`
- One `.log` file with all messages, e.g. `export_sql\export_20250227_143000.log`

---

## 5. Configuration Priority

Settings are applied in the following order (each source overrides the previous one):

| Priority | Source | Notes |
|---|---|---|
| 1 (lowest) | `appsettings.json` | Required base file |
| 2 | Environment variables (`OIE_...`) | Useful for CI/CD or to avoid storing credentials on disk |
| 3 (highest) | CLI arguments (`--Oracle:...`) | Quick terminal override |

---

## 6. Overriding Settings Without Editing `appsettings.json`

### 6A — Environment Variables

Prefix: `OIE_` — Section separator: `__` (double underscore)

**Windows PowerShell**
```powershell
$env:OIE_Oracle__ConnectionString = "User Id=...;Password=...;Data Source=//HOST:1521/SVC;"
$env:OIE_Oracle__OutputDir        = "C:\temp\export_sql"
dotnet run
```

**Windows CMD**
```cmd
set OIE_Oracle__ConnectionString=User Id=...;Password=...;Data Source=//HOST:1521/SVC;
set OIE_Oracle__OutputDir=C:\temp\export_sql
dotnet run
```

**Linux / macOS**
```bash
export OIE_Oracle__ConnectionString="User Id=...;Password=...;Data Source=//HOST:1521/SVC;"
export OIE_Oracle__OutputDir="./export_sql"
dotnet run
```

### 6B — CLI Arguments

**From source**
```bash
dotnet run -- --Oracle:ConnectionString="User Id=...;Password=...;Data Source=//HOST:1521/SVC;"
dotnet run -- --Oracle:OutputDir="C:\temp\export_sql"
dotnet run -- --Oracle:QuoteIdentifiers=true
dotnet run -- --Oracle:OneFilePerTable=false
```

**From executable**
```bash
.\OracleInsertExporter.exe --Oracle:ConnectionString="User Id=...;..."
.\OracleInsertExporter.exe --Oracle:OutputDir="C:\temp\export_sql"
```

---

## 7. Build / Publish

**Windows x64 — single self-contained file**
```bash
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
# Output: bin\Release\net8.0\win-x64\publish\OracleInsertExporter.exe
```

**Linux x64 — single self-contained file**
```bash
dotnet publish -c Release -r linux-x64 --self-contained true /p:PublishSingleFile=true
# Output: bin/Release/net8.0/linux-x64/publish/OracleInsertExporter
```

**Runtime-dependent (smaller file)**
```bash
dotnet publish -c Release --self-contained false
# Requires .NET 8 Runtime to be installed on the target machine
```

> ⚠️ `appsettings.json` must be in the same folder as the executable, unless `ConnectionString` is passed via environment variable or CLI argument.

---

## 8. Running the Generated `.sql` Files (Import)

| Tool | How to run |
|---|---|
| SQL Developer | Open the `.sql` file → **Run Script (F5)** |
| SQLcl / SQL*Plus | `@ORDERS.sql` |
| DBeaver | Open the file → **Execute SQL Script** |

The `COMMIT` is commented out by default for safety. To make changes permanent, run it manually:

```sql
COMMIT;
```

> ⚠️ The generated `.sql` files do not handle duplicates. If the target table already contains rows with the same primary keys, the `INSERT` statements will fail with `ORA-00001`. Add a `TRUNCATE` or `DELETE` before the `INSERT` statements if needed.

---

## 9. Troubleshooting

| Problem / Message | Likely Cause | Solution |
|---|---|---|
| `No columns found for TABLE` | Wrong table name or insufficient permissions on `ALL_TAB_COLUMNS` | Check the name (must be UPPERCASE) and Oracle user permissions |
| `ORA-12154: TNS:could not resolve...` | `Data Source` unreachable | Verify HOST, PORT and SERVICE_NAME. Test the connection with SQL Developer |
| Empty `.sql` file (header only) | Table is empty or the `WHERE` clause filters out all rows | Check `WhereByTable` or run a manual `SELECT` on the table |
| `ORA-00001: unique constraint violated` | Duplicate rows in the target table | Add `DELETE` or `TRUNCATE` before the `INSERT` statements |
| `ConnectionString missing` | Field is empty in `appsettings.json` and not passed via ENV/CLI | Set `ConnectionString` in the file or via `OIE_Oracle__ConnectionString` |
| `No tables configured` | `Tables` array is empty in `appsettings.json` | Add at least one table name to the `Tables` array |
