# TVE.PureCQRS

`TVE.PureCQRS` là một thư viện CQRS/Mediator nhẹ, tối giản, hỗ trợ:

- `Send` request có hoặc không có response
- `Publish` notification tới nhiều handler
- đăng ký handler tự động từ assembly
- pipeline behavior cho các tác vụ cross-cutting như logging, validation, caching

Thư viện hiện hỗ trợ `net7.0`, `net8.0` và `net9.0`.

## Mới trong v1.1.0

- **`CancellationToken` chảy xuyên pipeline**: `RequestHandlerDelegate<TResponse>` nay nhận `CancellationToken`; behavior nên gọi `next(cancellationToken)`. Behavior cũ gọi `next()` vẫn hoạt động.
- **Covariant notification dispatch**: một notification được giao tới handler đăng ký cho chính type đó *và* cho type cha / interface notification mà nó kế thừa — độc lập với DI container.
- **Exception handlers/actions**: `IRequestExceptionHandler<,,>` để phục hồi lỗi và trả fallback; `IRequestExceptionAction<,>` cho side-effect (log, metrics). Chỉ được gắn vào pipeline khi có handler/action thực sự tồn tại.
- **Command void đi qua pipeline đầy đủ**: `IRequest` (không response) nay chạy qua behaviors và exception handling giống request có response.

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
        var response = await next(cancellationToken); // forward the token downstream
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

### 5. Exception handlers & actions

Xử lý lỗi phát sinh trong handler mà không cần `try/catch` rải rác. Có hai loại:

- `IRequestExceptionHandler<TRequest, TResponse, TException>` — **phục hồi** lỗi: gọi `state.SetHandled(response)` để trả về giá trị thay cho việc ném exception. Được thử theo thứ tự type exception cụ thể nhất trước; handler đầu tiên đánh dấu handled sẽ thắng.
- `IRequestExceptionAction<TRequest, TException>` — **side-effect** (logging, metrics...) khi có lỗi; exception vẫn được ném lại sau đó. Mọi action khớp đều chạy.

```csharp
using TVE.PureCQRS;

public sealed record GetPriceQuery(string Symbol) : IRequest<decimal>;

// Phục hồi: khi service lỗi thì trả giá mặc định
public sealed class GetPriceExceptionHandler
    : IRequestExceptionHandler<GetPriceQuery, decimal, HttpRequestException>
{
    public Task Handle(GetPriceQuery request, HttpRequestException ex,
        RequestExceptionHandlerState<decimal> state, CancellationToken ct)
    {
        state.SetHandled(0m); // trả về 0 thay vì ném lỗi
        return Task.CompletedTask;
    }
}

// Side-effect: log mọi lỗi rồi để nó tiếp tục ném
public sealed class LogAnyException : IRequestExceptionAction<GetPriceQuery, Exception>
{
    public Task Execute(GetPriceQuery request, Exception ex, CancellationToken ct)
    {
        Console.WriteLine($"Error handling {request.Symbol}: {ex.Message}");
        return Task.CompletedTask;
    }
}
```

Không cần cấu hình thêm: chỉ cần khai báo handler/action trong assembly đã đăng ký, thư viện tự phát hiện và gắn vào pipeline. Nếu app không dùng, pipeline giữ nguyên fast-path (không thêm chi phí try/catch).

### 6. Covariant (polymorphic) notification

Handler đăng ký cho type notification cha sẽ nhận cả notification con.

```csharp
public abstract record DomainEvent : INotification;
public sealed record OrderPlaced(int OrderId) : DomainEvent;

// Chạy cho MỌI DomainEvent, kể cả OrderPlaced
public sealed class DomainEventAuditor : INotificationHandler<DomainEvent>
{
    public Task Handle(DomainEvent e, CancellationToken ct)
    {
        Console.WriteLine($"Audit: {e.GetType().Name}");
        return Task.CompletedTask;
    }
}

await publisher.Publish(new OrderPlaced(1)); // DomainEventAuditor cũng chạy
```

Một handler đăng ký cho cả type cha lẫn con chỉ được gọi **một lần** (dedupe theo instance).

### 7. Constrained open-generic request handler

Handler generic được đóng lúc runtime cho từng cặp request/response cụ thể, kể cả khi arity lệch:

```csharp
public sealed class Box<T> { public T Value { get; init; } = default!; }
public sealed record WrapQuery<T>(T Input) : IRequest<Box<T>>;

public sealed class WrapHandler<T> : IRequestHandler<WrapQuery<T>, Box<T>>
{
    public Task<Box<T>> Handle(WrapQuery<T> request, CancellationToken ct)
        => Task.FromResult(new Box<T> { Value = request.Input });
}

var box = await sender.Send(new WrapQuery<int>(7)); // -> Box<int> { Value = 7 }
```

Ràng buộc generic (`where T : ...`) được kiểm tra trước khi đóng; kết quả `ObjectFactory` được cache
theo cặp (request, response).

## API chính

- `IMediator`: kết hợp `ISender` và `IPublisher`
- `ISender.Send(...)`: gửi request tới một handler duy nhất
- `IPublisher.Publish(...)`: phát notification tới nhiều handler
- `IRequest<TResponse>`: request có response
- `IRequest`: request không có response
- `INotification`: marker interface cho sự kiện
- `IPipelineBehavior<TRequest, TResponse>`: middleware cho request
- `IRequestExceptionHandler<TRequest, TResponse, TException>`: phục hồi lỗi và trả fallback
- `IRequestExceptionAction<TRequest, TException>`: side-effect khi có lỗi (không nuốt exception)
- `Unit`: kiểu "không có giá trị"; command `IRequest` tương đương `IRequest<Unit>` nội bộ

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
