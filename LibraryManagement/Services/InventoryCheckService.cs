using LibraryManagement.Models;
using LibraryManagement.Repositories;

namespace LibraryManagement.Services;

public sealed class InventoryCheckService
{
    private readonly InventoryCheckRepository _repository;
    public InventoryCheckService() : this(new InventoryCheckRepository()) { }
    public InventoryCheckService(InventoryCheckRepository repository) => _repository = repository;
    public List<InventoryCheckRow> Check() => _repository.GetReport().Where(row => row.NeedsReview).ToList();
}
