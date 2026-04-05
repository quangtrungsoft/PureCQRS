namespace TVE.PureCQRS;

/// <summary>
/// Combined interface for sending and publishing
/// </summary>
public interface IMediator : ISender, IPublisher;