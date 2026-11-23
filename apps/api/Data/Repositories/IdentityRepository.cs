using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Data.Repositories;

public sealed class IdentityRepository(ErpDbContext db) : IIdentityRepository
{
    public User? FindActiveUserByEmail(string email) =>
        db.Users.FirstOrDefault(u => u.Email == email && u.Active);

    public User? FindActiveUserById(string userId) =>
        db.Users.FirstOrDefault(u => u.Id == userId && u.Active);

    public Role RoleById(string roleId) => db.Roles.First(r => r.Id == roleId);

    public Membership MembershipFor(string userId, string tenantId, string legalEntityId) =>
        db.Memberships.First(m => m.UserId == userId && m.TenantId == tenantId && m.LegalEntityId == legalEntityId);

    public int Save() => db.SaveChanges();
}
