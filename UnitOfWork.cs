using Domain.Shared;
using Infrastructure.EventDispatching;

namespace Infrastructure.DataAccess;

/// <summary>
/// Unit of work.
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly EFDbContext context;
    private readonly InternalDomainEventDispatcher<Guid> internalDomainEventDispatcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="UnitOfWork"/> class.
    /// </summary>
    /// <param name="context">context.</param>
    /// <param name="internalDomainEventDispatcher">internalDomainEventDispatcher.</param>
    public UnitOfWork(EFDbContext context, InternalDomainEventDispatcher<Guid> internalDomainEventDispatcher)
    {
        this.context = context;
        this.internalDomainEventDispatcher = internalDomainEventDispatcher;
    }

    /// <inheritdoc/>
    public async Task SaveAsync()
    {
        using var transaction = await this.context.Database.BeginTransactionAsync();

        var modifiedEntries = this.context.ChangeTracker.Entries<IHasDomainEvents<Guid>>().ToList();
        var events = modifiedEntries.SelectMany(x => x.Entity.UncommittedChanges()).ToList().AsReadOnly();
        var eventHashCodes = new List<int>();

        this.EventsDispatcher(events, ref eventHashCodes);

        this.context.ChangeTracker.Entries<IHasDomainEvents<Guid>>().ToList().ForEach(x => x.Entity.MarkChangesAsCommitted());

        await this.context.SaveChangesAsync();

        await transaction.CommitAsync();
    }

    /// <summary>
    /// EventsDispatcher
    /// </summary>
    /// <param name="events">events.</param>
    /// <param name="eventHashCodes">eventHashCodes.</param>
    protected void EventsDispatcher(IReadOnlyList<DomainEvent<Guid>> events, ref List<int> eventHashCodes)
    {
        var eventsForDispatch = new List<DomainEvent<Guid>>();

        foreach (var ev in events)
        {
            if (eventHashCodes.Contains(ev.GetHashCode()))
            {
                continue;
            }

            eventHashCodes.Add(ev.GetHashCode());
            eventsForDispatch.Add(ev);
        }

        if (eventsForDispatch.Any())
        {
            this.internalDomainEventDispatcher.Dispatch(eventsForDispatch, this.context);

            var newEvents = this.context.ChangeTracker.Entries<IHasDomainEvents<Guid>>().SelectMany(x => x.Entity.UncommittedChanges()).ToList().AsReadOnly();
            this.EventsDispatcher(newEvents, ref eventHashCodes);
        }

        return;
    }
}