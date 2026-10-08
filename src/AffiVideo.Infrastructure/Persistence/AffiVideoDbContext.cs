using Microsoft.EntityFrameworkCore;

namespace AffiVideo.Infrastructure.Persistence;

public sealed class AffiVideoDbContext(DbContextOptions<AffiVideoDbContext> options) : DbContext(options);
