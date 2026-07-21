namespace TVE.PureCQRS.Tests;

public class ExceptionHandlingTests
{
    [Fact]
    public async Task Handler_recovers_and_returns_fallback()
    {
        var (mediator, recorder) = TestHost.Build();

        var result = await mediator.Send(new RiskyQuery(ShouldThrow: true));

        Assert.Equal("fallback", result);
        Assert.Contains("recovered", recorder.Events);
    }

    [Fact]
    public async Task Action_runs_on_exception_as_side_effect()
    {
        var (mediator, recorder) = TestHost.Build();

        await mediator.Send(new RiskyQuery(ShouldThrow: true));

        Assert.Contains("action", recorder.Events);
    }

    [Fact]
    public async Task Happy_path_is_not_affected_by_exception_behaviors()
    {
        var (mediator, recorder) = TestHost.Build();

        var result = await mediator.Send(new RiskyQuery(ShouldThrow: false));

        Assert.Equal("ok", result);
        Assert.DoesNotContain("recovered", recorder.Events);
        Assert.DoesNotContain("action", recorder.Events);
    }

    [Fact]
    public async Task Unmatched_exception_propagates()
    {
        var (mediator, _) = TestHost.Build();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => mediator.Send(new UnrecoverableQuery()));
    }

    [Fact]
    public async Task Missing_handler_throws_handler_not_found()
    {
        var (mediator, _) = TestHost.Build();

        await Assert.ThrowsAsync<HandlerNotFoundException>(
            () => mediator.Send(new OrphanQuery()));
    }
}
