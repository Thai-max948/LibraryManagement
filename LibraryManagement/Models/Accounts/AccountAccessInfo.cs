namespace LibraryManagement.Models
{
    public sealed record AccountAccessInfo(
        string Role,
        string AccessLevel,
        bool CanManageCatalog,
        bool CanManageReaders,
        bool CanUseCirculation,
        bool CanViewFeesAndHistory,
        bool CanManageUsers);
}
