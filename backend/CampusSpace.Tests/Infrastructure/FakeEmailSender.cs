using System.Collections.Concurrent;
using CampusSpace.Api.Notifications;

namespace CampusSpace.Tests.Infrastructure;

/// <summary>Records every email and answers with <see cref="Respond"/> (default: Sent). Never touches the network.</summary>
public sealed class FakeEmailSender : IEmailSender
{
    public ConcurrentQueue<EmailMessage> Messages { get; } = new();

    public Func<EmailMessage, EmailSendResult> Respond { get; set; } = _ => EmailSendResult.Sent("<fake@brevo.test>");

    public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        Messages.Enqueue(message);
        return Task.FromResult(Respond(message));
    }
}
