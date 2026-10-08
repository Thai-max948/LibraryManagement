namespace LibraryManagement.Data;

// Keep startup DDL in dependency order before any MainWindow ViewModel can query the database.
public static class LibraryDatabaseStartupMigration
{
    public static void Apply() => ApplyWithResult();

    public static BookCopyMigrationResult ApplyWithResult()
    {
        BookValueMigration.Apply();
        BookAuditMigration.Apply();
        var bookCopyMigration = BookCopyMigration.Apply();
        LecturerReaderMigration.Apply();
        LoanPolicyMigration.Apply();
        ReturnOutcomeMigration.Apply();
        CirculationAuditMigration.Apply();
        HistorySchemaMigration.Apply();
        FeeMigration.Apply();
        NotificationMigration.Apply();
        return bookCopyMigration;
    }
}
