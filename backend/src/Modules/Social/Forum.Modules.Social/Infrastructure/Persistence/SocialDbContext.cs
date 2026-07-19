using Forum.Infrastructure.Messaging;
using Forum.Infrastructure.Messaging.Inbox;
using Forum.Infrastructure.Messaging.Outbox;
using Forum.Infrastructure.Persistence;
using Forum.Modules.Social.Domain.Conversations;
using Forum.Modules.Social.Domain.Friendships;
using Forum.Modules.Social.Domain.Groups;
using Forum.Modules.Social.Domain.Notifications;
using Forum.Modules.Social.Domain.Presence;
using Forum.Modules.Social.Domain.Privacy;
using Forum.Modules.Social.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;

namespace Forum.Modules.Social.Infrastructure.Persistence;

/// <summary>
/// The Social module's unit of work. Owns the <c>forum_social</c> schema (friendships, blocks, groups,
/// memberships, invites, conversations, participants, messages, notifications, privacy, presence, outbox/inbox).
/// User references are logical ULIDs into <c>forum_identity</c> — no cross-schema FK exists here; the read views
/// join users at view level (Content precedent).
/// </summary>
internal sealed class SocialDbContext : ForumDbContext
{
    public const string Schema = "forum_social";

    public SocialDbContext(DbContextOptions<SocialDbContext> options, IDomainEventDispatcher dispatcher)
        : base(options, dispatcher)
    {
    }

    public DbSet<Friendship> Friendships => Set<Friendship>();

    public DbSet<SocialBlock> SocialBlocks => Set<SocialBlock>();

    public DbSet<Group> Groups => Set<Group>();

    public DbSet<GroupMembership> GroupMemberships => Set<GroupMembership>();

    public DbSet<GroupInvite> GroupInvites => Set<GroupInvite>();

    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<ConversationParticipant> ConversationParticipants => Set<ConversationParticipant>();

    public DbSet<Message> Messages => Set<Message>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<UserPrivacySettings> PrivacySettings => Set<UserPrivacySettings>();

    public DbSet<UserPresence> Presence => Set<UserPresence>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(SocialDbContext).Assembly,
            static type => type.Namespace == typeof(FriendshipConfiguration).Namespace);
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());

        base.OnModelCreating(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<Ulid>()
            .HaveConversion<UlidToStringConverter>()
            .HaveMaxLength(26)
            .AreUnicode(false);
}
