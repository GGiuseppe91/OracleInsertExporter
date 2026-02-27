# OracleInsertExporter

Esporta tabelle Oracle come istruzioni `INSERT INTO` in file `.sql`.

---

## 0. Avvio Rapido

```
1. Modifica appsettings.json → inserisci ConnectionString e Tables
2. dotnet run
3. Trovi i file .sql e il .log nella cartella export_sql\
```

---

## 1. Cosa Fa

- Legge la configurazione da `appsettings.json` (connection string, tabelle, filtri)
- Si connette al database Oracle tramite ODP.NET Managed Driver
- Esporta tutte le righe delle tabelle configurate come `INSERT INTO <tabella> (...) VALUES (...);`
- Salva i file `.sql` in una cartella di output configurabile
- Registra ogni operazione in un file `.log` con timestamp
- Modalità consigliata: un file `.sql` per ogni tabella (`OneFilePerTable = true`)

---

## 2. Requisiti

| Componente | Dettaglio |
|---|---|
| .NET SDK | Versione 8 o superiore |
| Driver Oracle | `Oracle.ManagedDataAccess.Core` — già incluso via NuGet |
| Accesso Oracle | Credenziali con almeno `SELECT` su `ALL_TAB_COLUMNS` e sulle tabelle da esportare |

---

## 3. Configurazione (`appsettings.json`)

### Riepilogo parametri

| Parametro | Tipo | Default | Descrizione |
|---|---|---|---|
| `ConnectionString` | string | *(obbligatorio)* | Connection string Oracle nel formato ODP.NET |
| `OutputDir` | string | `export_sql` | Cartella dove vengono salvati i file `.sql` e `.log` |
| `OneFilePerTable` | bool | `true` | `true` = un file per tabella; `false` = un unico file |
| `QuoteIdentifiers` | bool | `false` | `true` = nomi colonne/tabelle tra virgolette doppie |
| `CommitEveryRowsComment` | int | `500` | Frequenza del commento `-- COMMIT;` (0 = disabilitato) |
| `Tables` | array | *(obbligatorio)* | Lista tabelle. Formato: `TABELLA` oppure `SCHEMA.TABELLA` |
| `WhereByTable` | oggetto | *(vuoto)* | Clausola `WHERE` opzionale per tabella (filtra le righe) |
| `OrderByByTable` | oggetto | *(vuoto)* | Clausola `ORDER BY` opzionale per tabella |

### Esempio completo

```json
{
  "Oracle": {
    "ConnectionString": "User Id=USER;Password=PWD;Data Source=//HOST:1521/SERVICE;",
    "OutputDir": "export_sql",
    "OneFilePerTable": true,
    "QuoteIdentifiers": false,
    "CommitEveryRowsComment": 500,
    "Tables": [
      "CTCP_CDL",
      "ALTRA_TABELLA",
      "SCHEMA2.TABELLA_X"
    ],
    "WhereByTable": {
      "CTCP_CDL": "WHERE STATUS = 'A'",
      "ALTRA_TABELLA": "WHERE DATA > DATE '2024-01-01'"
    },
    "OrderByByTable": {
      "CTCP_CDL": "ORDER BY ID"
    }
  }
}
```

> ⚠️ **Sicurezza:** non committare mai `appsettings.json` con credenziali reali. Aggiungilo al `.gitignore` e usa le variabili d'ambiente (vedi sezione 6A).

---

## 4. Esecuzione da Sorgente

```bash
dotnet restore
dotnet run
```

### Output atteso in console

```
Log: C:\export_sql\export_20250227_143000.log
Connesso a Oracle. Schema corrente: MYSCHEMA
Output: C:\export_sql
  CTCP_CDL: righe esportate = 1523
  ALTRA_TABELLA: righe esportate = 47
  SCHEMA2.TABELLA_X: righe esportate = 8901
Esportazione completata.
```

### File generati

- Un file `.sql` per ogni tabella, es. `export_sql\CTCP_CDL_20250227_143001.sql`
- Un file `.log` con tutti i messaggi, es. `export_sql\export_20250227_143000.log`

---

## 5. Priorità delle Configurazioni

Le impostazioni vengono applicate nell'ordine seguente (la successiva sovrascrive la precedente):

| Priorità | Sorgente | Note |
|---|---|---|
| 1 (minima) | `appsettings.json` | File base, obbligatorio |
| 2 | Variabili d'ambiente (`OIE_...`) | Utili per CI/CD o per non salvare credenziali su file |
| 3 (massima) | Argomenti CLI (`--Oracle:...`) | Override rapido da terminale |

---

## 6. Override senza Modificare `appsettings.json`

### 6A — Variabili d'ambiente

Prefisso: `OIE_` — Separatore sezioni: `__` (doppio underscore)

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

### 6B — Argomenti CLI

**Da sorgente**
```bash
dotnet run -- --Oracle:ConnectionString="User Id=...;Password=...;Data Source=//HOST:1521/SVC;"
dotnet run -- --Oracle:OutputDir="C:\temp\export_sql"
dotnet run -- --Oracle:QuoteIdentifiers=true
dotnet run -- --Oracle:OneFilePerTable=false
```

**Da eseguibile**
```bash
.\OracleInsertExporter.exe --Oracle:ConnectionString="User Id=...;..."
.\OracleInsertExporter.exe --Oracle:OutputDir="C:\temp\export_sql"
```

---

## 7. Build / Publish

**Windows x64 — file singolo autonomo**
```bash
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
# Output: bin\Release\net8.0\win-x64\publish\OracleInsertExporter.exe
```

**Linux x64 — file singolo autonomo**
```bash
dotnet publish -c Release -r linux-x64 --self-contained true /p:PublishSingleFile=true
# Output: bin/Release/net8.0/linux-x64/publish/OracleInsertExporter
```

**Dipendente dal runtime (file più leggero)**
```bash
dotnet publish -c Release --self-contained false
# Richiede .NET 8 Runtime installato sulla macchina di destinazione
```

> ⚠️ `appsettings.json` deve trovarsi nella stessa cartella dell'eseguibile, a meno che non si passi la `ConnectionString` via variabile d'ambiente o argomento CLI.

---

## 8. Esecuzione dei File `.sql` Generati (Import)

| Strumento | Come eseguire |
|---|---|
| SQL Developer | Apri il file `.sql` → **Run Script (F5)** |
| SQLcl / SQL*Plus | `@CTCP_CDL.sql` |
| DBeaver | Apri il file → **Esegui script SQL** |

Il `COMMIT` è commentato per sicurezza. Per rendere permanenti le modifiche, eseguilo manualmente:

```sql
COMMIT;
```

> ⚠️ I file `.sql` non gestiscono i duplicati. Se nella tabella di destinazione esistono già righe con le stesse chiavi primarie, gli `INSERT` falliranno con `ORA-00001`. Aggiungi manualmente `TRUNCATE` o `DELETE` prima degli `INSERT` se necessario.

---

## 9. Risoluzione Problemi

| Problema / Messaggio | Causa probabile | Soluzione |
|---|---|---|
| `Nessuna colonna trovata per TABELLA` | Nome tabella errato o permessi insufficienti su `ALL_TAB_COLUMNS` | Verifica il nome (deve essere in MAIUSCOLO) e i permessi Oracle |
| `ORA-12154: TNS:could not resolve...` | `Data Source` non raggiungibile | Verifica HOST, PORT e SERVICE_NAME. Testa con SQL Developer |
| File `.sql` vuoto (solo header) | Tabella vuota oppure il `WHERE` filtra tutte le righe | Controlla `WhereByTable` o esegui una `SELECT` manuale |
| `ORA-00001: unique constraint violated` | Righe duplicate nella tabella di destinazione | Aggiungi `DELETE` o `TRUNCATE` prima degli `INSERT` |
| `ConnectionString mancante` | Campo vuoto in `appsettings.json` e non passato via ENV/CLI | Imposta la `ConnectionString` nel file o tramite `OIE_Oracle__ConnectionString` |
| `Nessuna tabella configurata` | Array `Tables` vuoto in `appsettings.json` | Aggiungi almeno un nome di tabella nell'array `Tables` |
