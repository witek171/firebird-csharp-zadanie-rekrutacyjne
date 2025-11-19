using System;
using System.IO;
using FirebirdSql.Data.FirebirdClient;

namespace DbMetaTool;

public static class Program
{
	// Przykładowe wywołania:
	// DbMetaTool build-db --db-dir "C:\db\fb5" --scripts-dir "C:\scripts"
	// DbMetaTool export-scripts --connection-string "..." --output-dir "C:\out"
	// DbMetaTool update-db --connection-string "..." --scripts-dir "C:\scripts"
	public static int Main(string[] args)
	{
		if (args.Length == 0)
		{
			Console.WriteLine("Użycie:");
			Console.WriteLine("  build-db --db-dir <ścieżka> --scripts-dir <ścieżka>");
			Console.WriteLine("  export-scripts --connection-string <connStr> --output-dir <ścieżka>");
			Console.WriteLine("  update-db --connection-string <connStr> --scripts-dir <ścieżka>");
			return 1;
		}

		try
		{
			var command = args[0].ToLowerInvariant();

			switch (command)
			{
				case "build-db":
				{
					string dbDir = GetArgValue(args, "--db-dir");
					string scriptsDir = GetArgValue(args, "--scripts-dir");

					BuildDatabase(dbDir, scriptsDir);
					Console.WriteLine("Baza danych została zbudowana pomyślnie.");
					return 0;
				}

				case "export-scripts":
				{
					string connStr = GetArgValue(args, "--connection-string");
					string outputDir = GetArgValue(args, "--output-dir");

					ExportScripts(connStr, outputDir);
					Console.WriteLine("Skrypty zostały wyeksportowane pomyślnie.");
					return 0;
				}

				case "update-db":
				{
					string connStr = GetArgValue(args, "--connection-string");
					string scriptsDir = GetArgValue(args, "--scripts-dir");

					UpdateDatabase(connStr, scriptsDir);
					Console.WriteLine("Baza danych została zaktualizowana pomyślnie.");
					return 0;
				}

				default:
					Console.WriteLine($"Nieznane polecenie: {command}");
					return 1;
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine("Błąd: " + ex.Message);
			return -1;
		}
	}

	private static string GetArgValue(string[] args, string name)
	{
		int idx = Array.IndexOf(args, name);
		if (idx == -1 || idx + 1 >= args.Length)
			throw new ArgumentException($"Brak wymaganego parametru {name}");
		return args[idx + 1];
	}

	/// <summary>
	/// Buduje nową bazę danych Firebird 5.0 na podstawie skryptów.
	/// </summary>
	public static void BuildDatabase(string databaseDirectory, string scriptsDirectory)
	{
		string databasePath = Path.Combine(databaseDirectory, FirebirdHelper.DefaultDbFileName);

		string connectionString =
			$"User={FirebirdHelper.DefaultUser};Password={FirebirdHelper.DefaultPassword};Database={databasePath};DataSource=localhost;";

		try
		{
			FirebirdHelper.CreateDatabase(databasePath);
			FirebirdHelper.ExecuteScripts(connectionString, scriptsDirectory);
		}
		catch (Exception ex)
		{
			Console.WriteLine(ex is FbException fb
				? $"Błąd SQL: {fb.Message} (SQLSTATE: {fb.SQLSTATE})"
				: $"Błąd: {ex.Message}");

			string dbDir = Path.GetDirectoryName(databasePath)!;
			if (Directory.Exists(dbDir))
				Directory.Delete(dbDir, recursive: true);

			Environment.Exit(1);
		}
	}

	/// <summary>
	/// Generuje skrypty metadanych z istniejącej bazy danych Firebird 5.0.
	/// </summary>
	public static void ExportScripts(string connectionString, string outputDirectory)
	{
		try
		{
			if (!Directory.Exists(outputDirectory))
			{
				Directory.CreateDirectory(outputDirectory);
				Console.WriteLine($"Utworzono katalog wyjściowy: {outputDirectory}");
			}

			FirebirdHelper.ExportAllDdlUsingFbRestore(connectionString, outputDirectory);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Błąd: {ex.Message}");
			Environment.Exit(1);
		}
	}

	/// <summary>
	/// Aktualizuje istniejącą bazę danych Firebird 5.0 na podstawie skryptów.
	/// </summary>
	public static void UpdateDatabase(string connectionString, string scriptsDirectory)
	{
		// TODO:
		// 1) Połącz się z bazą danych przy użyciu connectionString.
		// 2) Wykonaj skrypty z katalogu scriptsDirectory (tylko obsługiwane elementy).
		// 3) Zadbaj o poprawną kolejność i bezpieczeństwo zmian.
		throw new NotImplementedException();
	}
}