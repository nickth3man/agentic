using Microsoft.EntityFrameworkCore;

namespace Agentic.Chat.Data;

public sealed class ChatDbContext(DbContextOptions<ChatDbContext> options) : DbContext(options)
{
    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<Message> Messages => Set<Message>();

    public static string GetDefaultDatabasePath() => ChatDatabase.GetDefaultFilePath();

    public static string BuildSqliteConnectionString(string filePath)
        => ChatDatabase.ToConnectionString(filePath);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Title).HasMaxLength(200).IsRequired();
            entity.Property(c => c.Model).HasMaxLength(200).IsRequired();
            entity.HasIndex(c => c.UpdatedAt);
            entity.HasMany(c => c.Messages)
                .WithOne(m => m.Conversation)
                .HasForeignKey(m => m.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(m => m.Id);
            entity.Property(m => m.Role).HasMaxLength(32).IsRequired();
            entity.Property(m => m.Content).IsRequired();
            entity.HasIndex(m => new { m.ConversationId, m.CreatedAt });
        });
    }
}
