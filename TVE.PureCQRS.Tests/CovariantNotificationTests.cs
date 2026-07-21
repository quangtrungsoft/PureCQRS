namespace TVE.PureCQRS.Tests;

public class CovariantNotificationTests
{
    [Fact]
    public async Task Notification_reaches_concrete_base_and_catch_all_handlers()
    {
        var (mediator, recorder) = TestHost.Build();

        await mediator.Publish(new OrderShipped(42));

        Assert.Contains("shipped:42", recorder.Events); // concrete INotificationHandler<OrderShipped>
        Assert.Contains("base:42", recorder.Events);    // base INotificationHandler<OrderEvent>
        Assert.Contains("all", recorder.Events);         // catch-all INotificationHandler<INotification>
    }

    [Fact]
    public async Task Handler_is_not_invoked_more_than_once()
    {
        var (mediator, recorder) = TestHost.Build();

        await mediator.Publish(new OrderShipped(7));

        Assert.Equal(1, recorder.Count("shipped:7"));
        Assert.Equal(1, recorder.Count("base:7"));
        Assert.Equal(1, recorder.Count("all"));
    }

    [Fact]
    public async Task Publish_as_object_uses_runtime_type()
    {
        var (mediator, recorder) = TestHost.Build();

        object notification = new OrderShipped(99);
        await mediator.Publish(notification);

        Assert.Contains("shipped:99", recorder.Events);
        Assert.Contains("base:99", recorder.Events);
    }
}
