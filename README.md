# Simple TVE.PureCQRS Implementation - Complete Guide

## Overview

This is a complete implementation of the TVE.PureCQRS pattern in C#. It provides a lightweight, flexible mediator for handling requests, queries, commands, and notifications with support for pipeline behaviors (cross-cutting concerns).

## New in v1.1.0

- **`CancellationToken` in the pipeline delegate** — `RequestHandlerDelegate<TResponse>` now takes a `CancellationToken`; behaviors should call `next(cancellationToken)`. Existing `next()` calls still compile.
- **Covariant (polymorphic) notification dispatch** — a notification reaches handlers registered for its concrete type *and* for any base type / notification interface it derives from, independent of the DI container.
- **Exception handlers & actions** — `IRequestExceptionHandler<TRequest, TResponse, TException>` (recover + fallback) and `IRequestExceptionAction<TRequest, TException>` (side-effects). Wired into the pipeline only when such handlers exist, so the zero-behavior fast path is preserved otherwise.
- **Void commands run through the full pipeline** — `IRequest` is modelled as `IRequest<Unit>`, so commands now go through behaviors and exception handling just like value-returning requests.
- **Constrained open-generic request handlers** — handlers such as `WrapHandler<T> : IRequestHandler<WrapQuery<T>, Box<T>>` are closed at runtime for the concrete request/response pair (including arity mismatches the DI container can't handle), with generic-constraint validation and a cached `ObjectFactory`.

See [release notes](TVE.PureCQRS/docs/releasenotes/v1.1.0.md) for details.

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│                  Application Layer                      │
│         (Controllers, Services, Use Cases)              │
└────────────────┬────────────────────────────────────────┘
                 │
                 │ Send/Publish
                 ▼
┌─────────────────────────────────────────────────────────┐
│                    IMediator                            │
│   ├─ ISender (Send<TResponse>)                          │
│   └─ IPublisher (Publish<TNotification>)                │
└────────────────┬────────────────────────────────────────┘
                 │
        ┌────────┴────────┐
        ▼                 ▼
┌──────────────┐  ┌──────────────────┐
│  ISender     │  │  IPublisher      │
│              │  │                  │
│ Single       │  │  Multiple        │
│ Handler      │  │  Handlers        │
└──────┬───────┘  └────────┬─────────┘
       │                   │
       ▼                   ▼
  [Pipeline]        [Notification]
  - Logging         - Send Email
  - Validation      - Log Event
  - Auth            - Update Cache
  - Performance     - etc.
       │                   │
       ▼                   ▼
  [IRequestHandler]   [INotificationHandler]
```

## Key Components

### 1. **IRequest & IRequest<TResponse>**
- Represents a request/command/query
- `IRequest` - No return value
- `IRequest<TResponse>` - Returns TResponse

### 2. **IRequestHandler & IRequestHandler<TRequest, TResponse>**
- Handles individual requests
- Must implement `Handle(TRequest request, CancellationToken cancellationToken)`
- One handler per request type

### 3. **INotification**
- Represents an event/notification
- Can have multiple handlers

### 4. **INotificationHandler<TNotification>**
- Handles notifications
- Multiple handlers can handle the same notification
- Executed in parallel with Task.WhenAll

### 5. **ISender**
- Sends requests to a single handler
- Methods: `Send<TResponse>(request)`, `Send(request)`
- Returns response from the handler

### 6. **IPublisher**
- Publishes notifications to multiple handlers
- Methods: `Publish<TNotification>(notification)`, `Publish(notification)`
- No return value

### 7. **IMediator**
- Inherits from both ISender and IPublisher
- Main entry point for the application

### 8. **IPipelineBehavior<TRequest, TResponse>**
- Implements cross-cutting concerns
- Examples: Logging, Validation, Authorization, Performance Monitoring
- Executed before the actual handler

## Usage Examples

### Creating a Command
```csharp
public class CreateUserCommand : IRequest<int>
{
    public string Username { get; set; }
    public string Email { get; set; }
}
```

### Creating a Handler
```csharp
public class CreateUserHandler : IRequestHandler<CreateUserCommand, int>
{
    public async Task<int> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        // Create user and return ID
        return userId;
    }
}
```

### Creating a Notification
```csharp
public class UserCreatedNotification : INotification
{
    public int UserId { get; set; }
    public string Username { get; set; }
}
```

### Creating Notification Handlers
```csharp
public class SendEmailHandler : INotificationHandler<UserCreatedNotification>
{
    public async Task Handle(UserCreatedNotification notification, CancellationToken cancellationToken)
    {
        // Send email
    }
}

public class LogEventHandler : INotificationHandler<UserCreatedNotification>
{
    public async Task Handle(UserCreatedNotification notification, CancellationToken cancellationToken)
    {
        // Log event
    }
}
```

### Building the Mediator
```csharp
var mediator = new MediatorBuilder()
    .AddHandlersFromAssembly(typeof(CreateUserHandler).Assembly)
    .AddPipelineBehavior<LoggingBehavior<CreateUserCommand, int>>()
    .AddPipelineBehavior<ValidationBehavior<CreateUserCommand, int>>()
    .Build();
```

### Using the Mediator
```csharp
// Send a command
var userId = await mediator.Send(new CreateUserCommand 
{ 
    Username = "john", 
    Email = "john@example.com" 
});

// Publish a notification
await mediator.Publish(new UserCreatedNotification 
{ 
    UserId = userId, 
    Username = "john" 
});
```

## Pipeline Behaviors Flow

Pipeline behaviors are executed in the order they are registered:

```
Request comes in
    ↓
[Logging Behavior] ← Outermost (logs start)
    ↓
[Validation Behavior] ← Validates request
    ↓
[Performance Behavior] ← Measures time
    ↓
[Authorization Behavior] ← Checks permissions
    ↓
[IRequestHandler.Handle()] ← The actual handler
    ↓
Response bubbles back through all behaviors
    ↓
[Performance Behavior] ← Logs duration
    ↓
[Authorization Behavior] ← (cleanup if needed)
    ↓
[Validation Behavior] ← (cleanup if needed)
    ↓
[Logging Behavior] ← Logs completion
    ↓
Response returned to caller
```

## Lifetime Scopes

- **Transient**: New instance every time
- **Singleton**: Single instance for application lifetime
- **Scoped**: Single instance per scope (not fully implemented in this basic version)

## File Organization

```
SimpleMediator/
├── Abstractions/
│   ├── IRequest.cs
│   ├── INotification.cs
│   ├── IRequestHandler.cs
│   ├── INotificationHandler.cs
│   ├── IPipelineBehavior.cs
│   ├── ISender.cs
│   ├── IPublisher.cs
│   └── IMediator.cs
├── Core/
│   ├── Mediator.cs
│   ├── HandlerRegistry.cs
│   ├── ServiceProvider.cs
│   └── Exceptions.cs
├── MediatorBuilder.cs
└── Examples/
    ├── Commands.cs
    ├── Handlers/
    │   ├── CommandHandlers.cs
    │   ├── QueryHandlers.cs
    │   └── NotificationHandlers.cs
    ├── Behaviors/
    │   └── PipelineBehaviors.cs
    └── Demo.cs
```

## Key Differences: Send vs Publish

| Aspect | Send | Publish |
|--------|------|---------|
| Handlers | 1 | Many |
| Return Value | Yes | No |
| Exception Handling | Single handler | All handlers |
| Use Case | Commands/Queries | Events/Notifications |
| Interface | ISender | IPublisher |

## Exception Handling

### No Handler Registered
```csharp
throw new NoHandlerRegisteredException(requestType);
```

### Multiple Handlers (Request)
```csharp
throw new MultipleHandlersRegisteredException(requestType);
```

## Advanced Features

1. **Auto-Discovery**: Register handlers from assembly
2. **Manual Registration**: Add handlers individually
3. **Pipeline Behaviors**: Cross-cutting concerns
4. **Dependency Injection**: Built-in DI container
5. **Async/Await Support**: All operations are async
6. **CancellationToken**: Graceful cancellation support

## Performance Considerations

- Handlers are cached after first resolution
- Notification handlers execute in parallel (Task.WhenAll)
- Pipeline behaviors add minimal overhead
- Reflection is used during setup, not during request processing

## Testing

Example test structure:
```csharp
[TestMethod]
public async Task CreateUserCommand_WithValidData_ReturnsUserId()
{
    var mediator = new MediatorBuilder()
        .AddRequestHandler<CreateUserCommand, CreateUserHandler>()
        .Build();

    var command = new CreateUserCommand 
    { 
        Username = "test", 
        Email = "test@example.com" 
    };

    var result = await mediator.Send(command);
    
    Assert.IsNotNull(result);
    Assert.IsTrue(result > 0);
}
```