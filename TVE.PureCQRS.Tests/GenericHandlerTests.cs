namespace TVE.PureCQRS.Tests;

public class GenericHandlerTests
{
    [Fact]
    public async Task Open_generic_handler_is_closed_for_value_type_argument()
    {
        var (mediator, _) = TestHost.Build();

        var box = await mediator.Send(new WrapQuery<int>(7));

        Assert.Equal(7, box.Value);
    }

    [Fact]
    public async Task Open_generic_handler_is_closed_for_reference_type_argument()
    {
        var (mediator, _) = TestHost.Build();

        var box = await mediator.Send(new WrapQuery<string>("hi"));

        Assert.Equal("hi", box.Value);
    }

    [Fact]
    public async Task Different_closed_types_resolve_independently()
    {
        var (mediator, _) = TestHost.Build();

        var a = await mediator.Send(new WrapQuery<int>(1));
        var b = await mediator.Send(new WrapQuery<string>("x"));

        Assert.Equal(1, a.Value);
        Assert.Equal("x", b.Value);
    }

    [Fact]
    public async Task Constraint_satisfied_reference_type_is_closed()
    {
        var (mediator, _) = TestHost.Build();

        var widget = await mediator.Send(new MakeQuery<Widget>());

        Assert.Equal("widget", widget.Name);
    }

    [Fact]
    public async Task Constraint_violation_is_not_closed_and_throws()
    {
        var (mediator, _) = TestHost.Build();

        // MakeHandler<T> requires "where T : class, new()"; int is a value type, so no handler matches.
        await Assert.ThrowsAsync<HandlerNotFoundException>(
            () => mediator.Send(new MakeQuery<int>()));
    }
}
