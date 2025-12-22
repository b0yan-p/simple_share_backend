namespace simpleShare_backend.Models;

public abstract class BaseModel<TId>
{
    public TId Id { get; set; } = default!;
}
