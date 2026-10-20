using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;
using Xunit;

namespace ProjectTracker.Web.Tests;

/// <summary>
/// Integration tests need a disposable SQL Server database that already has the e-mail migration applied
/// (database/migrations/2026-09-22-email-notifications.sql). Point TRACKER_TEST_DB at it; without it the tests are skipped
/// (never silently passed) and no real database is ever touched by default.
/// </summary>
public sealed class SqlFactAttribute : FactAttribute
{
    public const string Variable = "TRACKER_TEST_DB";

    public SqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
            Skip = $"Set {Variable} to a scratch SQL Server database (with the e-mail migration applied) to run this test.";
    }
}

public sealed record CapturedMail(string From, string[] To, string Subject, string HtmlBody);

/// <summary>A minimal SMTP server: accepts messages, keeps them, and can be told to reject them (a broken mail relay).</summary>
public sealed class FakeSmtpServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly object _lock = new();
    private readonly List<CapturedMail> _mails = [];

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>When true every message is refused with a permanent SMTP error, like an unavailable relay.</summary>
    public volatile bool Reject;

    public FakeSmtpServer()
    {
        _listener.Start();
        _ = AcceptLoopAsync();
    }

    public IReadOnlyList<CapturedMail> Mails { get { lock (_lock) return _mails.ToList(); } }

    public IEnumerable<CapturedMail> To(string address) => Mails.Where(m => m.To.Contains(address, StringComparer.OrdinalIgnoreCase));

    public void Clear() { lock (_lock) _mails.Clear(); }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _ = Task.Run(() => HandleAsync(client));
            }
        }
        catch (Exception) when (_stop.IsCancellationRequested) { }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using var _ = client;
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII);
        var writer = new StreamWriter(stream, new ASCIIEncoding()) { NewLine = "\r\n", AutoFlush = true };
        var from = "";
        var to = new List<string>();
        await writer.WriteLineAsync("220 fake.smtp ready");
        while (await reader.ReadLineAsync() is { } line)
        {
            var command = line.ToUpperInvariant();
            if (command.StartsWith("EHLO") || command.StartsWith("HELO")) await writer.WriteLineAsync("250 fake.smtp");
            else if (command.StartsWith("MAIL FROM")) { from = Between(line); to.Clear(); await writer.WriteLineAsync("250 ok"); }
            else if (command.StartsWith("RCPT TO")) { to.Add(Between(line)); await writer.WriteLineAsync("250 ok"); }
            else if (command == "DATA")
            {
                await writer.WriteLineAsync("354 go ahead");
                var data = new StringBuilder();
                while (await reader.ReadLineAsync() is { } dataLine && dataLine != ".")
                    data.Append(dataLine.StartsWith("..") ? dataLine[1..] : dataLine).Append("\r\n");
                if (Reject) { await writer.WriteLineAsync("554 5.7.1 rejected by test relay"); continue; }
                var message = MimeMessage.Load(new MemoryStream(Encoding.ASCII.GetBytes(data.ToString())));
                lock (_lock) _mails.Add(new CapturedMail(from, to.ToArray(), message.Subject ?? "", message.HtmlBody ?? ""));
                await writer.WriteLineAsync("250 queued");
            }
            else if (command == "QUIT") { await writer.WriteLineAsync("221 bye"); break; }
            else await writer.WriteLineAsync("250 ok");
        }
    }

    private static string Between(string line)
    {
        var open = line.IndexOf('<');
        var close = line.IndexOf('>');
        return open >= 0 && close > open ? line[(open + 1)..close] : line;
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
    }
}

public sealed class FakeClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 9, 27, 5, 5, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// One isolated run of the real application (real controllers, real SQL, real SMTP client) against the scratch
/// database and a fake SMTP relay. Everything it creates is removed on disposal and the templates/settings it
/// edits are restored, so tests can be repeated.
/// </summary>
public sealed class Sandbox : IAsyncDisposable
{
    public const string AdminEmail = "admin@example.com";
    public const string AdminPassword = "ChangeMe#Admin2026!";
    public const string UserPassword = "Pm#Test2026!Aa";
    public const string BaseUrl = "https://tracker.test";
    public const string Marker = "[اختبار البريد]";

    private static readonly string Connection = Environment.GetEnvironmentVariable(SqlFactAttribute.Variable)!;

    public FakeSmtpServer Smtp { get; } = new();
    public FakeClock Clock { get; } = new();

    private WebApplicationFactory<Program> _factory = null!;
    private readonly List<Guid> _users = [];
    private readonly List<Guid> _projects = [];
    private List<(string Key, string Name, string Subject, string Body, bool Enabled)> _templateSnapshot = [];
    private List<(string Key, string Value)> _settingsSnapshot = [];

    private Action<IServiceCollection>? _configureServices;

    public static async Task<Sandbox> CreateAsync(Action<IServiceCollection>? configureServices = null)
    {
        var sandbox = new Sandbox { _configureServices = configureServices };
        sandbox._factory = sandbox.BuildFactory();
        sandbox._templateSnapshot = (await QueryAsync("SELECT TemplateKey, NameAr, Subject, MessageText, IsEnabled FROM EmailTemplates"))
            .Select(r => ((string)r[0]!, (string)r[1]!, (string)r[2]!, (string)r[3]!, (bool)r[4]!)).ToList();
        sandbox._settingsSnapshot = (await QueryAsync("SELECT setting_key, setting_value FROM app_settings WHERE setting_key LIKE 'email_%'"))
            .Select(r => ((string)r[0]!, (string)r[1]!)).ToList();
        return sandbox;
    }

    private WebApplicationFactory<Program> BuildFactory() => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("DB_CONNECTION_STRING", Connection);
        builder.UseSetting("SESSION_SECRET", "test-session-secret-0123456789-0123456789-abcdef");
        builder.UseSetting("Email:Host", "127.0.0.1");
        builder.UseSetting("Email:FallbackHost", "127.0.0.1");
        builder.UseSetting("Email:Port", Smtp.Port.ToString());
        builder.UseSetting("Email:EnableSsl", "false");
        builder.UseSetting("Email:FromEmail", "workflow@test.example");
        builder.UseSetting("Application:BaseUrl", BaseUrl);
        builder.UseSetting("Notifications:SchedulerEnabled", "false");
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
            _configureServices?.Invoke(services);
        });
    });

    /// <summary>A server restart: the host is thrown away and a new one starts on the same database.</summary>
    public void RestartServer()
    {
        _factory.Dispose();
        _factory = BuildFactory();
    }

    public T Service<T>(Func<IServiceProvider, T> pick)
    {
        using var scope = _factory.Services.CreateScope();
        return pick(scope.ServiceProvider);
    }

    public async Task<TResult> InScopeAsync<TResult>(Func<IServiceProvider, Task<TResult>> action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    // ── http ────────────────────────────────────────────────────────────────
    public async Task<HttpClient> LoginAsync(string email, string password)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.True(response.IsSuccessStatusCode, $"login failed for {email}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return client;
    }

    public Task<HttpClient> AdminAsync() => LoginAsync(AdminEmail, AdminPassword);

    public HttpClient Anonymous() => _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });

    // ── data ────────────────────────────────────────────────────────────────
    public async Task<(Guid Id, string Email)> NewUserAsync(string label, string? email = "auto", string role = "pm")
    {
        var id = Guid.NewGuid();
        var address = email == "auto" ? $"t{id:N}@test.example" : email;
        await ExecuteAsync("INSERT INTO profiles (id, full_name, email, role, password_hash, status) VALUES (@id, @name, @email, @role, @pw, 'active')",
            ("@id", id), ("@name", $"{Marker} {label}"), ("@email", address), ("@role", role), ("@pw", UserPassword));
        _users.Add(id);
        return (id, address!);
    }

    public async Task<Guid> NewProjectAsync(string label, string status = "active", int startedWeeksAgo = 4)
    {
        var id = Guid.NewGuid();
        var start = DateTime.Today.AddDays(-7 * startedWeeksAgo);
        await ExecuteAsync("INSERT INTO projects (id, name, description, start_date, end_date, total_weeks, department, status) VALUES (@id, @name, @d, @s, @e, 20, N'إدارة الاختبار', @status)",
            ("@id", id), ("@name", $"{Marker} {label}"), ("@d", "وصف مختصر للمشروع التجريبي"), ("@s", start), ("@e", start.AddDays(140)), ("@status", status));
        _projects.Add(id);
        return id;
    }

    /// <summary>Registers a row created through the API so it is removed when the sandbox is disposed.</summary>
    public void TrackProject(Guid id) => _projects.Add(id);

    public void TrackUser(Guid id) => _users.Add(id);

    public static async Task AddOverdueTaskAsync(Guid projectId, string taskName)
    {
        var milestone = Guid.NewGuid();
        await ExecuteAsync("INSERT INTO milestones (id, project_id, name, week_start, week_end) VALUES (@id, @p, N'مرحلة', 1, 3)", ("@id", milestone), ("@p", projectId));
        await ExecuteAsync("INSERT INTO tasks (id, milestone_id, name, week_start, week_end, progress) VALUES (NEWID(), @m, @n, 1, 2, 10)", ("@m", milestone), ("@n", taskName));
    }

    public static async Task<HttpResponseMessage> AssignAsync(HttpClient admin, Guid projectId, Guid userId) =>
        await admin.PostAsJsonAsync("/api/assignments", new { project_id = projectId, user_id = userId });

    public static async Task<HttpResponseMessage> UnassignAsync(HttpClient admin, Guid projectId, Guid userId)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/assignments") { Content = JsonContent.Create(new { project_id = projectId, user_id = userId }) };
        return await admin.SendAsync(request);
    }

    public static async Task<HttpResponseMessage> PatchProjectAsync(HttpClient admin, Guid projectId, object body) =>
        await admin.PatchAsJsonAsync($"/api/projects/{projectId}", body);

    // ── database ────────────────────────────────────────────────────────────
    public static async Task<List<object?[]>> QueryAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new SqlConnection(Connection);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<object?[]>();
        while (await reader.ReadAsync())
        {
            var row = new object?[reader.FieldCount];
            for (var i = 0; i < row.Length; i++) row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }

    public static async Task ExecuteAsync(string sql, params (string Name, object? Value)[] parameters) => await QueryAsync(sql, parameters);

    public static async Task<int> ScalarAsync(string sql, params (string Name, object? Value)[] parameters) => Convert.ToInt32((await QueryAsync(sql, parameters))[0][0]);

    public record LogRow(string TemplateKey, string Status, string? Recipient, string? Error, string CorrelationKey, int Attempts);

    public static async Task<List<LogRow>> LogAsync(Guid projectId, string? templateKey = null) =>
        (await QueryAsync("SELECT TemplateKey, Status, RecipientEmail, ErrorMessage, CorrelationKey, AttemptCount FROM EmailNotificationLog WHERE ProjectId = @p AND (@k IS NULL OR TemplateKey = @k) ORDER BY CreatedAt",
            ("@p", projectId), ("@k", templateKey)))
        .Select(r => new LogRow((string)r[0]!, (string)r[1]!, (string?)r[2], (string?)r[3], (string)r[4]!, (int)r[5]!)).ToList();

    public async ValueTask DisposeAsync()
    {
        _factory.Dispose();
        Smtp.Dispose();
        foreach (var (key, name, subject, body, enabled) in _templateSnapshot)
            await ExecuteAsync("UPDATE EmailTemplates SET NameAr=@n, Subject=@s, MessageText=@b, IsEnabled=@e WHERE TemplateKey=@k", ("@n", name), ("@s", subject), ("@b", body), ("@e", enabled), ("@k", key));
        foreach (var (key, value) in _settingsSnapshot)
            await ExecuteAsync("UPDATE app_settings SET setting_value=@v WHERE setting_key=@k", ("@v", value), ("@k", key));
        foreach (var project in _projects)
        {
            await ExecuteAsync("DELETE FROM EmailNotificationLog WHERE ProjectId = @p", ("@p", project));
            await ExecuteAsync("DELETE FROM audit_log WHERE project_id = @p", ("@p", project));
            await ExecuteAsync("DELETE FROM risks_issues WHERE project_id = @p", ("@p", project));
            await ExecuteAsync("DELETE FROM meeting_actions WHERE project_id = @p", ("@p", project));
            await ExecuteAsync("DELETE FROM project_updates WHERE project_id = @p", ("@p", project));
            await ExecuteAsync("DELETE n FROM phase_notes n JOIN milestones m ON m.id = n.milestone_id WHERE m.project_id = @p", ("@p", project));
            await ExecuteAsync("DELETE t FROM tasks t JOIN milestones m ON m.id = t.milestone_id WHERE m.project_id = @p", ("@p", project));
            await ExecuteAsync("DELETE FROM milestones WHERE project_id = @p", ("@p", project));
            await ExecuteAsync("DELETE FROM assignments WHERE project_id = @p", ("@p", project));
            await ExecuteAsync("DELETE FROM projects WHERE id = @p", ("@p", project));
        }
        foreach (var user in _users)
        {
            await ExecuteAsync("DELETE FROM EmailNotificationLog WHERE UserId = @u", ("@u", user));
            await ExecuteAsync("DELETE FROM user_sessions WHERE user_id = @u", ("@u", user));
            await ExecuteAsync("DELETE FROM login_otp WHERE user_id = @u", ("@u", user));
            await ExecuteAsync("DELETE FROM notification_log WHERE user_id = @u", ("@u", user));
            await ExecuteAsync("DELETE FROM assignments WHERE user_id = @u", ("@u", user));
            await ExecuteAsync("DELETE FROM profiles WHERE id = @u", ("@u", user));
        }
    }
}
