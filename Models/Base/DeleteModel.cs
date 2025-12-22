namespace simpleShare_backend.Models.Base;

public interface IDeletable
{
    DateTime? DeletedAt { get; set; }
}
