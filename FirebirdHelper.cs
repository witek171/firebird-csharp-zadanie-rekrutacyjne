using System.Text.RegularExpressions;
using FirebirdSql.Data.FirebirdClient;
using FirebirdSql.Data.Services;

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

		DisposableServiceWrapper<FbBackup> backupWrapper = null;
		DisposableServiceWrapper<FbRestore> restoreWrapper = null;

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

			Console.WriteLine("  > Krok 2/2: Przywracanie metadanych DDL do pliku TXT...");

			string ddlConnectionString = new FbConnectionStringBuilder()
			{
				DataSource = builder.DataSource,
				Database = filePath,
				UserID = builder.UserID,
				Password = builder.Password,
			}.ToString();

			restoreWrapper = new DisposableServiceWrapper<FbRestore>(new FbRestore(ddlConnectionString));

			restoreWrapper.Service.BackupFiles.Add(new FbBackupFile(tempBackupFile));

			restoreWrapper.Service.Options = FbRestoreFlags.MetaDataOnly;

			restoreWrapper.Service.Verbose = true;

			restoreWrapper.Service.Execute();

			Console.WriteLine($"  > Eksport DDL pomyślnie zapisany do {outputFileName}");
		}
		catch (Exception ex)
		{
			throw new Exception(
				$"Błąd eksportu przy użyciu FbRestore do pliku {Path.GetFileName(filePath)}: {ex.Message}", ex);
		}
		finally
		{
			backupWrapper?.Dispose();
			restoreWrapper?.Dispose();

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
}