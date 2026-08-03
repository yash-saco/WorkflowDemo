namespace WorkflowDemo.Api;

public sealed record Employee(string Id, string Name, string Role, string? ManagerId);

/// <summary>
/// Org hierarchy lookup. In production, back this with Azure AD / Entra, Workday, or your HR DB —
/// the rest of the system only depends on this interface.
/// </summary>
public interface IDirectory
{
    IEnumerable<Employee> All { get; }
    Employee? Get(string id);
    /// <summary>Walks the manager chain: levels=1 → N+1, levels=2 → N+2. Null if the chain ends.</summary>
    Employee? ManagerOf(string id, int levels);
}

public sealed class InMemoryDirectory : IDirectory
{
    private readonly Dictionary<string, Employee> _byId;

    public InMemoryDirectory()
    {
        var people = new[]
        {
            new Employee("u1", "Alice Chen",  "Team Member",  "u2"),
            new Employee("u2", "Bob Kumar",   "Manager",      "u3"),
            new Employee("u3", "Carol Diaz",  "IT Director",  "u4"),
            new Employee("u4", "Dana Osei",   "CEO",          null),
            new Employee("u5", "Evan Park",   "Team Member",  "u2"),
            new Employee("u6", "Fiona Grey",  "Finance",      "u4"),
            new Employee("u7", "Hana Suzuki", "HR",           "u4"),
        };
        _byId = people.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IEnumerable<Employee> All => _byId.Values;

    public Employee? Get(string id) => _byId.TryGetValue(id, out var e) ? e : null;

    public Employee? ManagerOf(string id, int levels)
    {
        var current = Get(id);
        for (var i = 0; i < levels && current is not null; i++)
            current = current.ManagerId is null ? null : Get(current.ManagerId);
        return current;
    }
}
