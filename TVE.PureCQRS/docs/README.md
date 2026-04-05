# TVE.PureCQRS

`TVE.PureCQRS` là một thư viện CQRS/Mediator nhẹ, tối giản, hỗ trợ:

- `Send` request có hoặc không có response
- `Publish` notification tới nhiều handler
- đăng ký handler tự động từ assembly
- pipeline behavior cho các tác vụ cross-cutting như logging, validation, caching

Thư viện hiện hỗ trợ `net7.0`, `net8.0` và `net9.0`.

## Cài đặt

Thêm package vào project:

```bash
dotnet add package TVE.PureCQRS
```

## Khái niệm chính

### Request có response

Kế thừa `IRequest<TResponse>` và xử lý bằng `IRequestHandler<TRequest, TResponse>`.

### Request không có response

Kế thừa `IRequest` và xử lý bằng `IRequestHandler<TRequest>`.

### Notification

Kế thừa `INotification` và xử lý bằng một hoặc nhiều `INotificationHandler<TNotification>`.

### Pipeline behavior

Dùng `IPipelineBehavior<TRequest, TResponse>` để chèn logic trước/sau handler.

## Đăng ký dịch vụ

Trong `Program.cs` hoặc `Startup`:

```csharp
using TVE.PureCQRS;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPureCQRS(config =>
{
    config.RegisterServicesFromAssemblyContaining<Program>();
    // config.AddOpenBehavior(typeof(LoggingBehavior<,>));
    // config.WithHandlerLifetime(ServiceLifetime.Transient);
    // config.Using<MyCustomMediator>();
});

var app = builder.Build();
app.Run();
```

Nếu chỉ muốn đăng ký theo assembly:

```csharp
builder.Services.AddPureCQRS(typeof(Program).Assembly);
```

## Ví dụ hoàn chỉnh

### 1. Request có response

```csharp
using TVE.PureCQRS;

public sealed record GetUserByIdQuery(Guid UserId) : IRequest<UserDto>;

public sealed record UserDto(Guid Id, string Name);

public sealed class GetUserByIdHandler : IRequestHandler<GetUserByIdQuery, UserDto>
{
    public Task<UserDto> Handle(GetUserByIdQuery request, CancellationToken cancellationToken)
    {
        var user = new UserDto(request.UserId, "Nguyen Van A");
        return Task.FromResult(user);
    }
}
```

Gọi từ service hoặc controller:

```csharp
public sealed class UserService
{
    private readonly ISender _sender;

    public UserService(ISender sender)
    {
        _sender = sender;
    }

    public Task<UserDto> GetUser(Guid id)
    {
        return _sender.Send(new GetUserByIdQuery(id));
    }
}
```

### 2. Request không có response

```csharp
using TVE.PureCQRS;

public sealed record CreateUserCommand(string Name) : IRequest;

public sealed class CreateUserHandler : IRequestHandler<CreateUserCommand>
{
    public Task Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        // Lưu dữ liệu, gọi repository, publish event...
        return Task.CompletedTask;
    }
}
```

Gọi:

```csharp
await sender.Send(new CreateUserCommand("Nguyen Van B"));
```

### 3. Notification với nhiều handler

```csharp
using TVE.PureCQRS;

public sealed record UserCreatedNotification(Guid UserId, string Name) : INotification;

public sealed class SendWelcomeEmailHandler : INotificationHandler<UserCreatedNotification>
{
    public Task Handle(UserCreatedNotification notification, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Send welcome email to {notification.Name}");
        return Task.CompletedTask;
    }
}

public sealed class WriteAuditLogHandler : INotificationHandler<UserCreatedNotification>
{
    public Task Handle(UserCreatedNotification notification, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Audit user created: {notification.UserId}");
        return Task.CompletedTask;
    }
}
```

Gọi:

```csharp
await publisher.Publish(new UserCreatedNotification(Guid.NewGuid(), "Nguyen Van B"));
```

Nếu có nhiều handler, thư viện sẽ chạy song song bằng `Task.WhenAll`.

### 4. Pipeline behavior

```csharp
using TVE.PureCQRS;

public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Before: {typeof(TRequest).Name}");
        var response = await next();
        Console.WriteLine($"After: {typeof(TRequest).Name}");
        return response;
    }
}
```

Đăng ký:

```csharp
builder.Services.AddPureCQRS(config =>
{
    config.RegisterServicesFromAssemblyContaining<Program>();
    config.AddOpenBehavior(typeof(LoggingBehavior<,>));
});
```

## API chính

- `IMediator`: kết hợp `ISender` và `IPublisher`
- `ISender.Send(...)`: gửi request tới một handler duy nhất
- `IPublisher.Publish(...)`: phát notification tới nhiều handler
- `IRequest<TResponse>`: request có response
- `IRequest`: request không có response
- `INotification`: marker interface cho sự kiện
- `IPipelineBehavior<TRequest, TResponse>`: middleware cho request

## Hành vi khi chạy

- Nếu request không có handler, thư viện sẽ ném `HandlerNotFoundException`
- Nếu truyền vào object không phải `IRequest`/`IRequest<TResponse>`, thư viện sẽ ném `InvalidRequestException`
- `Publish(object)` và `Publish<TNotification>` đều dùng runtime type của notification

## Mẫu sử dụng nhanh

```csharp
var user = await mediator.Send(new GetUserByIdQuery(Guid.NewGuid()));
await mediator.Send(new CreateUserCommand("Alice"));
await mediator.Publish(new UserCreatedNotification(Guid.NewGuid(), "Alice"));
```

## Ghi chú

Thư viện được thiết kế đơn giản, phù hợp cho các ứng dụng muốn một mediator nhẹ, ít phụ thuộc và dễ tự kiểm soát cách đăng ký handler.
