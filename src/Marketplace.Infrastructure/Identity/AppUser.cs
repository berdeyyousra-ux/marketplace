using Microsoft.AspNetCore.Identity;

namespace Marketplace.Infrastructure.Identity;

// The Identity user lives in Infrastructure; Domain and Application refer to users by Guid only (D-43).
public sealed class AppUser : IdentityUser<Guid>
{
    public const int DisplayNameMaxLength = 80;

    public AppUser()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString();
    }

    public string DisplayName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }
}
