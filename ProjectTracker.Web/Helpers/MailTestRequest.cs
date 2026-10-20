namespace ProjectTracker.Web.Helpers;

/// <summary>Body of the development-only mail test endpoint.</summary>
public sealed record MailTestRequest(string? To, string? Subject, string? Body);
