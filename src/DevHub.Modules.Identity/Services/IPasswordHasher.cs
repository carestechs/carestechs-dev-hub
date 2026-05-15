namespace DevHub.Modules.Identity.Services;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string encoded);
}
