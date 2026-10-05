namespace Tadmor.Services;

// The generic services seen without their type parameters, so the UI can
// pick one by its collection name (registered as keyed services).

public interface IDocuments
{
    DocKind Kind { get; }
    Task<List<Dictionary<string, object?>>> ListAsync();
    Task<Dictionary<string, object?>> GetAsync(int id);
    Task<List<Dictionary<string, object?>>> LinesAsync(int id);
    Task<int> CreateAsync(Input input);
    Task UpdateAsync(int id, Input input);
    Task DeleteAsync(int id);
    Task<int> PostAsync(int id);
    Task<int> UnpostAsync(int id);
}

public interface IPayments
{
    PayKind Kind { get; }
    Task<List<Dictionary<string, object?>>> ListAsync();
    Task<Dictionary<string, object?>> GetAsync(int id);
    Task<int> CreateAsync(Input input);
    Task UpdateAsync(int id, Input input);
    Task DeleteAsync(int id);
    Task<int> PostAsync(int id);
    Task<int> UnpostAsync(int id);
}

public interface IOrders
{
    OrderKind Kind { get; }
    Task<List<Dictionary<string, object?>>> ListAsync();
    Task<Dictionary<string, object?>> GetAsync(int id);
    Task<List<Dictionary<string, object?>>> LinesAsync(int id);
    Task<int> CreateAsync(Input input);
    Task UpdateAsync(int id, Input input);
    Task DeleteAsync(int id);
    Task ConfirmAsync(int id);
    Task CloseAsync(int id);
    Task CancelAsync(int id);
    Task<int> ChargeAsync(int id, Input input);
    Task<List<int>> MoveAsync(int id, Input input);
}
