using System.Collections.Concurrent;
using System.Net;
using System.Text;
using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Writing;
using BandUp.Application.Features.Writing.Finalization;
using BandUp.Application.Features.Writing.Publishing;
using BandUp.IntegrationTests.Infrastructure;
using BandUp.Worker;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sentry;
using Sentry.AspNetCore;
using Sentry.Extensions.Logging;
using Sentry.Extensibility;
using Sentry.Protocol.Envelopes;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class WritingSentryPrivacyTests(BandUpTestEnvironment environment) : WritingAcceptanceFixture(environment)
{
    private const string OfflineDsn = "http://public@127.0.0.1:1/42";
    private const string ApiPrivate = "synthetic_private_api";
    private const string Malformed = "synthetic_private_malformed";
    private const string Boundary = "synthetic_private_boundary";
    private const string Publication = "synthetic_private_publication";
    private const string Expiration = "synthetic_private_expiration";

    [Fact]
    public async Task ApiTransportDropsPrivateFailuresMalformedRequestsAndBoundaryCaptures()
    {
        var transport = new OfflineTransport();
        try
        {
            await using var factory = new BandUpApiFactory(TestEnvironment, overrides: new()
            {
                ["Sentry:Dsn"] = OfflineDsn,
                ["Features:WritingSubmissionEnabled"] = "true",
            }, configureServices: services =>
            {
                services.PostConfigure<SentryAspNetCoreOptions>(options => OfflineOptions(options, transport));
                services.AddSingleton<BoundaryProbe>();
                services.AddSingleton<IStartupFilter>(sp => sp.GetRequiredService<BoundaryProbe>());
                services.AddScoped<IPipelineBehavior<ListWritingPromptsQuery, WritingPromptListDto>, ApiProbe>();
            });
            using var client = ContentTestData.CreateClient(factory);
            Assert.True(SentrySdk.IsEnabled);
            var owner = await ContentTestData.RegisterAndLoginAsync(client, "sentry-synthetic");
            using var prompts = ContentTestData.Get("/api/writing/prompts", owner);
            using var failure = await client.SendAsync(prompts);
            Assert.Equal(HttpStatusCode.InternalServerError, failure.StatusCode);

            using var malformed = ContentTestData.Send(HttpMethod.Post, "/api/writing/submissions", owner);
            malformed.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            malformed.Headers.Add("X-Synthetic-Private", Malformed);
            malformed.Content = new StringContent($"{{\"{Malformed}\":", Encoding.UTF8, "application/json");
            using var rejected = await client.SendAsync(malformed);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

            using var ordinary = await client.GetAsync("/api/system/time");
            Assert.Equal(HttpStatusCode.OK, ordinary.StatusCode);
            var boundary = factory.Services.GetRequiredService<BoundaryProbe>();
            await Task.WhenAll(boundary.WritingRequestsCaptured.Task, boundary.AllowedCaptured.Task)
                .WaitAsync(TimeSpan.FromSeconds(10));
            CaptureAllowed("api");
            await SentrySdk.FlushAsync(TimeSpan.FromSeconds(10));
            AssertExport(transport, "api", ApiPrivate, Malformed, Boundary);
            Assert.Contains(transport.Payloads, payload => payload.Contains("allowed-after-writing-request", StringComparison.Ordinal));
        }
        finally
        {
            SentrySdk.Close();
        }
    }

    [Fact]
    public async Task RealWorkerTransportDropsWritingCycleFailuresWithoutContaminatingAllowedTelemetry()
    {
        var clock = new TestClock();
        var probes = new WorkerProbes();
        // Seed through acceptance with Sentry disabled before initializing the Worker hub.
        await using (var setup = Factory(clock))
        {
            var (owner, command) = await Seed(setup);
            using var scope = Scope(setup, owner);
            var accepted = (await Send(scope, command)).Value;
            probes.Submission = accepted.SubmissionId;
            clock.Advance(TimeSpan.FromMinutes(5));
        }

        var transport = new OfflineTransport();
        var settings = TestEnvironment.SettingsFor(MigratedConnectionString);
        settings["Sentry:Dsn"] = OfflineDsn;
        settings["Features:WritingSubmissionEnabled"] = "false";
        settings["Writing:Publishing:ExpirationBatchSize"] = "100";
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var worker = WorkerHost.Build([], configure: builder =>
        {
            builder.Configuration.AddInMemoryCollection(settings);
            builder.Services.AddSingleton<TimeProvider>(clock);
            builder.Services.AddSingleton(probes);
            builder.Services.PostConfigure<SentryLoggingOptions>(options => OfflineOptions(options, transport));
            builder.Services.AddScoped<IPipelineBehavior<ReserveWritingPublication, WritingPublishReservation?>, PublicationProbe>();
            builder.Services.AddScoped<IPipelineBehavior<ExpireWriting, bool>, ExpirationProbe>();
        });
        await worker.StartAsync(limit.Token);
        try
        {
            Assert.True(SentrySdk.IsEnabled);
            await probes.PublicationSeen.Task.WaitAsync(limit.Token);
            await probes.ExpirationSeen.Task.WaitAsync(limit.Token);
            CaptureAllowed("worker");
        }
        finally
        {
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await worker.StopAsync(shutdown.Token);
        }
        Assert.False(SentrySdk.IsEnabled);
        AssertExport(transport, "worker", Publication, Expiration);
    }

    private static void OfflineOptions(SentryOptions options, OfflineTransport transport)
    {
        options.Dsn = OfflineDsn;
        options.Transport = transport;
        options.EnableBackpressureHandling = false;
        options.DisableFileWrite = true;
        options.TracesSampleRate = 1;
        options.EnableLogs = true;
        options.CreateHttpMessageHandler = () => new RejectNetwork();
    }

    private static void CaptureAllowed(string host)
    {
        using var scope = SentrySdk.PushScope();
        SentrySdk.AddBreadcrumb($"allowed-breadcrumb-{host}");
        SentrySdk.CaptureMessage($"allowed-event-{host}");
        SentrySdk.StartTransaction($"allowed-transaction-{host}", "privacy-test").Finish();
        SentrySdk.Logger.LogInfo($"allowed-log-{host}");
        SentrySdk.Metrics.EmitCounter($"allowed_counter_{host}", 1);
    }

    private static void CapturePrivate(string marker)
    {
        SentrySdk.AddBreadcrumb(marker);
        SentrySdk.CaptureEvent(PrivateEvent(marker, "http://localhost/api/writing/submissions"));
        SentrySdk.StartTransaction(marker, "privacy-test").Finish();
        SentrySdk.Logger.LogError(marker);
        SentrySdk.Metrics.EmitCounter(marker, 1);
    }

    private static SentryEvent PrivateEvent(string marker, string url)
    {
        var request = new SentryRequest { Url = url, Data = marker };
        request.Headers["X-Synthetic-Private"] = marker;
        return new SentryEvent(new InvalidOperationException(marker)) { Request = request };
    }

    private static void AssertExport(OfflineTransport transport, string host, params string[] privateMarkers)
    {
        var exported = string.Join("\n", transport.Payloads);
        Assert.Contains($"allowed-event-{host}", exported, StringComparison.Ordinal);
        Assert.Contains($"allowed-breadcrumb-{host}", exported, StringComparison.Ordinal);
        Assert.Contains($"allowed-transaction-{host}", exported, StringComparison.Ordinal);
        Assert.Contains($"allowed-log-{host}", exported, StringComparison.Ordinal);
        Assert.Contains($"allowed_counter_{host}", exported, StringComparison.Ordinal);
        foreach (var marker in privateMarkers)
        {
            Assert.DoesNotContain(marker, exported, StringComparison.Ordinal);
        }
    }

    private sealed class BoundaryProbe : IStartupFilter
    {
        private int _writingCaptures;
        public TaskCompletionSource WritingRequestsCaptured { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowedCaptured { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, invoke) =>
            {
                if (!context.Request.Path.StartsWithSegments("/api/writing"))
                {
                    await invoke(context);
                    if (context.Request.Path == "/api/system/time")
                    {
                        SentrySdk.CaptureMessage("allowed-after-writing-request");
                        AllowedCaptured.TrySetResult();
                    }
                    return;
                }

                var url = $"http://localhost{context.Request.Path}";
                SentrySdk.AddBreadcrumb(Boundary, data: new Dictionary<string, string> { ["url"] = url });
                await invoke(context);
                // Simulate the outer ASP.NET integration capturing after the private scope unwinds.
                SentrySdk.CaptureEvent(PrivateEvent(Boundary, url));
                if (Interlocked.Increment(ref _writingCaptures) == 2)
                {
                    WritingRequestsCaptured.TrySetResult();
                }
            });
            next(app);
        };
    }

    private sealed class ApiProbe : IPipelineBehavior<ListWritingPromptsQuery, WritingPromptListDto>
    {
        public Task<Result<WritingPromptListDto>> HandleAsync(ListWritingPromptsQuery query, CancellationToken ct,
            RequestHandlerDelegate<WritingPromptListDto> next)
        {
            CapturePrivate(ApiPrivate);
            throw new InvalidOperationException(ApiPrivate);
        }
    }

    private sealed class WorkerProbes
    {
        public Guid Submission { get; set; }
        public TaskCompletionSource PublicationSeen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ExpirationSeen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class PublicationProbe(WorkerProbes probes) : IPipelineBehavior<ReserveWritingPublication, WritingPublishReservation?>
    {
        public Task<Result<WritingPublishReservation?>> HandleAsync(ReserveWritingPublication command, CancellationToken ct,
            RequestHandlerDelegate<WritingPublishReservation?> next)
        {
            if (probes.PublicationSeen.Task.IsCompleted)
            {
                return Task.FromResult(Result<WritingPublishReservation?>.Success(null));
            }
            CapturePrivate(Publication);
            probes.PublicationSeen.TrySetResult();
            throw new IOException(Publication);
        }
    }

    private sealed class ExpirationProbe(WorkerProbes probes) : IPipelineBehavior<ExpireWriting, bool>
    {
        public Task<Result<bool>> HandleAsync(ExpireWriting command, CancellationToken ct, RequestHandlerDelegate<bool> next)
        {
            if (command.SubmissionId != probes.Submission || probes.ExpirationSeen.Task.IsCompleted)
            {
                return Task.FromResult(Result<bool>.Success(false));
            }
            CapturePrivate(Expiration);
            probes.ExpirationSeen.TrySetResult();
            throw new IOException(Expiration);
        }
    }

    private sealed class OfflineTransport : ITransport
    {
        public ConcurrentQueue<string> Payloads { get; } = new();

        public async Task SendEnvelopeAsync(Envelope envelope, CancellationToken cancellationToken = default)
        {
            using MemoryStream bytes = new();
            await envelope.SerializeAsync(bytes, null, cancellationToken);
            Payloads.Enqueue(Encoding.UTF8.GetString(bytes.ToArray()));
        }
    }

    private sealed class RejectNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Sentry tests only permit the in-memory transport.");
    }
}
