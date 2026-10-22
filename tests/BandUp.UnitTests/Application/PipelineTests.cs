using BandUp.Application;
using BandUp.Application.Abstractions;
using BandUp.Application.Behaviors;
using BandUp.Application.Common;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace BandUp.UnitTests.Application;

public sealed class PipelineTests
{
    [Fact]
    public void INF_09_Behaviors_Are_Registered_In_Logging_Validation_Transaction_Order()
    {
        ServiceCollection services = new();

        services.AddPipelineBehaviors();

        Type?[] registered = services
            .Where(descriptor => descriptor.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(descriptor => descriptor.ImplementationType)
            .ToArray();

        registered.ShouldBe(
        [
            typeof(LoggingBehavior<,>),
            typeof(ValidationBehavior<,>),
            typeof(TransactionBehavior<,>),
        ]);
    }

    [Fact]
    public async Task INF_09_Pipeline_Executes_Behaviors_Outermost_First_Then_The_Handler()
    {
        List<string> order = [];
        ServiceCollection services = new();

        services.AddScoped<ICommandHandler<TestCommand, string>>(_ => new RecordingHandler(order));
        services.AddScoped<IPipelineBehavior<TestCommand, string>>(_ => new RecordingBehavior<TestCommand, string>(order, "logging"));
        services.AddScoped<IPipelineBehavior<TestCommand, string>>(_ => new RecordingBehavior<TestCommand, string>(order, "validation"));
        services.AddScoped<IPipelineBehavior<TestCommand, string>>(_ => new RecordingBehavior<TestCommand, string>(order, "transaction"));

        await using ServiceProvider provider = services.BuildServiceProvider();
        IDispatcher dispatcher = new Dispatcher(provider);

        Result<string> result = await dispatcher.SendAsync<TestCommand, string>(
            new TestCommand("hi"),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        order.ShouldBe(["logging", "validation", "transaction", "handler"]);
    }

    [Fact]
    public async Task INF_09_Query_Pipeline_Excludes_The_Transaction_Behavior()
    {
        List<string> order = [];
        ServiceCollection services = new();

        services.AddScoped<IQueryHandler<TestQuery, string>>(_ => new RecordingQueryHandler(order));
        services.AddScoped<IPipelineBehavior<TestQuery, string>>(_ => new RecordingBehavior<TestQuery, string>(order, "logging"));
        services.AddScoped<IPipelineBehavior<TestQuery, string>>(_ => new RecordingTransactionBehavior<TestQuery, string>(order));

        await using ServiceProvider provider = services.BuildServiceProvider();
        IDispatcher dispatcher = new Dispatcher(provider);

        await dispatcher.QueryAsync<TestQuery, string>(new TestQuery("hi"), CancellationToken.None);

        order.ShouldBe(["logging", "query"]);
    }

    [Fact]
    public async Task INF_09_Transaction_Commits_On_Success()
    {
        IUnitOfWork unitOfWork = Substitute.For<IUnitOfWork>();
        TransactionBehavior<TestCommand, string> behavior = new(unitOfWork);

        Result<string> result = await behavior.HandleAsync(
            new TestCommand("hi"),
            CancellationToken.None,
            () => Task.FromResult(Result<string>.Success("ok")));

        result.IsSuccess.ShouldBeTrue();

        Received.InOrder(() =>
        {
            unitOfWork.BeginTransactionAsync(Arg.Any<CancellationToken>());
            unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
            unitOfWork.CommitTransactionAsync(Arg.Any<CancellationToken>());
        });

        await unitOfWork.DidNotReceive().RollbackTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task INF_09_Transaction_Rolls_Back_On_Failure_Result()
    {
        IUnitOfWork unitOfWork = Substitute.For<IUnitOfWork>();
        TransactionBehavior<TestCommand, string> behavior = new(unitOfWork);

        Result<string> result = await behavior.HandleAsync(
            new TestCommand("hi"),
            CancellationToken.None,
            () => Task.FromResult(Result<string>.Failure(Error.NotFound("missing"))));

        result.IsFailure.ShouldBeTrue();

        await unitOfWork.Received(1).RollbackTransactionAsync(Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().CommitTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task INF_09_Transaction_Rolls_Back_On_Exception()
    {
        IUnitOfWork unitOfWork = Substitute.For<IUnitOfWork>();
        TransactionBehavior<TestCommand, string> behavior = new(unitOfWork);
        RequestHandlerDelegate<string> next = () => throw new InvalidOperationException("boom");

        await Should.ThrowAsync<InvalidOperationException>(() =>
            behavior.HandleAsync(new TestCommand("hi"), CancellationToken.None, next));

        await unitOfWork.Received(1).RollbackTransactionAsync(Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().CommitTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task INF_05_Invalid_Command_Returns_Validation_Error_With_An_Errors_Map()
    {
        ValidationBehavior<TestCommand, string> behavior = new([new TestCommandValidator()]);
        bool handlerRan = false;

        Result<string> result = await behavior.HandleAsync(
            new TestCommand(string.Empty),
            CancellationToken.None,
            () =>
            {
                handlerRan = true;
                return Task.FromResult(Result<string>.Success("unreachable"));
            });

        handlerRan.ShouldBeFalse();
        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe(Error.ValidationCode);
        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.ValidationErrors.ShouldContainKey(nameof(TestCommand.Value));
        result.Error.ValidationErrors[nameof(TestCommand.Value)].ShouldContain("Message must not be empty.");
    }

    [Fact]
    public async Task INF_05_Valid_Command_Reaches_The_Handler()
    {
        ValidationBehavior<TestCommand, string> behavior = new([new TestCommandValidator()]);

        Result<string> result = await behavior.HandleAsync(
            new TestCommand("hi"),
            CancellationToken.None,
            () => Task.FromResult(Result<string>.Success("ok")));

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("ok");
    }

    private sealed record TestCommand(string Value) : ICommand<string>;

    private sealed record TestQuery(string Value) : IQuery<string>;

    private sealed class TestCommandValidator : AbstractValidator<TestCommand>
    {
        public TestCommandValidator() =>
            RuleFor(command => command.Value).NotEmpty().WithMessage("Message must not be empty.");
    }

    private sealed class RecordingHandler(List<string> order) : ICommandHandler<TestCommand, string>
    {
        public Task<Result<string>> HandleAsync(TestCommand command, CancellationToken cancellationToken)
        {
            order.Add("handler");
            return Task.FromResult(Result<string>.Success(command.Value));
        }
    }

    private sealed class RecordingQueryHandler(List<string> order) : IQueryHandler<TestQuery, string>
    {
        public Task<Result<string>> HandleAsync(TestQuery query, CancellationToken cancellationToken)
        {
            order.Add("query");
            return Task.FromResult(Result<string>.Success(query.Value));
        }
    }

    private sealed class RecordingBehavior<TRequest, TResponse>(List<string> order, string name)
        : IPipelineBehavior<TRequest, TResponse>
    {
        public Task<Result<TResponse>> HandleAsync(
            TRequest request,
            CancellationToken cancellationToken,
            RequestHandlerDelegate<TResponse> next)
        {
            order.Add(name);
            return next();
        }
    }

    private sealed class RecordingTransactionBehavior<TRequest, TResponse>(List<string> order)
        : IPipelineBehavior<TRequest, TResponse>, ICommandOnlyPipelineBehavior
    {
        public Task<Result<TResponse>> HandleAsync(
            TRequest request,
            CancellationToken cancellationToken,
            RequestHandlerDelegate<TResponse> next)
        {
            order.Add("transaction");
            return next();
        }
    }
}