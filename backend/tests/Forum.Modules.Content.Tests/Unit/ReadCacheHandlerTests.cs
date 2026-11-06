using FluentValidation;
using Forum.Common.Paging;
using Forum.Common.Security;
using Forum.Common.Telemetry;
using Forum.Modules.Content.Application;
using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Application.Caching;
using Forum.Modules.Content.Application.Categories;
using Forum.Modules.Content.Application.Comments;
using Forum.Modules.Content.Application.Paging;
using Forum.Modules.Content.Application.Threads;
using Forum.Modules.Content.Domain.Categories;
using Forum.Modules.Content.Domain.Comments;
using Forum.SharedKernel.Results;
using Forum.TestUtilities;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Forum.Modules.Content.Tests.Unit;

/// <summary>
/// The handlers' side of the read-cache contract: the read gate runs BEFORE any cache call (a rejected reader never
/// reaches the cache), the search cursor must match the requested sort, and every write handler flushes exactly the
/// §3 row it owns (<c>docs/specs/READ-CACHE-CONTRACT.md</c>).
/// </summary>
public sealed class ReadCacheHandlerTests
{
    private static readonly Ulid Owner = Ulid.NewUlid();
    private static readonly Ulid PrivateCategory = Ulid.NewUlid();
    private static readonly Ulid ThreadId = Ulid.NewUlid();

    [Fact]
    public async Task Feed_of_a_private_category_is_refused_before_the_cache_is_consulted()
    {
        var queries = Substitute.For<IContentQueries>();
        queries.GetCategoryReadInfoAsync(PrivateCategory, Arg.Any<CancellationToken>())
            .Returns(new CategoryReadInfo(PrivateCategory, Owner, IsPrivate: true));
        var cache = Substitute.For<IContentReadCache>();
        var handler = new GetThreadFeedQueryHandler(queries, AnonymousReadAccess(queries), cache);

        var result = await handler.Handle(new GetThreadFeedQuery(PrivateCategory, null, 20), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.NotFound);
        cache.ReceivedCalls().ShouldBeEmpty("the gate decides first; a cached page of a private category never leaks");
    }

    [Fact]
    public async Task Comment_tree_of_a_private_category_is_refused_before_the_cache_is_consulted()
    {
        var queries = Substitute.For<IContentQueries>();
        queries.GetThreadCategoryReadInfoAsync(ThreadId, Arg.Any<CancellationToken>())
            .Returns(new CategoryReadInfo(PrivateCategory, Owner, IsPrivate: true));
        var cache = Substitute.For<IContentReadCache>();
        var handler = new GetCommentTreeQueryHandler(queries, AnonymousReadAccess(queries), cache);

        var result = await handler.Handle(new GetCommentTreeQuery(ThreadId), CancellationToken.None);

        result.Error.Type.ShouldBe(ErrorType.NotFound);
        cache.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Public_feed_goes_to_the_cache_with_the_decoded_cursor_and_clamped_limit()
    {
        var category = Ulid.NewUlid();
        var queries = Substitute.For<IContentQueries>();
        queries.GetCategoryReadInfoAsync(category, Arg.Any<CancellationToken>())
            .Returns(new CategoryReadInfo(category, Owner, IsPrivate: false));
        var cache = Substitute.For<IContentReadCache>();
        var page = new CursorPage<ThreadFeedItemResponse>([], null, false);
        cache.GetFeedPageAsync(category, Arg.Any<ThreadFeedCursor?>(), 50, Arg.Any<CancellationToken>()).Returns(page);
        var cursor = new ThreadFeedCursor(true, DateTimeOffset.UtcNow, Ulid.NewUlid());
        var handler = new GetThreadFeedQueryHandler(queries, AnonymousReadAccess(queries), cache);

        var result = await handler.Handle(new GetThreadFeedQuery(category, cursor.Encode(), 500), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeSameAs(page);
        await cache.Received(1).GetFeedPageAsync(category, cursor, 50, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Search_rejects_a_cursor_issued_for_the_other_sort_without_touching_the_cache()
    {
        var cache = Substitute.For<IContentReadCache>();
        var handler = new SearchThreadsQueryHandler(
            AnonymousReadAccess(Substitute.For<IContentQueries>()), cache, AnonymousUser());
        var relevanceCursor = ThreadSearchCursor.ForRelevance(0.5f, DateTimeOffset.UtcNow, Ulid.NewUlid()).Encode();

        var result = await handler.Handle(
            new SearchThreadsQuery("kubernetes", ThreadSearchSort.Newest, relevanceCursor, 20), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ContentErrors.CursorSortMismatch);
        cache.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Anonymous_search_uses_the_anonymous_scope_and_an_empty_readable_set()
    {
        var cache = Substitute.For<IContentReadCache>();
        var page = new CursorPage<ThreadFeedItemResponse>([], null, false);
        cache.SearchAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ThreadSearchSort>(), Arg.Any<ThreadSearchCursor?>(),
                Arg.Any<int>(), Arg.Any<IReadOnlyCollection<Ulid>>(), Arg.Any<CancellationToken>())
            .Returns(page);
        var queries = Substitute.For<IContentQueries>();
        var handler = new SearchThreadsQueryHandler(AnonymousReadAccess(queries), cache, AnonymousUser());

        var result = await handler.Handle(
            new SearchThreadsQuery("  kubernetes ", ThreadSearchSort.Newest, null, 20), CancellationToken.None);

        result.Value.ShouldBeSameAs(page);
        await cache.Received(1).SearchAsync(
            ReadCacheScope.Anonymous, "kubernetes", ThreadSearchSort.Newest, null, 20,
            Arg.Is<IReadOnlyCollection<Ulid>>(static readable => readable.Count == 0), Arg.Any<CancellationToken>());
        // An anonymous reader never resolves a permission: the private-category list is not even fetched.
        await queries.DidNotReceive().ListPrivateCategoriesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_moderator_of_a_private_category_searches_in_a_scope_that_names_that_category()
    {
        var moderator = Ulid.NewUlid();
        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(true);
        user.Id.Returns(moderator);
        user.HasPermissionAsync(Permissions.Moderate, PermissionScopes.Category, PrivateCategory, Arg.Any<CancellationToken>())
            .Returns(true);
        var queries = Substitute.For<IContentQueries>();
        queries.ListPrivateCategoriesAsync(Arg.Any<CancellationToken>())
            .Returns([new CategoryReadInfo(PrivateCategory, Owner, true)]);
        var cache = Substitute.For<IContentReadCache>();
        cache.SearchAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ThreadSearchSort>(), Arg.Any<ThreadSearchCursor?>(),
                Arg.Any<int>(), Arg.Any<IReadOnlyCollection<Ulid>>(), Arg.Any<CancellationToken>())
            .Returns(new CursorPage<ThreadFeedItemResponse>([], null, false));
        var handler = new SearchThreadsQueryHandler(new ContentReadAccess(user, queries), cache, user);

        await handler.Handle(new SearchThreadsQuery("q", ThreadSearchSort.Relevance, null, 20), CancellationToken.None);

        // The scope is the user id + the readable set: a revocation changes the key (the §3 join/leave row on A).
        await cache.Received(1).SearchAsync(
            $"{moderator}+{PrivateCategory}", "q", ThreadSearchSort.Relevance, null, 20,
            Arg.Is<IReadOnlyCollection<Ulid>>(readable => readable.Single() == PrivateCategory), Arg.Any<CancellationToken>());
        ReadCacheScope.Of(moderator, []).ShouldBe(moderator.ToString());
    }

    [Fact]
    public async Task Creating_a_comment_flushes_only_that_threads_tree_after_the_write_is_durable()
    {
        var thread = Thread.Create(PrivateCategory, Owner, "title", "body");
        var threads = Substitute.For<IThreadRepository>();
        threads.GetByIdAsync(thread.Id, Arg.Any<CancellationToken>()).Returns(thread);
        var user = OwnerUser();
        var cache = Substitute.For<IContentReadCache>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var order = new List<string>();
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            order.Add("save");
            return Task.FromResult(1);
        });
#pragma warning disable CA2012 // NSubstitute's When() needs the call expression, not its ValueTask
        cache.When(static c => c.CommentWrittenAsync(Arg.Any<Ulid>())).Do(_ => order.Add("flush"));
#pragma warning restore CA2012
        var handler = new CreateCommentCommandHandler(
            new CreateCommentCommandValidator(), threads, Substitute.For<ICommentRepository>(), user,
            Substitute.For<IOutboxWriter>(), unitOfWork, cache, TimeProvider.System, TestMetrics.Create());

        var result = await handler.Handle(new CreateCommentCommand(thread.Id, null, "hello"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await cache.Received(1).CommentWrittenAsync(thread.Id);
        await cache.DidNotReceive().ThreadChangedAsync();
        await cache.DidNotReceive().CommentDeletedAsync();
        order.ShouldBe(["save", "flush"], "the flush must follow the durable write");
    }

    [Fact]
    public async Task Deleting_a_comment_flushes_feeds_and_trees_but_not_search()
    {
        var thread = Thread.Create(PrivateCategory, Owner, "title", "body");
        var comment = Comment.CreateRoot(thread.Id, Owner, "to delete");
        var threads = Substitute.For<IThreadRepository>();
        threads.GetByIdAsync(thread.Id, Arg.Any<CancellationToken>()).Returns(thread);
        var comments = Substitute.For<ICommentRepository>();
        comments.GetByIdAsync(comment.Id, Arg.Any<CancellationToken>()).Returns(comment);
        var cache = Substitute.For<IContentReadCache>();
        var handler = new DeleteCommentCommandHandler(
            comments, threads, OwnerUser(), Substitute.For<IOutboxWriter>(), Substitute.For<IUnitOfWork>(), cache, TimeProvider.System);

        var result = await handler.Handle(new DeleteCommentCommand(comment.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await cache.Received(1).CommentDeletedAsync();
        await cache.DidNotReceive().ThreadChangedAsync();
        await cache.DidNotReceive().CategoryChangedAsync();
    }

    [Fact]
    public async Task Thread_writes_flush_everything()
    {
        var thread = Thread.Create(PrivateCategory, Owner, "title", "body");
        var threads = Substitute.For<IThreadRepository>();
        threads.GetByIdAsync(thread.Id, Arg.Any<CancellationToken>()).Returns(thread);
        var cache = Substitute.For<IContentReadCache>();
        var user = OwnerUser();
        var outbox = Substitute.For<IOutboxWriter>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var update = new UpdateThreadCommandHandler(
            new UpdateThreadCommandValidator(), threads, user, outbox, unitOfWork, cache, TimeProvider.System);
        (await update.Handle(new UpdateThreadCommand(thread.Id, "new title", "new body"), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        var pin = new PinThreadCommandHandler(threads, user, outbox, unitOfWork, cache, TimeProvider.System);
        (await pin.Handle(new PinThreadCommand(thread.Id, true), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        var delete = new DeleteThreadCommandHandler(threads, user, outbox, unitOfWork, cache, TimeProvider.System);
        (await delete.Handle(new DeleteThreadCommand(thread.Id), CancellationToken.None)).IsSuccess.ShouldBeTrue();

        await cache.Received(3).ThreadChangedAsync();
        await cache.DidNotReceive().CommentDeletedAsync();
        await cache.DidNotReceive().CommentWrittenAsync(Arg.Any<Ulid>());
    }

    [Fact]
    public async Task Creating_a_category_flushes_nothing_while_updating_one_flushes_everything()
    {
        var category = Category.Create("slug", "Name", "desc", Visibility.Public, Owner);
        var categories = Substitute.For<ICategoryRepository>();
        categories.GetBySlugAsync("slug", Arg.Any<CancellationToken>()).Returns(category);
        categories.SlugExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        var cache = Substitute.For<IContentReadCache>();
        var user = OwnerUser();

        var create = new CreateCategoryCommandHandler(
            new CreateCategoryCommandValidator(), categories, user, Substitute.For<IOutboxWriter>(),
            Substitute.For<IUnitOfWork>(), TimeProvider.System);
        (await create.Handle(new CreateCategoryCommand("fresh", "Fresh", "", Visibility.Public), CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
        cache.ReceivedCalls().ShouldBeEmpty("a new category has no threads and the category list is never cached");

        var update = new UpdateCategoryCommandHandler(
            new UpdateCategoryCommandValidator(), categories, user, Substitute.For<IUnitOfWork>(), cache);
        (await update.Handle(new UpdateCategoryCommand("slug", "Renamed", "", Visibility.Private), CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
        await cache.Received(1).CategoryChangedAsync();
    }

    // ---- fixtures ----

    private static ContentReadAccess AnonymousReadAccess(IContentQueries queries) => new(AnonymousUser(), queries);

    private static ICurrentUser AnonymousUser()
    {
        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(false);
        user.Id.Returns((Ulid?)null);
        return user;
    }

    private static ICurrentUser OwnerUser()
    {
        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(true);
        user.Id.Returns(Owner);
        user.IsOwner(Owner).Returns(true);
        user.HasPermissionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Ulid?>(), Arg.Any<CancellationToken>())
            .Returns(true);
        return user;
    }
}
