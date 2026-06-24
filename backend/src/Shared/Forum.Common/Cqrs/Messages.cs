namespace Forum.Common.Cqrs;

/// <summary>Marker for a command that returns no value (only success/failure).</summary>
public interface ICommand;

/// <summary>Marker for a command that returns <typeparamref name="TResponse"/> on success.</summary>
public interface ICommand<TResponse>;

/// <summary>Marker for a read-only query returning <typeparamref name="TResponse"/>.</summary>
public interface IQuery<TResponse>;
