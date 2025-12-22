
namespace simpleShare_backend.Models.Base;

public interface IModifiable
{
    DateTime? ModifiedAt { get; set; }
}
