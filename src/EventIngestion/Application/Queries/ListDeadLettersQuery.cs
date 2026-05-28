using SmartSentinelEye.EventIngestion.Application.DTOs;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Queries;

public sealed record ListDeadLettersQuery(int Limit)
    : IQuery<Result<IReadOnlyList<DeadLetterDto>, ListDeadLettersError>>;

public abstract record ListDeadLettersError(string Code, string Message, System.Net.HttpStatusCode Status)
    : ApiError(Code, Message, Status);
