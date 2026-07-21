namespace TVE.PureCQRS;

/// <summary>
/// Represents a void response. Commands (<see cref="IRequest"/>) are modelled as
/// <c>IRequest&lt;Unit&gt;</c> internally so they flow through the exact same pipeline
/// (behaviors, exception handling) as requests that return a value.
/// </summary>
public readonly struct Unit : IEquatable<Unit>, IComparable<Unit>, IComparable
{
    /// <summary>The single <see cref="Unit"/> value.</summary>
    public static readonly Unit Value = default;

    /// <summary>A completed task whose result is <see cref="Value"/>.</summary>
    public static Task<Unit> Task { get; } = System.Threading.Tasks.Task.FromResult(Value);

    /// <inheritdoc />
    public bool Equals(Unit other) => true;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Unit;

    /// <inheritdoc />
    public override int GetHashCode() => 0;

    /// <inheritdoc />
    public int CompareTo(Unit other) => 0;

    int IComparable.CompareTo(object? obj) => 0;

    /// <inheritdoc />
    public override string ToString() => "()";

    /// <summary>All <see cref="Unit"/> values are equal, so this is always <c>true</c>.</summary>
    public static bool operator ==(Unit left, Unit right) => true;

    /// <summary>All <see cref="Unit"/> values are equal, so this is always <c>false</c>.</summary>
    public static bool operator !=(Unit left, Unit right) => false;
}
