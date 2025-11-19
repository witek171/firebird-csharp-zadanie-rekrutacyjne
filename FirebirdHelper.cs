using System.Text.RegularExpressions;
using FirebirdSql.Data.FirebirdClient;

namespace DbMetaTool;

public static class FirebirdHelper
{
	public const string DefaultUser = "SYSDBA";
	public const string DefaultPassword = "masterkey";
	public const string DefaultDbFileName = "recruit_db.fdb";

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

		using FbConnection dbConnection = new(connectionString);
		dbConnection.Open();

		foreach (string filePath in scriptFiles)
		{
			string fileName = Path.GetFileName(filePath);
			string sqlScript = File.ReadAllText(filePath);

			string[] commands = Regex.Split(sqlScript, @"^\s*;\s*$",
				RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

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
}