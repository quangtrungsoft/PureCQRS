namespace TVE.PureCQRS.Tests;

public class CancellationTokenTests
{
    [Fact]
    public async Task Token_flows_through_behavior_and_handler()
    {
        var (mediator, recorder) = TestHost.Build(cfg => cfg.AddOpenBehavior(typeof(TokenProbeBehavior<,>)));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => mediator.Send(new Ping(), cts.Token));

        Assert.Contains("behavior:Ping", recorder.Events);      // behavior executed
        Assert.True(recorder.LastTokenCancelled);               // handler observed the cancelled token
    }

    [Fact]
    public async Task Non_cancelled_token_completes_normally()
    {
        var (mediator, recorder) = TestHost.Build(cfg => cfg.AddOpenBehavior(typeof(TokenProbeBehavior<,>)));

        var result = await mediator.Send(new Ping(), CancellationToken.None);

        Assert.Equal("pong", result);
        Assert.False(recorder.LastTokenCancelled);
    }
}
