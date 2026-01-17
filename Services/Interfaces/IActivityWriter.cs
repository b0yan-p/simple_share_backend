using simpleShare_backend.Models.Application.Activity;

namespace simpleShare_backend.Services.Interfaces;

public interface IActivityWriter
{
    /// <summary>
    /// This implementation should:
    ///     1. serialize evt.Data in JSON
    ///     2. Add Activity in dbCOntext
    ///     3. add OutboxMessage for push notification
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="evt"></param>
    void Enqueue<T>(ActivityEvent<T> evt);
}
