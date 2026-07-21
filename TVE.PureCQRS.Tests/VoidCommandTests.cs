namespace TVE.PureCQRS.Tests;

public class VoidCommandTests
{
    [Fact]
    public async Task Void_command_reaches_handler_through_pipeline()
    {
        var (mediator, recorder) = TestHost.Build(cfg => cfg.AddOpenBehavior(typeof(TokenProbeBehavior<,>)));

        await mediator.Send(new DoWork(Fail: false));

        Assert.Contains("work", recorder.Events);
        Assert.Contains("behavior:DoWork", recorder.Events); // pipeline behavior ran for the void command
    }

    [Fact]
    public async Task Void_command_exception_is_recovered()
    {
        var (mediator, recorder) = TestHost.Build();

        // Recovered by IRequestExceptionHandler<DoWork, Unit, InvalidOperationException>; no throw.
        await mediator.Send(new DoWork(Fail: true));

        Assert.Contains("void-recovered", recorder.Events);
    }

    [Fact]
    public async Task Void_command_via_object_overload_works()
    {
        var (mediator, recorder) = TestHost.Build();

        object command = new DoWork(Fail: false);
        await mediator.Send(command);

        Assert.Contains("work", recorder.Events);
    }
}
