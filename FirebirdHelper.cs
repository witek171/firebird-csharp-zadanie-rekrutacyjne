using System.Text.RegularExpressions;
using FirebirdSql.Data.FirebirdClient;
using FirebirdSql.Data.Services;

namespace DbMetaTool;

public static class FirebirdHelper
{
	public const string DefaultUser = "SYSDBA";
	public const string DefaultPassword = "masterkey";
	public const string DefaultDbFileName = "default_db.fdb";

	public static void CreateDatabase(string databasePath)
	{
		string directory = Path.GetDirectoryName(databasePath)!;
		if (!Directory.Exists(directory))
			Directory.CreateDirectory(directory);

		if (File.Exists(databasePath))
			File.Delete(databasePath);

		string createDbConnectionString =
			$"User={DefaultUser};Password={DefaultPassword};Database={databasePath};DataSource=localhost;";

		FbConnection.CreateDatabase(createDbConnectionString);
	}

	public static void ExecuteScripts(string connectionString, string scriptsDirectory)
	{
		if (!Directory.Exists(scriptsDirectory))
			throw new DirectoryNotFoundException($"Katalog skryptów nie istnieje: {scriptsDirectory}");

		string[] scriptFiles = Directory.GetFiles(scriptsDirectory, "*.txt");
		Array.Sort(scriptFiles);

		if (scriptFiles.Length == 0)
			throw new FileNotFoundException($"Brak plików .txt w katalogu: {scriptsDirectory}");

		foreach (string file in scriptFiles)
			if (new FileInfo(file).Length == 0)
				throw new Exception($"Plik jest pusty: {file}");

		foreach (string filePath in scriptFiles)
		{
			string fileName = Path.GetFileName(filePath);
			string sqlScript = File.ReadAllText(filePath);

			string cleanedScript = System.Text.RegularExpressions.Regex.Replace(
				sqlScript,
				@"/\*.*?\*/",
				"",
				System.Text.RegularExpressions.RegexOptions.Singleline);

			string[] rawCommands = cleanedScript.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

			List<string> commands = new();
			foreach (string command in rawCommands)
			{
				string trimmedCommand = command.Trim();

				if (!string.IsNullOrEmpty(trimmedCommand))
					commands.Add(trimmedCommand);
			}

			using FbConnection dbConnection = new(connectionString);
			dbConnection.Open();

			using FbTransaction transaction = dbConnection.BeginTransaction();
			try
			{
				foreach (string commandText in commands)
				{
					string trimmedCommand = commandText.Trim();
					if (!string.IsNullOrEmpty(trimmedCommand))
					{
						using FbCommand command = new(trimmedCommand, dbConnection, transaction);
						command.ExecuteNonQuery();
					}
				}

				transaction.Commit();
				Console.WriteLine($"  > Skrypt '{fileName}' wykonany pomyślnie.");
			}
			catch (FbException ex)
			{
				transaction.Rollback();
				Console.WriteLine("BŁĄD");
				throw new Exception(
					$"Błąd SQL w '{fileName}'. Transakcja wycofana. Szczegóły: {ex.Message}", ex);
			}
		}
	}

	internal class DisposableServiceWrapper<T> : IDisposable where T : FbService
	{
		public T Service { get; }

		public DisposableServiceWrapper(T service)
		{
			Service = service ?? throw new ArgumentNullException(nameof(service));
		}

		public void Dispose()
		{
			if (Service is IDisposable disposable)
			{
				disposable.Dispose();
			}
			else
			{
				try
				{
					((dynamic)Service).Close();
				}
				catch
				{
					/* Ignorowanie błędów */
				}
			}
		}
	}


	public static void ExportAllDdlUsingFbRestore(string connectionString, string outputDirectory)
	{
		const string outputFileName = "01_FULL_DDL.txt";
		string filePath = Path.Combine(outputDirectory, outputFileName);

		var builder = new FbConnectionStringBuilder(connectionString);

		string tempBackupFile = Path.Combine(Path.GetTempPath(),
			Path.GetFileNameWithoutExtension(Path.GetRandomFileName()) + ".fbk");

		DisposableServiceWrapper<FbBackup>? backupWrapper = null;

		try
		{
			if (!Directory.Exists(outputDirectory))
			{
				Directory.CreateDirectory(outputDirectory);
				Console.WriteLine($"Utworzono katalog wyjściowy: {outputDirectory}");
			}

			Console.WriteLine("  > Krok 1/2: Tworzenie tymczasowego backupu bazy...");

			backupWrapper = new DisposableServiceWrapper<FbBackup>(new FbBackup(connectionString));
			backupWrapper.Service.BackupFiles.Add(new FbBackupFile(tempBackupFile));
			backupWrapper.Service.Execute();

			Console.WriteLine("  > Krok 2/2: Wyodrębnianie metadanych DDL...");

			ExtractDdlFromDatabase(connectionString, filePath);

			Console.WriteLine($"  > Eksport DDL pomyślnie zapisany do {outputFileName}");
		}
		catch (Exception ex)
		{
			throw new Exception(
				$"Błąd eksportu DDL do pliku {Path.GetFileName(filePath)}: {ex.Message}", ex);
		}
		finally
		{
			backupWrapper?.Dispose();

			if (File.Exists(tempBackupFile))
			{
				try
				{
					File.Delete(tempBackupFile);
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Ostrzeżenie: Nie udało się usunąć pliku tymczasowego: {ex.Message}");
				}
			}
		}
	}

	private static void ExtractDdlFromDatabase(string connectionString, string outputFilePath)
	{
		using var connection = new FbConnection(connectionString);
		connection.Open();

		using var writer = new StreamWriter(outputFilePath, false, System.Text.Encoding.UTF8);

		writer.WriteLine("/* ============================================ */");
		writer.WriteLine("/* DOMENY (DOMAINS)                             */");
		writer.WriteLine("/* ============================================ */");
		writer.WriteLine();
		ExportDomains(connection, writer);

		writer.WriteLine();
		writer.WriteLine("/* ============================================ */");
		writer.WriteLine("/* TABELE (TABLES)                              */");
		writer.WriteLine("/* ============================================ */");
		writer.WriteLine();
		ExportTables(connection, writer);

		writer.WriteLine();
		writer.WriteLine("/* ============================================ */");
		writer.WriteLine("/* PROCEDURY SKŁADOWANE (STORED PROCEDURES)     */");
		writer.WriteLine("/* ============================================ */");
		writer.WriteLine();
		ExportProcedures(connection, writer);
	}

	private static void ExportDomains(FbConnection connection, StreamWriter writer)
	{
		string query = @"
        SELECT 
            RDB$FIELD_NAME AS DOMAIN_NAME,
            RDB$FIELD_TYPE AS FIELD_TYPE,
            RDB$FIELD_LENGTH AS FIELD_LENGTH,
            RDB$FIELD_PRECISION AS FIELD_PRECISION,
            RDB$FIELD_SCALE AS FIELD_SCALE,
            RDB$NULL_FLAG AS NOT_NULL,
            RDB$DEFAULT_SOURCE AS DEFAULT_VALUE,
            RDB$VALIDATION_SOURCE AS CHECK_CONSTRAINT
        FROM RDB$FIELDS
        WHERE RDB$SYSTEM_FLAG = 0
        AND RDB$FIELD_NAME NOT STARTING WITH 'RDB$'
        ORDER BY RDB$FIELD_NAME";

		using var cmd = new FbCommand(query, connection);
		using var reader = cmd.ExecuteReader();

		while (reader.Read())
		{
			string domainName = reader["DOMAIN_NAME"].ToString()!.Trim();
			string fieldType = GetFieldTypeName(
				Convert.ToInt32(reader["FIELD_TYPE"]),
				reader["FIELD_LENGTH"],
				reader["FIELD_PRECISION"],
				reader["FIELD_SCALE"]);

			writer.Write($"CREATE DOMAIN {domainName} AS {fieldType}");

			if (reader["DEFAULT_VALUE"] != DBNull.Value)
				writer.Write($" {reader["DEFAULT_VALUE"].ToString()!.Trim()}");

			if (reader["NOT_NULL"] != DBNull.Value && Convert.ToInt32(reader["NOT_NULL"]) == 1)
				writer.Write(" NOT NULL");

			if (reader["CHECK_CONSTRAINT"] != DBNull.Value)
				writer.Write($" {reader["CHECK_CONSTRAINT"].ToString()!.Trim()}");

			writer.WriteLine(";");
			writer.WriteLine();
		}
	}

	private static void ExportTables(FbConnection connection, StreamWriter writer)
	{
		string query = @"
        SELECT RDB$RELATION_NAME 
        FROM RDB$RELATIONS 
        WHERE RDB$SYSTEM_FLAG = 0 
        AND RDB$VIEW_BLR IS NULL
        ORDER BY RDB$RELATION_NAME";

		using var cmd = new FbCommand(query, connection);
		using var reader = cmd.ExecuteReader();

		var tables = new List<string>();
		while (reader.Read())
			tables.Add(reader["RDB$RELATION_NAME"].ToString()!.Trim());

		foreach (var tableName in tables)
		{
			writer.WriteLine($"CREATE TABLE {tableName}");
			writer.WriteLine("(");

			ExportTableColumns(connection, tableName, writer);

			writer.WriteLine(");");
			writer.WriteLine();
		}
	}

	private static void ExportTableColumns(FbConnection connection, string tableName, StreamWriter writer)
	{
		string query = @"
        SELECT 
            RF.RDB$FIELD_NAME AS FIELD_NAME,
            RF.RDB$FIELD_SOURCE AS FIELD_SOURCE,
            F.RDB$FIELD_TYPE AS FIELD_TYPE,
            F.RDB$FIELD_LENGTH AS FIELD_LENGTH,
            F.RDB$FIELD_PRECISION AS FIELD_PRECISION,
            F.RDB$FIELD_SCALE AS FIELD_SCALE,
            RF.RDB$NULL_FLAG AS NOT_NULL,
            RF.RDB$DEFAULT_SOURCE AS DEFAULT_VALUE,
            RF.RDB$FIELD_POSITION AS FIELD_POSITION
        FROM RDB$RELATION_FIELDS RF
        JOIN RDB$FIELDS F ON RF.RDB$FIELD_SOURCE = F.RDB$FIELD_NAME
        WHERE RF.RDB$RELATION_NAME = @TableName
        ORDER BY RF.RDB$FIELD_POSITION";

		using var cmd = new FbCommand(query, connection);
		cmd.Parameters.AddWithValue("@TableName", tableName);
		using var reader = cmd.ExecuteReader();

		var columns = new List<string>();
		while (reader.Read())
		{
			string fieldName = reader["FIELD_NAME"].ToString()!.Trim();
			string fieldType = GetFieldTypeName(
				Convert.ToInt32(reader["FIELD_TYPE"]),
				reader["FIELD_LENGTH"],
				reader["FIELD_PRECISION"],
				reader["FIELD_SCALE"]);

			string column = $"    {fieldName} {fieldType}";

			if (reader["DEFAULT_VALUE"] != DBNull.Value)
				column += $" {reader["DEFAULT_VALUE"].ToString()!.Trim()}";

			if (reader["NOT_NULL"] != DBNull.Value && Convert.ToInt32(reader["NOT_NULL"]) == 1)
				column += " NOT NULL";

			columns.Add(column);
		}

		writer.WriteLine(string.Join(",\n", columns));
	}

	private static void ExportProcedures(FbConnection connection, StreamWriter writer)
	{
		string query = @"
        SELECT 
            RDB$PROCEDURE_NAME,
            RDB$PROCEDURE_SOURCE
        FROM RDB$PROCEDURES
        WHERE RDB$SYSTEM_FLAG = 0
        ORDER BY RDB$PROCEDURE_NAME";

		using var cmd = new FbCommand(query, connection);
		using var reader = cmd.ExecuteReader();

		while (reader.Read())
		{
			string procName = reader["RDB$PROCEDURE_NAME"].ToString()!.Trim();
			string source = reader["RDB$PROCEDURE_SOURCE"]?.ToString()?.Trim() ?? "";

			writer.WriteLine($"CREATE PROCEDURE {procName}");
			writer.WriteLine(source);
			writer.WriteLine(";");
			writer.WriteLine();
		}
	}

	private static string GetFieldTypeName(int fieldType, object fieldLength, object precision,
		object scale)
	{
		int length = fieldLength != DBNull.Value ? Convert.ToInt32(fieldLength) : 0;
		int prec = precision != DBNull.Value ? Convert.ToInt32(precision) : 0;
		int scl = scale != DBNull.Value ? Convert.ToInt32(scale) : 0;

		return fieldType switch
		{
			7 => scl < 0 ? $"NUMERIC(15,{Math.Abs(scl)})" : "SMALLINT",
			8 => scl < 0 ? $"NUMERIC(18,{Math.Abs(scl)})" : "INTEGER",
			10 => "FLOAT",
			12 => "DATE",
			13 => "TIME",
			14 => $"CHAR({length})",
			16 => scl < 0 ? $"NUMERIC(18,{Math.Abs(scl)})" : "BIGINT",
			27 => "DOUBLE PRECISION",
			35 => "TIMESTAMP",
			37 => $"VARCHAR({length})",
			261 => "BLOB SUB_TYPE TEXT",
			_ => $"UNKNOWN_TYPE_{fieldType}"
		};
	}
}