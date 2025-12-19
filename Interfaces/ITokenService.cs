using simpleShare_backend.Models.Identity;

namespace simpleShare_backend.Interfaces
{
    public interface ITokenService
    {
        string CreateToken(User user, IList<string> roles);
    }
}