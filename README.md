# DbMetaTool

## Opis Aplikacji

**DbMetaTool** to **aplikacja konsolowa** stworzona w **.NET 8** do zarządzania schematami bazy danych **Firebird 5**. Umożliwia ona **budowanie nowej bazy danych** od podstaw, **aktualizowanie istniejącej bazy** za pomocą skryptów SQL oraz **eksportowanie metadanych (DDL)**, takich jak Domeny, Tabele i Procedury Składowane, do pliku tekstowego.

---

## Wymagania

* Środowisko uruchomieniowe **.NET 8**
* Dostęp do serwera bazy danych **Firebird** (wersja 5 lub kompatybilna)
* Wymaga biblioteki **FirebirdSql.Data.FirebirdClient**.

---

## Domyślne Ustawienia

Aplikacja używa następujących domyślnych poświadczeń i nazwy pliku bazy danych dla operacji `build-db`:

* **Użytkownik:** `SYSDBA`
* **Hasło:** `masterkey`
* **Domyślna nazwa pliku DB:** `default_db.fdb`

---
## Wymagane Pliki/Katalogi
* Poprawny ciąg połączenia (np. User=SYSDBA;Password=masterkey;Database=C:\path\to\db.fdb;DataSource=localhost;).
* --db-dir	Katalog, w którym zostanie utworzony nowy plik bazy danych (default_db.fdb).	Katalog zostanie utworzony, jeśli nie istnieje.
* --scripts-dir	Katalog zawierający skrypty (pliki lub plik tekstowy) do wykonania. Są one wykonywane w porządku alfabetycznym nazw plików.

## Instrukcja Uruchomienia

* Ponieważ udostępniany jest kod źródłowy (.csproj), narzędzie należy uruchamiać za pomocą polecenia **`dotnet run`** z katalogu głównego projektu, w którym znajduje się plik **`DbMetaTool.csproj`**.

* Poniżej znajdują się gotowe polecenia do wklejenia i użycia:

1. Budowanie nowej bazy (build-db):

```bash 
dotnet run -- build-db --db-dir "C:\db\fb5" --scripts-dir "C:\scripts"
```


2. Eksport metadanych DDL (export-scripts):

```bash
dotnet run -- export-scripts --connection-string "User=SYSDBA;Password=masterkey;Database=C:\db\fb5\default_db.FDB;DataSource=localhost;" --output-dir "C:\out"
```


3. Aktualizacja istniejącej bazy (update-db):

```bash
dotnet run -- update-db --connection-string "User=SYSDBA;Password=masterkey;Database=C:\db\fb5\default_db.FDB;DataSource=localhost;" --scripts-dir "C:\scripts"
```

## Obsługa Błędów
* Jeśli skrypty w katalogu są puste aplikacja zgłosi błąd.

* W przypadku błędu SQL podczas wykonywania skryptu, cała transakcja dla tego skryptu jest wycofywana (ROLLBACK), a aplikacja kończy działanie z błędem.

* Jeśli operacja build-db zakończy się niepowodzeniem, utworzony plik bazy danych i jego katalog zostaną usunięte.
